"""
Unity projects and editors on this machine, for Forge MCP's project tools.

Everything here works without a Unity editor running: the editors Unity Hub installed (each version's binary),
the projects Unity Hub knows, which project an editor has open (from the process list), and the three ways of
starting Unity on a project - its editor window, a headless editor that stays open and connects to the server
(batch mode, the MCP bridge allowed in it), and one method run in batch mode that then quits. A new project can be
made with a chosen editor version, Forge MCP added to its packages so it connects once opened.
"""

from __future__ import annotations

import json
import os
import platform
import re
import signal
import subprocess
import time
from dataclasses import dataclass, field
from pathlib import Path

PACKAGE_NAME = "io.glitchr.mcp"
SYSTEM = platform.system()


# ---- Paths -----------------------------------------------------------------------------------------------------------

def normalize(path: str | os.PathLike) -> str:
    """A project path compared as the system compares them: absolute, links resolved, no trailing separator,
    case-folded where the file system ignores case (macOS, Windows)."""
    p = os.path.realpath(os.path.expanduser(str(path))).replace("\\", "/").rstrip("/")
    if p.lower().endswith("/assets"):
        p = p[: -len("/assets")]
    return p.casefold() if SYSTEM in ("Darwin", "Windows") else p


def hub_folder() -> Path:
    """Unity Hub's settings folder (its project list, its custom install folder)."""
    home = Path.home()
    if SYSTEM == "Darwin":
        return home / "Library/Application Support/UnityHub"
    if SYSTEM == "Windows":
        return Path(os.environ.get("APPDATA", home)) / "UnityHub"
    return home / ".config/UnityHub"


