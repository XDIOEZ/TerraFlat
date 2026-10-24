"""Run production shared-spawn scheduling outside Unity; never reload the user's Editor."""
from pathlib import Path
import json
import re
import subprocess
import sys


def extract_block(source: str, signature: str) -> str:
    if source.count(signature) != 1:
        raise ValueError(f"Production signature changed: {signature}")
    start = source.index(signature)
    cursor = source.index("{", start) + 1
    depth = 1
    while depth:
        depth += (source[cursor] == "{") - (source[cursor] == "}")
        cursor += 1
    return source[start:cursor]


def main() -> None:
    root = Path(__file__).resolve().parents[3]
    output = root / ".codex-tmp/natural-spawn-tests"
    output.mkdir(parents=True, exist_ok=True)
    game = root / "Assets/5_Scripts/5-3_GamePlay"
    for source in (
        game / "Entities/AI/Spawning/MonsterSpawnerManager.Countdown.cs",
        game / "Entities/AI/Spawning/MonsterSpawnerManager.NaturalSelection.cs",
        game / "World/Map/Data/MonsterSpawnerSaveData.cs",
    ):
        (output / source.name).write_bytes(source.read_bytes())

    # Only remove Editor/JSON annotations; all runtime configuration methods remain unchanged.
    config = (game / "Entities/Spawner/SpawnerConfig.cs").read_text(encoding="utf-8-sig")
    config = config.replace("using Sirenix.OdinInspector;", "").replace("using Newtonsoft.Json;", "")
    config = "\n".join(line for line in config.splitlines()
                       if not line.strip().startswith("[") or
                       line.strip().startswith(("[MemoryPack.", "[Serializable]")))
    (output / "SpawnerConfig.cs").write_text(config, encoding="utf-8")
    monitor = (game / "Entities/AI/Spawning/MonsterSpawnerManager.Monitoring.cs").read_text(encoding="utf-8-sig")
    projection = extract_block(monitor, "    public struct CreatureSpawnMonitorEntry")
    projection += "\n" + extract_block(monitor, "    internal static CreatureSpawnMonitorEntry ReadCountdownMonitor(")
    (output / "Monitor.cs").write_text("using System;\npublic partial class MonsterSpawnerManager\n{\n" + projection + "\n}", encoding="utf-8")

    (output / "Harness.cs").write_bytes(Path(__file__).with_name("Harness.cs").read_bytes())
    core = root / "Assets/Packages/MemoryPack.Core.1.21.3/lib/netstandard2.1/MemoryPack.Core.dll"
    generator = root / "Assets/Packages/MemoryPack.Generator.1.21.3/analyzers/dotnet/cs/MemoryPack.Generator.dll"
    (output / "SpawnTests.csproj").write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>'
        '<OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>'
        '<ImplicitUsings>enable</ImplicitUsings><Nullable>disable</Nullable>'
        '<DisableImplicitFrameworkDefines>true</DisableImplicitFrameworkDefines>'
        '<DefineConstants>UNITY_2022_3_OR_NEWER</DefineConstants>'
        '<AllowUnsafeBlocks>true</AllowUnsafeBlocks></PropertyGroup><ItemGroup>'
        f'<Reference Include="MemoryPack.Core"><HintPath>{core}</HintPath></Reference>'
        f'<Analyzer Include="{generator}" /></ItemGroup></Project>', encoding="utf-8")

    directory = root / "Assets/Resources/GameConfig/Spawners"
    settings = json.loads((directory / "spawner-settings.json").read_text(encoding="utf-8-sig"))
    assert settings["naturalSpawnIntervalGameSeconds"] > 0
    forbidden = {"scheduleMode", "spawnsPerDay", "daysBetweenSpawns", "spawnTriggerTime",
                 "recoveryCheckInterval", "recoveryTargetPopulation", "growthIntervalDays", "unboundedDailyGrowth"}
    configs = [json.loads(p.read_text(encoding="utf-8-sig"))["config"] for p in sorted((directory / "Rules").glob("*.json"))]
    assert len(configs) == 9 and sum(len(c["spawnEntries"]) for c in configs) == 10
    for config in configs:
        assert not forbidden.intersection(config), config["id"]
        assert "ignoreEcologyBudget" in config
    assert next(c for c in configs if c["id"] == "zombie_horde")["spawnChance"] == 0
    print("PASS: 9 official rules / 10 species; one global interval, no per-rule timing or recovery bypass.", flush=True)
    # Verify that the real main-loop and commit-time guard call the tested scheduler and real terrain filters.
    manager = (game / "Entities/AI/Spawning/MonsterSpawnerManager.cs").read_text(encoding="utf-8")
    assert manager.count("AdvanceCountdownSchedule(") == 1
    for old in ("SpeciesCountdowns", "ProcessTimedWindows", "ProcessMilestoneGrowth", "QueueDeathReplacement"):
        assert old not in manager
    guard = extract_block(manager, "    private bool IsValidSpawnPosition(")
    for required in ("IsRuntimeEntityPresentationReady", "IsRuntimeTerrainReady", "CanSwimAt", "IsWalkableSpawnPosition",
                     "IsBiomeAllowed", "IsGroundTileAllowed", "IsLightAllowed", "IsVisibleByAnyActiveCamera"):
        assert required in guard, required
    assert "IgnoreEcologyBudget" not in extract_block(manager, "    private bool IsWithinPlayerPopulationLimit(")
    creation = (game / "Entities/AI/Spawning/MonsterSpawnerManager.IncrementalCreation.cs").read_text(encoding="utf-8")
    assert "RecordNaturalSpawnFailure" in extract_block(creation, "    private void AdvanceNaturalCreatureBirth()")
    assert "IsValidSpawnPosition" in extract_block(creation, "    private bool IsNaturalCreatureCommitAllowed(")
    subprocess.run(["dotnet", "run", "--project", str(output / "SpawnTests.csproj")], cwd=root, check=True)


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, AssertionError, subprocess.CalledProcessError) as error:
        print(f"FAIL: {error}", file=sys.stderr)
        raise SystemExit(1)
