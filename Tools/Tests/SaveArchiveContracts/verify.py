"""Compile the actual Data assembly and test isolated published snapshot contracts."""
from pathlib import Path
import importlib.util
import re
import subprocess
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[3]
out = root / "Temp" / "SqliteArchiveContractValidation"
out.mkdir(parents=True, exist_ok=True)
response = max((root / "Library/Bee").rglob("Data.rsp"), key=lambda path: path.stat().st_mtime)
text = response.read_text(encoding="utf-8-sig")
text = re.sub(r"^-out:.*$", f'-out:"{(out / "Data.dll").as_posix()}"', text, flags=re.M)
text = re.sub(r"^-refout:.*$", f'-refout:"{(out / "Data.ref.dll").as_posix()}"', text, flags=re.M)
for path in (root / "Assets/5_Scripts/5-1_Data").rglob("*.cs"):
    relative = path.relative_to(root).as_posix()
    if f'"{relative}"' not in text:
        text += f'\n"{relative}"\n'
(out / "Data.rsp").write_text(text, encoding="utf-8")
engine = next(Path(p) for p in re.findall(r'^-r:"([^"]+)"', text, re.M) if p.endswith("UnityEngine.CoreModule.dll"))
if not engine.is_absolute(): engine = root / engine
editor = engine.parents[2]
subprocess.run([str(editor / "NetCoreRuntime/dotnet.exe"), str(editor / "DotNetSdkRoslyn/csc.dll"),
                "@" + str(out / "Data.rsp")], cwd=root, check=True)
spec = importlib.util.spec_from_file_location("world_model_compiler", root / "Tools/Tests/SharedHydrologyHeightSampling/verify.py")
world_compiler = importlib.util.module_from_spec(spec)
spec.loader.exec_module(world_compiler)
world_response = max((root / "Library/Bee").rglob("FlatWorld.WorldModel.rsp"), key=lambda path: path.stat().st_mtime)
world_assembly = world_compiler.compile_world_model(out, world_response)
project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
props = ET.SubElement(project, "PropertyGroup")
for key, value in {"OutputType": "Exe", "TargetFramework": "net8.0", "EnableDefaultCompileItems": "false",
                   "DisableImplicitFrameworkDefines": "true"}.items():
    ET.SubElement(props, key).text = value
group = ET.SubElement(project, "ItemGroup")
ET.SubElement(group, "Compile", Include=str(Path(__file__).with_name("Program.cs")))
ET.SubElement(group, "Compile", Include=str(Path(__file__).with_name("MachineArchiveStubs.cs")))
ET.SubElement(group, "Compile", Include=str(Path(__file__).with_name("CoreContractStubs.cs")))
ET.SubElement(group, "Compile", Include=str(root / "Assets/5_Scripts/5-3_GamePlay/World/Machines/MachineArchiveMetadataSnapshot.cs"))
ET.SubElement(group, "Compile", Include=str(root / "Assets/5_Scripts/5-3_GamePlay/World/Fluids/Core/FluidState.cs"))
ET.SubElement(group, "Compile", Include=str(root / "Assets/5_Scripts/5-3_GamePlay/World/Fluids/Atmosphere/AtmosphereState.cs"))
ET.SubElement(group, "Compile", Include=str(root / "Assets/5_Scripts/5-3_GamePlay/World/Machines/FuelAbsorptionState.cs"))
ET.SubElement(group, "Compile", Include=str(root / "Assets/5_Scripts/5-3_GamePlay/World/Time/SerializableGradientKeySaveSnapshots.cs"))
for path in ("World/Map/Data/PlanetData.cs", "World/Space/PlanetData.cs", "World/Fluids/Atmosphere/PlanetData.Atmosphere.cs",
             "World/Map/Data/MapSave.cs", "World/Map/Data/EcologyWorldSaveData.cs", "World/Map/Data/RegionalWeatherRecord.cs"):
    ET.SubElement(group, "Compile", Include=str(root / "Assets/5_Scripts/5-3_GamePlay" / path))
for name in ("GameSaveData.cs", "GameSaveData.Difficulty.cs", "GameSaveData.Dimensions.cs",
             "GameSaveData.GameEvents.cs", "GameSaveData.MonsterSpawner.cs", "GameEventSaveData.cs", "MonsterSpawnerSaveData.cs"):
    ET.SubElement(group, "Compile", Include=str(root / "Assets/5_Scripts/5-3_GamePlay/World/Map/Data" / name))

