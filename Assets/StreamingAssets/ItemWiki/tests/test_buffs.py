"""BUFF 编辑事务回归：只向临时目录写入，不触碰正式游戏配置。"""
import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import wiki_server as wiki
from buff_validation import validate_definition, validate_catalogs


class BuffWikiTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.config = self.root / "Buffs"
        self.config.mkdir()
        self.manifest = self.config / "buff-manifest.json"
        self.source = {"id": "燃烧", "durationSeconds": 5.0, "tickIntervalSeconds": 1.0,
                       "stackMode": "add_stacks", "maxStacks": 5,
                       "effects": [{"phase": "tick", "typeId": "core:true_damage", "value": 1.0, "scaleWithStacks": True}]}
        self.target = self.config / "damage.json"
        self.target.write_text(json.dumps({"schemaVersion": 1, "buffs": [self.source, {"id": "其它", "effects": []}]}, ensure_ascii=False), encoding="utf-8")
        self.manifest.write_text(json.dumps({"schemaVersion": 1, "packages": [{"id": "damage", "path": "damage.json"}]}), encoding="utf-8")
        self.addCleanup(patch.stopall)
        for name, value in {"PROJECT_ROOT": self.root, "BUFF_ROOT": self.config,
                            "BUFF_MANIFEST": self.manifest, "BACKUP_ROOT": self.root / "backups"}.items():
            patch.object(wiki, name, value).start()
        self.payload = {"definitionKind": "buff", "itemId": "燃烧", "packagePath": "damage.json",
                        "expectedHash": wiki.file_sha256(self.target), "source": copy.deepcopy(self.source)}

    def test_atomic_save_keeps_other_entries_and_backup(self):
        original = self.target.read_bytes()
        self.payload["source"]["effects"][0]["value"] = 2
        result = wiki.save_buff(self.payload)
        self.assertTrue(result["ok"])
        self.assertEqual((self.root / result["backup"]).read_bytes(), original)
        values = wiki.read_json(self.target)["buffs"]
        self.assertEqual(values[0]["effects"][0]["value"], 2)
        self.assertEqual(values[1], {"id": "其它", "effects": []})

    def test_stale_edit_is_rejected(self):
        self.target.write_bytes(self.target.read_bytes() + b"\n")
        original = self.target.read_bytes()
        with self.assertRaises(wiki.WikiConflictError):
            wiki.save_buff(self.payload)
        self.assertEqual(self.target.read_bytes(), original)

    def test_invalid_values_never_modify_file(self):
        for field, value in [("maxStacks", 0), ("maxStacks", 2.5), ("maxStacks", True),
                             ("tickIntervalSeconds", 0), ("tickIntervalSeconds", float("nan")),
                             ("durationSeconds", 0), ("visualBaseScale", -1), ("unknown", 1)]:
            with self.subTest(field=field, value=value):
                candidate = copy.deepcopy(self.payload)
                candidate["source"][field] = value
                original = self.target.read_bytes()
                with self.assertRaises(wiki.WikiValidationError):
                    wiki.save_buff(candidate)
                self.assertEqual(self.target.read_bytes(), original)

    def test_path_manifest_and_type_boundaries(self):
        for changes in ({"packagePath": "../outside.json"}, {"packagePath": "new.json"},
                        {"definitionKind": "item"}, {"itemId": "changed"}):
            with self.subTest(changes=changes):
                with self.assertRaises(wiki.WikiValidationError):
                    wiki.save_buff({**self.payload, **changes})
        self.manifest.write_text(json.dumps({"schemaVersion": 1, "packages": [{"id": "damage", "path": "damage.json", "enabled": False}]}), encoding="utf-8")
        with self.assertRaises(wiki.WikiValidationError):
            wiki.save_buff(self.payload)

    def test_nullable_duration_and_effect_phase_validation(self):
        permanent = copy.deepcopy(self.source)
        permanent["durationSeconds"] = None
        validate_definition(permanent)
        permanent["effects"][0]["phase"] = "start"
        with self.assertRaises(ValueError):
            validate_definition(permanent)

    def test_duplicate_buff_rejected_across_packages(self):
        root = {"schemaVersion": 1, "buffs": [self.source]}
        with self.assertRaises(ValueError):
            validate_catalogs({"one": root, "two": root}, [{"id": "one"}, {"id": "two"}])

    def test_public_server_refuses_buff_write(self):
        handler = object.__new__(wiki.WikiRequestHandler)
        handler.public_readonly = True
        handler.path = "/api/buffs/save"
        responses = []
        handler.send_json = lambda status, body: responses.append((status, body))
        handler.do_POST()
        self.assertEqual(responses[0][0], 403)
        self.assertFalse(responses[0][1]["ok"])


if __name__ == "__main__":
    unittest.main()