def editor_folders() -> list[Path]:
    """Where Unity Hub installs editors: its custom install folder first, then the platform's default."""
    folders: list[Path] = []
    try:
        custom = json.loads((hub_folder() / "secondaryInstallPath.json").read_text(encoding="utf-8"))
        if isinstance(custom, str) and custom.strip():
            folders.append(Path(custom))
    except (OSError, ValueError):
        pass
    if SYSTEM == "Darwin":
        folders.append(Path("/Applications/Unity/Hub/Editor"))
    elif SYSTEM == "Windows":
        folders.append(Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Unity/Hub/Editor")
    else:
        folders.append(Path.home() / "Unity/Hub/Editor")
    return folders


def editor_binary(version_folder: Path) -> Path:
    """The editor executable inside one version's folder."""
    if SYSTEM == "Darwin":
        return version_folder / "Unity.app/Contents/MacOS/Unity"
    if SYSTEM == "Windows":
        return version_folder / "Editor/Unity.exe"
    return version_folder / "Editor/Unity"


def log_folder() -> Path:
    if SYSTEM == "Darwin":
        return Path.home() / "Library/Logs/ForgeMCP"
    return Path.home() / ".forge-mcp/logs"


def package_root() -> Path | None:
    """This Forge MCP package's folder (the one holding Server~), when the server runs from it."""
    root = Path(__file__).resolve().parents[3]
    try:
        if json.loads((root / "package.json").read_text(encoding="utf-8")).get("name") == PACKAGE_NAME:
            return root
    except (OSError, ValueError):
        pass
    return None


# ---- What is installed, what exists ------------------------------------------------------------------------------------

def installed_editors() -> dict[str, Path]:
    """Each installed editor version and its executable, newest first."""
    found: dict[str, Path] = {}
    for folder in editor_folders():
        if not folder.is_dir():
            continue
        for child in folder.iterdir():
            binary = editor_binary(child)
            if child.is_dir() and binary.exists() and child.name not in found:
                found[child.name] = binary
    return dict(sorted(found.items(), key=lambda kv: version_key(kv[0]), reverse=True))


def version_key(version: str) -> tuple:
    """6000.3.28f1 -> (6000, 3, 28, 'f', 1): versions compared as Unity orders them."""
    m = re.match(r"(\d+)\.(\d+)\.(\d+)([abfp]?)(\d*)", version)
    if not m:
        return (0, 0, 0, "", 0)
    rank = {"a": 0, "b": 1, "f": 2, "p": 3}.get(m.group(4), 2)
    return (int(m.group(1)), int(m.group(2)), int(m.group(3)), rank, int(m.group(5) or 0))


def is_project(path: str | os.PathLike) -> bool:
    p = Path(path)
    return (p / "Assets").is_dir() and (p / "ProjectSettings").is_dir()


def project_version(path: str | os.PathLike) -> str | None:
    """The editor version a project was last saved with (ProjectSettings/ProjectVersion.txt)."""
    try:
        text = (Path(path) / "ProjectSettings/ProjectVersion.txt").read_text(encoding="utf-8")
    except OSError:
        return None
    m = re.search(r"^m_EditorVersion:\s*(\S+)", text, re.MULTILINE)
    return m.group(1) if m else None


def hub_projects() -> list[str]:
    """The projects Unity Hub lists (projects-v1.json), as their paths."""
    try:
        data = json.loads((hub_folder() / "projects-v1.json").read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return []
    entries = data.get("data", data) if isinstance(data, dict) else {}
    paths = []
    for key, value in entries.items():
        path = value.get("path") if isinstance(value, dict) else None
        paths.append(path or key)
    return [p for p in paths if isinstance(p, str)]


_PROJECT_ARG = re.compile(r"-projectpath\s+(.+?)(?=\s+-[A-Za-z]|\s*$)", re.IGNORECASE)


def parse_running(ps_lines: list[str]) -> dict[str, dict]:
    """Editors in a process list (`pid command` lines): each project's normalized path -> pid, its path as given,
    and whether it runs in batch mode. Unity's helpers (licensing, import workers) are left out."""
    running: dict[str, dict] = {}
    for line in ps_lines:
        line = line.strip()
        pid, _, command = line.partition(" ")
        if not pid.isdigit() or "Unity" not in command or "AssetImportWorker" in command or "-adb2" in command:
            continue
        if not re.search(r"(MacOS/Unity|Unity\.exe|/Editor/Unity)(\s|$)", command):
            continue
        m = _PROJECT_ARG.search(command)
        if not m:
            continue
        given = m.group(1).strip().strip('"')
        running.setdefault(normalize(given), {"pid": int(pid), "path": given, "headless": "-batchmode" in command.lower()})
    return running


def running_editors() -> dict[str, dict]:
    """The editors running now, by the project they have open (see parse_running)."""
    if SYSTEM == "Windows":
        cmd = ["powershell", "-NoProfile", "-Command",
               "Get-CimInstance Win32_Process -Filter \"Name='Unity.exe'\" | ForEach-Object { \"$($_.ProcessId) $($_.CommandLine)\" }"]
    else:
        cmd = ["ps", "-axo", "pid=,command="]
    try:
        out = subprocess.run(cmd, capture_output=True, text=True, timeout=10).stdout
    except (OSError, subprocess.SubprocessError):
        return {}
    return parse_running(out.splitlines())


# ---- Starting Unity --------------------------------------------------------------------------------------------------

class ProjectError(Exception):
    """A request the machine cannot satisfy (no such project, version not installed, project already open)."""


@dataclass
class Launch:
    project: str
    version: str
    pid: int
    headless: bool
    log: str
    args: list[str] = field(default_factory=list)


def _log_path(project: str, what: str) -> Path:
    folder = log_folder()
    folder.mkdir(parents=True, exist_ok=True)
    name = re.sub(r"[^A-Za-z0-9._-]+", "_", Path(project).name) or "project"
    return folder / f"{name}.{what}.log"


def resolve_binary(project: str | None, version: str | None) -> tuple[str, Path]:
    """The editor to use: the version asked for, else the project's own; refused when it is not installed."""
    editors = installed_editors()
    wanted = version or (project_version(project) if project else None)
    if not wanted:
        if not editors:
            raise ProjectError("No Unity editor is installed (Unity Hub's editor folders are empty).")
        wanted = next(iter(editors))
    if wanted not in editors:
        have = ", ".join(editors) or "none"
        raise ProjectError(f"Unity {wanted} is not installed (installed: {have}). Install it with Unity Hub, or pass another version.")
    return wanted, editors[wanted]


def open_args(binary: Path, project: str, headless: bool, log: Path) -> list[str]:
    args = [str(binary), "-projectPath", project, "-logFile", str(log)]
    if headless:
        args[1:1] = ["-batchmode", "-nographics"]
    return args


def open_project(project: str, version: str | None = None, headless: bool = False) -> Launch:
    """Starts an editor on a project: its window, or headless (batch mode, staying open with the MCP bridge
    allowed so it connects to the server). Refused when the project is already open (Unity locks it)."""
    project = os.path.realpath(os.path.expanduser(project))
    if not is_project(project):
        raise ProjectError(f"{project} is not a Unity project (no Assets and ProjectSettings folders).")
    already = running_editors().get(normalize(project))
    if already:
        raise ProjectError(f"{project} is already open (pid {already['pid']}{', headless' if already['headless'] else ''}).")
    version, binary = resolve_binary(project, version)
    log = _log_path(project, "headless" if headless else "editor")
    args = open_args(binary, project, headless, log)
    env = dict(os.environ)
    if headless:
        env["UNITY_MCP_ALLOW_BATCH"] = "1"
    process = subprocess.Popen(args, stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                               env=env, start_new_session=True)
    return Launch(project=project, version=version, pid=process.pid, headless=headless, log=str(log), args=args)


def add_forge_mcp(project: str, source: Path | None = None) -> str | None:
    """Forge MCP added to a project's Packages/manifest.json as a local package (this package's folder), so the
    editor connects to the server once opened. Returns the dependency written, or None when the package's folder is
    unknown (a server not run from the package)."""
    source = source or package_root()
    if source is None:
        return None
    manifest_path = Path(project) / "Packages/manifest.json"
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        manifest = {}
    deps = manifest.setdefault("dependencies", {})
    deps[PACKAGE_NAME] = f"file:{Path(source).as_posix()}"
    manifest_path.parent.mkdir(parents=True, exist_ok=True)
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return deps[PACKAGE_NAME]


def create_project(project: str, version: str | None = None, with_forge_mcp: bool = True, timeout: float = 900) -> dict:
    """A new project made by Unity itself (-createProject in batch mode) with the editor version asked for (else
    the newest installed); Forge MCP added to its packages unless with_forge_mcp is false."""
    project = os.path.realpath(os.path.expanduser(project))
    target = Path(project)
    if target.exists() and any(target.iterdir()):
        raise ProjectError(f"{project} exists and is not empty: a new project needs a new or empty folder.")
    version, binary = resolve_binary(None, version)
    log = _log_path(project, "create")
    args = [str(binary), "-batchmode", "-quit", "-createProject", project, "-logFile", str(log)]
    started = time.monotonic()
    try:
        result = subprocess.run(args, stdin=subprocess.DEVNULL, capture_output=True, text=True, timeout=timeout)
    except subprocess.TimeoutExpired as exc:
        raise ProjectError(f"Unity {version} did not finish creating {project} in {timeout:.0f} s (log: {log}).") from exc
    if not is_project(project):
        raise ProjectError(f"Unity {version} exited with {result.returncode} without creating {project}. {tail(log)}")
    added = add_forge_mcp(project) if with_forge_mcp else None
    return {"project": project, "version": version, "seconds": round(time.monotonic() - started, 1),
            "forge_mcp": added, "log": str(log), "exit_code": result.returncode}


def run_method(project: str, method: str, version: str | None = None, timeout: float = 1800) -> dict:
    """One static editor method run on a closed project in batch mode (-executeMethod), Unity quitting after it."""
    project = os.path.realpath(os.path.expanduser(project))
    if not is_project(project):
        raise ProjectError(f"{project} is not a Unity project.")
    if not re.fullmatch(r"[A-Za-z_][\w.]*\.[A-Za-z_]\w*", method or ""):
        raise ProjectError(f"'{method}' is not a method name (Namespace.Class.Method).")
    already = running_editors().get(normalize(project))
    if already:
        raise ProjectError(f"{project} is open (pid {already['pid']}): Unity opens a project once at a time; "
                           "close it first or call the method in the open editor.")
    version, binary = resolve_binary(project, version)
    log = _log_path(project, "method")
    args = [str(binary), "-batchmode", "-quit", "-projectPath", project, "-executeMethod", method, "-logFile", str(log)]
    started = time.monotonic()
    try:
        result = subprocess.run(args, stdin=subprocess.DEVNULL, capture_output=True, text=True, timeout=timeout)
    except subprocess.TimeoutExpired as exc:
        raise ProjectError(f"{method} did not finish in {timeout:.0f} s (log: {log}).") from exc
    return {"project": project, "version": version, "method": method, "exit_code": result.returncode,
            "seconds": round(time.monotonic() - started, 1), "log": str(log), "log_tail": tail(log)}


def tail(log: str | os.PathLike, lines: int = 30) -> str:
    try:
        return "\n".join(Path(log).read_text(encoding="utf-8", errors="replace").splitlines()[-lines:])
    except OSError:
        return ""


def unsaved_backups(project: str) -> list[str]:
    """The scene backups Unity keeps while a scene has unsaved changes (Temp/__Backupscenes): a sign of unsaved work."""
    folder = Path(project) / "Temp/__Backupscenes"
    try:
        return sorted(p.name for p in folder.iterdir() if p.is_file())
    except OSError:
        return []


def terminate(project: str, timeout: float = 60) -> dict:
    """Stops the editor that has a project open when it cannot be asked to quit (not connected): SIGTERM, which
    Unity takes as a request to exit, then a wait for it to go. Refused while Unity keeps unsaved-scene backups."""
    project = os.path.realpath(os.path.expanduser(project))
    running = running_editors().get(normalize(project))
    if running is None:
        return {"project": project, "stopped": False, "message": "not open"}
    backups = unsaved_backups(project)
    if backups:
        raise ProjectError(f"{project} has unsaved scene changes (Temp/__Backupscenes: {', '.join(backups)}); "
                           "save them in Unity, or close it there.")
    os.kill(running["pid"], signal.SIGTERM)
    closed = wait_until_closed(project, timeout)
    return {"project": project, "pid": running["pid"], "stopped": closed,
            "message": "stopped" if closed else f"pid {running['pid']} is still running after {timeout:.0f} s"}


def wait_until_closed(project: str, timeout: float = 120, poll: float = 1.0) -> bool:
    key = normalize(project)
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if key not in running_editors():
            return True
        time.sleep(poll)
    return False
