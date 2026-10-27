from pathlib import Path
import subprocess
import tempfile
from xml.sax.saxutils import escape


root = Path(__file__).resolve().parents[3]
production = root / "Assets/5_Scripts/5-0_WorldModel/WorldSceneBindingIndex.cs"
ownership = root / "Assets/5_Scripts/5-0_WorldModel/WorldItemOwnershipIndex.cs"
checks = Path(__file__).with_name("Program.cs")

with tempfile.TemporaryDirectory(prefix="flatworld-world-bindings-") as directory:
    project = Path(directory) / "WorldSceneBindings.Tests.csproj"
    project.write_text(
        """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <Nullable>disable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="{}" />
    <Compile Include="{}" />
    <Compile Include="{}" />
  </ItemGroup>
</Project>
""".format(escape(str(production)), escape(str(ownership)), escape(str(checks))),
        encoding="utf-8",
    )
    subprocess.run(["dotnet", "run", "--project", str(project), "-c", "Release"], check=True)
