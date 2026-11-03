#!/usr/bin/env python3
"""在空闲 Unity 编辑器中验证真实贴图导入、备份和恢复；只操作本次创建的临时测试图。"""

import json
import re
import shutil
import sys
import uuid
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / "Tools"))
from UnityMcpFast import UnityMcp, default_url


def find_result(value, key):
    if isinstance(value, str):
        try:
            return find_result(json.loads(value), key)
        except (ValueError, TypeError):
            return None
    if isinstance(value, dict):
        if key in value:
            return value
        for child in value.values():
            found = find_result(child, key)
            if found is not None:
                return found
    elif isinstance(value, list):
        for child in value:
            found = find_result(child, key)
            if found is not None:
                return found
    return None


def main():
    client = UnityMcp(default_url(), 180)
    checks = []
    guid = uuid.uuid4().hex
    relative = f"Assets/6_Art/Generated/Tools/FWTextureRegression_{guid}.png"
    source = ROOT / relative
    meta = Path(str(source) + ".meta")
    backups = ROOT / "ArtSourceBackups/PixelArtTextures" / guid
    output = ROOT / "Library/FlatWorldTextureTask"
    output.mkdir(parents=True, exist_ok=True)
    created = False

    def call(tool, args, expected):
        result, _ = client.request("tools/call", {"name": tool, "arguments": args})
        (output / "regression-last-receipt.json").write_text(
            json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
        if result.get("isError"):
            raise RuntimeError(json.dumps(result, ensure_ascii=False))
        found = find_result(result, expected)
        if found is None:
            raise RuntimeError(f"没有 {expected} 回执：{result}")
        return found

    def evaluate(code, expected="operation"):
        return call("editor.js.eval", {"code": code, "mode": "script"}, expected)

    def assert_check(condition, label):
        if not condition:
            raise AssertionError(label)
        checks.append(label)
        print("PASS: " + label, flush=True)

    path_json = json.dumps(relative)
    array = ("const paths=CS.System.Array.CreateInstance(puer.$typeof(CS.System.String),1);"
             f"paths.SetValue({path_json},0);")
    preview_code = array + "return CS.UnityEngine.JsonUtility.ToJson(CS.FlatWorldPixelArtTextureOptimizer.PreviewPaths(paths));"

    def restore():
        return evaluate("const old=CS.UnityEditor.Selection.objects;try {"
            f"CS.UnityEditor.Selection.activeObject=CS.UnityEditor.AssetDatabase.LoadMainAssetAtPath({path_json});"
            "return CS.UnityEngine.JsonUtility.ToJson(CS.FlatWorldPixelArtTextureOptimizer.Restore(true));"
            "} finally {CS.UnityEditor.Selection.objects=old;}")

    def apply(preview):
        encoded = json.dumps(json.dumps(preview, ensure_ascii=False), ensure_ascii=False)
        return evaluate(f"const p=CS.UnityEngine.JsonUtility.FromJson({encoded},"
            "puer.$typeof(CS.FlatWorldPixelArtTextureOptimizer.Report));"
            "return CS.UnityEngine.JsonUtility.ToJson(CS.FlatWorldPixelArtTextureOptimizer.Apply(p));")

    try:
        state = call("editor.state", {}, "isPlaying")
        if state["isPlaying"] or state.get("isPlayingOrWillChangePlaymode") or state.get("isCompiling") or state.get("isUpdating"):
            raise RuntimeError("Unity 不是空闲编辑态：测试未开始，不暂停或退出游戏。")
        image = Image.new("RGBA", (1024, 1024), (0, 0, 0, 0))
        draw = ImageDraw.Draw(image)
        draw.rectangle((128, 128, 511, 895), fill=(60, 90, 120, 255))
        draw.rectangle((512, 128, 895, 511), fill=(200, 100, 40, 255))
        draw.rectangle((512, 512, 895, 895), fill=(30, 180, 90, 96))
        source.parent.mkdir(parents=True, exist_ok=True)
        image.save(source)
        template = (ROOT / "Assets/6_Art/Generated/Atmosphere/AtmosphereAnalyzer/AtmosphereAnalyzer.png.meta").read_text(encoding="utf-8-sig")
        template = re.sub(r"(?m)^guid: .+$", "guid: " + guid, template)
        template = re.sub(r"(?m)^  spritePixelsToUnits: .+$", "  spritePixelsToUnits: 1024", template)
        template = re.sub(r"(?m)^  spritePivot: .+$", "  spritePivot: {x: 0.37, y: 0.21}", template)
        meta.write_text(template, encoding="utf-8")
        created = True
        evaluate(f"CS.UnityEditor.AssetDatabase.ImportAsset({path_json},CS.UnityEditor.ImportAssetOptions.ForceUpdate|CS.UnityEditor.ImportAssetOptions.ForceSynchronousImport);return {{imported:true}};", "imported")
        original, original_meta = source.read_bytes(), meta.read_bytes()

        preview = evaluate(preview_code)
        assert_check(preview["acceptedCount"] == 1 and not preview["errors"], "只读扫描接受安全的像素测试图")
        assert_check(source.read_bytes() == original and meta.read_bytes() == original_meta and not backups.exists(),
                     "预览不修改源图、导入设置或生成原图备份")
        report = apply(preview)
        assert_check(report["changedCount"] == 1 and not report["errors"], "真实 Unity 导入验证通过，GUID、Sprite ID、世界尺寸不变")
        low, low_meta = source.read_bytes(), meta.read_bytes()
        with Image.open(source) as reduced:
            expected = image.resize((512, 512), Image.Resampling.NEAREST)
            assert_check(reduced.size == (512, 512) and reduced.convert("RGBA").tobytes() == expected.tobytes(),
                         "输出逐像素等于最近邻采样，保留颜色和半透明 Alpha")
        entry = report["entries"][0]
        directory = ROOT / entry["backupDirectory"]
        assert_check((directory / "original.png").read_bytes() == original and
                     (directory / "original.png.meta").read_bytes() == original_meta,
                     "Assets 外的原图和原 meta 为完整逐字节备份")
        assert_check(re.search(r"(?m)^  spritePixelsToUnits: 512(?:\.0)?$", meta.read_text(encoding="utf-8-sig")) is not None,
                     "PPU 随源图等比例调整")
        before_records = list(backups.glob("*/record.json"))
        repeated = evaluate(preview_code)
        assert_check(repeated["acceptedCount"] == 0 and source.read_bytes() == low and
                     list(backups.glob("*/record.json")) == before_records,
                     "重复扫描不继续缩图、不覆盖备份")
        stale = apply(preview)
        assert_check(stale["changedCount"] == 0 and len(stale["errors"]) == 1 and source.read_bytes() == low,
                     "过期计划拒绝覆盖扫描之后的资源")

        source.write_bytes(low + b"post-optimization-edit")
        conflict = restore()
        assert_check(conflict["changedCount"] == 0 and conflict["errors"] and source.read_bytes().endswith(b"post-optimization-edit"),
                     "恢复拒绝覆盖压缩后新修改的图片")
        source.write_bytes(low)
        assert_check(meta.read_bytes() == low_meta, "恢复冲突不改写现有导入设置")
        stored_original = (directory / "original.png").read_bytes()
        (directory / "original.png").write_bytes(b"corrupt-backup-test")
        corrupted = restore()
        assert_check(corrupted["changedCount"] == 0 and corrupted["errors"] and source.read_bytes() == low,
                     "损坏备份不能覆盖工作图")
        (directory / "original.png").write_bytes(stored_original)

        restored = restore()
        assert_check(restored["changedCount"] == 1 and not restored["errors"] and source.read_bytes() == original,
                     "一键恢复高清原图")
        assert_check(meta.read_bytes() == original_meta and (directory / "original.png").read_bytes() == original,
                     "一键恢复原导入设置，并继续保留高清备份")
        assert_check(restore()["changedCount"] == 0, "重复恢复没有额外写入")

        for dimensions, expected_size in [((1672, 941), (512, 288)), ((941, 1672), (288, 512))]:
            image.resize(dimensions, Image.Resampling.NEAREST).save(source)
            evaluate(f"CS.UnityEditor.AssetDatabase.ImportAsset({path_json},CS.UnityEditor.ImportAssetOptions.ForceUpdate|CS.UnityEditor.ImportAssetOptions.ForceSynchronousImport);return {{imported:true}};", "imported")
            odd_original, odd_meta = source.read_bytes(), meta.read_bytes()
            odd = evaluate(preview_code)
            assert_check(odd["acceptedCount"] == 1 and not odd["errors"],
                         f"非整比例 {dimensions} 接受取整缩小，不再整除失败就跳过")
            odd_applied = apply(odd)
            assert_check(odd_applied["changedCount"] == 1 and not odd_applied["errors"] and Image.open(source).size == expected_size,
                         f"横竖图 {dimensions} 均按长边同步 PPU，实际输出 {expected_size}")
            geometry = evaluate(
                f"const sprites=CS.UnityEditor.AssetDatabase.LoadAllAssetsAtPath({path_json});let s=null;"
                "for(let i=0;i<sprites.Length;i++){const o=sprites.GetValue(i);if(o.GetType().Name==='Sprite')s=o;}"
                "return {geometry:true,width:s.rect.width/s.pixelsPerUnit,height:s.rect.height/s.pixelsPerUnit,"
                "ppu:s.pixelsPerUnit,pivotX:s.pivot.x/s.rect.width,pivotY:s.pivot.y/s.rect.height};", "geometry")
            tolerance = .5001 / geometry["ppu"] + .00002
            assert_check(abs(geometry["width"] - dimensions[0] / 1024) <= tolerance and
                         abs(geometry["height"] - dimensions[1] / 1024) <= tolerance and
                         abs(geometry["pivotX"] - .37) < .00001 and abs(geometry["pivotY"] - .21) < .00001,
                         f"{dimensions} 世界尺寸误差不超过半个目标像素，归一化握柄锚点不变")
            assert_check(restore()["changedCount"] == 1 and source.read_bytes() == odd_original and meta.read_bytes() == odd_meta,
                         f"{dimensions} 原图及原导入设置可完整恢复")

        stripes = Image.new("RGBA", (2048, 2048), (0, 0, 0, 255))
        stripe_draw = ImageDraw.Draw(stripes)
        for x in range(2, 2048, 4):
            stripe_draw.rectangle((x, 0, x + 1, 2047), fill=(255, 255, 255, 255))
        stripes.save(source)
        evaluate(f"CS.UnityEditor.AssetDatabase.ImportAsset({path_json},CS.UnityEditor.ImportAssetOptions.ForceUpdate|CS.UnityEditor.ImportAssetOptions.ForceSynchronousImport);return {{imported:true}};", "imported")
        detail = evaluate(preview_code)
        assert_check(detail["acceptedCount"] == 1 and detail["entries"][0]["width"] == 1024,
                     "小档画质不通过时从原图尝试高一档，而非直接保留超大图")
        assert_check(apply(detail)["changedCount"] == 1 and evaluate(preview_code)["acceptedCount"] == 0,
                     "回退高一档的工作图也不会再次缩水或覆盖原图备份")
        assert_check(restore()["changedCount"] == 1, "高一档结果可恢复原图")
        generator = (ROOT / "Assets/Editor/FlatWorld/ContentTools/FlatWorldSpriteAtlasPrebuildGenerator.cs").read_text(encoding="utf-8-sig")
        assert_check("FlatWorldPixelArtTextureOptimizer." not in generator, "构建链不再自动调用贴图优化器")
        summary = {"passed": len(checks), "checks": checks}
        (output / "regression-report.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding="utf-8")
        print(json.dumps(summary, ensure_ascii=False), flush=True)
    finally:
        if created:
            try:
                evaluate(f"CS.UnityEditor.AssetDatabase.DeleteAsset({path_json});return {{deleted:true}};", "deleted")
            except Exception as error:
                print("测试图清理未完成：" + str(error), file=sys.stderr)
            if backups.exists():
                shutil.rmtree(backups)
        client.close()


if __name__ == "__main__":
    main()
