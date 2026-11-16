"""
manage_projects: Unity projects on this machine, with or without an editor open - list them and the installed
editor versions, open one (its window or headless) and wait for it to connect, create one with a chosen version,
close one, or run one editor method on a closed one in batch mode. Runs on the server alone (no Unity needed).
"""

import asyncio
import time
from typing import Annotated, Any, Literal

from fastmcp import Context
from mcp.types import ToolAnnotations

from services import unity_projects as projects
from services.registry import mcp_for_unity_tool
from transport.legacy.unity_connection import async_send_command_with_retry
from transport.plugin_hub import PluginHub
from transport.unity_transport import send_with_unity_instance


async def _connected() -> dict[str, dict]:
    """Connected editors by their project's normalized path: Name@hash, version."""
    found: dict[str, dict] = {}
    if not PluginHub.is_configured():
        return found
    sessions = (await PluginHub.get_sessions()).sessions or {}
    for session in sessions.values():
        path = getattr(session, "project_path", None)
        if path and getattr(session, "hash", None):
            found[projects.normalize(path)] = {
                "instance": f"{session.project}@{session.hash}",
                "unity_version": getattr(session, "unity_version", None),
            }
    return found


async def _wait_connected(project: str, timeout: float) -> str | None:
    key = projects.normalize(project)
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        hit = (await _connected()).get(key)
        if hit:
            return hit["instance"]
        await asyncio.sleep(2.0)
    return None


@mcp_for_unity_tool(
    unity_target=None,
    group=None,
    description=(
        "Unity projects on this machine, whether or not an editor is open (runs on the server, no Unity needed). "
        "list: Unity Hub's projects and any open one, each with its editor version, whether that version is installed, "
        "its running editor (pid, headless) and its connected instance (Name@hash). versions: the installed editors. "
        "open: start an editor on `project` (its own version unless `version` is given) - its window, or headless=true "
        "(batch mode, staying open and connected) - and wait up to `timeout` seconds for it to connect; returns its "
        "instance, also addressable by the project's path in any tool's unity_instance. create: a new project at "
        "`project` (a new or empty folder) made by Unity `version` (default: the newest installed), Forge MCP added to "
        "its packages; open=true then opens it. close: quit the project's editor (scenes and assets saved first unless "
        "save=false, which refuses while a scene has unsaved changes) and wait for it to exit. run_method: run one "
        "static editor method (Namespace.Class.Method) on a closed project in batch mode, Unity quitting after it; "
        "returns the exit code and the log's end. Unity opens a project once at a time: open and run_method refuse a "
        "project already open."
    ),
    annotations=ToolAnnotations(title="Manage Projects", readOnlyHint=False, destructiveHint=True, openWorldHint=False),
)
async def manage_projects(
    ctx: Context,
    action: Annotated[Literal["list", "versions", "open", "create", "close", "run_method"], "What to do."],
    project: Annotated[str, "The project's folder (absolute path). Required for open, create, close, run_method."] | None = None,
    version: Annotated[str, "Unity editor version, e.g. 6000.3.28f1 (open/run_method: default the project's own; create: default the newest installed)."] | None = None,
    headless: Annotated[bool, "For open: batch mode, no window (the editor stays open and connects). Default false."] | None = None,
    method: Annotated[str, "For run_method: the static method to execute, Namespace.Class.Method."] | None = None,
    save: Annotated[bool, "For close: save open scenes and assets first (default true)."] | None = None,
    open: Annotated[bool, "For create: open the new project afterwards (its window, or headless when headless=true)."] | None = None,
    timeout: Annotated[float, "Seconds to wait: open (for the connection, default 300), create (default 900), close (default 120), run_method (default 1800)."] | None = None,
) -> dict[str, Any]:
    try:
        if action == "versions":
            editors = projects.installed_editors()
            return {"success": True, "data": {"versions": [{"version": v, "binary": str(b)} for v, b in editors.items()]}}

        if action == "list":
            editors = projects.installed_editors()
            running = projects.running_editors()
            connected = await _connected()
            paths: dict[str, str] = {}
            for path in projects.hub_projects():
                paths.setdefault(projects.normalize(path), path)
            for key, info in running.items():
                paths.setdefault(key, info["path"])
            rows = []
            for key, path in paths.items():
                ver = projects.project_version(path)
                run = running.get(key)
                rows.append({
                    "project": path,
                    "exists": projects.is_project(path),
                    "version": ver,
                    "installed": ver in editors if ver else None,
                    "running": {"pid": run["pid"], "headless": run["headless"]} if run else None,
                    "instance": connected.get(key, {}).get("instance"),
                })
            rows.sort(key=lambda r: (r["running"] is None, r["project"].lower()))
            return {"success": True, "data": {"projects": rows, "installed_versions": list(editors)}}

        if not project:
            return {"success": False, "error": f"`project` (the project's folder) is required for {action}."}

        if action == "open":
            launch = await asyncio.to_thread(projects.open_project, project, version, bool(headless))
            instance = await _wait_connected(launch.project, timeout or 300)
            data = {"project": launch.project, "version": launch.version, "pid": launch.pid, "headless": launch.headless,
                    "log": launch.log, "instance": instance}
            if instance is None:
                return {"success": False, "error": "The editor started but did not connect in time: it may still be importing, "
                        "or Forge MCP is missing from the project or its auto-start is off. Read its log.", "data": data}
            return {"success": True, "message": f"{launch.project} is open as {instance}.", "data": data}

        if action == "create":
            made = await asyncio.to_thread(projects.create_project, project, version, True, timeout or 900)
            if open:
                launch = await asyncio.to_thread(projects.open_project, made["project"], made["version"], bool(headless))
                made["instance"] = await _wait_connected(launch.project, 600)
                made["pid"] = launch.pid
            return {"success": True, "message": f"Created {made['project']} with Unity {made['version']}.", "data": made}

        if action == "close":
            key = projects.normalize(project)
            hit = (await _connected()).get(key)
            if hit is None:
                running = projects.running_editors().get(key)
                if running is None:
                    return {"success": True, "message": f"{project} is not open."}
                return {"success": False, "error": f"{project} is open (pid {running['pid']}) but not connected; close it from Unity."}
            params = {"action": "quit"}
            if save is not None:
                params["save"] = save
            reply = await send_with_unity_instance(async_send_command_with_retry, hit["instance"], "manage_editor", params)
            if isinstance(reply, dict) and reply.get("success") is False:
                return reply
            closed = await asyncio.to_thread(projects.wait_until_closed, project, timeout or 120)
            return {"success": closed, "message": f"{hit['instance']} closed." if closed else f"{hit['instance']} was asked to quit but is still running.",
                    "data": {"instance": hit["instance"], "reply": reply}}

        if action == "run_method":
            if not method:
                return {"success": False, "error": "`method` (Namespace.Class.Method) is required for run_method."}
            result = await asyncio.to_thread(projects.run_method, project, method, version, timeout or 1800)
            return {"success": result["exit_code"] == 0, "message": f"{method} exited with {result['exit_code']}.", "data": result}

        return {"success": False, "error": f"Unknown action '{action}'."}
    except projects.ProjectError as e:
        return {"success": False, "error": str(e)}
