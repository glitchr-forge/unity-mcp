"""Tests for the manage_projects tool (the server alone: no Unity is started)."""
import asyncio
from types import SimpleNamespace

import pytest

import services.tools.manage_projects as mod
from services import unity_projects as up
from services.tools.manage_projects import manage_projects


@pytest.fixture
def machine(monkeypatch, tmp_path):
    """One installed editor, two Hub projects (one open and connected), no real processes."""
    game, other = tmp_path / "Game", tmp_path / "Other"
    for p in (game, other):
        (p / "Assets").mkdir(parents=True)
        (p / "ProjectSettings").mkdir()
        (p / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 6000.3.28f1\n")
    state = {"running": {up.normalize(game): {"pid": 11, "path": str(game), "headless": False}},
             "sessions": {"s1": SimpleNamespace(project="Game", hash="abc", project_path=str(game), unity_version="6000.3.28f1")},
             "sent": []}
    monkeypatch.setattr(up, "installed_editors", lambda: {"6000.3.28f1": tmp_path / "Unity"})
    monkeypatch.setattr(up, "hub_projects", lambda: [str(game), str(other)])
    monkeypatch.setattr(up, "running_editors", lambda: state["running"])
    monkeypatch.setattr(mod.PluginHub, "is_configured", classmethod(lambda cls: True))

    async def sessions(cls, user_id=None):
        return SimpleNamespace(sessions=state["sessions"])

    monkeypatch.setattr(mod.PluginHub, "get_sessions", classmethod(sessions))

    async def send(fn, instance, tool, params):
        state["sent"].append((instance, tool, params))
        return {"success": True}

    monkeypatch.setattr(mod, "send_with_unity_instance", send)
    return SimpleNamespace(game=game, other=other, state=state)


def run(**kwargs):
    return asyncio.run(manage_projects(SimpleNamespace(), **kwargs))


def test_versions(machine):
    result = run(action="versions")
    assert result["success"] and result["data"]["versions"][0]["version"] == "6000.3.28f1"


def test_list_shows_running_and_connected(machine):
    rows = {r["project"]: r for r in run(action="list")["data"]["projects"]}
    game, other = rows[str(machine.game)], rows[str(machine.other)]
    assert game["running"] == {"pid": 11, "headless": False} and game["instance"] == "Game@abc"
    assert other["running"] is None and other["instance"] is None and other["installed"] is True


def test_project_required(machine):
    assert run(action="open")["success"] is False


def test_open_waits_for_the_connection(machine, monkeypatch):
    def fake_open(project, version, headless):
        machine.state["sessions"]["s2"] = SimpleNamespace(project="Other", hash="def", project_path=project, unity_version="6000.3.28f1")
        return up.Launch(project=project, version="6000.3.28f1", pid=22, headless=headless, log="/tmp/x.log")

    monkeypatch.setattr(up, "open_project", fake_open)
    result = run(action="open", project=str(machine.other), headless=True, timeout=5)
    assert result["success"] and result["data"]["instance"] == "Other@def" and result["data"]["headless"]


def test_open_reports_a_refusal(machine, monkeypatch):
    def refuse(project, version, headless):
        raise up.ProjectError("already open")

    monkeypatch.setattr(up, "open_project", refuse)
    assert run(action="open", project=str(machine.game)) == {"success": False, "error": "already open"}


def test_close_asks_the_editor_to_quit(machine, monkeypatch):
    monkeypatch.setattr(up, "wait_until_closed", lambda project, timeout: True)
    result = run(action="close", project=str(machine.game), save=False)
    assert result["success"]
    assert machine.state["sent"] == [("Game@abc", "manage_editor", {"action": "quit", "save": False})]


def test_close_of_a_closed_project(machine):
    assert run(action="close", project=str(machine.other))["message"].endswith("is not open.")


def test_run_method_needs_a_method(machine):
    assert "method" in run(action="run_method", project=str(machine.other))["error"]
