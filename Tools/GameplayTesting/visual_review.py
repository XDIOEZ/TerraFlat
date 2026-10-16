"""汇总本轮 PlayMode Lua 截图，检查基础画质并生成供 AI 集中看图的总览页。"""

import argparse
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont, ImageOps


PER_PAGE = 12
THUMB_SIZE = (304, 176)
CELL_SIZE = (324, 218)


def _inspect(path: Path):
    """这里只检查客观图像缺陷；布局、角色、HUD 仍须视觉模型实际复核。"""
    with Image.open(path) as source:
        source.verify()
    with Image.open(path) as source:
        width, height = source.size
        rgb = source.convert("RGB")
        rgb.thumbnail((128, 128))
        pixels = list(rgb.get_flattened_data())
        count = len(pixels)
        problems = []
        if width < 320 or height < 180:
            problems.append("截图分辨率异常偏低")
        if count:
            if len(set(pixels)) <= 2:
                problems.append("画面几乎只有单一颜色")
            elif sum(max(pixel) < 12 for pixel in pixels) / count >= 0.99:
                problems.append("疑似黑屏")
            elif sum(min(pixel) > 243 for pixel in pixels) / count >= 0.99:
                problems.append("疑似白屏")
        if source.mode in ("RGBA", "LA"):
            alpha = source.getchannel("A")
            if alpha.getextrema()[1] == 0:
                problems.append("截图完全透明")
    return (width, height), problems


def _make_sheets(photos, directory: Path):
    """每页最多 12 张，防止把整套截图压缩成一张不可辨认的小图。"""
    sheets = []
    font = ImageFont.load_default()
    for begin in range(0, len(photos), PER_PAGE):
        items = photos[begin:begin + PER_PAGE]
        columns = min(3, len(items))
        rows = (len(items) + columns - 1) // columns
        sheet = Image.new("RGB", (CELL_SIZE[0] * columns, CELL_SIZE[1] * rows), (24, 27, 32))
        draw = ImageDraw.Draw(sheet)
        for local_index, entry in enumerate(items):
            x = (local_index % columns) * CELL_SIZE[0] + 10
            y = (local_index // columns) * CELL_SIZE[1] + 9
            with Image.open(entry["path"]) as source:
                preview = ImageOps.contain(source.convert("RGB"), THUMB_SIZE)
            sheet.paste(preview, (x + (THUMB_SIZE[0] - preview.width) // 2, y))
            caption = f"{entry['caseId']}  #{entry['index']}  ({entry['size'][0]}x{entry['size'][1]})"
            draw.text((x, y + 183), caption[:47], fill=(235, 240, 247), font=font)
        name = f"visual-contact-sheet-{len(sheets) + 1:02d}.png"
        sheet.save(directory / name)
        sheets.append(name)
    return sheets


def review_images(gameplay_report_path: Path):
    """在测试全部结束后运行一次；结果落盘到同一轮 Runs 目录。"""
    gameplay_report_path = Path(gameplay_report_path).resolve()
    report = json.loads(gameplay_report_path.read_text(encoding="utf-8"))
    run_directory = gameplay_report_path.parent
    cases = [case for case in report.get("cases", [])
             if case.get("status") in ("passed", "failed", "interrupted", "running")]
    issues = []
    photos = []
    for case in cases:
        case_id = case.get("id", "unknown")
        valid_count = 0
        for screenshot in case.get("screenshots", []):
            path_text = screenshot.get("path")
            if not path_text:
                issues.append({"caseId": case_id, "problem": "截图记录没有文件路径"})
                continue
            path = Path(path_text).resolve()
            if not path.is_relative_to(run_directory):
                issues.append({"caseId": case_id, "problem": "截图路径超出本轮结果目录"})
                continue
            if screenshot.get("error"):
                issues.append({"caseId": case_id, "problem": "截图采集失败：" + str(screenshot["error"]),
                               "path": str(path)})
                continue
            if not path.is_file():
                issues.append({"caseId": case_id, "problem": "截图文件不存在", "path": str(path)})
                continue
            try:
                size, image_issues = _inspect(path)
            except (OSError, ValueError) as error:
                issues.append({"caseId": case_id, "problem": "无法解码截图：" + str(error), "path": str(path)})
                continue
            valid_count += 1
            entry = {"caseId": case_id, "index": screenshot.get("index"),
                     "path": str(path), "size": size, "label": screenshot.get("label")}
            photos.append(entry)
            for problem in image_issues:
                issues.append({"caseId": case_id, "problem": problem, "path": str(path)})
        if valid_count == 0:
            issues.append({"caseId": case_id, "problem": "已执行用例没有有效截图"})

    sheets = _make_sheets(photos, run_directory)
    status = "not_applicable" if not cases else "issues_found" if issues else "needs_semantic_review"
    result = {"status": status, "runId": report.get("runId"), "executedCases": len(cases),
              "imageCount": len(photos), "issues": issues, "contactSheets":
              [str(run_directory / name) for name in sheets],
              "semanticReviewComplete": False}
    (run_directory / "visual-review.json").write_text(
        json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")

    lines = ["# Lua 截图统一复核", "", f"- 初步状态：`{status}`",
             f"- 已执行用例：{len(cases)}；可读取图片：{len(photos)}；异常：{len(issues)}",
             "- 图像程序只检查缺图、坏图、黑屏、白屏、纯色与极低分辨率。",
             "- **必须由 AI 实际查看下方全部总览页及可疑原图**，检查玩家、HUD、UI 遮挡、场景、特效等；",
             "  修复证实的问题后重跑相关测试。没有模型看图之前，不能写“视觉通过”。", ""]
    for name in sheets:
        lines += [f"![本轮定时截图汇总]({name})", ""]
    if issues:
        lines += ["## 自动检测的疑点", ""]
        for issue in issues:
            lines.append(f"- `{issue['caseId']}`：{issue['problem']}")
    (run_directory / "visual-review.md").write_text("\n".join(lines) + "\n", encoding="utf-8")
    return result


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="对一轮完整 PlayMode Lua 报告统一汇总和预检截图")
    parser.add_argument("report", type=Path, help="Runs/<runId>/report.json")
    args = parser.parse_args()
    result = review_images(args.report)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    raise SystemExit(1 if result["status"] == "issues_found" else 0)
