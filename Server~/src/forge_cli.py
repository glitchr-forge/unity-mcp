"""
forge-mcp: Forge MCP on its own, with no Unity editor open.

    forge-mcp serve                      the server (HTTP, 127.0.0.1:8080), in this terminal
    forge-mcp service install            the server as a login service (macOS launchd), kept running
    forge-mcp service uninstall|status
    forge-mcp project list               Unity Hub's projects and the open ones, versions, connected instances
    forge-mcp project versions           the installed editors
    forge-mcp project open PATH [--version V] [--headless]
    forge-mcp project create PATH [--version V] [--no-forge-mcp] [--open]
    forge-mcp project close PATH [--no-save]
    forge-mcp project run PATH METHOD [--version V]

Projects are opened, made and run by Unity itself (its window, batch mode); an editor opened with Forge MCP in its
packages connects to the server, which then controls it by its Name@hash or its folder.
"""

from __future__ import annotations

import json
import os
import plistlib
import signal
import subprocess
import time
import sys
import urllib.request
from pathlib import Path

import click

from services import unity_projects as projects

LABEL = "io.glitchr.forge-mcp"
DEFAULT_URL = "http://127.0.0.1:8080"


def _out(data) -> None:
    click.echo(json.dumps(data, indent=2, default=str))


# ---- forge-mcp ---------------------------------------------------------------------------------------------------------

@click.group()
def main() -> None:
    """Forge MCP without Unity open: its server, its service, the machine's Unity projects."""


@main.command()
@click.option("--http-url", default=DEFAULT_URL, show_default=True, help="Where the server listens.")
def serve(http_url: str) -> None:
    """Run the server here (HTTP, every editor's tools scoped to its project)."""
    import main as server
    sys.argv = ["forge-mcp", "--transport", "http", "--http-url", http_url, "--project-scoped-tools"]
    server.main()


# ---- forge-mcp service --------------------------------------------------------------------------------------------------

def plist_path() -> Path:
    return Path.home() / "Library/LaunchAgents" / f"{LABEL}.plist"


def service_plist(http_url: str, python: str | None = None) -> dict:
    """The launchd agent: this Python running forge_cli serve, at login and kept alive, its output in the log folder."""
    logs = projects.log_folder()
    path = os.pathsep.join([str(Path.home() / ".local/bin"), "/opt/homebrew/bin", "/usr/local/bin", "/usr/bin", "/bin"])
    return {
        "Label": LABEL,
        "ProgramArguments": [python or sys.executable, "-m", "forge_cli", "serve", "--http-url", http_url],
        "WorkingDirectory": str(Path(__file__).resolve().parent),
        "EnvironmentVariables": {"PATH": path, "PYTHONPATH": str(Path(__file__).resolve().parent)},
        "RunAtLoad": True,
        "KeepAlive": True,
        "StandardOutPath": str(logs / "server.log"),
        "StandardErrorPath": str(logs / "server.log"),
    }


def _launchctl(*args: str) -> subprocess.CompletedProcess:
    return subprocess.run(["launchctl", *args], capture_output=True, text=True)


def _reachable(http_url: str) -> bool:
    try:
        with urllib.request.urlopen(f"{http_url}/health", timeout=3) as r:
            return r.status == 200
    except OSError:
        return False


def _listeners(http_url: str) -> list[int]:
    """The processes listening on the URL's port."""
    port = http_url.rsplit(":", 1)[-1].split("/")[0]
    out = subprocess.run(["lsof", "-nP", f"-iTCP:{port}", "-sTCP:LISTEN", "-t"], capture_output=True, text=True).stdout
    return [int(p) for p in out.split() if p.isdigit()]


def is_forge_server(command: str) -> bool:
    """Whether a process's command line is a Forge MCP server (the package's entry points, however launched)."""
    return any(mark in command for mark in ("mcp-for-unity", "forge_cli", "from main import main"))


