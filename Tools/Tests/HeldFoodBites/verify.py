"""Compile and exercise production bite geometry and HLSL without touching Unity."""
from pathlib import Path
import ctypes
import os
import subprocess
import xml.sax.saxutils

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / "Library/CodexFoodBitesValidation/Offline"
FOOD = ROOT / "Assets/5_Scripts/5-3_GamePlay/Entities/Item/Modules/Player/Food"


def verify_lua_plan():
    lua = ctypes.CDLL(str(ROOT / "Assets/Plugins/x86_64/xlua.dll"))
    lua.luaL_newstate.restype = ctypes.c_void_p
    lua.luaL_openlibs.argtypes = [ctypes.c_void_p]
    lua.xluaL_loadbuffer.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_int, ctypes.c_char_p]
    lua.lua_pcall.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_int, ctypes.c_int]
    lua.lua_tonumber.argtypes = [ctypes.c_void_p, ctypes.c_int]
    lua.lua_tonumber.restype = ctypes.c_double
    lua.lua_tolstring.argtypes = [ctypes.c_void_p, ctypes.c_int, ctypes.c_void_p]
    lua.lua_tolstring.restype = ctypes.c_char_p
    lua.lua_close.argtypes = [ctypes.c_void_p]
    state = lua.luaL_newstate()
    try:
        lua.luaL_openlibs(state)
        rules = (ROOT / "Assets/StreamingAssets/GameplayTests/Systems/Rendering/rules.lua").read_text(encoding="utf-8-sig")
        source = ("local count=0; test={system=function(...) count=count+1; assert(count<=256) end};\n" +
                  rules + "\nreturn count").encode("utf-8")
        status = lua.xluaL_loadbuffer(state, source, len(source), b"held-food-offline-lua")
        if status == 0:
            status = lua.lua_pcall(state, 0, 1, 0)
        if status:
            raise RuntimeError(lua.lua_tolstring(state, -1, None).decode("utf-8"))
        print(f"PASS: project XLua syntax and command plan {int(lua.lua_tonumber(state, -1))}/256; gameplay callbacks were not executed.")
    finally:
        lua.lua_close(state)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    includes = [FOOD / "FoodBiteGeometry.cs", Path(__file__).with_name("Program.cs")]
    compile_items = "".join('<Compile Include="' + xml.sax.saxutils.escape(str(p)) + '" />' for p in includes)
    project = OUT / "HeldFoodBites.csproj"
    project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework>'
                       '<OutputType>Exe</OutputType><ImplicitUsings>enable</ImplicitUsings>'
                       '<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>' +
                       compile_items + '</ItemGroup></Project>', encoding="utf-8")
    subprocess.run(["dotnet", "run", "--project", str(project), "--configuration", "Release",
                    "-p:UseSharedCompilation=false"], check=True, cwd=ROOT)
    shader = (ROOT / "Assets/9_Shaders/Shader/Sprite-Lit-Master.shader").read_text(encoding="utf-8-sig")
    assert shader.count("ClipHeldFoodBites(float2(i.") == 3, "a Sprite pass omits clipping"
    start = shader.index("void ClipHeldFoodBites(")
    end = shader.index("\n        float4 _SpriteLocalBounds", start)
    function = shader[start:end]
    hlsl = OUT / "HeldFoodBites.hlsl"
    hlsl.write_text("Texture2D _HeldFoodBiteCircles; SamplerState sampler_HeldFoodBiteCircles;\n"
                    "float4 _HeldFoodBites;\n"
                    "#define SAMPLE_TEXTURE2D_LOD(tex,samp,uv,lod) tex.SampleLevel(samp,uv,lod)\n" +
                    function + "\nfloat4 Fragment(float2 local : TEXCOORD0) : SV_Target\n"
                    "{ ClipHeldFoodBites(local); return 1; }\n", encoding="utf-8")
    sdk = Path(os.environ.get("ProgramFiles(x86)", "C:/Program Files (x86)")) / "Windows Kits/10/bin"
    fxc = sorted(sdk.glob("*/x64/fxc.exe"))[-1]
    subprocess.run([str(fxc), "/nologo", "/Ges", "/T", "ps_4_0", "/E", "Fragment",
                    "/Fo", str(OUT / "HeldFoodBites.dxbc"), str(hlsl)], check=True, cwd=ROOT)
    for source in [FOOD / "Mod_HeldFood.cs", FOOD / "FoodBiteGeometry.cs"]:
        text = source.read_text(encoding="utf-8-sig")
        assert all(old not in text for old in ["FoodBiteMaskGenerator", "GetPixels", "SetPixels", "SpriteMask"]), source
    print("PASS: actual shared HLSL compiles for DX11; all three passes clip; CPU rasterizer is removed.")
    verify_lua_plan()
    print("Unity lifecycle, MPB, Sprite flip and zero-allocation Lua assertions require an idle Editor.")


if __name__ == "__main__":
    main()
