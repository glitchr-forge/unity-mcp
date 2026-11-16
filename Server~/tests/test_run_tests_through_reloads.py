"""A test run's start is sent again while the editor is away for a reload, and only then."""

import pytest

import services.tools.run_tests as mod

AWAY = {"success": False, "error": "Unity session not available; please retry",
        "data": {"reason": "no_unity_session", "retry_after_ms": 250}}
STARTED = {"success": True, "data": {"job_id": "job", "status": "running"}}


@pytest.fixture
def sends(monkeypatch):
    calls = []

    def script(*responses):
        queue = list(responses)

        async def send(_fn, instance, command, params):
            calls.append((instance, command, params))
            return queue.pop(0) if len(queue) > 1 else queue[0]

        monkeypatch.setattr(mod.unity_transport, "send_with_unity_instance", send)

    async def no_sleep(_s):
        return None

    monkeypatch.setattr(mod.asyncio, "sleep", no_sleep)
    return calls, script


@pytest.mark.asyncio
async def test_a_start_lost_to_a_reload_is_sent_again(sends):
    calls, script = sends
    script(AWAY, dict(AWAY, data={"reason": "stale_connection"}), STARTED)
    response = await mod._send_through_reloads("Game@hash", "run_tests", {"mode": "EditMode"})
    assert response == STARTED
    assert len(calls) == 3
    assert all(c == ("Game@hash", "run_tests", {"mode": "EditMode"}) for c in calls)


@pytest.mark.asyncio
async def test_another_failure_is_not_sent_again(sends):
    calls, script = sends
    busy = {"success": False, "error": "tests_running", "data": {"reason": "tests_running"}}
    script(busy)
    assert await mod._send_through_reloads(None, "run_tests", {}) == busy
    assert len(calls) == 1


@pytest.mark.asyncio
async def test_the_editor_that_never_comes_back_is_given_up_on(sends):
    calls, script = sends
    script(AWAY)
    # (A budget already spent: one try, and its answer returned as it is.)
    assert await mod._send_through_reloads(None, "run_tests", {}, budget_s=0.0) == AWAY
    assert len(calls) == 1
