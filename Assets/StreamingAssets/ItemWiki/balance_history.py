"""ItemWiki 自适应数值历史：本地持久化、旧 Unity 工作台迁移与安全热更新队列。"""

from __future__ import annotations

import json
import math
import os
from pathlib import Path
import re
import time
import uuid
from typing import Any


def valid_number(value: Any) -> bool:
    return type(value) in (int, float) and math.isfinite(value) and abs(value) <= 1_000_000_000


def normalize_path(value: Any) -> str:
    """允许声明过的属性和数组索引，禁止任意路径、原型字段与运行时变换。"""
    path = str(value or "")
    parts = path.split(".")
    if not 1 <= len(parts) <= 20 or any(
        not (part.isidentifier() or (part.isdigit() and len(part) <= 5))
        or part in {"__proto__", "constructor", "prototype"}
        for part in parts
    ):
        raise ValueError("调参字段路径无效")
    if parts[0] in {"weight", "volume", "amount", "durability", "maxDurability"} and len(parts) == 1:
        return path
    if parts[0] == "health" and len(parts) >= 2 and parts[1] in {"hp", "maxHp", "defense"}:
        return path
    if len(parts) >= 4 and parts[0] == "modules" and parts[2] in {"parameters", "data"}:
        if any(part.lower() in {
            "bitdata", "type", "impactkind", "moduleid", "guid", "id", "itemid",
            "position", "rotation", "scale", "color", "transform",
            "localposition", "localeulerangles", "localscale",
        } for part in parts[3:]):
            raise ValueError("此字段属于身份、表现或运行状态，禁止用调参器修改")
        return path
    raise ValueError("此字段不属于可调的物品玩法数值")


def value_at(root: dict[str, Any], path: str) -> Any:
    cursor: Any = root
    for part in path.split("."):
        if isinstance(cursor, dict):
            if part not in cursor:
                raise ValueError("调参字段在当前物品定义中不存在")
            cursor = cursor[part]
        elif isinstance(cursor, list) and part.isdigit():
            index = int(part)
            if index >= len(cursor):
                raise ValueError("调参数组位置已经变化")
            cursor = cursor[index]
        else:
            raise ValueError("调参字段路径已经变化")
    if not valid_number(cursor):
        raise ValueError("调参字段必须为有限数值")
    return float(cursor)


def atomic_json(path: Path, data: dict[str, Any]) -> None:
    """写入 Library 专属持久文件，使用临时文件和原子替换避免中断损坏。"""
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    try:
        with temporary.open("w", encoding="utf-8", newline="") as handle:
            json.dump(data, handle, ensure_ascii=False, indent=2, allow_nan=False)
            handle.write("\n")
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary, path)
    finally:
        if temporary.exists():
            temporary.unlink()


