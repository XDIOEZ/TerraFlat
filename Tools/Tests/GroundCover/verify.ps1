param()
$ErrorActionPreference = 'Stop'
$taskRepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))
$taskOutput = Join-Path $taskRepoRoot 'Temp\GroundCoverValidation'
New-Item -ItemType Directory -Path $taskOutput -Force | Out-Null
Push-Location $taskRepoRoot
try {
    # 补齐播放期间尚未写入 csproj 的世界生成源码，不编译无关玩法程序集。
    $taskWorldSource = Join-Path $taskRepoRoot 'Assets\5_Scripts\5-0_WorldModel\**\*.cs'
    $taskProfilerRelative = 'Assets\5_Scripts\5-0_WorldModel\Generation\DeterministicChunkGenerator.MacroHydrology.cs'
    $taskProfilerSource = Join-Path $taskRepoRoot $taskProfilerRelative
    $taskProfilerCopy = Join-Path $taskOutput 'DeterministicChunkGenerator.MacroHydrology.cs'
    $taskProfilerText = [System.IO.File]::ReadAllText($taskProfilerSource)
    $taskProfilerPattern = '(ProfilerMarker \w+ =) new\("[^"]+"\);'
    if ([regex]::Matches($taskProfilerText, $taskProfilerPattern).Count -ne 4) {
        throw 'Profiler 标记变化，需要重新检查离线隔离。'
    }
    # 独立 CLR 没有 Unity 原生 Profiler，仅在临时副本禁用标记初始化，生成算法保持原样。
    [System.IO.File]::WriteAllText($taskProfilerCopy,
        [regex]::Replace($taskProfilerText, $taskProfilerPattern, '$1 default;'),
        [System.Text.UTF8Encoding]::new($false))
    $taskTargetsXml = @'
<Project>
  <ItemGroup Condition="'$(MSBuildProjectName)' == 'FlatWorld.WorldModel'">
    <Compile Include="{0}" Exclude="@(Compile)" />
    <Compile Remove="{1};{2}" />
    <Compile Include="{3}" />
  </ItemGroup>
</Project>
'@ -f [System.Security.SecurityElement]::Escape($taskWorldSource),
    [System.Security.SecurityElement]::Escape($taskProfilerSource),
    [System.Security.SecurityElement]::Escape($taskProfilerRelative),
    [System.Security.SecurityElement]::Escape($taskProfilerCopy)
    $taskTargetsPath = Join-Path $taskOutput 'CurrentWorldSources.targets'
    [System.IO.File]::WriteAllText($taskTargetsPath, $taskTargetsXml, [System.Text.UTF8Encoding]::new($false))
    $taskAssemblies = Join-Path $taskOutput 'Managed'
    & dotnet build FlatWorld.WorldModel.csproj --no-restore -nologo -v:q '-clp:ErrorsOnly' `
        "-p:CustomAfterMicrosoftCommonTargets=$taskTargetsPath" "-p:OutputPath=$taskAssemblies"
    if ($LASTEXITCODE -ne 0) { throw '世界生成程序集编译失败。' }
    $taskProgram = [System.Security.SecurityElement]::Escape((Join-Path $PSScriptRoot 'Program.cs'))
    $taskProductionProject = [xml][System.IO.File]::ReadAllText((Join-Path $taskRepoRoot 'FlatWorld.WorldModel.csproj'))
    $taskCoreModule = $taskProductionProject.SelectSingleNode(
        "//*[local-name()='Reference' and @Include='UnityEngine.CoreModule']/*[local-name()='HintPath']").InnerText
    if (![System.IO.Path]::IsPathRooted($taskCoreModule)) { $taskCoreModule = Join-Path $taskRepoRoot $taskCoreModule }
    $taskReferences = @(
        (Join-Path $taskAssemblies 'FlatWorld.WorldModel.dll'),
        (Join-Path $taskAssemblies 'FlatWorld.WorldTopology.dll'),
        (Join-Path $taskRepoRoot 'Library\ScriptAssemblies\Unity.Mathematics.dll'),
        (Join-Path $taskRepoRoot 'Library\ScriptAssemblies\Unity.Burst.dll'),
        $taskCoreModule
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
