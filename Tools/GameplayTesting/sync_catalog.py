"""根据各系统清单更新总入口和路由索引；仅整理文件，不执行测试或连接 Unity。"""
import json
from pathlib import Path
import uuid

ROOT = Path(__file__).resolve().parents[2]
STREAMING = ROOT / "Assets" / "StreamingAssets"
TESTS = STREAMING / "GameplayTests"


def read(relative):
    return json.loads((STREAMING / relative).read_text(encoding="utf-8-sig"))


def write(path, data):
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    metadata = Path(str(path) + ".meta")
    if not metadata.exists():
        metadata.write_text("fileFormatVersion: 2\nguid: " + uuid.uuid4().hex +
                            "\nTextScriptImporter:\n  externalObjects: {}\n  userData:\n"
                            "  assetBundleName:\n  assetBundleVariant:\n", encoding="utf-8")


def collect(suite_path, destination, known):
    suite = read(suite_path)
    result = []
    for case in suite["cases"]:
        case_id = case["id"]
        key = case_id.lower()
        if key in known:
            if known[key] != case:
                raise ValueError("跨清单重复 ID 对应不同案例：" + case_id)
        else:
            known[key] = case
            destination.append(case)
        if case.get("enabled", True):
            result.append(case_id)
    return result


def sync():
    catalog_path = TESTS / "systems.catalog.json"
    catalog = json.loads(catalog_path.read_text(encoding="utf-8"))
    rule_cases, live_cases = [], []
    rule_ids, live_ids = {}, {}
    for system in catalog["systems"]:
        suite_path = system["rulesSuite"]
        suite = read(suite_path)
        if suite.get("executionMode") != "rules":
            raise ValueError("系统规则清单必须声明 rules 模式：" + suite_path)
        system["ruleCaseIds"] = collect(suite_path, rule_cases, rule_ids)
        folder = (STREAMING / suite_path).parent
        own_live = folder / "gameplay-suite.json"
        if (folder / "gameplay.lua").exists() and not own_live.exists():
            write(own_live, {"schemaVersion": 1, "executionMode": "gameplay",
                  "buildShowroom": False, "continueAfterFailure": True, "cases": [{
                  "id": system["id"] + "_gameplay", "name": system["name"] + "：实际流程",
                  "luaPath": (folder / "gameplay.lua").relative_to(STREAMING).as_posix(), "enabled": True}]})
        integration_suites = list(system.get("integrationSuites", []))
        if own_live.exists():
            relative = own_live.relative_to(STREAMING).as_posix()
            integration_suites = [relative] + [path for path in integration_suites if path != relative]
        system["integrationSuites"] = integration_suites
        mapped = []
        for integration in integration_suites:
            if read(integration).get("executionMode", "gameplay") != "gameplay":
                raise ValueError("现场入口必须使用 gameplay 模式：" + integration)
            mapped.extend(collect(integration, live_cases, live_ids))
        system["integrationCaseIds"] = list(dict.fromkeys(mapped))
        system["rulesCoverage"] = "partial"
        system["integrationCoverage"] = "partial" if mapped else "missing"
        # 新主题优先放进本系统；超过十份时应合并主题，避免无数碎片入口。
        if len(list(folder.rglob("*.lua"))) > 10:
            raise ValueError("系统 Lua 超过十份，请合并主题：" + system["id"])
    for name, mode, cases in (("all-systems-rules.json", "rules", rule_cases),
                              ("all-systems-gameplay.json", "gameplay", live_cases)):
        write(TESTS / name, {"schemaVersion": 1, "executionMode": mode,
              "buildShowroom": False, "continueAfterFailure": True, "cases": cases})
    write(catalog_path, catalog)
    matrix = ["# 游戏系统测试索引", "",
              f"当前按源码职责统计 {len(catalog['systems'])} 个测试域，包含玩法系统、平台服务和测试框架。系统之间会共享模块，不按文件夹数量机械计数。",
              "", "每个系统均已有规则入口；以下案例数按当前清单统计，均为部分覆盖，不代表执行结果。共享现场集在总清单中只执行一次；具体断言和缺口以对应 README 为准。",
              "", "| 分组 | 系统 ID | 系统与覆盖说明 | 规则案例数 | 现场案例数 |",
              "| --- | --- | --- | ---: | ---: |"]
    for system in catalog["systems"]:
        document = Path(system["coverageDocument"]).relative_to("GameplayTests").as_posix()
        matrix.append(f"| {system['group']} | `{system['id']}` | [{system['name']}]({document}) | "
                      f"{len(system['ruleCaseIds'])} | {len(system['integrationCaseIds'])} |")
    matrix.extend(["", "现场案例数含共享机械/气液/太空清单，同一案例在总场景清单中只登记一次。零代表目前尚无现场入口，应补对应实际流程。",
                   "", "系统归属、依赖和案例映射以 [systems.catalog.json](systems.catalog.json) 为源；新增主题后运行 `python Tools/GameplayTesting/sync_catalog.py` 更新总入口。",
                   "", "维护与执行方法见 [README](README.md)。", ""])
    index = TESTS / "系统索引.md"
    index.write_text("\n".join(matrix), encoding="utf-8")
    meta = Path(str(index) + ".meta")
    if not meta.exists():
        meta.write_text("fileFormatVersion: 2\nguid: " + uuid.uuid4().hex +
                        "\nTextScriptImporter:\n  externalObjects: {}\n  userData:\n"
                        "  assetBundleName:\n  assetBundleVariant:\n", encoding="utf-8")
    print(f"已整理 {len(catalog['systems'])} 个系统、{len(rule_cases)} 个规则案例、{len(live_cases)} 个现场案例；未执行测试。")


if __name__ == "__main__":
    sync()
