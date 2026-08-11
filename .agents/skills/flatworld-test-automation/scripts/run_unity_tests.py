#!/usr/bin/env python3
"""Run FlatWorld Unity tests without interactive Unity/MCP test calls."""

from __future__ import annotations

import argparse
import ctypes
import json
import os
import re
import subprocess
import sys
import time
import uuid
import xml.etree.ElementTree as ET
from pathlib import Path
from typing import Any


BRIDGE_SOURCE = Path("Assets/Editor/FlatWorld/Automation/FlatWorldSkillTestBridge.cs")
RESULT_DIR = Path("Library/FlatWorldSkillTests")
TEST_ASSEMBLY = "FlatWorld.GameTest"
GOLDEN_PATH_CATEGORY = "Runtime.GoldenPath"
GOLDEN_REQUEST_PREFIX = "golden-request-"
GOLDEN_RUNNING_PREFIX = "golden-running-"
GOLDEN_RESULT_PREFIX = "golden-result-"
GOLDEN_OPERATION_IDS = {
    "audio.cue-playback",
    "buff.burning",
    "building.placement",
    "combat.player-respawn",
    "combat.target-damage",
    "dialogue.player-speech",
    "environment.ecology",
    "environment.tile-effects",
    "environment.time-weather",
    "inventory.crafting",
    "item.drop-lifecycle",
    "map.chunk-load-speed",
    "map.hydrology",
    "navigation.loaded-grid",
    "player.admin-invincibility",
    "player.admin-move-speed",
    "player.interaction-retry",
    "player.run-transition",
    "player.spawn-land",
    "quest.progression",
    "save.auto",
    "ui.inventory-panel",
    "world.model-streaming",
    "world.wrap",
}

DEFAULT_GOLDEN_CONFIGURATION: dict[str, Any] = {
    "schemaVersion": 1,
    "presetName": "default",
    "world": {
        "seed": 424242,
        "radius": 512,
        "chunkSizeX": 16,
        "chunkSizeY": 16,
        "noiseScale": 0.01,
        "topologyMode": "Wrapped",
        "difficulty": "Simple",
        "autoGenerateMap": True,
    },
    "player": {
        "cameraOrthographicSize": 10.0,
        "screenshotOrthographicSize": 20.0,
        "wrapMoveSpeed": 12.0,
        "maximumMoveSpeed": 24.0,
        "waypointCount": 12,
        "waypointStepChunks": 1.5,
        "middleScreenshotWaypointIndex": 5,
    },
    "scenarios": {
        "enableAllOperations": True,
        "enabledOperationIds": [],
        "disabledOperationIds": [],
        "worldWrap": True,
        "hydrology": True,
        "burningBuff": True,
    },
    "hydrology": {
        "overrideGeneration": True,
        "hydrologyRegionSize": 64,
        "runoffCellSize": 16,
        "runoffSampleStride": 4,
        "maxTraceSteps": 128,
        "minimumVisibleCourseLength": 8,
        "seaLevel": 0.5,
        "infiltrationFloor": 0.0,
        "riverStartFlow": 0.02,
        "tributaryStartFlow": 0.01,
        "fullWidthFlow": 0.2,
        "maxRiverWidth": 5,
        "floodplainStartFlow": 0.02,
        "lakeMinFlow": 0.05,
        "windwardRainGain": 0.8,
        "leewardRainLoss": 0.6,
    },
    "execution": {
        "startupTimeoutSeconds": 90.0,
        "worldEntryTimeoutSeconds": 180.0,
        "moveTimeoutSeconds": 20.0,
        "screenshotTimeoutSeconds": 15.0,
        "errorCollectionSeconds": 10.0,
        "minimumVisitedChunks": 10,
        "minimumObservedChunks": 50,
        "screenshotSettleFrames": 2,
        "screenshotSettleSeconds": 0.35,
        "positionTolerance": 0.5,
    },
}