def _stop_server(http_url: str, timeout: float = 15) -> list[int]:
    """Stops the Forge MCP server answering on the URL; refuses a process that is not one."""
    stopped = []
    for pid in _listeners(http_url):
        command = subprocess.run(["ps", "-o", "command=", "-p", str(pid)], capture_output=True, text=True).stdout.strip()
        if not is_forge_server(command):
            raise click.ClickException(f"Port is held by pid {pid}, which is not a Forge MCP server ({command[:80]}); not stopping it.")
        os.kill(pid, signal.SIGTERM)
        stopped.append(pid)
    deadline = time.monotonic() + timeout
    while _reachable(http_url) and time.monotonic() < deadline:
        time.sleep(0.5)
    if _reachable(http_url):
        raise click.ClickException(f"The server on {http_url} did not stop in {timeout:.0f} s.")
    return stopped


@main.group()
def service() -> None:
    """The server as a login service (macOS), running whether or not Unity is open."""


@service.command("install")
@click.option("--http-url", default=DEFAULT_URL, show_default=True)
@click.option("--replace", is_flag=True, help="Stop the Forge MCP server already answering on the URL (editors reconnect).")
def service_install(http_url: str, replace: bool) -> None:
    """Install and start the service (a server already answering on the URL is replaced with --replace)."""
    if sys.platform != "darwin":
        raise click.ClickException("The service is a macOS launchd agent; elsewhere run `forge-mcp serve` from your startup.")
    if _reachable(http_url) and _launchctl("print", f"gui/{os.getuid()}/{LABEL}").returncode != 0:
        if not replace:
            raise click.ClickException(f"A server already answers on {http_url} (started by an editor or by hand): "
                                       "`forge-mcp service install --replace` stops it and installs the service in its place.")
        click.echo(f"Stopped the server on {http_url} (pid {', '.join(map(str, _stop_server(http_url)))}).")
    projects.log_folder().mkdir(parents=True, exist_ok=True)
    plist_path().parent.mkdir(parents=True, exist_ok=True)
    _launchctl("bootout", f"gui/{os.getuid()}/{LABEL}")
    with open(plist_path(), "wb") as f:
        plistlib.dump(service_plist(http_url), f)
    done = _launchctl("bootstrap", f"gui/{os.getuid()}", str(plist_path()))
    if done.returncode != 0:
        raise click.ClickException(f"launchctl bootstrap failed: {done.stderr.strip()}")
    click.echo(f"Forge MCP runs as {LABEL} on {http_url} (log: {projects.log_folder() / 'server.log'}).")


@service.command("uninstall")
def service_uninstall() -> None:
    """Stop the service and remove it."""
    _launchctl("bootout", f"gui/{os.getuid()}/{LABEL}")
    if plist_path().exists():
        plist_path().unlink()
    click.echo("Forge MCP's service is removed.")


@service.command("status")
@click.option("--http-url", default=DEFAULT_URL, show_default=True)
def service_status(http_url: str) -> None:
    """Whether the service is installed and the server answers."""
    loaded = sys.platform == "darwin" and _launchctl("print", f"gui/{os.getuid()}/{LABEL}").returncode == 0
    _out({"installed": plist_path().exists(), "loaded": loaded, "answers": _reachable(http_url), "url": http_url})


# ---- forge-mcp project --------------------------------------------------------------------------------------------------

@main.group()
def project() -> None:
    """Unity projects on this machine: list, open, create, close, run a method."""


def _instances(http_url: str) -> list[dict]:
    try:
        with urllib.request.urlopen(f"{http_url}/api/instances", timeout=5) as r:
            return json.loads(r.read().decode("utf-8")).get("instances", [])
    except (OSError, ValueError):
        return []


