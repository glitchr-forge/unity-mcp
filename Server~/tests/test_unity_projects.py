import json
from pathlib import Path
from types import SimpleNamespace

import pytest

from services import unity_projects as up


def make_project(root: Path, version: str = "6000.3.28f1") -> Path:
    (root / "Assets").mkdir(parents=True)
    (root / "ProjectSettings").mkdir()
    (root / "ProjectSettings/ProjectVersion.txt").write_text(
        f"m_EditorVersion: {version}\nm_EditorVersionWithRevision: {version} (abc)\n", encoding="utf-8")
    return root


@pytest.fixture
def darwin_editors(tmp_path, monkeypatch):
    """Two editors installed the macOS way in a fake Hub folder."""
    hub = tmp_path / "Hub/Editor"
    for v in ("6000.3.28f1", "6000.5.2f1"):
        binary = hub / v / "Unity.app/Contents/MacOS/Unity"
        binary.parent.mkdir(parents=True)
        binary.write_text("")
    (hub / "not-an-editor").mkdir()
    monkeypatch.setattr(up, "SYSTEM", "Darwin")
    monkeypatch.setattr(up, "editor_folders", lambda: [hub])
    monkeypatch.setattr(up, "log_folder", lambda: tmp_path / "logs")
    return hub


def test_versions_order_as_unity_does():
    versions = ["6000.3.28f1", "6000.5.2f1", "2022.3.10f1", "6000.3.28p1", "6000.3.28b2", "6000.3.9f1"]
    assert sorted(versions, key=up.version_key, reverse=True) == [
        "6000.5.2f1", "6000.3.28p1", "6000.3.28f1", "6000.3.28b2", "6000.3.9f1", "2022.3.10f1"]


def test_installed_editors_newest_first(darwin_editors):
    editors = up.installed_editors()
    assert list(editors) == ["6000.5.2f1", "6000.3.28f1"]
    assert editors["6000.3.28f1"].name == "Unity"


def test_project_version_and_is_project(tmp_path):
    p = make_project(tmp_path / "Game", "6000.3.28f1")
    assert up.is_project(p)
    assert up.project_version(p) == "6000.3.28f1"
    assert not up.is_project(tmp_path)
    assert up.project_version(tmp_path) is None


def test_running_editors_from_a_process_list():
    lines = [
        "  75286 /Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/MacOS/Unity -projectpath /Users/me/Games/Space Game -acceptSoftwareTermsForThisRunOnly",
        "  1195 /Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/MacOS/Unity -adb2 -batchMode -name AssetImportWorker0 -projectPath /Users/me/Games/Space Game",
        "  67635 /Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/Helpers/UnityLicensingClient.app/Contents/MacOS/Unity.Licensing.Client --namedPipe x",
        "  85451 /Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/MacOS/Unity -projectPath /Users/me/Sites/Puzzle",
        "  90000 /Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/MacOS/Unity -batchmode -nographics -projectPath /Users/me/Headless Game -logFile /tmp/x.log",
        "  123 /bin/zsh",
    ]
    running = {v["path"]: v for v in up.parse_running(lines).values()}
    assert set(running) == {"/Users/me/Games/Space Game", "/Users/me/Sites/Puzzle", "/Users/me/Headless Game"}
    assert running["/Users/me/Games/Space Game"]["pid"] == 75286
    assert running["/Users/me/Headless Game"]["headless"] is True
    assert running["/Users/me/Sites/Puzzle"]["headless"] is False


def test_hub_projects_read_from_its_list(tmp_path, monkeypatch):
    (tmp_path / "projects-v1.json").write_text(json.dumps({
        "schema_version": "v1",
        "data": {"/a/One": {"path": "/a/One", "version": "6000.3.28f1"}, "/b/Two": {"title": "Two"}},
    }), encoding="utf-8")
    monkeypatch.setattr(up, "hub_folder", lambda: tmp_path)
    assert up.hub_projects() == ["/a/One", "/b/Two"]


def test_open_args_window_and_headless(tmp_path):
    binary, log = Path("/U/Unity"), tmp_path / "x.log"
    assert up.open_args(binary, "/p", False, log) == ["/U/Unity", "-projectPath", "/p", "-logFile", str(log)]
    assert up.open_args(binary, "/p", True, log)[:3] == ["/U/Unity", "-batchmode", "-nographics"]


def test_open_refuses_an_open_project(tmp_path, darwin_editors, monkeypatch):
    p = make_project(tmp_path / "Game")
    monkeypatch.setattr(up, "running_editors", lambda: {up.normalize(p): {"pid": 42, "path": str(p), "headless": False}})
    with pytest.raises(up.ProjectError, match="already open"):
        up.open_project(str(p))


