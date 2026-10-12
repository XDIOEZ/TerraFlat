param()
$ErrorActionPreference = 'Stop'
$taskRepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$taskOutput = Join-Path $taskRepoRoot 'Temp\GroundCoverValidation'
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
Push-Location $taskRepoRoot
try {
    # 只编译到忽略的 Temp 目录，不操作正在运行的 Unity。
    & dotnet build GamePlay.csproj --no-restore -nologo -v:q '-clp:ErrorsOnly'
    if ($LASTEXITCODE -ne 0) { throw '生产程序集编译失败。' }
    $taskProgram = [System.Security.SecurityElement]::Escape((Join-Path $PSScriptRoot 'Program.cs'))
    $taskReferences = @(
        (Join-Path $taskRepoRoot 'Temp\bin\Debug\FlatWorld.WorldModel.dll'),
        (Join-Path $taskRepoRoot 'Temp\bin\Debug\FlatWorld.WorldTopology.dll'),
        (Join-Path $taskRepoRoot 'Library\ScriptAssemblies\Unity.Mathematics.dll'),
        (Join-Path $taskRepoRoot 'Library\ScriptAssemblies\Unity.Burst.dll')
    )
    $taskReferenceXml = foreach ($taskReference in $taskReferences) {
        if (!(Test-Path -LiteralPath $taskReference)) { throw "缺少依赖：$taskReference" }
        $taskName = [System.IO.Path]::GetFileNameWithoutExtension($taskReference)
        $taskPath = [System.Security.SecurityElement]::Escape($taskReference)
        "<Reference Include='$taskName'><HintPath>$taskPath</HintPath></Reference>"
    }
    $taskProject = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings><EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup><Compile Include="$taskProgram" />
    $($taskReferenceXml -join "`n")
  </ItemGroup>
</Project>
"@
    $taskProjectPath = Join-Path $taskOutput 'GroundCover.Tests.csproj'
    [System.IO.File]::WriteAllText($taskProjectPath, $taskProject, [System.Text.UTF8Encoding]::new($false))
    & dotnet run --project $taskProjectPath --configuration Release -- $taskRepoRoot
    if ($LASTEXITCODE -ne 0) { throw '草花生成回归失败。' }
}
finally { Pop-Location }