def snapshot_definition(path, name):
    source = path.read_text(encoding="utf-8-sig")
    declaration = re.search(r"\[[^\]\r\n]*\bMemoryPackable\b[^\]\r\n]*\](?:\s*\[[^\]]+\])*\s*public\s+(?:sealed\s+)?partial\s+class\s+" + re.escape(name) + r"\b", source)
    if declaration is None:
        raise ValueError("missing snapshot attribute: " + name)
    start = declaration.start()
    opening = source.index("{", declaration.end())
    depth = 0
    for index in range(opening, len(source)):
        if source[index] == "{": depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                result = source[start:index + 1]
                if name == "ChunkSaveRecord":
                    # 外部火焰工作区改动不属于 SQLite 提交，也不能成为固定样本依赖。
                    result = re.sub(r'^\s*\[[^\]]*MemoryPackOrder\(15\)[^\]]*\]\s*public\s+List<FireCellSaveData>\s+FireCells[^\r\n]*', '', result, flags=re.M)
                    result = re.sub(r'\s*\|\|\s*\(FireCells\?\.Count\s*\?\?\s*0\)\s*>\s*0', '', result)
                return result
    raise ValueError("missing snapshot end: " + name)

def enum_definition(path, name):
    source = path.read_text(encoding="utf-8-sig")
    declaration = re.search(r'public\s+enum\s+' + re.escape(name) + r'\s*\{[^}]*\}', source, re.S)
    if declaration is None: raise ValueError("missing persisted enum: " + name)
    return declaration.group(0)

game_save = root / "Assets/5_Scripts/5-3_GamePlay/Core/Save"
snapshot_source = "using System; using System.Collections.Generic; using System.IO; using MemoryPack; using Newtonsoft.Json; using UnityEngine; using Sirenix.OdinInspector; using FlatWorld.WorldModel;\n" + "\n".join([
    snapshot_definition(game_save / "SaveDataMgr.SnapshotPayload.cs", "FrozenEcologyChunkSaveRecord"),
    snapshot_definition(game_save / "SaveDataMgr.SqliteArchive.cs", "SqliteChunkSnapshot"),
    *[snapshot_definition(root / path, name) for path, name in [
        ("Assets/5_Scripts/5-3_GamePlay/World/Machines/CultivatorLogic.cs", "CultivatorHarvestOutput"),
        ("Assets/5_Scripts/5-3_GamePlay/World/Machines/CultivatorLogic.cs", "CultivatorPlotState"),
        ("Assets/5_Scripts/5-3_GamePlay/World/Machines/CultivatorLogic.cs", "CultivatorState"),
        ("Assets/5_Scripts/5-3_GamePlay/World/Machines/MachineState.cs", "RecipeProcessingState"),
        ("Assets/5_Scripts/5-3_GamePlay/World/Machines/ManualProcessingToolRequirement.cs", "ManualProcessingState"),
        ("Assets/5_Scripts/5-3_GamePlay/World/Machines/FireDrillLogic.cs", "FireDrillRuntimeState"),
        ("Assets/5_Scripts/5-3_GamePlay/World/Machines/StorageLogic.cs", "MachineStorageState"),
        ("Assets/5_Scripts/5-3_GamePlay/World/Machines/FluidMachineState.cs", "FluidMachineState"),
        ("Assets/5_Scripts/5-3_GamePlay/World/Machines/FurnaceLogic.cs", "FurnaceRuntimeState"),
        ("Assets/5_Scripts/5-3_GamePlay/Entities/Item/Modules/Crafting/Mod_Fuel.cs", "FuelData"),
        ("Assets/5_Scripts/5-3_GamePlay/Items/Inventory/Inventory_Furnace.cs", "ModSmeltingData"),
        ("Assets/5_Scripts/5-3_GamePlay/World/Machines/ElectricHeaterLogic.cs", "ElectricHeaterState"),
        ("Assets/5_Scripts/5-3_GamePlay/World/Machines/ElectricLightLogic.cs", "ElectricLightState"),
        ("Assets/5_Scripts/5-3_GamePlay/World/Machines/InductionStoveLogic.cs", "InductionStoveState"),
        ("Assets/5_Scripts/5-3_GamePlay/World/Time/DayTimeSystem.cs", "SerializableGradient"),
        ("Assets/5_Scripts/5-3_GamePlay/World/Time/DayTimeSystem.cs", "SerializableKeyframe"),
        ("Assets/5_Scripts/5-3_GamePlay/World/Time/DayTimeSystem.cs", "SerializableTimeData"),
        ("Assets/5_Scripts/5-3_GamePlay/World/Time/DayTimeSystem.cs", "DayTimeSaveData"),
        ("Assets/5_Scripts/5-3_GamePlay/World/Time/SeasonCalendar.cs", "SeasonCalendarHistoryEntry"),
        ("Assets/5_Scripts/5-3_GamePlay/World/Time/SeasonCalendar.cs", "SeasonCycleSettings"),
        ("Assets/5_Scripts/5-3_GamePlay/World/Environment/SnowCoverState.cs", "SnowCoverState"),
        ("Assets/5_Scripts/5-3_GamePlay/Core/Save/SaveDataMgr.ChunkDifferences.cs", "ChunkSaveRecord"),
        ("Assets/5_Scripts/5-3_GamePlay/Core/Save/SaveDataMgr.ChunkDifferences.cs", "TileCellSaveDelta"),
        ("Assets/5_Scripts/5-3_GamePlay/Core/Save/SaveDataMgr.ChunkDifferences.cs", "GrassCellSaveDelta"),
        ("Assets/5_Scripts/5-3_GamePlay/Core/Save/SaveDataMgr.ChunkDifferences.cs", "RuntimeTileCellSaveDelta"),
        ("Assets/5_Scripts/5-3_GamePlay/Core/Save/SaveDataMgr.Agriculture.cs", "AgricultureCellSaveData"),
        ("Assets/5_Scripts/5-3_GamePlay/Core/Save/SaveDataMgr.TerrainLayers.cs", "SupportCellSaveData"),
        ("Assets/5_Scripts/5-3_GamePlay/Core/Save/SaveDataMgr.TerrainLayers.cs", "GroundLayerCellSaveData"),
        ("Assets/5_Scripts/5-3_GamePlay/Core/Save/SaveDataMgr.TerrainLayers.cs", "GroundCoverLayerSaveData"),
        ("Assets/5_Scripts/5-3_GamePlay/Core/Save/SaveDataMgr.TerrainLayers.cs", "LiquidCellSaveData"),
        ("Assets/5_Scripts/5-3_GamePlay/Core/Save/SaveDataMgr.TerrainLayers.cs", "ContaminationCellSaveData"),
        ("Assets/5_Scripts/5-3_GamePlay/Core/Save/SaveDataMgr.TerrainLayers.cs", "ContaminationValueSaveData"),
        ("Assets/5_Scripts/5-3_GamePlay/Core/Save/SaveDataMgr.WeatherSurface.cs", "SnowCellSaveData")]]])