def test_open_refuses_a_version_not_installed(tmp_path, darwin_editors, monkeypatch):
    p = make_project(tmp_path / "Game", "2021.3.1f1")
    monkeypatch.setattr(up, "running_editors", lambda: {})
    with pytest.raises(up.ProjectError, match="2021.3.1f1 is not installed"):
        up.open_project(str(p))


def test_open_headless_starts_unity_with_the_bridge_allowed(tmp_path, darwin_editors, monkeypatch):
    p = make_project(tmp_path / "Game")
    monkeypatch.setattr(up, "running_editors", lambda: {})
    seen = {}

    def fake_popen(args, **kwargs):
        seen.update(args=args, env=kwargs["env"])
        return SimpleNamespace(pid=777)

    monkeypatch.setattr(up.subprocess, "Popen", fake_popen)
    launch = up.open_project(str(p), headless=True)
    assert launch.pid == 777 and launch.version == "6000.3.28f1" and launch.headless
    assert "-batchmode" in seen["args"] and seen["args"][seen["args"].index("-projectPath") + 1] == str(p.resolve())
    assert seen["env"]["UNITY_MCP_ALLOW_BATCH"] == "1"


def test_create_refuses_a_folder_with_files(tmp_path, darwin_editors):
    (tmp_path / "Busy").mkdir()
    (tmp_path / "Busy/readme.txt").write_text("x")
    with pytest.raises(up.ProjectError, match="not empty"):
        up.create_project(str(tmp_path / "Busy"))


def test_create_runs_unity_and_adds_forge_mcp(tmp_path, darwin_editors, monkeypatch):
    target = tmp_path / "New Game"
    package = tmp_path / "io.glitchr.mcp"
    package.mkdir()

    def fake_run(args, **kwargs):
        assert args[args.index("-createProject") + 1] == str(target.resolve())
        assert "6000.3.28f1" in args[0]
        make_project(target)
        (target / "Packages").mkdir()
        (target / "Packages/manifest.json").write_text(json.dumps({"dependencies": {"com.unity.ugui": "2.0.0"}}))
        return SimpleNamespace(returncode=0)

    monkeypatch.setattr(up.subprocess, "run", fake_run)
    monkeypatch.setattr(up, "package_root", lambda: package)
    made = up.create_project(str(target), "6000.3.28f1")
    deps = json.loads((target / "Packages/manifest.json").read_text())["dependencies"]
    assert deps["com.unity.ugui"] == "2.0.0"
    assert deps["io.glitchr.mcp"] == f"file:{package.as_posix()}" == made["forge_mcp"]


def test_run_method_refuses_bad_names_and_open_projects(tmp_path, darwin_editors, monkeypatch):
    p = make_project(tmp_path / "Game")
    monkeypatch.setattr(up, "running_editors", lambda: {})
    with pytest.raises(up.ProjectError, match="not a method name"):
        up.run_method(str(p), "Build; rm -rf /")
    monkeypatch.setattr(up, "running_editors", lambda: {up.normalize(p): {"pid": 1, "path": str(p), "headless": False}})
    with pytest.raises(up.ProjectError, match="is open"):
        up.run_method(str(p), "Game.Builder.Build")


def test_service_plist_runs_the_server_at_login():
    import forge_cli
    plist = forge_cli.service_plist("http://127.0.0.1:8080", python="/py")
    assert plist["Label"] == "io.glitchr.forge-mcp"
    assert plist["ProgramArguments"] == ["/py", "-m", "forge_cli", "serve", "--http-url", "http://127.0.0.1:8080"]
    assert plist["RunAtLoad"] and plist["KeepAlive"]


@pytest.mark.asyncio
async def test_unity_instance_accepts_a_project_folder(tmp_path, monkeypatch):
    from transport.unity_instance_middleware import UnityInstanceMiddleware
    p = make_project(tmp_path / "Game")
    middleware = UnityInstanceMiddleware()

    async def discover(ctx):
        return [SimpleNamespace(id="Game@abc", hash="abc", name="Game", project_path=str(p)),
                SimpleNamespace(id="Other@def", hash="def", name="Other", project_path=str(tmp_path / "Other"))]

    monkeypatch.setattr(middleware, "_discover_instances", discover)
    assert await middleware._resolve_instance_value(str(p) + "/", None) == "Game@abc"
    with pytest.raises(ValueError, match="No connected Unity instance"):
        await middleware._resolve_instance_value(str(tmp_path / "Nowhere"), None)
