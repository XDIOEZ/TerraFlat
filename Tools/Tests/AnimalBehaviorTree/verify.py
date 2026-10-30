"""编译正式树执行器与同一采样夹具，并用项目 XLua 运行实际 Lua 断言。"""
from __future__ import annotations

import ctypes
import json
from pathlib import Path
import re
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / ".codex-tmp/animal-behavior-tree-tests"
ENGINE = ROOT / "Assets/5_Scripts/5-3_GamePlay/Entities/AI/AnimalBehaviorTreeRuntime.cs"
SCENARIOS = ROOT / "Assets/5_Scripts/5-3_GamePlay/Development/Testing/GameplayTestAnimalBehaviorTreeScenarios.cs"
LUA = ROOT / "Assets/StreamingAssets/GameplayTests/Systems/AI/behavior-tree.lua"
NEWTONSOFT = ROOT / "Library/PackageCache/com.unity.nuget.newtonsoft-json@3.2.1/Runtime/Newtonsoft.Json.dll"


def extract_block(source, signature):
    if source.count(signature) != 1:
        raise ValueError("生产方法签名不唯一或已变化：" + signature)
    start = source.index(signature)
    cursor = source.index("{", start) + 1
    depth = 1
    while depth and cursor < len(source):
        depth += (source[cursor] == "{") - (source[cursor] == "}")
        cursor += 1
    if depth:
        raise ValueError("生产方法缺少闭括号：" + signature)
    return source[start:cursor]


def extract_expression(source, signature):
    if source.count(signature) != 1:
        raise ValueError("生产方法签名不唯一或已变化：" + signature)
    start = source.index(signature)
    return source[start:source.index(";", start) + 1]


def lua_literal(value):
    if value is None:
        return "nil"
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, (int, float)):
        return str(value)
    if isinstance(value, str):
        return json.dumps(value, ensure_ascii=False)
    if isinstance(value, list):
        return "{" + ",".join(lua_literal(item) for item in value) + "}"
    return "{" + ",".join("[" + lua_literal(key) + "]=" + lua_literal(item)
                           for key, item in value.items()) + "}"


