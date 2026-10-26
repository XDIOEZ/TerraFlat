"""在独立进程编译正式熊仇恨账本并执行项目 Lua 断言，不刷新或中断 Unity。"""
from __future__ import annotations

import argparse
import ctypes
import json
from pathlib import Path
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / ".codex-tmp/bear-provocation-tests"
LUA_SOURCE = ROOT / "Assets/StreamingAssets/GameplayTests/Systems/AI/rules.lua"


def run(command: list[str], name: str) -> None:
    result = subprocess.run(command, cwd=ROOT, capture_output=True, text=True, encoding="utf-8", errors="replace")
    (OUT / name).write_text(result.stdout + result.stderr, encoding="utf-8")
    print((result.stdout + result.stderr)[-12000:])
    if result.returncode:
        raise RuntimeError(f"{name} failed: {result.returncode}")


def compile_ledger() -> Path:
    source = ROOT / "Assets/5_Scripts/5-3_GamePlay/Entities/AI/AIProximityHatredLedger.cs"
    if not source.is_file():
        raise FileNotFoundError(source)
    project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
    properties = ET.SubElement(project, "PropertyGroup")
    for key, value in {"OutputType": "Exe", "TargetFramework": "net8.0", "ImplicitUsings": "enable",
                       "Nullable": "disable", "EnableDefaultCompileItems": "false"}.items():
        ET.SubElement(properties, key).text = value
    group = ET.SubElement(project, "ItemGroup")
    actor_source = (source.parent / "AI_BearTerritorialRetaliation.cs").read_text(encoding="utf-8-sig")

    def production_method(signature):
        if actor_source.count(signature) != 1:
            raise ValueError("熊生产方法签名已变更：" + signature)
        start = actor_source.index(signature)
        cursor = actor_source.index("{", start) + 1
        depth = 1
        while depth and cursor < len(actor_source):
            depth += (actor_source[cursor] == "{") - (actor_source[cursor] == "}")
            cursor += 1
        if depth:
            raise ValueError("熊生产方法括号缺失：" + signature)
        return actor_source[start:cursor]

    methods = [production_method(signature) for signature in (
        "internal void HandleConfirmedHit(DamageReceiverDamageInfo info)",
        "internal static Item ResolveRetaliationCreditActor(DamageReceiverDamageInfo info)",
        "protected override void UpdateExtraTimers(float deltaTime)",
        "protected virtual void ObserveNearbyTerritorialPlayers()",
        "internal bool ObserveNearbyTerritorialPlayer(Item target)",
        "protected override void OnPreEvaluate()",
        "private bool IsValidLivingTarget(Item target)",
        "private bool IsEligibleVisibleHatredTarget(CombatIdentity identity)",
        "private void RemoveInvalidTrackedTargets()",
        "private void RemoveExpiredTrackedTargets()",
    )]
    extracted = OUT / "ProductionBearEntrypoints.cs"
    extracted.write_text("using FlatWorld.Combat;\nusing UnityEngine;\npublic partial class AI_BearTerritorialRetaliation\n{\n" +
                         "\n".join(methods) + "\n}\n", encoding="utf-8")
    for path in [source, extracted, Path(__file__).with_name("Harness.cs"), Path(__file__).with_name("HitCreditHarness.cs")]:
        ET.SubElement(group, "Compile", Include=str(path))
    project_path = OUT / "BearLedgerTests.csproj"
    ET.ElementTree(project).write(project_path, encoding="utf-8", xml_declaration=True)
    run(["dotnet", "build", str(project_path), "--configuration", "Release", "-nologo", "-v:q"], "ledger-build.log")
    return OUT / "bin/Release/net8.0/BearLedgerTests.dll"


def lua_literal(value) -> str:
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
    return "{" + ",".join("[" + lua_literal(str(key)) + "]=" + lua_literal(item) for key, item in value.items()) + "}"


