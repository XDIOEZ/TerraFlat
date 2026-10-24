"""Verify built-in actor sway configuration without touching the running Editor."""
from __future__ import annotations

import copy
import json
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[3]


def load_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def merge(parent: dict, child: dict) -> dict:
    result = copy.deepcopy(parent)
    for key, value in child.items():
        result[key] = merge(result.get(key, {}), value) if isinstance(value, dict) else copy.deepcopy(value)
    return result


def main() -> int:
    actors_root = ROOT / "Assets/StreamingAssets/GameConfig/Actors"
    definitions = {}
    for package in load_json(actors_root / "actor-manifest.json")["packages"]:
        if not package.get("enabled", True):
            continue
        for actor in load_json(actors_root / package["path"])["actors"]:
            if actor["id"] in definitions:
                raise ValueError("Duplicate actor: " + actor["id"])
            definitions[actor["id"]] = actor

    def resolve(actor_id: str, chain: tuple = ()) -> dict:
        if actor_id in chain:
            raise ValueError("Actor inheritance cycle: " + actor_id)
        own = definitions[actor_id]
        resolved = merge(resolve(own["parent"], chain + (actor_id,)), own) if own.get("parent") else copy.deepcopy(own)
        for slot in own.get("removeModules", []):
            resolved.get("modules", {}).pop(slot, None)
        return resolved

    entries = {}
    for path in (ROOT / "Assets/AddressableAssetsData/AssetGroups").glob("*.asset"):
        for guid, address in re.findall(r"  - m_GUID: ([0-9a-f]{32})\r?\n    m_Address: (.+)",
                                       path.read_text(encoding="utf-8-sig")):
            address = address.strip().strip('"')
            if address in entries and entries[address] != guid:
                raise ValueError("Duplicate Addressables address: " + address)
            entries[address] = guid

    count = 0
    for actor_id, own in definitions.items():
        if own.get("abstract", False):
            continue
        actor = resolve(actor_id)
        animation = actor["modules"]["animator"]
        assert animation["enabled"] is True, actor_id + ": animator disabled"
        assert animation["id"] == "动画接收模块", actor_id + ": wrong animator capability"
        assert animation["parameters"]["visualMode"] in (1, "SingleSprite"), actor_id + ": not sway mode"
        address = actor["visual"]["spriteAddress"]
        match = re.fullmatch(r"(.+)\[([^\[\]]+)\]", address)
        path, sub_sprite = match.groups() if match else (address, None)
        assert path.startswith("Assets/"), actor_id + ": missing Editor-safe sprite path"
        assert (ROOT / path).is_file(), actor_id + ": missing texture"
        meta = (ROOT / (path + ".meta")).read_text(encoding="utf-8-sig")
        guid = re.search(r"^guid: ([0-9a-f]{32})$", meta, re.M).group(1)
        assert entries.get(path) == guid, actor_id + ": texture not registered for Player builds"
        if sub_sprite:
            assert re.search(r"^      name: " + re.escape(sub_sprite) + r"\s*$", meta, re.M), actor_id + ": missing sprite slice"
        if actor_id == "SnowLeopard":
            assert sub_sprite == "MiniWolf_Idle_1", "SnowLeopard must use its own initial frame"
        if actor_id == "Zombie":
            assert sub_sprite == "Character_Zombie_Idle_Right", "Zombie must not inherit Wolf's sprite"
        print(f"PASS {actor_id}: SingleSprite -> {sub_sprite or Path(path).stem}")
        count += 1
    assert count >= 12, "Existing built-in species are missing"

    source = (ROOT / "Assets/5_Scripts/5-3_GamePlay/Entities/Item/Modules/Animation/Mod_AnimatorController.SingleSprite.cs").read_text(encoding="utf-8-sig")
    start = source.index("private void ApplyInitialSingleSpriteFrame()")
    end = source.index("private bool IsVisualOnlyRoot", start)
    initialization = source[start:end]
    assert "targetSpriteRenderer.sprite = definition.Sprite" in initialization
    for old_dependency in (".Play(", ".Update(", ".Rebind(", "TryResolveState(", "Visual.AnimationState"):
        assert old_dependency not in initialization, "Sway initialization depends on Animator: " + old_dependency
    assert "SampleInitialSingleSpriteFrame" not in source
    print(f"PASS {count} actor configurations, Sprite imports, Player addresses, and Animator-free initialization")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (AssertionError, KeyError, ValueError, OSError) as error:
        print("FAIL:", error, file=sys.stderr)
        raise SystemExit(1)
