"""Read-only audit of FlatWorld content dependency definitions.

This is an upper-bound/static reachability check, not a Unity play test.
Run at the repository root: python Tools/ContentAudit/audit_progression.py
"""

import collections
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
CONFIG = ROOT / "Assets/StreamingAssets/GameConfig"
MECHANICAL = ROOT / "Assets/Resources/Config/Mechanical/mechanical-catalog.json"


def load(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def packages(directory, manifest, field):
    base = CONFIG / directory
    return [
        (entry["path"], row)
        for entry in load(base / manifest)["packages"]
        if entry.get("enabled", True)
        for row in load(base / entry["path"]).get(field, [])
    ]


def requirements(entries):
    return [
        (entry.get("match", "exact_item"), entry.get("tag") if entry.get("match") == "tag" else entry.get("itemId"))
        for entry in entries
    ]


def outputs(entries):
    return [entry["itemId"] for entry in entries if entry.get("itemId")]


def main():
    records = packages("Items", "item-manifest.json", "items")
    items = {row["id"]: row for _, row in records}
    crafting = packages("Recipes", "recipe-manifest.json", "recipes")
    ecology = packages("WorldGeneration/NaturalItems", "natural-item-manifest.json", "ecologyRules")
    cave_resources = packages("WorldGeneration/NaturalItems", "natural-item-manifest.json", "caveResourceRules")
    actors = packages("Actors", "actor-manifest.json", "actors")
    tiles = packages("Tiles", "tile-manifest.json", "tiles")
    loot = {table["id"]: table["entries"] for table in load(CONFIG / "LootTables/loot-tables.json")["lootTables"]}
    liquids = load(CONFIG / "Liquids/liquids.json")["liquids"]
    mechanical = load(MECHANICAL)

    # One transformation = (source label, input requirements, item/fluid outputs, gate).
    transforms = []
    producer = collections.defaultdict(list)
    source_roots = collections.defaultdict(set)
    for name, recipe in crafting:
        transform = ("制作:" + str(recipe.get("id")), requirements(recipe.get("inputs", [])),
                     outputs(recipe.get("outputs", [])), ("workbench" if recipe.get("requiredStation") == "workbench" else ""))
        transforms.append(transform)
    for _, item in records:
        for station, process in (item.get("processing") or {}).items():
            station_item = {"grind": "StoneMortar", "weave": "MechanicalLoom_Summoner"}.get(station, "")
            transforms.append(("物品加工:" + item["id"] + "/" + station,
                               [("exact_item", item["id"])], outputs(process.get("outputs", [])), station_item))
        for reaction in item.get("reactions") or []:
            product = outputs(reaction.get("outputs", []))
            if reaction.get("liquidOutput", {}).get("liquidId"):
                product.append("fluid:" + reaction["liquidOutput"]["liquidId"])
            transforms.append(("加热反应:" + reaction["id"], requirements(reaction.get("inputs", [])), product, "heat"))
        for transition in (item.get("matter") or {}).get("transitions", []):
            if transition.get("outputItemId"):
                transforms.append(("温湿变化:" + transition.get("id", item["id"]),
                                   [("exact_item", item["id"])], [transition["outputItemId"]], ""))
        # Crops expose harvest results in modules.cropYield, not in recipes/loot.
        crop = (item.get("modules") or {}).get("cropYield") or {}
        for entry in (crop.get("parameters") or {}).get("outputs", []):
            if entry.get("itemId") and entry.get("probability", 1) > 0:
                transforms.append(("成熟采收:" + item["id"], [("exact_item", item["id"])],
                                   [entry["itemId"]], ""))
        # The ground-cover harvesting tool has an explicit grass yield
        # independent of the usual crafting/loot catalog.
        for module in (item.get("modules") or {}).values():
            if module.get("prefab") == "Module_GroundCoverHarvest":
                yield_id = (module.get("parameters") or {}).get("grassYieldItemId")
                if yield_id:
                    transforms.append(("草地采集:" + item["id"],
                                       [("exact_item", item["id"])], [yield_id], "grass"))
        # RuntimeItemMatterDefinition.Compiler adds wood.charcoal for Wood-tagged items.
        if "Wood" in (item.get("tags") or []):
            transforms.append(("木材炭化:" + item["id"], [("exact_item", item["id"])],
                               ["Charcoal", "WoodAsh"], "heat"))

    station_map = {
        "hand_drill": "HandDrill_Summoner",
        "hand_forge": "CopperAnvil_Summoner",
        "mechanical_hammer": "MechanicalHammer_Summoner",
        "sawmill": "Sawmill_Summoner",
        "millstone": "Millstone_Summoner",
        "mechanical_loom": "MechanicalLoom_Summoner",
    }
    for process in mechanical.get("Processes", []):
        station = process["Station"]
        transforms.append(("机器加工:" + station + "/" + process["Input"],
                           [("exact_item", process["Input"])],
                           [entry["ItemName"] for entry in process.get("Outputs", []) if entry.get("ItemName")],
                           station_map.get(station, "unknown_station:" + station)))

    for liquid in liquids:
        lid = "fluid:" + liquid["id"]
        if liquid.get("solidification", {}).get("outputItemId"):
            transforms.append(("液体凝固:" + liquid["id"], [("exact_item", lid)],
                               [liquid["solidification"]["outputItemId"]], "solidify"))
        if liquid.get("heatProcess", {}).get("outputItemId"):
            transforms.append(("液体加热:" + liquid["id"], [("exact_item", lid)],
                               [liquid["heatProcess"]["outputItemId"]], "heat"))

    # Electrical/fluid reactions are conditional on the corresponding
    # machine being installed, powered and supplied with real fluid.
    for node in mechanical.get("Nodes", []):
        for reaction in (node.get("Fluid") or {}).get("Reactions", []):
            ins = [("exact_item", "fluid:" + entry["FluidId"]) for entry in reaction.get("Inputs", []) if entry.get("FluidId")]
            products = ["fluid:" + entry["FluidId"] for entry in reaction.get("Outputs", []) if entry.get("FluidId")]
            if node["Id"] == "WaterElectrolyzer":
                gate = "WaterElectrolyzer_Summoner"
            else:
                gate = node["Id"] + "_Summoner"
            transforms.append(("流体反应:" + reaction["Id"], ins, products, gate))

    for path, rule in ecology:
        chance = rule.get("spawnChance", 0)
        multiplier = rule.get("spawnChanceMultiplier", 1)
        if rule.get("itemId") and isinstance(chance, (int, float)) and chance > 0 and multiplier > 0:
            source_roots[rule["itemId"]].add("生态:" + path)
    for path, rule in cave_resources:
        if rule.get("itemId") and 0 <= rule.get("veinThreshold", 1) < 1:
            source_roots[rule["itemId"]].add("洞穴矿脉:" + path)
    for path, actor in actors:
        table = actor.get("lootTableId")
        if table in loot:
            for entry in loot[table]:
                if entry.get("probability", 1) > 0 and entry.get("itemId"):
                    source_roots[entry["itemId"]].add("可能生物掉落:" + actor["id"])
    for liquid in liquids:
        if liquid.get("worldWater") and liquid["id"] in ("core:dirty_water", "core:sea_water", "core:salt_water"):
            source_roots["fluid:" + liquid["id"]].add("可能天然水源")
    # Natural ground tile extraction; terrain presence/tool tier are runtime gates.
    for _, tile in tiles:
        harvest = tile.get("groundHarvest") or {}
        if harvest.get("itemId"):
            needed_tool = {
                "Shovel": "Shovel_Wood", "Pickaxe": "Pickaxe_Stone"
            }.get(harvest.get("requiredTool"), "")
            transforms.append(("挖掘自然地块:" + tile["id"],
                               [("exact_item", needed_tool)] if needed_tool else [],
                               [harvest["itemId"]], "terrain"))

    references = []
    for label, inputs, product, gate in transforms:
        for match, iid in inputs:
            if match == "exact_item" and iid and not iid.startswith("fluid:") and iid not in items:
                references.append((label, "input", iid))
        for iid in product:
            if not iid.startswith("fluid:") and iid not in items:
                references.append((label, "output", iid))
            producer[iid].append(label)
    for iid, item in items.items():
        if item.get("parent") and item["parent"] not in items:
            references.append(("parent:" + iid, "parent", item["parent"]))

    def inherited_loot(iid):
        seen = set()
        while iid in items and iid not in seen:
            seen.add(iid)
            entry = items[iid]
            if entry.get("lootTableId"):
                tid = entry["lootTableId"]
                return tid, loot.get(tid, [])
            iid = entry.get("parent")
        return "", []

    def has_tag(have, tag):
        return any(tag in items[iid].get("tags", []) for iid in have if iid in items)

    def reachable(with_all_loot):
        have = set(source_roots)
        reasons = {item: "自然或生物候选来源" for item in have}
        if with_all_loot:
            for label, entries in loot.items():
                for entry in entries:
                    iid = entry.get("itemId")
                    if iid and entry.get("probability", 1) > 0 and iid not in have:
                        have.add(iid)
                        reasons[iid] = "任意掉落表候选:" + label
        while True:
            added = 0
            for iid in list(have):
                table, entries = inherited_loot(iid)
                for entry in entries:
                    out = entry.get("itemId")
                    if out and entry.get("probability", 1) > 0 and out not in have:
                        have.add(out)
                        reasons[out] = "对象掉落:" + iid + "/" + table
                        added += 1
            for label, needed, produced, gate in transforms:
                if gate and gate not in ("heat", "solidify", "grass", "terrain") and (
                    (gate == "workbench" and "WorkBench_Summoner" not in have)
                    or (gate != "workbench" and gate not in have)
                ):
                    continue
                if not all((key in have if match == "exact_item" else has_tag(have, key))
                           for match, key in needed if key):
                    continue
                for out in produced:
                    if out not in have:
                        have.add(out)
                        reasons[out] = label
                        added += 1
            if added == 0:
                return have, reasons

    reached, proof = reachable(False)
    optimistic, _ = reachable(True)
    print("COUNTS", json.dumps({
        "itemDefinitions": len(records), "uniqueItemIds": len(items),
        "craftingRecipes": len(crafting), "allDefinedTransformations": len(transforms),
        "ecologyRules": len(ecology), "caveResourceRules": len(cave_resources),
        "actorDefinitions": len(actors),
        "sourceCandidateIds": len(source_roots), "staticallyReachableItemIds": len(reached & set(items)),
        "optimisticallyReachableItemIds": len(optimistic & set(items)),
        "unprovenItemIds": len(set(items) - reached), "invalidReferences": len(references)
    }, ensure_ascii=False))
    print("INVALID_REFERENCES", references)
    print("MACHINE_STATIONS", dict(collections.Counter(p["Station"] for p in mechanical["Processes"])))
    special = [
        "Ore_Stone", "Log", "Stick_Wood", "Fiber", "Rope", "StoneSlab", "ChippedTool",
        "DrilledLog", "HandDrill_Summoner", "DrilledPlank", "DrilledStick",
        "Ingot_Copper", "Ingot_RawIron", "Ingot_WroughtIron", "Ingot_Steel",
        "CopperAnvil_Summoner", "CopperPlate", "IronPlate", "Glass", "Cloth", "Rubber",
        "MechanicalHammer_Summoner", "WorkBench_Summoner", "Smelter_Summoner",
        "BlastFurnace_Summoner", "Generator_Basic_Summoner", "WaterElectrolyzer_Summoner",
        "GasTankBlock_Iron_Summoner", "Battery_Basic_Summoner", "Wire_Copper_Summoner",
        "FluidPipe_Summoner", "GasPump_Mechanical_Summoner", "fluid:core:hydrogen", "fluid:core:oxygen",
    ]
    special += sorted(i for i in items if i.startswith("Ship") and i.endswith("_Summoner"))
    special += ["SpaceCultivator_Summoner"]
    print("CRITICAL_PROGRESSION")
    for iid in special:
        marker = "possible" if iid in reached else "only_with_any_loot" if iid in optimistic else "not_proven"
        print(iid, marker, proof.get(iid, ""), "; ".join(producer[iid][:3]))
    print("NO_SOURCE_PROVEN_NON_ABSTRACT")
    for iid in sorted(set(items) - reached):
        item = items[iid]
        if not item.get("abstract"):
            print(iid, "|", item.get("gameName", ""), "|", "pickup=" + str(item.get("canBePickedUp")),
                  "|", "potential_producers=" + ",".join(producer[iid][:3]))
    print("NOTE: This is static candidate reachability, not a proof of player access or viable energy/temperature/quantities.")


if __name__ == "__main__":
    main()