class RunnerError(RuntimeError):
    pass


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Run FlatWorld.GameTest categories through the open Unity Editor or batchmode."
    )
    parser.add_argument("--category", action="append", default=[], help="NUnit category; repeat as needed.")
    parser.add_argument("--test", action="append", default=[], help="Exact fully qualified test name; repeat as needed.")
    parser.add_argument(
        "--smoke",
        action="store_true",
        help="Run the curated, one-critical-check-per-subsystem Smoke category.",
    )
    parser.add_argument("--all", action="store_true", help="Run the entire FlatWorld.GameTest assembly.")
    parser.add_argument(
        "--golden-path",
        action="store_true",
        help="Run the deterministic create-world, move-player, and chunk-streaming path.",
    )
    parser.add_argument(
        "--golden-config",
        type=Path,
        help="JSON file containing partial GoldenPath executor configuration.",
    )
    parser.add_argument(
        "--golden-set",
        action="append",
        default=[],
        metavar="PATH=JSON_VALUE",
        help="Override one GoldenPath configuration field; repeat as needed.",
    )
    parser.add_argument("--list-categories", action="store_true", help="List categories declared under Assets/GameTest.")
    parser.add_argument(
        "--check-encoding",
        action="store_true",
        help="Validate that all Assets/**/*.cs files are UTF-8 and contain no replacement characters.",
    )
    parser.add_argument("--mode", choices=("EditMode", "PlayMode"), default="PlayMode")
    parser.add_argument("--project", type=Path, help="Unity project root. Defaults to automatic discovery.")
    parser.add_argument("--unity", type=Path, help="Unity executable for batchmode fallback.")
    parser.add_argument("--force-batch", action="store_true", help="Use a separate Unity batchmode process.")
    parser.add_argument("--timeout", type=float, default=600.0, help="Maximum wait in seconds (default: 600).")
    args = parser.parse_args()

    if args.golden_path:
        if args.smoke or args.all or args.category or args.test:
            parser.error("--golden-path cannot be combined with --smoke/--all/--category/--test")
        args.category = [GOLDEN_PATH_CATEGORY]
    elif args.golden_config is not None or args.golden_set:
        parser.error("--golden-config/--golden-set require --golden-path")
    elif args.smoke:
        if args.all or args.category or args.test:
            parser.error("--smoke cannot be combined with --all/--category/--test")
        args.category = ["Smoke"]

    args.category = unique_nonempty(args.category)
    args.test = unique_nonempty(args.test)
    if args.timeout <= 0:
        parser.error("--timeout must be greater than zero")
    if (
        not args.list_categories
        and not args.check_encoding
        and not args.all
        and not args.category
        and not args.test
    ):
        parser.error("select --category/--test, or pass --all")
    if args.all and (args.category or args.test):
        parser.error("--all cannot be combined with --category or --test")
    return args


def unique_nonempty(values: list[str]) -> list[str]:
    return list(dict.fromkeys(value.strip() for value in values if value.strip()))


