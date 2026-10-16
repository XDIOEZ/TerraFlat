"""统一截图复核的无 Unity 依赖回归。"""

import json
import tempfile
import unittest
from pathlib import Path

from PIL import Image

from visual_review import review_images


class VisualReviewTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.run_directory = Path(self.temporary.name) / "Runs" / "test-run"
        self.run_directory.mkdir(parents=True)
        self.report_path = self.run_directory / "report.json"

    def write_report(self, cases):
        self.report_path.write_text(json.dumps({"runId": "test-run", "cases": cases}), encoding="utf-8")

    def screenshot(self, color, name="screenshot-01.png"):
        directory = self.run_directory / "cases" / "sample"
        directory.mkdir(parents=True, exist_ok=True)
        path = directory / name
        if color == "gradient":
            image = Image.new("RGB", (640, 360))
            image.putdata([(x % 256, y % 256, (x + y) % 256)
                           for y in range(360) for x in range(640)])
        else:
            image = Image.new("RGB", (640, 360), color)
        image.save(path)
        return {"index": 1, "path": str(path), "label": "测试图片"}

    def test_valid_picture_creates_review_sheets_but_requires_real_visual_review(self):
        self.write_report([{"id": "sample", "status": "passed",
                            "screenshots": [self.screenshot("gradient")]}])
        result = review_images(self.report_path)
        self.assertEqual(result["status"], "needs_semantic_review")
        self.assertEqual(result["imageCount"], 1)
        self.assertFalse(result["semanticReviewComplete"])
        self.assertTrue(Path(result["contactSheets"][0]).exists())
        self.assertTrue((self.run_directory / "visual-review.md").is_file())

    def test_black_or_corrupted_image_is_reported(self):
        black = self.screenshot("black")
        damaged = self.run_directory / "cases" / "sample" / "screenshot-02.png"
        damaged.write_bytes(b"not PNG")
        self.write_report([{"id": "sample", "status": "failed", "screenshots":
                            [black, {"index": 2, "path": str(damaged)}]}])
        result = review_images(self.report_path)
        self.assertEqual(result["status"], "issues_found")
        self.assertTrue(any("单一颜色" in issue["problem"] for issue in result["issues"]))
        self.assertTrue(any("解码" in issue["problem"] for issue in result["issues"]))

    def test_executed_case_must_have_one_image_but_skipped_does_not(self):
        self.write_report([{"id": "sample", "status": "passed", "screenshots": []},
                           {"id": "skipped", "status": "skipped", "screenshots": []}])
        result = review_images(self.report_path)
        self.assertEqual(result["status"], "issues_found")
        self.assertEqual(len(result["issues"]), 1)
        self.assertEqual(result["issues"][0]["caseId"], "sample")


if __name__ == "__main__":
    unittest.main()
