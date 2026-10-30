"""在独立进程编译正式缓存与 Hook，并用替身隔离 Unity 资源。"""
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[3]
HERE = Path(__file__).resolve().parent
OUT = ROOT / "Temp" / "WorldGenerationProfileCachingValidation"
MANAGEMENT = ROOT / "Assets/5_Scripts/5-3_GamePlay/World/Chunk/Management"


def block(source: str, marker: str) -> str:
    start = source.index(marker)
    opening = source.index("{", start)
    depth = 0
    for index in range(opening, len(source)):
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[start:index + 1]
    raise ValueError(marker)


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    ecology = (ROOT / "Assets/5_Scripts/5-3_GamePlay/World/Map/Data/EcologyWorldSaveData.cs").read_text(encoding="utf-8")
    rule = block(ecology, "public partial class EcologyRuleSaveData")
    fields = rule[rule.index("#region 规则字段"):rule.index("#endregion") + len("#endregion")]
    fields = re.sub(r"\[MemoryPackOrder\(\d+\)\]\s*", "", fields)
    method = block((MANAGEMENT / "ChunkMgr.Ecology.cs").read_text(encoding="utf-8"),
                   "internal static ChunkGenerationProfileSnapshot GetCachedWorldGenerationProfile(")
    pairing = block((MANAGEMENT / "ChunkMgr.Ecology.cs").read_text(encoding="utf-8"),
                    "private ChunkGenerationProfileSnapshot AttachCavePortalPairing(")
    (OUT / "SourceParts.cs").write_text(
        "using System; using System.Collections.Generic; using FlatWorld.WorldModel; using FlatWorld.Networking;\n"
        + "public partial class EcologyRuleSaveData {\n" + fields + "\n}\n"
        + "public partial class ChunkMgr {\n" + method + "\n" + pairing + "\n}\n", encoding="utf-8")
    project = f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
      <OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems>
      </PropertyGroup><ItemGroup>
      <Compile Include="{MANAGEMENT / 'WorldGenerationProfileCache.cs'}" />
      <Compile Include="{ROOT / 'Assets/5_Scripts/5-3_GamePlay/World/Map/WorldGenerationRuntimeHooks.cs'}" />
      <Compile Include="{ROOT / 'Assets/5_Scripts/5-3_GamePlay/Development/Testing/GameplayTestSystemProbe.GenerationProfileCache.cs'}" />
      <Compile Include="{OUT / 'SourceParts.cs'}" />
      <Compile Include="{HERE / 'Program.cs'}" />
      <Reference Include="Newtonsoft.Json"><HintPath>{ROOT / 'Library/PackageCache/com.unity.nuget.newtonsoft-json@3.2.1/Runtime/Newtonsoft.Json.dll'}</HintPath></Reference>
      </ItemGroup></Project>'''
    (OUT / "Test.csproj").write_text(project, encoding="utf-8")
    subprocess.run(["dotnet", "run", "--project", str(OUT / "Test.csproj"), "-c", "Release"],
                   cwd=ROOT, check=True)


if __name__ == "__main__":
    main()
