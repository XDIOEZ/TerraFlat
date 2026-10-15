"""按系统选择快速 Lua 规则或真实场景清单，复用当前 Unity Editor。"""
import argparse
import json
import os
import fnmatch
from pathlib import Path
import sys
import time
import uuid

ROOT = Path(__file__).resolve().parents[2]
PIPELINE = ROOT / "Library" / "FlatWorldGameplayTests" / "Pipeline"
CATALOG = ROOT / "Assets" / "StreamingAssets" / "GameplayTests" / "systems.catalog.json"
RULES_SUITE = "GameplayTests/all-systems-rules.json"
GAMEPLAY_SUITE = "GameplayTests/all-systems-gameplay.json"


def load_catalog():
    return json.loads(CATALOG.read_text(encoding="utf-8"))["systems"]


def choose_systems(ids, changed_paths):
    systems = load_catalog()
    known = {system["id"] for system in systems}
    selected = set(ids or [])
    unknown = selected - known
    if unknown:
        raise ValueError(f"未知系统：{', '.join(sorted(unknown))}；使用 --list-systems 查看")
    unmatched = []
    for path in changed_paths or []:
        normalized = path.replace("\\", "/").removeprefix("./")
        if Path(normalized).is_absolute():
            normalized = Path(normalized).resolve().relative_to(ROOT.resolve()).as_posix()
        matched = {system["id"] for system in systems
                   if any(fnmatch.fnmatchcase(normalized, pattern) for pattern in system["sourcePaths"])}
        if not matched:
            unmatched.append(normalized)
        selected.update(matched)
    if unmatched:
        raise ValueError("修改路径尚未登记系统归属，请补 systems.catalog.json：" + ", ".join(unmatched))
    if changed_paths:
        # 按修改路径回归时也覆盖使用该规则的系统；显式系统选择仍保持精确范围。
        if "testing" in selected:
            selected = known
        else:
            while True:
                affected = {system["id"] for system in systems
                            if selected.intersection(system.get("dependsOn", []))}
                if affected.issubset(selected):
                    break
                selected.update(affected)
    return [system for system in systems if system["id"] in selected]


def wait_json(path: Path, deadline: float):
    while time.monotonic() < deadline:
        if path.is_file():
            try:
                return json.loads(path.read_text(encoding="utf-8"))
            except (OSError, json.JSONDecodeError):
                pass  # 文件正在被 Editor 原子替换，下一轮再读。
        time.sleep(0.5)
    raise TimeoutError(f"等待 Unity Editor 报告超时：{path}")


