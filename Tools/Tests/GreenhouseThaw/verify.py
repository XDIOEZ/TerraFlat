"""离线执行正式温室、融雪与冻水元数据规则，不连接或刷新 Unity。"""
from pathlib import Path
import argparse
import ctypes
import json
import re
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
OUTPUT = ROOT / "Temp/GreenhouseThawValidation"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--gameplay-assembly", type=Path, help="额外验证外部编译好的完整 GamePlay.dll，不加载 Unity 原生环境")
    args = parser.parse_args()
    OUTPUT.mkdir(parents=True, exist_ok=True)
    response = max((ROOT / "Library/Bee").rglob("FlatWorld.WorldModel.rsp"), key=lambda p: p.stat().st_mtime)
    content = response.read_text(encoding="utf-8-sig")
    refs = [Path(p) if Path(p).is_absolute() else ROOT / p
            for p in re.findall(r'^-r:"([^"]+)"', content, re.M)]
    engine = next(p for p in refs if p.name == "UnityEngine.CoreModule.dll")
    editor = engine.parents[2]
    content = re.sub(r'^-out:.*$', f'-out:"{(OUTPUT / "FlatWorld.WorldModel.dll").as_posix()}"', content, flags=re.M)
    content = re.sub(r'^-refout:.*$', f'-refout:"{(OUTPUT / "FlatWorld.WorldModel.ref.dll").as_posix()}"', content, flags=re.M)
    for source in (ROOT / "Assets/5_Scripts/5-0_WorldModel").rglob("*.cs"):
        relative = source.relative_to(ROOT).as_posix()
        if f'"{relative}"' not in content:
            content += f'\n"{relative}"\n'
    rsp = OUTPUT / "WorldModel.rsp"
    rsp.write_text(content, encoding="utf-8")
    subprocess.run([str(editor / "NetCoreRuntime/dotnet.exe"), str(editor / "DotNetSdkRoslyn/csc.dll"),
                    "@" + str(rsp)], cwd=ROOT, check=True)

    project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
    props = ET.SubElement(project, "PropertyGroup")
    for key, value in {"OutputType": "Exe", "TargetFramework": "net8.0",
                       "EnableDefaultCompileItems": "false"}.items():
        ET.SubElement(props, key).text = value
    group = ET.SubElement(project, "ItemGroup")
    sources = [Path(__file__).with_name("Program.cs"),
               ROOT / "Assets/5_Scripts/5-3_GamePlay/World/Environment/WorldSurfaceThawRules.cs",
               ROOT / "Assets/5_Scripts/5-3_GamePlay/World/Fluids/Atmosphere/AtmosphericGreenhouseTemperatureRules.cs"]
    for source in sources:
        ET.SubElement(group, "Compile", Include=str(source))
    paths = [OUTPUT / "FlatWorld.WorldModel.dll", engine,
             ROOT / "Library/ScriptAssemblies/FlatWorld.WorldTopology.dll"]
    paths += [p for p in refs if p.name in {"MemoryPack.Core.dll", "Unity.Mathematics.dll"}]
    for path in paths:
        ET.SubElement(ET.SubElement(group, "Reference", Include=path.stem), "HintPath").text = str(path)
    project_path = OUTPUT / "GreenhouseThaw.Tests.csproj"
    ET.ElementTree(project).write(project_path, encoding="utf-8", xml_declaration=True)
    subprocess.run(["dotnet", "run", "--project", str(project_path), "--configuration", "Release"], cwd=ROOT, check=True)
    if args.gameplay_assembly:
        validate_real_atmosphere_services(args.gameplay_assembly.resolve(), engine)

    suite = json.loads((ROOT / "Assets/StreamingAssets/GameplayTests/Systems/Environment/rules-suite.json").read_text(encoding="utf-8-sig"))
    for case in suite["cases"]:
        assert (ROOT / "Assets/StreamingAssets" / case["luaPath"]).is_file(), case["luaPath"]
    validate_lua_syntax_and_registered_routes()
    print("PASS: Environment suite references existing Lua; runtime/visual validation still requires Unity.")


