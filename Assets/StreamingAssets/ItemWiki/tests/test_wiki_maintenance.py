"""物品字典的继承、服务身份和公开资源边界检查，不改游戏数据。"""
import http.client
import json
from pathlib import Path
import sys
import threading
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import wiki_server as wiki


class WikiMaintenanceTests(unittest.TestCase):
    def test_inheritance_matches_runtime(self):
        sources = {
            "base": {"id": "Base", "formerIds": ["Former"], "processing": {"grind": {}}, "modules": {"keep": {"prefab": "A", "parameters": {"old": 1}}, "remove": {"prefab": "X"}}},
            "child": {"id": "Child", "parent": "Base", "removeModules": ["remove"], "modules": {"keep": {"prefab": "B", "parameters": {"new": 2}}}},
        }
        result = wiki.resolve_items(sources)["child"]
        self.assertNotIn("processing", result)
        self.assertNotIn("formerIds", result)
        self.assertNotIn("remove", result["modules"])
        self.assertEqual({"new": 2}, result["modules"]["keep"]["parameters"])
        self.assertIn("processing", sources["base"])

    def test_existing_project_can_be_reused(self):
        old = {"service": "FlatWorldItemWiki", "publicReadOnly": False, "writable": True, "projectRoot": str(wiki.PROJECT_ROOT), "version": 2}
        self.assertTrue(wiki.is_same_wiki(old, False))
        self.assertFalse(wiki.is_same_wiki({**old, "projectRoot": "other-project"}, False))
        self.assertFalse(wiki.is_same_wiki(old, True))
        self.assertTrue(wiki.is_same_wiki({**old, "publicReadOnly": True, "writable": False, "projectId": wiki.PROJECT_ID, "projectRoot": None}, True))

    def test_current_item_catalog_remains_valid(self):
        packages = wiki.load_manifest_packages()
        roots = {package["id"]: wiki.read_json(wiki.ITEM_ROOT / package["path"]) for package in packages}
        wiki.validate_all_items(roots, packages)

    def test_public_routes_and_fresh_data(self):
        class Handler(wiki.WikiRequestHandler):
            public_readonly = True
            def log_message(self, *args):
                pass

        server = wiki.ExclusiveThreadingHTTPServer(("127.0.0.1", 0), Handler)
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        try:
            connection = http.client.HTTPConnection("127.0.0.1", server.server_port, timeout=3)
            connection.request("GET", "/api/wiki/status")
            response = connection.getresponse()
            status = json.loads(response.read())
            self.assertEqual(200, response.status)
            self.assertEqual(wiki.PROJECT_ID, status["projectId"])
            self.assertFalse(status["writable"])
            connection.request("HEAD", "/api/wiki/status")
            response = connection.getresponse()
            self.assertEqual(200, response.status)
            self.assertEqual(b"", response.read())
            connection.request("GET", "/Assets/StreamingAssets/GameConfig/Recipes/recipe-manifest.json", headers={"If-Modified-Since": "Wed, 31 Dec 2099 23:59:59 GMT"})
            response = connection.getresponse()
            self.assertEqual(200, response.status)
            self.assertIn("no-store", response.getheader("Cache-Control"))
            response.read()
            for request_path in ["/Assets/Resources/Config/Mechanical/mechanical-catalog.json", "/Assets/StreamingAssets/GameConfig/Liquids/liquids.json"]:
                connection.request("GET", request_path)
                response = connection.getresponse()
                self.assertEqual(200, response.status, request_path)
                response.read()
            for request_path in ["/.git/config", "/Assets/StreamingAssets/ItemWiki/wiki_server.py", "/Assets/StreamingAssets/GameConfig/Items/not-in-manifest.json"]:
                connection.request("GET", request_path)
                response = connection.getresponse()
                self.assertEqual(404, response.status, request_path)
                response.read()
            connection.close()
        finally:
            server.shutdown()
            server.server_close()
            thread.join()


if __name__ == "__main__":
    unittest.main()
