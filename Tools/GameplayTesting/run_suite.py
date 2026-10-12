"""一次调用：复用当前 Unity Editor，先跑 EditMode，再跑一轮 GameplayTestScene PlayMode。"""
import argparse
import json
import os
from pathlib import Path
import sys
import time
import uuid

ROOT = Path(__file__).resolve().parents[2]
PIPELINE = ROOT / "Library" / "FlatWorldGameplayTests" / "Pipeline"


def wait_json(path: Path, deadline: float):
    while time.monotonic() < deadline:
        if path.is_file():
            try:
                return json.loads(path.read_text(encoding="utf-8"))
            except (OSError, json.JSONDecodeError):
                pass  # 文件正在被 Editor 原子替换，下一轮再读。
        time.sleep(0.5)
    raise TimeoutError(f"等待 Unity Editor 报告超时：{path}")


def run(suite: str, editor_timeout: float, run_timeout: float):
    request_id = uuid.uuid4().hex
    inbox = PIPELINE / "inbox"
    outbox = PIPELINE / "outbox"
    inbox.mkdir(parents=True, exist_ok=True)
    outbox.mkdir(parents=True, exist_ok=True)
    request = inbox / f"{request_id}.json"
    receipt = outbox / f"{request_id}.json"
    temporary = inbox / f"{request_id}.tmp"
    temporary.write_text(json.dumps({"suitePath": suite}), encoding="utf-8")
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

    print(f"[FlatWorld] Editor 已接单：{acknowledged['runId']}，等待 EditMode → PlayMode...", flush=True)
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
        "editMode": report.get("editMode"), "playMode": report.get("playMode")
    }
    print(json.dumps(summary, ensure_ascii=False, indent=2))
    return 0 if report.get("status") == "passed" else 1


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="FlatWorld Unity 同进程两阶段回归流水线")
    parser.add_argument("suite", nargs="?", default="GameplayTests/quick-suite.json")
    parser.add_argument("--editor-timeout", type=float, default=45,
                        help="Editor 收到本地请求的等待秒数")
    parser.add_argument("--run-timeout", type=float, default=900,
                        help="本轮 EditMode+PlayMode 流水线执行上限秒数")
    args = parser.parse_args()
    try:
        sys.exit(run(args.suite, args.editor_timeout, args.run_timeout))
    except (TimeoutError, OSError, KeyError) as failure:
        print(f"[FlatWorld] {failure}", file=sys.stderr)
        sys.exit(3)