def verify_lua(assembly: Path) -> None:
    # 使用项目实际 XLua 原生库，仅创建独立 Lua 状态，不加载 Unity 或正式世界。
    library = ctypes.CDLL(str(ROOT / "Assets/Plugins/x86_64/xlua.dll"))
    pointer, integer = ctypes.c_void_p, ctypes.c_int
    signatures = {
        "luaL_newstate": (pointer, []), "luaL_openlibs": (None, [pointer]), "lua_close": (None, [pointer]),
        "lua_gettop": (integer, [pointer]), "lua_settop": (None, [pointer, integer]),
        "lua_type": (integer, [pointer, integer]), "lua_tonumber": (ctypes.c_double, [pointer, integer]),
        "lua_toboolean": (integer, [pointer, integer]),
        "lua_tolstring": (pointer, [pointer, integer, ctypes.POINTER(ctypes.c_size_t)]),
        "lua_pushnil": (None, [pointer]), "lua_next": (integer, [pointer, integer]),
        "lua_pushcclosure": (None, [pointer, pointer, integer]), "xlua_setglobal": (integer, [pointer, ctypes.c_char_p]),
        "xluaL_loadbuffer": (integer, [pointer, ctypes.c_char_p, integer, ctypes.c_char_p]),
        "lua_pcall": (integer, [pointer, integer, integer, integer]),
    }
    for name, (restype, arguments) in signatures.items():
        method = getattr(library, name)
        method.restype, method.argtypes = restype, arguments

    def read_string(state, index):
        length = ctypes.c_size_t()
        address = library.lua_tolstring(state, index, ctypes.byref(length))
        return ctypes.string_at(address, length.value).decode("utf-8") if address else ""

    def read_value(state, index):
        kind = library.lua_type(state, index)
        if kind == 0:
            return None
        if kind == 1:
            return bool(library.lua_toboolean(state, index))
        if kind == 3:
            value = library.lua_tonumber(state, index)
            return int(value) if value.is_integer() else value
        if kind == 4:
            return read_string(state, index)
        if kind != 5:
            raise ValueError(f"Unsupported Lua test argument type: {kind}")
        absolute = index if index > 0 else library.lua_gettop(state) + index + 1
        values = {}
        library.lua_pushnil(state)
        while library.lua_next(state, absolute):
            values[read_value(state, -2)] = read_value(state, -1)
            library.lua_settop(state, -2)
        if values and set(values) == set(range(1, len(values) + 1)):
            return [values[key] for key in range(1, len(values) + 1)]
        return values

    def evaluate(state, source, name, results=0):
        encoded = source.encode("utf-8")
        if library.xluaL_loadbuffer(state, encoded, len(encoded), name.encode("utf-8")):
            raise RuntimeError(read_string(state, -1))
        if library.lua_pcall(state, 0, results, 0):
            raise RuntimeError(read_string(state, -1))

    state = library.luaL_newstate()
    failures = []
    captures = []
    callback_type = ctypes.CFUNCTYPE(integer, pointer)

    @callback_type
    def probe(callback_state):
        try:
            args = read_value(callback_state, 1)
            result = subprocess.run(["dotnet", str(assembly)], cwd=ROOT, input=json.dumps(args), capture_output=True,
                                    text=True, encoding="utf-8", errors="replace")
            if result.returncode:
                raise RuntimeError(result.stdout + result.stderr)
            data = json.loads(result.stdout)
            captures.append({"arguments": args, "data": data})
            evaluate(callback_state, "return " + lua_literal(data), "bear-ledger-sample", 1)
        except Exception as error:
            failures.append(str(error))
            library.lua_pushnil(callback_state)
        return 1

    try:
        library.luaL_openlibs(state)
        library.lua_pushcclosure(state, ctypes.cast(probe, pointer), 0)
        library.xlua_setglobal(state, b"__bear_production_probe")
        evaluate(state, """
            test = {system = function(system, operation, arguments, validator, label)
                if system == 'ai' and operation == 'bear_provocation_sequence' then
                    local data = __bear_production_probe(arguments)
                    assert(data, '生产账本采样失败')
                    validator(data)
                end
            end}
        """, "bear-offline-lua-context")
        source = LUA_SOURCE.read_text(encoding="utf-8-sig")
        evaluate(state, source, str(LUA_SOURCE))
        if failures:
            raise RuntimeError("\n".join(failures))
        if len(captures) < 3:
            raise AssertionError("熊的 Lua 场景缺少阈值、多人或受击断言")
        (OUT / "lua-samples.json").write_text(json.dumps(captures, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"PASS actual XLua syntax and {len(captures)} Bear Lua scenarios against the compiled production ledger")
    finally:
        library.lua_close(state)


def current_sources_targets() -> Path:
    project = ET.Element("Project")
    existing = {}
    for path in ROOT.glob("*.csproj"):
        nodes = list(ET.parse(path).iter())
        sources = [node.get("Include") for node in nodes
                   if node.tag.split("}")[-1] == "Compile" and node.get("Include")]
        existing[path.stem] = {str((ROOT / source).resolve()).lower() for source in sources}
        missing = [source for source in sources if not (ROOT / source).is_file()]
        if missing:
            group = ET.SubElement(project, "ItemGroup", Condition=f"'$(MSBuildProjectName)' == '{path.stem}'")
            for source in missing:
                ET.SubElement(group, "Compile", Remove=source)
        missing_analyzers = [node.get("Include") for node in nodes
                             if node.tag.split("}")[-1] == "Analyzer" and node.get("Include")
                             and "$(" not in node.get("Include") and "@(" not in node.get("Include")
                             and not (ROOT / node.get("Include")).is_file()]
        if missing_analyzers:
            group = ET.SubElement(project, "ItemGroup", Condition=f"'$(MSBuildProjectName)' == '{path.stem}'")
            for analyzer in missing_analyzers:
                ET.SubElement(group, "Analyzer", Remove=analyzer)
    groups, owner_cache = {}, {}

    def owner(folder):
        if folder not in owner_cache:
            definitions = list(folder.glob("*.asmdef"))
            owner_cache[folder] = (json.loads(definitions[0].read_text(encoding="utf-8-sig"))["name"] if definitions
                                   else owner(folder.parent) if folder != ROOT else None)
        return owner_cache[folder]

    for path in (ROOT / "Assets/5_Scripts").rglob("*.cs"):
        assembly = owner(path.parent)
        if assembly not in existing or str(path.resolve()).lower() in existing[assembly]:
            continue
        if assembly not in groups:
            groups[assembly] = ET.SubElement(project, "ItemGroup", Condition=f"'$(MSBuildProjectName)' == '{assembly}'")
        ET.SubElement(groups[assembly], "Compile", Include=str(path))
    result = OUT / "CurrentSources.targets"
    ET.ElementTree(project).write(result, encoding="utf-8", xml_declaration=True)
    return result


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--build", action="store_true")
    args = parser.parse_args()
    OUT.mkdir(parents=True, exist_ok=True)
    assembly = compile_ledger()
    verify_lua(assembly)
    run(["dotnet", str(assembly), "--credit"], "hit-credit.log")
    if args.build:
        target = current_sources_targets()
        run(["dotnet", "build", "FlatWorld.GameTest.csproj", "--ignore-failed-sources", "-nologo", "-v:q", "-clp:ErrorsOnly",
             f"-p:CustomAfterMicrosoftCommonTargets={target}"], "gameplay-build.log")


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, RuntimeError, AssertionError) as error:
        print("FAIL:", error, file=sys.stderr)
        raise SystemExit(1)