def validate_lua_syntax_and_registered_routes():
    lua = ctypes.CDLL(str(ROOT / "Assets/Plugins/x86_64/xlua.dll"))
    lua.luaL_newstate.restype = ctypes.c_void_p
    lua.lua_close.argtypes = [ctypes.c_void_p]
    lua.xluaL_loadbuffer.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p]
    lua.xluaL_loadbuffer.restype = ctypes.c_int
    lua.lua_tolstring.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.POINTER(ctypes.c_size_t)]
    lua.lua_tolstring.restype = ctypes.c_char_p
    main = (ROOT / "Assets/5_Scripts/5-3_GamePlay/Development/Testing/GameplayTestSystemProbe.cs").read_text(encoding="utf-8-sig")
    count = 0
    for stem, register, probe in (("greenhouse", "RegisterGreenhouseOperations", "Greenhouse"),
                                  ("surface_thaw", "RegisterSurfaceThawOperations", "SurfaceThaw")):
        path = ROOT / f"Assets/StreamingAssets/GameplayTests/Systems/Environment/{stem}.lua"
        source = path.read_bytes()
        state = lua.luaL_newstate()
        assert state, "native Lua allocation failed"
        try:
            status = lua.xluaL_loadbuffer(state, source, len(source), str(path).encode("utf-8"))
            error = lua.lua_tolstring(state, -1, None) if status else None
            assert status == 0, (path, error.decode("utf-8", errors="replace") if error else status)
        finally:
            lua.lua_close(state)
        assert main.count(f"{register}(result);") == 1, register
        registration = (ROOT / f"Assets/5_Scripts/5-3_GamePlay/Development/Testing/GameplayTestSystemProbe.{probe}.cs").read_text(encoding="utf-8-sig")
        for operation in re.findall(r'test\.system\(\s*"environment"\s*,\s*"([^"]+)"', source.decode("utf-8-sig")):
            assert registration.count(f'"environment/{operation}"') == 1, operation
            count += 1
    print(f"PASS: two Lua files compile with production XLua; {count} operations have registered routes (not executed).")


def validate_real_atmosphere_services(gameplay, engine):
    assert gameplay.is_file(), gameplay
    response = max((ROOT / "Library/Bee").rglob("GamePlay.rsp"), key=lambda p: p.stat().st_mtime)
    refs = [Path(p) if Path(p).is_absolute() else ROOT / p
            for p in re.findall(r'^-r:"([^"]+)"', response.read_text(encoding="utf-8-sig"), re.M)]
    needed = {"MemoryPack.Core.dll", "Newtonsoft.Json.dll", "FlatWorld.WorldTopology.dll"}
    references = [gameplay, OUTPUT / "FlatWorld.WorldModel.dll", engine]
    references += [path for path in refs if path.name in needed]
    project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
    props = ET.SubElement(project, "PropertyGroup")
    for key, value in {"OutputType": "Exe", "TargetFramework": "net8.0",
                       "EnableDefaultCompileItems": "false"}.items():
        ET.SubElement(props, key).text = value
    group = ET.SubElement(project, "ItemGroup")
    ET.SubElement(group, "Compile", Include=str(Path(__file__).with_name("ServiceProgram.cs")))
    for path in references:
        ET.SubElement(ET.SubElement(group, "Reference", Include=path.stem), "HintPath").text = str(path)
    path = OUTPUT / "GreenhouseThaw.Services.csproj"
    ET.ElementTree(project).write(path, encoding="utf-8", xml_declaration=True)
    subprocess.run(["dotnet", "run", "--project", str(path), "--configuration", "Release", "--", str(ROOT)],
                   cwd=ROOT, check=True)


if __name__ == "__main__":
    main()