class BalanceHistory:
    """在 Wiki 的 SAVE_LOCK 内调用，本地历史不参与游戏配置和公开分享。"""

    def __init__(self, project_root: Path):
        self.root = project_root / "Library" / "FlatWorldItemWiki"
        self.path = self.root / "balance-history.json"
        self.legacy = project_root / "Assets" / "Editor" / "FlatWorld" / "DataTables" / "Balance" / "BalanceTuningDatabase.asset"
        self.requests = self.root / "balance-live" / "pending"
        self.statuses = self.root / "balance-live" / "statuses"

    def load(self) -> dict[str, Any]:
        if self.path.is_file():
            with self.path.open("r", encoding="utf-8") as handle:
                data = json.load(handle)
            if data.get("schemaVersion") != 1 or not isinstance(data.get("items"), dict):
                raise ValueError("数值调整历史格式无效")
            return data
        # 第一次读取即持久化迁移结果，避免每次切换物品都重新解析数 MB 的旧 YAML。
        data = {"schemaVersion": 1, "items": self._import_legacy()}
        atomic_json(self.path, data)
        return data

    def _import_legacy(self) -> dict[str, Any]:
        """首次从未提交的 Unity .asset 读取旧步长和历史，不触碰旧资源本身。"""
        if not self.legacy.is_file():
            return {}
        contents = self.legacy.read_text(encoding="utf-8-sig")
        items: dict[str, dict[str, Any]] = {}
        for match in re.finditer(r"(?ms)^  - Key: (.*?)\r?\n(.*?)(?=^  - Key: |\Z)", contents):
            old_key, body = match.groups()
            key_parts = old_key.split("|", 3)
            if len(key_parts) != 4 or key_parts[0] != "json":
                continue
            item_id = key_parts[2].lower()
            path = re.sub(r"\[(\d+)\]", r".\1", key_parts[3])
            try:
                path = normalize_path(path)
            except ValueError:
                continue

            def extract(field: str, default: str = "") -> str:
                found = re.search(r"(?m)^    " + re.escape(field) + r": ([^\r\n]*)", body)
                return found.group(1).strip() if found else default

            try:
                step = float(extract("Step", "1"))
                direction = int(extract("LastDirection", "0"))
                streak = int(extract("DirectionStreak", "0"))
            except ValueError:
                continue
            if not valid_number(step) or step <= 0:
                continue
            history = []
            # 老版本 C# 数据中每步保留完整状态；只迁移有合法数值的条目。
            history_text = body.split("    History:", 1)[-1]
            for historical in re.split(r"(?m)^    - Before: ", history_text)[1:]:
                def field(name: str, default: str = "0") -> str:
                    found = re.search(r"(?m)^      " + re.escape(name) + r": ([^\r\n]*)", historical)
                    return found.group(1).strip() if found else default
                try:
                    before = float(historical.splitlines()[0].strip())
                    after = float(field("After"))
                    step_before = float(field("StepBefore", str(step)))
                    step_after = float(field("StepAfter", str(step)))
                    direction_before = int(field("DirectionBefore"))
                    streak_before = int(field("StreakBefore"))
                    if all(valid_number(v) for v in (before, after, step_before, step_after)):
                        history.append({
                            "before": before, "after": after,
                            "stepBefore": step_before, "stepAfter": step_after,
                            "directionBefore": direction_before, "streakBefore": streak_before,
                            "directionAfter": 0, "streakAfter": 0, "feedback": "旧工作台记录",
                            "timestamp": field("Timestamp", ""),
                        })
                except ValueError:
                    continue
            items.setdefault(item_id, {})[path] = {
                "step": step, "direction": direction, "streak": streak,
                "history": history[-300:],
            }
        return items

    def item_state(self, item_id: str) -> dict[str, Any]:
        if not item_id or len(item_id) > 200:
            raise ValueError("物品 ID 无效")
        return self.load()["items"].get(item_id.lower(), {})

    def set_step(self, item_id: str, path: str, step: float) -> dict[str, Any]:
        """单独持久化步长，修改步长不会触碰游戏源数据或增加虚假的操作历史。"""
        if not item_id or len(item_id) > 200:
            raise ValueError("物品 ID 无效")
        path = normalize_path(path)
        if not valid_number(step) or not 0.0001 <= step <= 1000000:
            raise ValueError("步长需要介于 0.0001 到 1000000")
        data = self.load()
        current = data["items"].setdefault(item_id.lower(), {}).setdefault(path, {})
        current["step"] = float(step)
        current.setdefault("direction", 0)
        current.setdefault("streak", 0)
        current.setdefault("history", [])
        atomic_json(self.path, data)
        return current

    def validate_change(
        self, change: Any, item_id: str, before: dict[str, Any],
        after: dict[str, Any],
    ) -> tuple[dict[str, Any], dict[str, Any]]:
        if not isinstance(change, dict) or change.get("kind") not in {"adjust", "manual", "undo"}:
            raise ValueError("数值调整事件无效")
        path = normalize_path(change.get("fieldPath"))
        before_value, after_value = value_at(before, path), value_at(after, path)
        if before_value == after_value:
            raise ValueError("当前数值没有发生改变")
        if not valid_number(change.get("before")) or not valid_number(change.get("after")):
            raise ValueError("数值调整结果无效")
        if not math.isclose(before_value, change["before"], abs_tol=1e-5, rel_tol=1e-7) or \
           not math.isclose(after_value, change["after"], abs_tol=1e-5, rel_tol=1e-7):
            raise ValueError("游戏配置变化与调参事件不一致")
        numbers = ("stepBefore", "stepAfter")
        if any(not valid_number(change.get(name)) or change[name] <= 0 for name in numbers):
            raise ValueError("调参步长必须为有限正数")
        direction = change.get("direction")
        if type(direction) is not int or direction not in {-1, 0, 1}:
            raise ValueError("调参方向无效")

        data = self.load()
        rows = data["items"].setdefault(item_id.lower(), {})
        current = rows.get(path, {})
        history = current.get("history", [])
        if change["kind"] == "undo":
            if not history or not math.isclose(history[-1]["after"], before_value, abs_tol=1e-5):
                raise ValueError("历史与当前配置不一致，不能撤销，请重新读取")
            if not math.isclose(history[-1]["before"], after_value, abs_tol=1e-5):
                raise ValueError("撤销只能恢复上一数值")
        else:
            if history and not math.isclose(history[-1]["after"], before_value, abs_tol=1e-5):
                # 用户可能在外部或双击输入编辑了同一字段；保存允许继续但重建趋势分支。
                history = []
            history = list(history)
            history.append({
                "before": before_value, "after": after_value,
                "stepBefore": change["stepBefore"], "stepAfter": change["stepAfter"],
                "directionBefore": int(current.get("direction", 0)),
                "streakBefore": int(current.get("streak", 0)),
                "directionAfter": direction, "streakAfter": max(0, int(change.get("streak", 0))),
                "feedback": str(change.get("feedback", ""))[:48],
                "timestamp": time.strftime("%Y-%m-%d %H:%M:%S"),
            })
        if change["kind"] == "undo":
            prior = history.pop()
            rows[path] = {
                "step": prior["stepBefore"], "direction": prior["directionBefore"],
                "streak": prior["streakBefore"], "history": history,
            }
        else:
            rows[path] = {
                "step": change["stepAfter"], "direction": direction,
                "streak": max(0, min(1000, int(change.get("streak", 0)))),
                "history": history[-300:],
            }
        return data, {"itemId": item_id, "fieldPath": path, "value": after_value, "kind": change["kind"]}

    def committed(self, data: dict[str, Any], request: dict[str, Any]) -> str:
        """仅源文件成功写入后才更新历史，并通知 Unity Editor 执行热更新。"""
        atomic_json(self.path, data)
        request_id = uuid.uuid4().hex
        atomic_json(self.requests / (request_id + ".json"), {**request, "requestId": request_id})
        return request_id

    def live_status(self, request_id: str) -> dict[str, Any]:
        if not re.fullmatch(r"[0-9a-f]{32}", request_id):
            raise ValueError("热更新请求 ID 无效")
        status_file = self.statuses / (request_id + ".json")
        if status_file.exists():
            with status_file.open("r", encoding="utf-8") as handle:
                return json.load(handle)
        return {"requestId": request_id, "state": "pending"}
