"""编译正式反应与库存事务源码，不连接或刷新 Unity。"""
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / "Temp/Tests/Petrochemicals"


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    source = (ROOT / "Assets/5_Scripts/5-3_GamePlay/Entities/Item/Definitions/ItemMatterReaction.cs").read_text(encoding="utf-8-sig")
    # 仅裁去依赖场景的温度调度；DTO、编译器和匹配器原文进入同一次编译。
    prefix = source[:source.index("#region 状态推进与反应解析")]
    resolver = source[source.index("public static class ItemReactionResolver"):]
    resolver = resolver[:resolver.rindex("#endregion")]
    generated = OUT / "ProductionReactionDefinitions.cs"
    generated.write_text(prefix + resolver, encoding="utf-8")
    liquid_source = (ROOT / "Assets/5_Scripts/5-3_GamePlay/Items/Food/MixedLiquidContents.cs").read_text(encoding="utf-8-sig")
    liquid_prefix = liquid_source[:liquid_source.index("public static class MixedLiquidContents")]
    storage = liquid_source[liquid_source.index("    #region 组分存储"):]
    storage = storage[:storage.index("    #endregion")] + "    #endregion\n"
    restore = liquid_source[liquid_source.index("    public static LiquidContainerState Clone"):]
    restore = restore[:restore.index("    #endregion")]
    generated_liquid = OUT / "ProductionLiquidStorage.cs"
    generated_liquid.write_text(liquid_prefix + "public static class MixedLiquidContents\n{\n" + storage + restore + "}\n", encoding="utf-8")
    heat_source = (ROOT / "Assets/5_Scripts/5-3_GamePlay/Items/Food/Mod_VesselHeating.cs").read_text(encoding="utf-8-sig")
    heat_method = heat_source[heat_source.index("    private static bool ProcessComponents("):heat_source.index("    private static CraftingTransaction PrepareOutput(")]
    generated_heat = OUT / "ProductionLiquidHeatProcessing.cs"
    generated_heat.write_text("using System;\nusing System.Collections.Generic;\nusing UnityEngine;\npublic static class PetrochemicalHeatTestEntry\n{\n"
        "public const float TemperatureTransferPerSecond = 10f;\n"
        "public static bool Process(LiquidContainerState state, Func<string, int, CraftingTransaction> prepare, Action persist) => ProcessComponents(state, 100f, 3f, prepare, persist);\n"
        + heat_method + "}\n", encoding="utf-8")
    project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
    props = ET.SubElement(project, "PropertyGroup")
    for name, value in {"OutputType": "Exe", "TargetFramework": "net8.0", "EnableDefaultCompileItems": "false", "ImplicitUsings": "enable", "Nullable": "disable"}.items():
        ET.SubElement(props, name).text = value
    group = ET.SubElement(project, "ItemGroup")
    for file in [generated, generated_liquid, generated_heat, Path(__file__).with_name("Program.cs"), Path(__file__).with_name("TestDoubles.cs"),
                 ROOT / "Assets/5_Scripts/5-3_GamePlay/World/Environment/ThermalRuntime.cs",
                 ROOT / "Assets/5_Scripts/5-3_GamePlay/Items/Crafting/CraftingMaterialAllocator.cs",
                 ROOT / "Assets/5_Scripts/5-3_GamePlay/Items/Crafting/CraftingTransaction.cs"]:
        ET.SubElement(group, "Compile", Include=str(file))
    reference = ET.SubElement(group, "Reference", Include="Newtonsoft.Json")
    ET.SubElement(reference, "HintPath").text = "$(MSBuildSDKsPath)/../Newtonsoft.Json.dll"
    path = OUT / "Petrochemicals.Tests.csproj"
    ET.ElementTree(project).write(path, encoding="utf-8", xml_declaration=True)
    result = subprocess.run(["dotnet", "run", "--project", str(path), "--configuration", "Release", "--", str(ROOT)], cwd=ROOT)
    raise SystemExit(result.returncode)


if __name__ == "__main__":
    main()