def build_golden_configuration(args: argparse.Namespace) -> dict[str, Any]:
    configuration = json.loads(json.dumps(DEFAULT_GOLDEN_CONFIGURATION))
    if args.golden_config is not None:
        path = args.golden_config.expanduser().resolve()
        try:
            supplied = json.loads(path.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as error:
            raise RunnerError(f"Could not read GoldenPath configuration {path}: {error}") from error
        if not isinstance(supplied, dict):
            raise RunnerError("GoldenPath configuration root must be a JSON object.")
        merge_known_configuration(configuration, supplied, "")

    for assignment in args.golden_set:
        if "=" not in assignment:
            raise RunnerError(f"Invalid --golden-set {assignment!r}; expected PATH=JSON_VALUE.")
        dotted_path, raw_value = assignment.split("=", 1)
        try:
            value = json.loads(raw_value)
        except json.JSONDecodeError:
            value = raw_value
        set_known_configuration_value(configuration, dotted_path.strip(), value)
    validate_golden_operation_selection(configuration)
    return configuration


def validate_golden_operation_selection(configuration: dict[str, Any]) -> None:
    """在提交给 Unity 前拒绝拼写错误、重复或互相冲突的操作选择。"""
    scenarios = configuration["scenarios"]
    enabled = scenarios["enabledOperationIds"]
    disabled = scenarios["disabledOperationIds"]
    for field_name, operation_ids in (
        ("enabledOperationIds", enabled),
        ("disabledOperationIds", disabled),
    ):
        if not all(isinstance(operation_id, str) for operation_id in operation_ids):
            raise RunnerError(
                f"GoldenPath scenarios.{field_name} must contain only strings."
            )
        normalized = [operation_id.casefold() for operation_id in operation_ids]
        if len(normalized) != len(set(normalized)):
            raise RunnerError(
                f"GoldenPath scenarios.{field_name} contains duplicate operation IDs."
            )
        known_by_normalized_id = {
            operation_id.casefold(): operation_id for operation_id in GOLDEN_OPERATION_IDS
        }
        unknown = sorted(
            (
                operation_id
                for operation_id in operation_ids
                if operation_id.casefold() not in known_by_normalized_id
            ),
            key=str.casefold,
        )
        if unknown:
            raise RunnerError(
                f"GoldenPath scenarios.{field_name} contains unknown operations: "
                + ", ".join(unknown)
            )

    disabled_normalized = {operation_id.casefold() for operation_id in disabled}
    conflicts = sorted(
        (
            operation_id
            for operation_id in enabled
            if operation_id.casefold() in disabled_normalized
        ),
        key=str.casefold,
    )
    if conflicts:
        raise RunnerError(
            "GoldenPath operations cannot be both enabled and disabled: "
            + ", ".join(conflicts)
        )
    if not scenarios["enableAllOperations"] and not enabled:
        raise RunnerError(
            "GoldenPath scenarios.enabledOperationIds cannot be empty when "
            "enableAllOperations is false."
        )


def merge_known_configuration(
    target: dict[str, Any], supplied: dict[str, Any], prefix: str
) -> None:
    for key, value in supplied.items():
        path = f"{prefix}.{key}" if prefix else key
        if key not in target:
            raise RunnerError(f"Unknown GoldenPath configuration field: {path}")
        if isinstance(target[key], dict):
            if not isinstance(value, dict):
                raise RunnerError(f"GoldenPath configuration section {path} must be an object.")
            merge_known_configuration(target[key], value, path)
        else:
            require_matching_configuration_type(path, target[key], value)
            target[key] = value


def set_known_configuration_value(
    configuration: dict[str, Any], dotted_path: str, value: Any
) -> None:
    parts = [part for part in dotted_path.split(".") if part]
    if not parts:
        raise RunnerError("GoldenPath override path cannot be empty.")
    current: dict[str, Any] = configuration
    for part in parts[:-1]:
        if part not in current or not isinstance(current[part], dict):
            raise RunnerError(f"Unknown GoldenPath configuration section: {dotted_path}")
        current = current[part]
    leaf = parts[-1]
    if leaf not in current or isinstance(current[leaf], dict):
        raise RunnerError(f"Unknown GoldenPath configuration field: {dotted_path}")
    require_matching_configuration_type(dotted_path, current[leaf], value)
    current[leaf] = value


def require_matching_configuration_type(path: str, expected: Any, value: Any) -> None:
    if isinstance(expected, bool):
        valid = isinstance(value, bool)
    elif isinstance(expected, int):
        valid = isinstance(value, int) and not isinstance(value, bool)
    elif isinstance(expected, float):
        valid = isinstance(value, (int, float)) and not isinstance(value, bool)
    else:
        valid = isinstance(value, type(expected))
    if not valid:
        raise RunnerError(
            f"GoldenPath configuration field {path} expects "
            f"{type(expected).__name__}, got {type(value).__name__}."
        )


def is_unity_project(path: Path) -> bool:
    return (path / "ProjectSettings" / "ProjectVersion.txt").is_file() and (path / "Assets").is_dir()


def discover_project(explicit: Path | None) -> Path:
    if explicit is not None:
        project = explicit.expanduser().resolve()
        if not is_unity_project(project):
            raise RunnerError(f"Not a Unity project root: {project}")
        return project

    starts = [Path.cwd().resolve(), Path(__file__).resolve().parent]
    checked: set[Path] = set()
    for start in starts:
        for candidate in (start, *start.parents):
            if candidate in checked:
                continue
            checked.add(candidate)
            if is_unity_project(candidate):
                return candidate
    raise RunnerError("Could not discover a Unity project. Pass --project explicitly.")


def list_categories(project: Path) -> int:
    pattern = re.compile(r"\[Category\(\s*\"([^\"]+)\"\s*\)\]")
    categories: set[str] = set()
    for source in (project / "Assets" / "GameTest").rglob("*.cs"):
        text = source.read_text(encoding="utf-8", errors="replace")
        categories.update(pattern.findall(text))
    for category in sorted(categories, key=str.casefold):
        print(category)
    return 0 if categories else 2


def validate_source_encoding(project: Path) -> int:
    invalid: list[str] = []
    replacements: list[str] = []
    source_root = project / "Assets"
    for source in source_root.rglob("*.cs"):
        try:
            text = source.read_text(encoding="utf-8", errors="strict")
        except UnicodeDecodeError:
            invalid.append(source.relative_to(project).as_posix())
            continue
        if "\ufffd" in text:
            replacements.append(source.relative_to(project).as_posix())

    if invalid or replacements:
        details = [
            f"Source encoding preflight failed: {len(invalid)} non-UTF-8, "
            f"{len(replacements)} containing U+FFFD."
        ]
        if invalid:
            details.append("Non-UTF-8:\n  " + "\n  ".join(invalid))
        if replacements:
            details.append("Contains replacement character:\n  " + "\n  ".join(replacements))
        raise RunnerError("\n".join(details))

    return sum(1 for _ in source_root.rglob("*.cs"))


def pid_exists(pid: int) -> bool:
    if pid <= 0:
        return False
    if os.name == "nt":
        process_query_limited_information = 0x1000
        kernel32 = ctypes.windll.kernel32  # type: ignore[attr-defined]
        handle = kernel32.OpenProcess(process_query_limited_information, False, pid)
        if not handle:
            return False
        kernel32.CloseHandle(handle)
        return True
    try:
        os.kill(pid, 0)
    except (OSError, PermissionError):
        return False
    return True


def active_editor_pid(project: Path) -> int | None:
    instance_file = project / "Library" / "EditorInstance.json"
    try:
        data = json.loads(instance_file.read_text(encoding="utf-8"))
        pid = int(data.get("process_id", 0))
    except (OSError, ValueError, TypeError, json.JSONDecodeError):
        return None
    return pid if pid_exists(pid) else None


def atomic_write_json(path: Path, payload: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_suffix(path.suffix + ".tmp")
    temporary.write_text(json.dumps(payload, ensure_ascii=False, indent=2), encoding="utf-8")
    os.replace(temporary, path)


def run_in_open_editor(project: Path, args: argparse.Namespace, editor_pid: int) -> dict[str, Any]:
    bridge = project / BRIDGE_SOURCE
    if not bridge.is_file():
        raise RunnerError(f"Open-Editor bridge is missing: {bridge}")

    request_id = uuid.uuid4().hex
    result_dir = project / RESULT_DIR
    request_path = result_dir / f"request-{request_id}.json"
    running_path = result_dir / f"running-{request_id}.json"
    result_path = result_dir / f"result-{request_id}.json"
    payload = {
        "id": request_id,
        "mode": args.mode,
        "categories": args.category,
        "testNames": args.test,
        "createdUtc": utc_now(),
    }
    atomic_write_json(request_path, payload)

    deadline = time.monotonic() + args.timeout
    try:
        while time.monotonic() < deadline:
            if result_path.is_file():
                try:
                    result = json.loads(result_path.read_text(encoding="utf-8"))
                    result.setdefault("resultFile", str(result_path))
                    return result
                except (OSError, json.JSONDecodeError):
                    time.sleep(0.1)
                    continue
            if not pid_exists(editor_pid):
                raise RunnerError("Unity Editor exited before returning a test result.")
            time.sleep(0.2)
    finally:
        if request_path.exists():
            request_path.unlink(missing_ok=True)

    state = "running" if running_path.exists() else "pending"
    raise RunnerError(f"Timed out after {args.timeout:g}s while the Editor request was {state}.")


def run_golden_path_in_open_editor(
    project: Path, args: argparse.Namespace, editor_pid: int
) -> dict[str, Any]:
    request_id = uuid.uuid4().hex
    result_dir = project / RESULT_DIR
    request_path = result_dir / f"{GOLDEN_REQUEST_PREFIX}{request_id}.json"
    running_path = result_dir / f"{GOLDEN_RUNNING_PREFIX}{request_id}.json"
    result_path = result_dir / f"{GOLDEN_RESULT_PREFIX}{request_id}.json"
    atomic_write_json(
        request_path,
        {
            "id": request_id,
            "createdUtc": utc_now(),
            "configuration": build_golden_configuration(args),
        },
    )

    deadline = time.monotonic() + args.timeout
    try:
        while time.monotonic() < deadline:
            if result_path.is_file():
                try:
                    result = json.loads(result_path.read_text(encoding="utf-8"))
                    result.setdefault("resultFile", str(result_path))
                    return result
                except (OSError, json.JSONDecodeError):
                    time.sleep(0.1)
                    continue
            if not pid_exists(editor_pid):
                raise RunnerError("Unity Editor exited before returning the golden-path result.")
            time.sleep(0.2)
    finally:
        request_path.unlink(missing_ok=True)

    state = "running" if running_path.exists() else "pending"
    raise RunnerError(
        f"Timed out after {args.timeout:g}s while the default-scene golden path was {state}."
    )


def utc_now() -> str:
    return time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime())


def project_unity_version(project: Path) -> str | None:
    text = (project / "ProjectSettings" / "ProjectVersion.txt").read_text(encoding="utf-8", errors="replace")
    match = re.search(r"^m_EditorVersion:\s*(\S+)", text, flags=re.MULTILINE)
    return match.group(1) if match else None


def find_unity(project: Path, explicit: Path | None) -> Path:
    candidates: list[Path] = []
    if explicit is not None:
        candidates.append(explicit.expanduser())
    env_path = os.environ.get("UNITY_PATH")
    if env_path:
        candidates.append(Path(env_path).expanduser())

    version = project_unity_version(project)
    if version and os.name == "nt":
        program_files = Path(os.environ.get("ProgramFiles", r"C:\Program Files"))
        candidates.append(program_files / "Unity" / "Hub" / "Editor" / version / "Editor" / "Unity.exe")
    elif version and sys.platform == "darwin":
        candidates.append(Path("/Applications/Unity/Hub/Editor") / version / "Unity.app/Contents/MacOS/Unity")
    elif version:
        candidates.append(Path.home() / "Unity" / "Hub" / "Editor" / version / "Editor" / "Unity")

    instance_file = project / "Library" / "EditorInstance.json"
    try:
        instance_path = Path(json.loads(instance_file.read_text(encoding="utf-8")).get("app_path", ""))
        if str(instance_path):
            candidates.append(instance_path)
    except (OSError, json.JSONDecodeError, TypeError):
        pass

    for candidate in candidates:
        resolved = candidate.resolve()
        if resolved.is_file():
            return resolved
    raise RunnerError(
        f"Could not locate Unity {version or ''}. Pass --unity or set UNITY_PATH."
    )


def start_editor(project: Path, args: argparse.Namespace) -> int:
    unity = find_unity(project, args.unity)
    process = subprocess.Popen(
        [str(unity), "-projectPath", str(project)],
        cwd=project,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )
    deadline = time.monotonic() + min(args.timeout, 240.0)
    while time.monotonic() < deadline:
        editor_pid = active_editor_pid(project)
        if editor_pid is not None:
            return editor_pid
        return_code = process.poll()
        if return_code is not None:
            raise RunnerError(f"Unity Editor exited during startup with code {return_code}.")
        time.sleep(0.5)
    raise RunnerError("Timed out while starting the Unity Editor.")


def run_in_batchmode(project: Path, args: argparse.Namespace) -> dict[str, Any]:
    unity = find_unity(project, args.unity)
    request_id = uuid.uuid4().hex
    result_dir = project / RESULT_DIR
    result_dir.mkdir(parents=True, exist_ok=True)
    xml_path = result_dir / f"batch-{request_id}.xml"
    log_path = result_dir / f"batch-{request_id}.log"
    command = [
        str(unity),
        "-batchmode",
        "-nographics",
        "-forgetProjectPath",
        "-projectPath",
        str(project),
        "-runTests",
        "-testPlatform",
        args.mode,
        "-assemblyNames",
        TEST_ASSEMBLY,
        "-testResults",
        str(xml_path),
        "-logFile",
        str(log_path),
    ]
    if args.category:
        command.extend(("-testCategory", ";".join(args.category)))
    if args.test:
        command.extend(("-testFilter", ";".join(args.test)))

    try:
        completed = subprocess.run(command, cwd=project, timeout=args.timeout, check=False)
    except subprocess.TimeoutExpired as exc:
        raise RunnerError(f"Unity batchmode timed out after {args.timeout:g}s. Log: {log_path}") from exc
    if not xml_path.is_file():
        tail = read_tail(log_path, 80)
        detail = f"\n{tail}" if tail else ""
        raise RunnerError(
            f"Unity batchmode returned {completed.returncode} without a test result. Log: {log_path}{detail}"
        )
    return parse_nunit_xml(xml_path, args, request_id, log_path)


def read_tail(path: Path, line_count: int) -> str:
    try:
        lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError:
        return ""
    return "\n".join(lines[-line_count:])


def parse_nunit_xml(
    xml_path: Path, args: argparse.Namespace, request_id: str, log_path: Path
) -> dict[str, Any]:
    root = ET.parse(xml_path).getroot()
    failures: list[dict[str, Any]] = []
    for test_case in root.iter("test-case"):
        if not test_case.attrib.get("result", "").lower().startswith("failed"):
            continue
        failure = test_case.find("failure")
        failures.append(
            {
                "fullName": test_case.attrib.get("fullname", test_case.attrib.get("name", "<unknown>")),
                "resultState": test_case.attrib.get("label", test_case.attrib.get("result", "Failed")),
                "message": child_text(failure, "message"),
                "stackTrace": child_text(failure, "stack-trace"),
                "durationSeconds": float(test_case.attrib.get("duration", 0.0) or 0.0),
            }
        )

    passed = int(root.attrib.get("passed", 0) or 0)
    failed = int(root.attrib.get("failed", 0) or 0)
    skipped = int(root.attrib.get("skipped", 0) or 0)
    inconclusive = int(root.attrib.get("inconclusive", 0) or 0)
    return {
        "id": request_id,
        "state": "completed",
        "outcome": root.attrib.get("result", "Unknown"),
        "mode": args.mode,
        "categories": args.category,
        "testNames": args.test,
        "startedUtc": root.attrib.get("start-time", ""),
        "finishedUtc": root.attrib.get("end-time", ""),
        "durationSeconds": float(root.attrib.get("duration", 0.0) or 0.0),
        "total": passed + failed + skipped + inconclusive,
        "passed": passed,
        "failed": failed,
        "skipped": skipped,
        "inconclusive": inconclusive,
        "failures": failures,
        "message": "",
        "resultFile": str(xml_path),
        "logFile": str(log_path),
    }


def child_text(parent: ET.Element | None, name: str) -> str:
    if parent is None:
        return ""
    child = parent.find(name)
    return "" if child is None or child.text is None else child.text.strip()


def report_result(result: dict[str, Any]) -> int:
    state = result.get("state", "error")
    if state != "completed":
        print(f"ERROR: {result.get('message', 'Unity test request failed.')}", file=sys.stderr)
        return 3

    passed = int(result.get("passed", 0) or 0)
    failed = int(result.get("failed", 0) or 0)
    skipped = int(result.get("skipped", 0) or 0)
    inconclusive = int(result.get("inconclusive", 0) or 0)
    total = int(result.get("total", passed + failed + skipped + inconclusive) or 0)
    duration = float(result.get("durationSeconds", 0.0) or 0.0)
    if total == 0:
        print("ERROR: No tests matched the requested assembly/category/test filters.", file=sys.stderr)
        return 2

    status = "PASS" if failed == 0 and inconclusive == 0 else "FAIL"
    print(
        f"{status}: {total} tests; {passed} passed, {failed} failed, "
        f"{skipped} skipped, {inconclusive} inconclusive ({duration:.2f}s)"
    )
    enabled_operations = result.get("enabledOperationIds", []) or []
    if enabled_operations:
        print(
            f"GoldenPath operations: {len(enabled_operations)} enabled ("
            + ", ".join(enabled_operations)
            + ")"
        )
    for failure in result.get("failures", []) or []:
        print(f"\n- {failure.get('fullName', '<unknown>')}")
        message = str(failure.get("message", "")).strip()
        stack = str(failure.get("stackTrace", "")).strip()
        if message:
            print(f"  {message}")
        if stack:
            print("  " + stack.replace("\n", "\n  "))
    warnings = result.get("warnings", []) or []
    if warnings:
        print(f"\nWARN: {len(warnings)} unique runtime warning(s)")
        for index, warning in enumerate(warnings[:5], start=1):
            message = str(warning.get("message", "")).strip() or "<empty warning>"
            occurrence_count = int(warning.get("occurrenceCount", 1) or 1)
            count_suffix = f" (x{occurrence_count})" if occurrence_count > 1 else ""
            print(f"  {index}. {message}{count_suffix}")
        if len(warnings) > 5:
            print(f"  ... {len(warnings) - 5} more warning(s) are stored in the result JSON.")
    if result.get("resultFile"):
        print(f"\nResult: {result['resultFile']}")
    if result.get("logFile"):
        print(f"Log: {result['logFile']}")
    return 0 if status == "PASS" else 1


def main() -> int:
    for stream in (sys.stdout, sys.stderr):
        reconfigure = getattr(stream, "reconfigure", None)
        if reconfigure is not None:
            reconfigure(encoding="utf-8", errors="replace")

    args = parse_args()
    try:
        project = discover_project(args.project)
        if args.list_categories:
            return list_categories(project)

        source_count = validate_source_encoding(project)
        if args.check_encoding and not args.all and not args.category and not args.test:
            print(f"PASS: {source_count} C# source files are valid UTF-8 with no U+FFFD characters.")
            return 0

        editor_pid = active_editor_pid(project)
        if args.golden_path and args.force_batch:
            raise RunnerError(
                "--golden-path runs the real GameStartScene and does not support --force-batch."
            )
        if args.golden_path and editor_pid is None:
            editor_pid = start_editor(project, args)
        if editor_pid is not None and args.force_batch:
            raise RunnerError(
                "The project is already open in Unity; --force-batch cannot safely open the same project."
            )
        if args.golden_path:
            result = run_golden_path_in_open_editor(project, args, editor_pid)
        elif editor_pid is not None:
            result = run_in_open_editor(project, args, editor_pid)
        else:
            result = run_in_batchmode(project, args)
        return report_result(result)
    except RunnerError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 3
    except (OSError, ET.ParseError, ValueError) as exc:
        print(f"ERROR: {type(exc).__name__}: {exc}", file=sys.stderr)
        return 3


if __name__ == "__main__":
    raise SystemExit(main())