@project.command("list")
@click.option("--http-url", default=DEFAULT_URL, show_default=True)
def project_list(http_url: str) -> None:
    """Unity Hub's projects and the open ones: version, installed or not, running editor, connected instance."""
    editors = projects.installed_editors()
    running = projects.running_editors()
    connected = {projects.normalize(i["project_path"]): f"{i['project']}@{i['hash']}"
                 for i in _instances(http_url) if i.get("project_path") and i.get("hash")}
    paths: dict[str, str] = {}
    for p in projects.hub_projects():
        paths.setdefault(projects.normalize(p), p)
    for key, info in running.items():
        paths.setdefault(key, info["path"])
    rows = []
    for key, p in sorted(paths.items(), key=lambda kv: kv[1].lower()):
        ver = projects.project_version(p)
        run = running.get(key)
        rows.append({"project": p, "version": ver, "installed": ver in editors if ver else None,
                     "running": run and {"pid": run["pid"], "headless": run["headless"]}, "instance": connected.get(key)})
    _out({"projects": rows, "installed_versions": list(editors)})


@project.command("versions")
def project_versions() -> None:
    """The installed Unity editors, newest first."""
    _out({v: str(b) for v, b in projects.installed_editors().items()})


@project.command("open")
@click.argument("path")
@click.option("--version", help="Editor version (default: the project's own).")
@click.option("--headless", is_flag=True, help="Batch mode, no window; the editor stays open and connects.")
def project_open(path: str, version: str | None, headless: bool) -> None:
    """Open a project in Unity (its window, or headless)."""
    try:
        launch = projects.open_project(path, version, headless)
    except projects.ProjectError as e:
        raise click.ClickException(str(e))
    _out({"project": launch.project, "version": launch.version, "pid": launch.pid, "headless": launch.headless, "log": launch.log})


@project.command("create")
@click.argument("path")
@click.option("--version", help="Editor version (default: the newest installed).")
@click.option("--no-forge-mcp", is_flag=True, help="Do not add Forge MCP to its packages.")
@click.option("--open", "open_after", is_flag=True, help="Open it once made.")
@click.option("--headless", is_flag=True, help="With --open: headless.")
def project_create(path: str, version: str | None, no_forge_mcp: bool, open_after: bool, headless: bool) -> None:
    """Create a project with a chosen Unity version."""
    try:
        made = projects.create_project(path, version, not no_forge_mcp)
        if open_after:
            made["pid"] = projects.open_project(made["project"], made["version"], headless).pid
    except projects.ProjectError as e:
        raise click.ClickException(str(e))
    _out(made)


@project.command("close")
@click.argument("path")
@click.option("--no-save", is_flag=True, help="Do not save (refused while a scene has unsaved changes).")
@click.option("--http-url", default=DEFAULT_URL, show_default=True)
def project_close(path: str, no_save: bool, http_url: str) -> None:
    """Close a project's editor through the server (scenes and assets saved first)."""
    key = projects.normalize(path)
    hit = next((f"{i['project']}@{i['hash']}" for i in _instances(http_url)
                if i.get("project_path") and projects.normalize(i["project_path"]) == key), None)
    if hit is None:
        raise click.ClickException(f"No editor connected to {http_url} has {path} open.")
    from cli.utils.config import CLIConfig
    from cli.utils.connection import run_command
    host, _, port = http_url.split("://", 1)[-1].partition(":")
    reply = run_command("manage_editor", {"action": "quit", "save": not no_save},
                        CLIConfig(host=host, port=int(port or 80), unity_instance=hit))
    if isinstance(reply, dict) and reply.get("success") is False:
        raise click.ClickException(reply.get("error") or reply.get("message") or "The editor refused to quit.")
    closed = projects.wait_until_closed(path)
    _out({"instance": hit, "closed": closed})


@project.command("run")
@click.argument("path")
@click.argument("method")
@click.option("--version", help="Editor version (default: the project's own).")
@click.option("--timeout", default=1800, show_default=True, type=float)
def project_run(path: str, method: str, version: str | None, timeout: float) -> None:
    """Run a static editor method on a closed project in batch mode (Unity quits after it)."""
    try:
        result = projects.run_method(path, method, version, timeout)
    except projects.ProjectError as e:
        raise click.ClickException(str(e))
    _out(result)
    sys.exit(result["exit_code"])


if __name__ == "__main__":
    main()
