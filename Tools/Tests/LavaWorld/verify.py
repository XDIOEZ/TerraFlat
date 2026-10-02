"""Validate volcanic content and run managed generation checks without opening Unity.

Requires Unity's existing Bee response file and a local .NET 8 SDK. All build
outputs stay in ignored Temp/LavaValidation, never in Library/ScriptAssemblies.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
CONFIG = ROOT / "Assets/StreamingAssets/GameConfig"
OUTPUT = ROOT / "Temp/LavaValidation"


def load(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def check_content() -> None:
    catalog = {}
    for package in load(CONFIG / "Items/item-manifest.json")["packages"]:
        if not package.get("enabled", True):
            continue
        for item in load(CONFIG / "Items" / package["path"])["items"]:
            assert item["id"] not in catalog, f"Duplicate item: {item['id']}"
            catalog[item["id"]] = item

    def resolved(item_id: str) -> dict:
        item = catalog[item_id]
        result = resolved(item["parent"]) if item.get("parent") else {}
        def merge(base: dict, change: dict) -> dict:
            result = dict(base)
            for key, value in change.items():
                result[key] = merge(result[key], value) if isinstance(value, dict) and isinstance(result.get(key), dict) else value
            return result
        return merge(result, item)

    bucket = resolved("IronBucket")
    assert not bucket["stackable"] and bucket["worldDropBehavior"] == "interactive"
    assert bucket["modules"]["vessel"]["parameters"]["capacity"] == 8
    assert not resolved("Mine_Obsidian")["canBePickedUp"] and resolved("Obsidian")["canBePickedUp"]
    for item_id in ("Dagger_Obsidian", "Spear_Obsidian"):
        modules = resolved(item_id)["modules"]
        assert modules["damage"]["prefab"] == "Mod_Damage"
        assert modules["animation"]["prefab"] == "Module_Weapon_AnimationAction"
    packages = load(CONFIG / "Recipes/recipe-manifest.json")["packages"]
    for name in ("obsidian", "iron_bucket"):
        assert any(p["path"] == f"crafting/{name}.json" and p.get("enabled", True) for p in packages)
        for recipe in load(CONFIG / f"Recipes/crafting/{name}.json")["recipes"]:
            for part in recipe["inputs"] + recipe["outputs"]:
                assert part["itemId"] in catalog and part["amount"] > 0
    recipe = load(CONFIG / "Recipes/crafting/iron_bucket.json")["recipes"][0]
    assert recipe["inputs"] == [{"match": "exact_item", "itemId": "Ingot_WroughtIron", "amount": 6}]
    lava = next(v for v in load(CONFIG / "Liquids/liquids.json")["liquids"] if v["id"] == "core:lava")
    assert not lava["drinkable"] and not lava["worldWater"]["waterContact"]
    assert lava["worldWater"]["temperature"] == 1100 and lava["worldWater"]["contactDamagePerSecond"] > 0
    rules = load(CONFIG / "WorldGeneration/NaturalItems/surface-stone.json")["ecologyRules"]
    for item_id in ("Obsidian", "Mine_Obsidian"):
        rule = next(r for r in rules if r["itemId"] == item_id)
        assert rule["requiredEnvironmentLayer"] == "lava.shore" and rule["minimumEnvironmentValue"] > 0
    print("PASS item inheritance, live manifests, recipes, bucket capacity, lava heat and shoreline content")


def run(command: list[str]) -> None:
    subprocess.run(command, cwd=ROOT, check=True)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--skip-compile", action="store_true", help="Reuse a freshly compiled Temp WorldModel DLL")
    args = parser.parse_args()
    check_content()
    OUTPUT.mkdir(parents=True, exist_ok=True)
    response = max((ROOT / "Library/Bee").rglob("FlatWorld.WorldModel.rsp"), key=lambda p: p.stat().st_mtime)
    text = response.read_text(encoding="utf-8-sig")
    references = [Path(p) if Path(p).is_absolute() else ROOT / p for p in re.findall(r'^-r:"([^"]+)"', text, re.M)]
    engine = next(p for p in references if p.name == "UnityEngine.CoreModule.dll")
    editor_data = engine.parents[2]
    if not args.skip_compile:
        text = re.sub(r'^-out:.*$', '-out:Temp/LavaValidation/FlatWorld.WorldModel.dll', text, flags=re.M)
        text = re.sub(r'^-refout:.*$', '-refout:Temp/LavaValidation/FlatWorld.WorldModel.ref.dll', text, flags=re.M)
        source = "Assets/5_Scripts/5-0_WorldModel/Generation/DeterministicChunkGenerator.Lava.cs"
        if source not in text:
            text += "\n" + source
        rsp = OUTPUT / "WorldModel.rsp"
        rsp.write_text(text, encoding="utf-8")
        run([str(editor_data / "NetCoreRuntime/dotnet.exe"), str(editor_data / "DotNetSdkRoslyn/csc.dll"), "@" + str(rsp)])
    project = ET.Element("Project", Sdk="Microsoft.NET.Sdk")
    group = ET.SubElement(project, "PropertyGroup")
    for key, value in {"OutputType": "Exe", "TargetFramework": "net8.0", "EnableDefaultCompileItems": "false"}.items():
        ET.SubElement(group, key).text = value
    group = ET.SubElement(project, "ItemGroup")
    ET.SubElement(group, "Compile", Include=str(Path(__file__).with_name("Program.cs")))
    paths = [OUTPUT / "FlatWorld.WorldModel.dll", engine]
    paths += [ROOT / "Library/ScriptAssemblies" / f"{name}.dll" for name in
              ("FlatWorld.WorldTopology", "Unity.Mathematics", "Unity.Burst", "Unity.Collections")]
    for path in paths:
        assert path.exists(), f"Missing Unity dependency: {path}"
        ET.SubElement(ET.SubElement(group, "Reference", Include=path.stem), "HintPath").text = str(path)
    project_path = OUTPUT / "LavaWorld.Tests.csproj"
    ET.ElementTree(project).write(project_path, encoding="utf-8", xml_declaration=True)
    run(["dotnet", "run", "--project", str(project_path), "--configuration", "Release"])


if __name__ == "__main__":
    main()
