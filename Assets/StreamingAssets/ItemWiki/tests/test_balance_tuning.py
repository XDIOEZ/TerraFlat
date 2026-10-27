"""数值调节保存契约：隔离临时配置，不写项目真实 Item JSON。"""

from __future__ import annotations

from pathlib import Path
import copy
import json
import sys
import tempfile
import unittest
from unittest import mock

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import balance_history
import wiki_server as wiki


class BalanceTuningTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.history = balance_history.BalanceHistory(self.root)

    def test_history_step_adjust_undo_and_status(self):
        before = {"modules": {"damage": {"parameters": {"DamageValues": {"Physical": 10}}}}}
        after = copy.deepcopy(before)
        after["modules"]["damage"]["parameters"]["DamageValues"]["Physical"] = 12
        path = "modules.damage.parameters.DamageValues.Physical"
        data, request = self.history.validate_change({
            "kind": "adjust", "fieldPath": path, "before": 10, "after": 12,
            "stepBefore": 2, "stepAfter": 2, "direction": 1, "streak": 1,
            "feedback": "偏低"
        }, "Knife_Test", before, after)
        request_id = self.history.committed(data, request)
        self.assertEqual(2, self.history.item_state("knife_test")[path]["step"])
        self.assertEqual(1, len(self.history.item_state("knife_test")[path]["history"]))
        self.assertEqual("pending", self.history.live_status(request_id)["state"])
        self.assertTrue((self.history.requests / (request_id + ".json")).exists())
        self.history.set_step("Knife_Test", path, 0.25)
        self.assertEqual(0.25, self.history.item_state("Knife_Test")[path]["step"])
        undo_data, undo_request = self.history.validate_change({
            "kind": "undo", "fieldPath": path, "before": 12, "after": 10,
            "stepBefore": 0.25, "stepAfter": 2, "direction": 0, "streak": 0
        }, "Knife_Test", after, before)
        self.history.committed(undo_data, undo_request)
        self.assertEqual([], self.history.item_state("knife_test")[path]["history"])
        self.assertEqual(10, undo_request["value"])

    def test_legacy_asset_import_preserves_original(self):
        self.history.legacy.parent.mkdir(parents=True)
        source = (
            "records:\n"
            "  - Key: json|Assets/StreamingAssets/GameConfig/Items/shells/weapons.json|Knife_Test|modules.damage.parameters.DamageValues.Physical\n"
            "    Step: 0.5\n"
            "    LastDirection: -1\n"
            "    DirectionStreak: 2\n"
            "    History:\n"
            "    - Before: 10\n"
            "      After: 9.5\n"
            "      StepBefore: 0.5\n"
            "      StepAfter: 0.5\n"
            "      DirectionBefore: 0\n"
            "      StreakBefore: 0\n"
            "  - Key: prefab|SomePath|Damage.Cutting\n"
            "    Step: 8\n"
            "    History: []\n"
        )
        self.history.legacy.write_text(source, encoding="utf-8")
        rows = self.history.item_state("knife_test")
        self.assertEqual(0.5, rows["modules.damage.parameters.DamageValues.Physical"]["step"])
        self.assertEqual(1, len(rows["modules.damage.parameters.DamageValues.Physical"]["history"]))
        self.assertEqual(source, self.history.legacy.read_text(encoding="utf-8"))
        self.assertTrue(self.history.path.exists())

    def test_disallow_unknown_and_non_gameplay_fields(self):
        self.assertRaises(ValueError, balance_history.normalize_path, "modules.foo.parameters.$transform.x")
        self.assertRaises(ValueError, balance_history.normalize_path, "__proto__.polluted")
        self.assertRaises(ValueError, balance_history.normalize_path, "visual.color.r")
        self.assertRaises(ValueError, balance_history.value_at, {"weight": True}, "weight")

    def test_wiki_save_reuses_validation_backup_and_parent_not_modified(self):
        item_root = self.root / "Assets/StreamingAssets/GameConfig/Items"
        item_root.mkdir(parents=True)
        shells = item_root / "shells"
        shells.mkdir()
        manifest = item_root / "item-manifest.json"
        manifest.write_text(json.dumps({"schemaVersion": 1, "packages": [
            {"id": "test", "path": "shells/test.json", "enabled": True}
        ]}), encoding="utf-8")
        data_file = shells / "test.json"
        data_file.write_text(json.dumps({"schemaVersion": 1, "items": [
            {"id": "Base", "abstract": True, "shellPrefab": "Prop", "gameName": "父模板",
             "modules": {"damage": {"parameters": {"DamageValues": {"Physical": 5}}}}},
            {"id": "Knife_Test", "parent": "Base", "gameName": "小刀"}
        ]}), encoding="utf-8")
        loot_file = self.root / "loot.json"
        loot_file.write_text('{"schemaVersion":1,"lootTables":[]}', encoding="utf-8")
        modified_file = self.root / "meta.json"
        new_source = {"id": "Knife_Test", "parent": "Base", "gameName": "小刀",
                      "modules": {"damage": {"parameters": {"DamageValues": {"Physical": 6}}}}}
        event = {"kind": "adjust", "fieldPath": "modules.damage.parameters.DamageValues.Physical",
                 "before": 5, "after": 6, "stepBefore": 1, "stepAfter": 1,
                 "direction": 1, "streak": 1, "feedback": "偏低"}
        with mock.patch.multiple(wiki,
            PROJECT_ROOT=self.root,
            ITEM_ROOT=item_root, ITEM_MANIFEST=manifest,
            LOOT_TABLE_FILE=loot_file, ITEM_METADATA_FILE=modified_file,
            BACKUP_ROOT=self.root / "Library/FlatWorldItemWiki/Backups",
            BALANCE_HISTORY=self.history):
            result = wiki.save_item({
                "definitionKind": "item", "itemId": "Knife_Test",
                "packagePath": "shells/test.json",
                "expectedHash": wiki.file_sha256(data_file),
                "source": new_source, "balanceChange": event
            })
        self.assertTrue(result["ok"])
        self.assertTrue(result["tuningRequestId"])
        self.assertTrue((self.root / result["backup"]).exists())
        items = json.loads(data_file.read_text(encoding="utf-8"))["items"]
        self.assertEqual(5, items[0]["modules"]["damage"]["parameters"]["DamageValues"]["Physical"])
        self.assertEqual(6, items[1]["modules"]["damage"]["parameters"]["DamageValues"]["Physical"])
        self.assertEqual(1, len(self.history.item_state("Knife_Test")[event["fieldPath"]]["history"]))


if __name__ == "__main__":
    unittest.main()