def compile_and_capture():
    loader = (ROOT / "Assets/5_Scripts/5-3_GamePlay/Entities/Item/Definitions/ItemDefinitionCatalogLoader.cs").read_text(encoding="utf-8-sig")
    operations = (ROOT / "Assets/5_Scripts/5-3_GamePlay/Development/Testing/GameplayTestSystemProbe.AnimalTreeCatalog.cs").read_text(encoding="utf-8-sig")
    methods = [extract_block(loader, signature) for signature in (
        "private static JObject CreateCombinedCatalog(IEnumerable<JObject> roots)",
        "private static JObject ParseCatalogRoot(string json, string sourceName)",
        "internal static List<JObject> ResolveDefinitionObjects(IEnumerable<string> catalogJsons)",
        "private static List<JObject> ResolveDefinitionObjects(JObject root)",
        "private static JObject ResolveOne(",
        "private static void RemoveReplacedModuleBodies(JObject inherited, JObject source)",
        "private static void RemoveInheritedModules(JObject result, JObject source, string id)",
    )]
    generated = OUT / "ProductionDefinitionInheritance.cs"
    generated.write_text("using System;\nusing System.IO;\nusing System.Linq;\nusing System.Collections.Generic;\n"
                         "using Newtonsoft.Json;\nusing Newtonsoft.Json.Linq;\n"
                         "public static class ItemDefinitionCatalogLoader { public const int SupportedSchemaVersion = 1;\n" +
                         "\n".join(methods) + "\n}\npublic static partial class GameplayTestSystemProbe {\n" +
                         extract_block(operations, "private static JObject EntityAnimalTreeModuleInheritance(JObject args)") + "\n" +
                         extract_block(operations, "private static JObject EntityAnimalTreeMappingInputs(JObject args)") +
                         "\n}\n", encoding="utf-8")
    actor_loader = (ROOT / "Assets/5_Scripts/5-3_GamePlay/Entities/AI/Definitions/ActorDefinitionCatalogLoader.cs").read_text(encoding="utf-8-sig")
    forage_source = (ROOT / "Assets/5_Scripts/5-3_GamePlay/Entities/AI/AnimalLandSurvivalCapabilities.cs").read_text(encoding="utf-8-sig")
    flight_nodes = (ROOT / "Assets/5_Scripts/5-3_GamePlay/Entities/AI/AnimalSpecialMovementBehaviorNodes.Flight.cs").read_text(encoding="utf-8-sig")
    flight_hunt = (ROOT / "Assets/5_Scripts/5-3_GamePlay/Entities/AI/Mod_AnimalFlightMovement.AquaticPredation.cs").read_text(encoding="utf-8-sig")
    ecs_source = (ROOT / "Assets/5_Scripts/5-3_GamePlay/Entities/AIECS/Gameplay/AiecsDefinitionCompiler.cs").read_text(encoding="utf-8-sig")
    ecs_probe = (ROOT / "Assets/5_Scripts/5-3_GamePlay/Entities/AIECS/Gameplay/AiecsBehaviorTreeParameterTestProbe.cs").read_text(encoding="utf-8-sig")
    combat_source = (ROOT / "Assets/5_Scripts/Shared/Combat/CombatContext.cs").read_text(encoding="utf-8-sig")
    combat_bridge = (ROOT / "Assets/5_Scripts/5-3_GamePlay/Entities/Combat/GameplayCombatBridge.cs").read_text(encoding="utf-8-sig")
    ecs_generated = OUT / "ProductionAiecsMapping.cs"
    ecs_generated.write_text(
        "using System;\nusing System.Linq;\nusing System.Collections.Generic;\nusing Newtonsoft.Json;\n"
        "using Newtonsoft.Json.Linq;\nusing Unity.Mathematics;\n" +
        "public sealed partial class ForagingDecisionFixture {\n" +
        extract_block(forage_source, "public bool TryChooseMeal(bool alreadyEating, out bool withinEatDistance)") + "\n}\n" +
        "public static partial class AnimalSpecialMovementBehaviorNodes {\n" +
        extract_block(flight_nodes, "private sealed class AquaticHuntActionNode : AnimalBehaviorTreeNode") + "\n}\n" +
        "public partial class Mod_AnimalFlightMovement {\n" +
        extract_expression(flight_hunt, "public void CancelAquaticHunt()") + "\n" +
        extract_block(flight_hunt, "private void ResetFishHunting(bool releaseCaptured)") + "\n}\n" +
        "public static partial class ActorDefinitionCatalogLoader {\n" +
        extract_block(actor_loader, "private static void ApplySharedBehaviorSubtrees(IEnumerable<JObject> sources, JObject shared)") + "\n}\n" +
        "public static class GameplayCombatBridge {\n" +
        extract_expression(combat_bridge, "public static float4 Values(CombatDamage damage)") + "\n}\n" +
        "namespace FlatWorld.Combat { public static class CombatRules {\n" +
        extract_expression(combat_source, "public static int BleedingTier(float4 actualLoss, bool hasBlood, bool alive, float random)") + "\n" +
        extract_block(combat_source, "public static int BleedingTier(float edged, bool hasBlood, bool alive, float random)") + "\n} }\n" +
        "namespace FlatWorld.AIECS.Gameplay {\n" +
        extract_block(ecs_source, "public struct AiecsBehaviorParameterSnapshot") + "\npublic static class AiecsDefinitionCompiler {\n" +
        "\n".join(extract_block(ecs_source, signature) for signature in (
            "public static AiecsBehaviorParameterSnapshot CompileBehaviorParameters(",
            "public static JObject MergeConfiguredModuleParameters(",
            "private static float FindTreeConditionNumber(",
            "private static bool TryFindTreeConditionNumber(",
            "private static JToken ResolveTreeArgument(",
        )) + "\n" +
        "\n".join(extract_expression(ecs_source, signature) for signature in (
            "private static float Number(", "private static float Speed(")) + "\n}\n" +
        "public static class AiecsBehaviorTreeParameterTestProbe {\n" +
        extract_block(ecs_probe, "private static JObject Capture(JObject args)") + "\n" +
        extract_expression(ecs_probe, "private static AiecsBehaviorParameterSnapshot Compile(") + "\n" +
        extract_block(ecs_probe, "private static JObject Snapshot(") + ";\n" +
        "public static JObject Run(JObject args) => Capture(args);\n} }\n", encoding="utf-8")
    project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
    props = ET.SubElement(project, "PropertyGroup")
    for key, value in {"OutputType": "Exe", "TargetFramework": "net8.0", "ImplicitUsings": "enable",
                       "Nullable": "disable", "EnableDefaultCompileItems": "false"}.items():
        ET.SubElement(props, key).text = value
    sources = ET.SubElement(project, "ItemGroup")
    for path in (ENGINE, SCENARIOS, generated, ecs_generated, Path(__file__).with_name("Harness.cs"),
                 Path(__file__).with_name("OfflineRuntimeStubs.cs"),
                 Path(__file__).with_name("ForagingDecisionFixture.cs"),
                 Path(__file__).with_name("AquaticHuntCleanupFixture.cs"),
                 ROOT / "Assets/5_Scripts/5-1_Data/CoreData/GameValue_float.cs",
                 ROOT / "Assets/5_Scripts/5-3_GamePlay/Entities/Combat/DamageType.cs"):
        ET.SubElement(sources, "Compile", Include=str(path))
    reference = ET.SubElement(sources, "Reference", Include="Newtonsoft.Json")
    ET.SubElement(reference, "HintPath").text = str(NEWTONSOFT)
    project_path = OUT / "AnimalBehaviorTreeTests.csproj"
    ET.ElementTree(project).write(project_path, encoding="utf-8", xml_declaration=True)
    build = subprocess.run(["dotnet", "build", str(project_path), "--configuration", "Release", "-nologo", "-v:q"],
                           cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace")
    (OUT / "build.log").write_text(build.stdout + build.stderr, encoding="utf-8")
    if build.returncode:
        raise RuntimeError(build.stdout + build.stderr)
    actors = ROOT / "Assets/StreamingAssets/GameConfig/Actors"
    result = subprocess.run(["dotnet", str(OUT / "bin/Release/net8.0/AnimalBehaviorTreeTests.dll"),
                             str(actors / "definitions"), str(actors / "behavior-subtrees.json")],
                            cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if result.returncode:
        raise RuntimeError(result.stdout + result.stderr)
    samples = json.loads(result.stdout)
    (OUT / "samples.json").write_text(json.dumps(samples, ensure_ascii=False, indent=2), encoding="utf-8")
    return samples


def verify_actual_lua(samples):
    library = ctypes.CDLL(str(ROOT / "Assets/Plugins/x86_64/xlua.dll"))
    pointer, integer = ctypes.c_void_p, ctypes.c_int
    signatures = {
        "luaL_newstate": (pointer, []), "luaL_openlibs": (None, [pointer]), "lua_close": (None, [pointer]),
        "lua_tolstring": (pointer, [pointer, integer, ctypes.POINTER(ctypes.c_size_t)]),
        "xluaL_loadbuffer": (integer, [pointer, ctypes.c_char_p, integer, ctypes.c_char_p]),
        "lua_pcall": (integer, [pointer, integer, integer, integer]),
    }
    for name, (restype, arguments) in signatures.items():
        function = getattr(library, name)
        function.restype, function.argtypes = restype, arguments

    def evaluate(state, source, name):
        encoded = source.encode("utf-8")
        if library.xluaL_loadbuffer(state, encoded, len(encoded), name.encode("utf-8")) or library.lua_pcall(state, 0, 0, 0):
            length = ctypes.c_size_t()
            address = library.lua_tolstring(state, -1, ctypes.byref(length))
            raise RuntimeError(ctypes.string_at(address, length.value).decode("utf-8") if address else "XLua 执行失败")

    state = library.luaL_newstate()
    if not state:
        raise RuntimeError("无法创建 XLua 状态")
    try:
        library.luaL_openlibs(state)
        evaluate(state, "samples=" + lua_literal(samples) + "\n" + """
            checked = 0
            test = {system = function(system, operation, arguments, validator, label)
                assert(system == 'ai', label)
                if operation == 'animal_tree_catalog' or operation == 'animal_tree_animation' then return end
                validator(assert(samples[operation], '缺少正式树采样：' .. operation))
                checked = checked + 1
            end}
        """, "animal-tree-offline-context")
        evaluate(state, LUA.read_text(encoding="utf-8-sig"), str(LUA))
        evaluate(state, "assert(checked == 7, '行为树 Lua 场景数量不符')", "animal-tree-count")
        evaluate(state, """
            checkedTransitions = 0
            test.system = function(system, operation, arguments, validator, label)
                if system == 'ai' and operation == 'state_machine' then
                    validator(samples[arguments.blocked and 'state_machine_blocked' or 'state_machine_open'])
                    checkedTransitions = checkedTransitions + 1
                end
            end
        """, "animal-tree-transition-context")
        rules = ROOT / "Assets/StreamingAssets/GameplayTests/Systems/AI/rules.lua"
        evaluate(state, rules.read_text(encoding="utf-8-sig"), str(rules))
        evaluate(state, "assert(checkedTransitions == 2, '树切换 Lua 场景数量不符')", "animal-tree-transition-count")
    finally:
        library.lua_close(state)
    print("PASS 9 actual XLua scenarios against production tree, inheritance, ECS mapping and combat code")


def verify_actor_tree_json():
    ai = ROOT / "Assets/5_Scripts/5-3_GamePlay/Entities/AI"
    sources = [ai / "AnimalBehaviorTreeNodeCatalog.cs", ai / "AnimalLandBehaviorNodes.cs",
               ai / "AnimalTemperatureSafetyBehaviorNodes.cs", *ai.glob("AnimalSpecialMovementBehaviorNodes*.cs")]
    registered = {"actions": set(), "conditions": set()}
    for source in sources:
        text = source.read_text(encoding="utf-8-sig")
        for kind, name in re.findall(r'AnimalBehaviorTreeRegistry\.Register(Action|Condition)\(\s*"([^"\n]+)"', text):
            registered["actions" if kind == "Action" else "conditions"].add(name)
    if not registered["actions"] or not registered["conditions"]:
        raise RuntimeError("未找到生产节点注册名")
    names = OUT / "registered-nodes.json"
    names.write_text(json.dumps({key: sorted(value) for key, value in registered.items()}, ensure_ascii=False), encoding="utf-8")
    actors = ROOT / "Assets/StreamingAssets/GameConfig/Actors"
    result = subprocess.run(["dotnet", str(OUT / "bin/Release/net8.0/AnimalBehaviorTreeTests.dll"), "catalog",
                             str(actors / "definitions"), str(actors / "behavior-subtrees.json"), str(names)],
                            cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if result.returncode:
        raise RuntimeError(result.stdout + result.stderr)
    print(result.stdout.strip())


def verify_foraging_thresholds():
    result = subprocess.run(["dotnet", str(OUT / "bin/Release/net8.0/AnimalBehaviorTreeTests.dll"), "foraging"],
                            cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if result.returncode:
        raise RuntimeError(result.stdout + result.stderr)
    print(result.stdout.strip())


def verify_aquatic_hunt_cleanup():
    result = subprocess.run(["dotnet", str(OUT / "bin/Release/net8.0/AnimalBehaviorTreeTests.dll"), "aquatic-hunt"],
                            cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if result.returncode:
        raise RuntimeError(result.stdout + result.stderr)
    print(result.stdout.strip())


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    verify_actual_lua(compile_and_capture())
    verify_actor_tree_json()
    verify_foraging_thresholds()
    verify_aquatic_hunt_cleanup()


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, RuntimeError) as error:
        print("FAIL:", error, file=sys.stderr)
        raise SystemExit(1)
