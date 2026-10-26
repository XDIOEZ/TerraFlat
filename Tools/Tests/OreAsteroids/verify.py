"""直接编译生产小行星、星体引力和降落领域；不连接或操作 Unity。"""
from pathlib import Path
import subprocess
import tempfile
from xml.sax.saxutils import escape


ROOT = Path(__file__).resolve().parents[3]
FOLDER = Path(__file__).resolve().parent
SPACE = ROOT / "Assets/5_Scripts/5-3_GamePlay/World/Space"
SOURCES = [
    FOLDER / "Program.cs",
    FOLDER / "TestDoubles.cs",
    SPACE / "SpaceOreAsteroidState.cs",
    SPACE / "SpaceOreAsteroidCatalog.cs",
    SPACE / "SpaceGameplaySettings.cs",
    SPACE / "Universe/UniverseState.cs",
    SPACE / "Universe/SpaceSectorCoordinates.cs",
    SPACE / "Universe/UniverseSectorFactory.cs",
    SPACE / "Universe/UniverseSimulation.cs",
    SPACE / "Universe/SpaceGravity.cs",
    SPACE / "Ships/ShipLandingGuidance.cs",
]


def main():
    includes = "\n".join(f'<Compile Include="{escape(str(path), quote_map)}" />' for path in SOURCES)
    project = f"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>disable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    {includes}
    <Reference Include="Newtonsoft.Json"><HintPath>$(MSBuildSDKsPath)/../Newtonsoft.Json.dll</HintPath></Reference>
  </ItemGroup>
</Project>
"""
    with tempfile.TemporaryDirectory(prefix="FlatWorld-OreAsteroids-") as temporary:
        project_path = Path(temporary) / "OreAsteroids.Tests.csproj"
        project_path.write_text(project, encoding="utf-8")
        return subprocess.run(["dotnet", "run", "--project", str(project_path), "-c", "Release", "--", str(ROOT)],
                              cwd=ROOT, check=False).returncode


quote_map = {'"': "&quot;"}
if __name__ == "__main__":
    raise SystemExit(main())