space_source = (root / "Assets/5_Scripts/5-3_GamePlay/World/Space/SpaceSessionState.cs").read_text(encoding="utf-8-sig")
space_field = re.search(r'\[MemoryPack\.MemoryPackOrder\(\d+\)\]\s*public string SpaceStateJson\s*;', space_source)
if space_field is None: raise ValueError("missing GameSaveData.SpaceStateJson contract")
snapshot_source += "\npublic partial class GameSaveData { " + space_field.group(0) + " }\n"
for path, name in [("Core/Difficulty/GameDifficulty.cs", "GameDifficultyId"),
                   ("World/Environment/WeatherEventScheduler.cs", "WeatherPhase"),
                   ("World/Time/SeasonCalendar.cs", "WorldSeason")]:
    snapshot_source += "\n" + enum_definition(root / "Assets/5_Scripts/5-3_GamePlay" / path, name)
(out / "SnapshotContracts.cs").write_text(snapshot_source, encoding="utf-8")
ET.SubElement(group, "Compile", Include=str(out / "SnapshotContracts.cs"))
ET.SubElement(group, "Analyzer", Include=str(root / "Assets/Packages/MemoryPack.Generator.1.21.3/analyzers/dotnet/cs/MemoryPack.Generator.dll"))
references = [out / "Data.dll", root / "Library/ScriptAssemblies/FlatWorld.ContentCache.dll",
              root / "Library/ScriptAssemblies/NaughtyAttributes.Core.dll",
              root / "Library/ScriptAssemblies/Kybernetik.UltEvents.dll",
              root / "Library/ScriptAssemblies/MemoryPack.Unity.dll"]
references += [world_assembly] + [root / "Library/ScriptAssemblies" / f"{name}.dll" for name in
                                 ("FlatWorld.WorldTopology", "Unity.Mathematics", "Unity.Burst", "Unity.Collections")]
names = {"UnityEngine.CoreModule", "MemoryPack.Core", "FastCloner", "Newtonsoft.Json", "Sirenix.OdinInspector.Attributes"}
for node in ET.parse(root / "Data.csproj").iter():
    if node.tag.split("}")[-1] == "Reference" and node.get("Include") in names:
        references.append(root / next(child.text for child in node if child.tag.split("}")[-1] == "HintPath"))
for path in references:
    ET.SubElement(ET.SubElement(group, "Reference", Include=path.stem), "HintPath").text = str(path)
ET.ElementTree(project).write(out / "Contracts.Tests.csproj", encoding="utf-8", xml_declaration=True)
subprocess.run(["dotnet", "run", "--project", str(out / "Contracts.Tests.csproj"), "-c", "Release"], cwd=root, check=True)
