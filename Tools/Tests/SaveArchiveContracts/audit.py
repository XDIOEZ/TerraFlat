from pathlib import Path
import subprocess

root = Path(__file__).resolve().parents[3]
out = root / "Temp" / "SaveArchivePayloadAudit"
out.mkdir(parents=True, exist_ok=True)
roslyn = Path(subprocess.check_output(["dotnet", "--list-sdks"], text=True).splitlines()[-1].split("[")[-1].strip(" ]"))
version = subprocess.check_output(["dotnet", "--version"], text=True).strip()
roslyn = roslyn / version / "Roslyn" / "bincore"
project = f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>
<TargetFramework>net8.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>
<Compile Include="{Path(__file__).with_name('ModulePayloadAudit.cs')}" />
<Reference Include="Microsoft.CodeAnalysis"><HintPath>{roslyn / 'Microsoft.CodeAnalysis.dll'}</HintPath></Reference>
<Reference Include="Microsoft.CodeAnalysis.CSharp"><HintPath>{roslyn / 'Microsoft.CodeAnalysis.CSharp.dll'}</HintPath></Reference>
</ItemGroup></Project>'''
(out / "Audit.csproj").write_text(project, encoding="utf-8")
responses = [max((root / "Library/Bee").rglob(name + ".rsp"), key=lambda p: p.stat().st_mtime)
             for name in ("GamePlay", "FlatWorld.Networking.Gameplay")]
with (out / "payload-types.json").open("w", encoding="utf-8") as result:
    subprocess.run(["dotnet", "run", "--project", str(out / "Audit.csproj"), "-c", "Release", "--",
                    str(root), *map(str, responses)], cwd=root, check=True, stdout=result)
print(out / "payload-types.json")