def run(suite: str, editor_timeout: float, run_timeout: float, case_ids=None, playmode_only=False):
    request_id = uuid.uuid4().hex
    inbox = PIPELINE / "inbox"
    outbox = PIPELINE / "outbox"
    inbox.mkdir(parents=True, exist_ok=True)
    outbox.mkdir(parents=True, exist_ok=True)
    request = inbox / f"{request_id}.json"
    receipt = outbox / f"{request_id}.json"
    temporary = inbox / f"{request_id}.tmp"
    request_data = {"suitePath": suite}
    if case_ids is not None:
        request_data["caseIds"] = case_ids
    if playmode_only:
        request_data["playModeOnly"] = True
    temporary.write_text(json.dumps(request_data), encoding="utf-8")
    os.replace(temporary, request)
    try:
        acknowledged = wait_json(receipt, time.monotonic() + editor_timeout)
    finally:
        # Unity 未接单就及时撤回请求，避免下次打开 Editor 时意外执行过期清单。
        request.unlink(missing_ok=True)
    receipt.unlink(missing_ok=True)
    if not acknowledged.get("ok"):
        print(json.dumps(acknowledged, ensure_ascii=False, indent=2))
        return 2

    print(f"[FlatWorld] Editor 已接单：{acknowledged['runId']}，等待选定测试模式...", flush=True)
    deadline = time.monotonic() + run_timeout
    report = wait_json(Path(acknowledged["reportPath"]), deadline)
    while report.get("status") == "running":
        if time.monotonic() >= deadline:
            raise TimeoutError(f"测试超过执行上限：{acknowledged['reportPath']}")
        time.sleep(0.5)
        report = wait_json(Path(acknowledged["reportPath"]), deadline)
    summary = {
        "runId": report.get("runId"), "status": report.get("status"),
        "reason": report.get("reason"), "reportPath": report.get("reportPath"),
        "gameplayReportPath": report.get("gameplayReportPath"),
        "runDirectory": report.get("runDirectory"), "summaryPath": report.get("summaryPath"),
        "packagePath": report.get("packagePath"), "screenshotPaths": report.get("screenshotPaths", []),
        "artifactError": report.get("artifactError"),
        "executionMode": report.get("executionMode"), "playModeOnly": report.get("playModeOnly"),
        "rules": report.get("rules"),
        "editMode": report.get("editMode"), "playMode": report.get("playMode")
    }
    print(json.dumps(summary, ensure_ascii=False, indent=2))
    return 0 if report.get("status") == "passed" else 1


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="FlatWorld 按系统运行 Lua 规则和场景回归")
    parser.add_argument("suite", nargs="?", help="指定清单；省略时执行全部系统快速规则")
    parser.add_argument("--editor-timeout", type=float, default=45,
                        help="Editor 收到本地请求的等待秒数")
    parser.add_argument("--run-timeout", type=float, default=900,
                        help="每轮流水线执行上限秒数")
    parser.add_argument("--cases", nargs="+", metavar="CASE_ID",
                        help="只执行指定的一个或多个 Lua 用例 ID，不修改源清单")
    parser.add_argument("--systems", nargs="+", metavar="SYSTEM_ID", help="选择系统，默认只运行快速规则")
    parser.add_argument("--changed", nargs="+", metavar="PATH", help="根据修改文件路径选择系统及使用它的系统")
    parser.add_argument("--list-systems", action="store_true", help="列出系统、规则清单和场景入口，不启动测试")
    parser.add_argument("--integration", action="store_true", help="系统规则通过后再执行所选系统的真实场景清单")
    parser.add_argument("--playmode-only", action="store_true",
                        help="只运行 PlayMode Lua，跳过快速规则及 EditMode 安全前置；默认选择全部场景案例")
    parser.add_argument("--plan", action="store_true", help="只显示系统选择和覆盖缺口，不向 Editor 提交请求")
    args = parser.parse_args()
    try:
        if args.list_systems:
            for system in load_catalog():
                print(f"{system['id']}: {system['name']} | {system['rulesSuite']} | 场景={','.join(system.get('integrationSuites', [])) or '暂无'}")
            sys.exit(0)
        if args.systems or args.changed:
            if args.cases or args.suite:
                raise ValueError("显式清单/--cases 与 --systems/--changed 不混用；系统规则用系统 ID 选择")
            chosen = choose_systems(args.systems, args.changed)
            if not chosen:
                raise ValueError("没有选中任何系统")
            print("[FlatWorld] 选中系统：" + ", ".join(system["id"] for system in chosen), flush=True)
            selected_cases = [case_id for system in chosen for case_id in system["ruleCaseIds"]]
            selected_live_cases = list(dict.fromkeys(case_id for system in chosen
                                      for case_id in system.get("integrationCaseIds", [])))
            missing = [system["id"] for system in chosen if not system.get("integrationCaseIds")]
            if args.plan:
                print(json.dumps({"playModeOnly": args.playmode_only, "rulesSuite": RULES_SUITE,
                                  "ruleCaseIds": [] if args.playmode_only else selected_cases,
                                  "integrationSuite": GAMEPLAY_SUITE, "integrationCaseIds": selected_live_cases,
                                  "systemsWithoutIntegration": missing,
                                  "coverageDocuments": [system["coverageDocument"] for system in chosen]},
                                 ensure_ascii=False, indent=2))
                sys.exit(0)
            if args.playmode_only:
                if not selected_live_cases:
                    raise ValueError("选中系统没有 PlayMode Lua 案例，未运行任何测试。")
                code = run(GAMEPLAY_SUITE, args.editor_timeout, args.run_timeout,
                           selected_live_cases, playmode_only=True)
                if missing:
                    print("[FlatWorld] 以下系统没有场景清单：" + ", ".join(missing), file=sys.stderr)
                    code = code or 4
            else:
                code = run(RULES_SUITE, args.editor_timeout, args.run_timeout, selected_cases)
            if not args.playmode_only and code == 0 and args.integration:
                if selected_live_cases:
                    # 所有现场案例合并到一轮隔离世界，避免逐系统重建资源与地形。
                    code = run(GAMEPLAY_SUITE, args.editor_timeout, args.run_timeout, selected_live_cases)
                if missing:
                    print("[FlatWorld] 以下系统没有场景清单，不能视为关键流程已覆盖：" + ", ".join(missing), file=sys.stderr)
                    code = code or 4
            sys.exit(code)
        if args.integration:
            raise ValueError("--integration 需要配合 --systems 或 --changed")
        if args.plan:
            raise ValueError("--plan 需要配合 --systems 或 --changed")
        suite = args.suite or (GAMEPLAY_SUITE if args.playmode_only else RULES_SUITE)
        sys.exit(run(suite, args.editor_timeout, args.run_timeout, args.cases,
                     playmode_only=args.playmode_only))
    except (TimeoutError, OSError, KeyError, ValueError) as failure:
        print(f"[FlatWorld] {failure}", file=sys.stderr)
        sys.exit(3)
