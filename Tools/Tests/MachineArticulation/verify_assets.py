"""静态核验气泵拆件、像素锚点和 Unity 导入元数据，不连接 Unity。"""
from __future__ import annotations

import json
import math
import re
from pathlib import Path

from PIL import Image, ImageChops

ROOT = Path(__file__).resolve().parents[3]
ART = ROOT / "Assets/6_Art/Generated/Chemistry/GasPump_Mechanical"
RIG = ART / "GasPump_ArticulationRig.json"


def main() -> None:
    checks = 0

    def require(condition: bool, label: str) -> None:
        nonlocal checks
        checks += 1
        if not condition:
            raise AssertionError(label)

    rig = json.loads(RIG.read_text(encoding="utf-8-sig"))
    alpha_cutoff = rig.get("sourceAlphaDiscardAtOrBelow")
    require(isinstance(alpha_cutoff, int) and 0 <= alpha_cutoff <= 32,
            "素材清单必须明确记录源图低Alpha噪点去除阈值，且不能抹去Alpha大于32的有效像素")

    def clean_source_alpha(source: Image.Image) -> Image.Image:
        normalized = source.convert("RGBA")
        normalized.putalpha(normalized.getchannel("A").point(lambda value: 0 if value <= alpha_cutoff else value))
        return normalized
    with Image.open(ART / "GasPump_Mechanical_Idle.png") as original:
        source_size = original.size
        require(source_size == (1254, 1254), "原气泵图不得为动画降采样")
    require(tuple(rig["canvasSize"]) == source_size, "拆件的基准画布与原图不一致")
    parts = rig["parts"]
    require(len(parts) == 8, "气泵应由前后壳、两活塞、两连杆、曲柄和表针八层构成")
    states = [part["stateKey"] for part in parts]
    require(len(set(states)) == len(states), "拆件状态键重复")
    original_meta = (ART / "GasPump_Mechanical_Idle.png.meta").read_text(encoding="utf-8-sig")
    guids: set[str] = {re.search(r"^guid:\s*([0-9a-f]{32})", original_meta, re.MULTILINE).group(1)}
    source_ppu = float(re.search(r"spritePixelsToUnits:\s*([\d.]+)", original_meta).group(1))
    catalog = json.loads((ROOT / "Assets/StreamingAssets/GameConfig/Items/shells/chemical_machines.json")
                        .read_text(encoding="utf-8-sig"))
    item = next(item for item in catalog["items"] if item["id"] == "GasPump_Mechanical")
    configured = item["modules"]["机械扭矩模块"]["parameters"]["Articulation"]
    configured_parts = {part["SpriteState"]: part for part in configured["Parts"]}
    require(len(configured_parts) == 8, "正式气泵定义没有完整登记八层零件")
    require(configured["WorkingFlowPhase"] == "gas", "正式动画没有绑定真实抽气反馈")
    require(configured["CrankRadiusPixels"] == rig["crankRadiusPixels"] and
            configured["ConnectingRodLengthPixels"] == rig["connectingRodLengthPixels"],
            "正式曲柄半径、杆长与素材锚点声明不一致")
    require([configured["CrankCenterPixels"]["x"], configured["CrankCenterPixels"]["y"]] ==
            rig["crankAxisCanvasPixels"], "正式曲柄轴心与素材定义不一致")
    require([configured["CanvasPivotPixels"]["x"], configured["CanvasPivotPixels"]["y"]] ==
            [source_size[0] / 2, source_size[1] / 2], "正式机体画布锚点没有保持原图中心")
    require([part["SpriteState"] for part in sorted(configured["Parts"], key=lambda part: part["Order"])] ==
            rig["layerOrder"], "正式图层顺序与素材遮挡关系不同")
    addresses = (ROOT / "Assets/AddressableAssetsData/AssetGroups/Default.asset").read_text(encoding="utf-8-sig")
    entries = {guid: address.strip() for guid, address in re.findall(
        r"- m_GUID:\s*([a-f0-9]{32})\s*\n\s*m_Address:\s*([^\r\n]+)", addresses)}
    sample_path = ROOT / "Temp/Tests/MachineArticulation/bin/net8.0/production-pump-poses.json"
    require(sample_path.is_file(), "请先运行同目录dotnet回归以导出正式解算器逐帧销轴采样")
    samples = json.loads(sample_path.read_text(encoding="utf-8-sig"))
    require(samples["radius"] == rig["crankRadiusPixels"] and
            samples["length"] == rig["connectingRodLengthPixels"], "离线运动学采样不是当前正式素材参数")
    with Image.open(ART / rig["generatedSourceBase"]) as original_base, \
            Image.open(ART / "GasPump_ArticulatedBack.png") as back, \
            Image.open(ART / "GasPump_ArticulatedFront.png") as front:
        white = Image.new("RGBA", source_size, (255, 255, 255, 255))
        assembled = Image.alpha_composite(Image.alpha_composite(white, back), front)
        expected_base = Image.alpha_composite(white, clean_source_alpha(original_base))
        require(assembled.tobytes() == expected_base.tobytes(), "固定前后壳合成后产生缺口、重叠变色或尺寸变化")
        require(ImageChops.multiply(back.getchannel("A"), front.getchannel("A")).getbbox() is None,
                "固定前后壳切片Alpha重叠导致重复着色")
    endpoints = 0
    for part in parts:
        name = part["stateKey"]
        relative = Path(part["file"])
        require(not relative.is_absolute() and ".." not in relative.parts, name + "：资源引用越出本目录")
        path = ART / relative
        require(path.is_file(), name + "：缺少拆件图片")
        x, y, width, height = part["cropRect"]
        sheet_size = part.get("sourceSheetSize", rig["sourceSheetSize"])
        require(all(isinstance(value, int) for value in (x, y, width, height)) and
                0 <= x < sheet_size[0] and 0 <= y < sheet_size[1] and
                width > 0 and height > 0 and x + width <= sheet_size[0] and
                y + height <= sheet_size[1], name + "：裁切矩形不是原图集内完整像素")
        with Image.open(path) as picture:
            require(picture.size == (width, height), name + "：拆图尺寸被缩放或与裁切元数据不符")
            require(picture.mode == "RGBA", name + "：拆件不是RGBA透明图")
            alpha = picture.getchannel("A")
            low, high = alpha.getextrema()
            require(low == 0 and high > 0 and alpha.getbbox() is not None,
                    name + "：拆件缺少透明区或没有可见像素")
            if configured_parts[name]["Motion"] != "static":
                with Image.open(ART / part["source"]) as source:
                    crop = clean_source_alpha(source).crop((x, y, x + width, y + height))
                    crop_white = Image.new("RGBA", crop.size, (255, 255, 255, 255))
                    require(Image.alpha_composite(crop_white, crop).tobytes() ==
                            Image.alpha_composite(crop_white, picture).tobytes(),
                            name + "：运动拆件未保持声明Alpha清理之外的源图像素")
        pivot_x, pivot_y = part["pivotCanvasPixels"]
        require(math.isfinite(pivot_x) and math.isfinite(pivot_y), name + "：旋转锚点不是有限数")
        declaration = configured_parts[name]
        require([declaration["PivotPixels"]["x"], declaration["PivotPixels"]["y"]] == [pivot_x, pivot_y],
                name + "：生产定义的装配目标与素材锚点不一致")
        require(abs(declaration["Scale"] - part["scale"]) < 1e-7,
                name + "：生产缩放与素材销轴标定不一致")
        if declaration["Motion"] in ("slider", "rod"):
            require(declaration["SliderAxisPixelsX"] in rig["sliderAxisCanvasPixels"],
                    name + "：生产运动轴线不是素材的真实缸轴")
        if "sourceRestAngleUnityRadians" in part:
            require(abs(declaration["SourceAngleRadians"] - part["sourceRestAngleUnityRadians"]) < 1e-7,
                    name + "：源零件朝向没有进入生产旋转补偿")
        meta = Path(str(path) + ".meta").read_text(encoding="utf-8-sig")
        guid = re.search(r"^guid:\s*([0-9a-f]{32})\s*$", meta, re.MULTILINE)
        require(guid is not None, name + "：Unity GUID 无效")
        require(guid.group(1) not in guids, name + "：Unity GUID 与其他拆件重复")
        guids.add(guid.group(1))
        expected_address = path.relative_to(ROOT).as_posix()
        require(entries.get(guid.group(1)) == expected_address, name + "：Addressables未按实际GUID与路径登记")
        visual_states = item["visual"]["spriteStates"]
        state = visual_states[name]
        declared_address = state["spriteAddress"] if isinstance(state, dict) else state
        require(declared_address == expected_address, name + "：正式物品Sprite状态映射到错误图片")
        for key, expected in (("textureType", 8), ("spriteMode", 1), ("filterMode", 0),
                              ("enableMipMap", 0), ("nPOTScale", 0), ("textureCompression", 0)):
            values = re.findall(r"^\s*" + key + r":\s*(\d+)\s*$", meta, re.MULTILINE)
            require(bool(values) and all(int(value) == expected for value in values),
                    name + "：导入设置改变原图清晰度或Sprite类型：" + key)
        ppu = float(re.search(r"spritePixelsToUnits:\s*([\d.]+)", meta).group(1))
        require(abs(ppu - source_ppu) < 1e-5, name + "：拆件PPU与原机体不一致")
        pivot = re.search(r"spritePivot:\s*\{x:\s*([^,]+),\s*y:\s*([^}]+)\}", meta)
        require(pivot is not None, name + "：缺少Unity像素锚点")
        source_x, source_y = part["pivotSourcePixels"]
        require(abs(part["spritePivotNormalized"][0] - (source_x - x) / width) < 1e-7 and
                abs(part["spritePivotNormalized"][1] - (1 - (source_y - y) / height)) < 1e-7,
                name + "：图集锚点未转换到真实裁片坐标")
        require(abs(float(pivot.group(1)) - part["spritePivotNormalized"][0]) < 1e-5 and
                abs(float(pivot.group(2)) - part["spritePivotNormalized"][1]) < 1e-5,
                name + "：Unity锚点未按裁切矩形换算，零件旋转会脱轴")
        for size in re.findall(r"^\s*maxTextureSize:\s*(\d+)\s*$", meta, re.MULTILINE):
            require(int(size) >= max(width, height), name + "：导入最大尺寸会缩小原像素")
        if "endpointSourcePixels" in part:
            endpoint = part["endpointSourcePixels"]
            distance = math.dist(part["pivotSourcePixels"], endpoint)
            require(abs(distance - part["sourceEndpointLengthPixels"]) < 1e-3,
                    name + "：源图真实销轴长度和声明不一致")
            angle = math.atan2(source_y - endpoint[1], endpoint[0] - source_x)
            require(abs(angle - part["sourceRestAngleUnityRadians"]) < 1e-6,
                    name + "：源图真实销轴方向与角度补偿不一致")
            if declaration["Motion"] == "rod":
                require(abs(distance * part["scale"] - rig["connectingRodLengthPixels"]) < 1e-5,
                        name + "：连杆两端销轴经固定缩放后不满足生产杆长")
                endpoints += 1
            elif declaration["Motion"] == "crank":
                require(abs(distance * part["scale"] - rig["crankRadiusPixels"]) < 1e-5,
                        name + "：曲柄盘偏心销与生产轨道半径脱节")
            if declaration["Motion"] in ("rod", "crank"):
                source_dx = (endpoint[0] - source_x) * part["scale"]
                source_dy = (source_y - endpoint[1]) * part["scale"]
                side = "left" if declaration.get("SliderAxisPixelsX") == samples["leftAxisX"] else "right"
                for frame in samples["frames"]:
                    pose = frame[side]
                    if declaration["Motion"] == "rod":
                        angle = pose["rodAngle"] - declaration["SourceAngleRadians"]
                        target_x = pose["sliderX"] - pose["pinX"]
                        target_y = pose["sliderY"] - pose["pinY"]
                    else:
                        angle = frame["phase"] - declaration["SourceAngleRadians"]
                        target_x = pose["pinX"] - samples["centerX"]
                        target_y = pose["pinY"] - samples["centerY"]
                    rotated_x = source_dx * math.cos(angle) - source_dy * math.sin(angle)
                    rotated_y = source_dx * math.sin(angle) + source_dy * math.cos(angle)
                    require(math.hypot(rotated_x - target_x, rotated_y - target_y) < 1e-5,
                            name + "：真实裁片销轴经旋转和固定缩放后无法贴合生产关节")
    require(endpoints == 2, "左右连杆必须各有两枚可校验的销轴锚点")
    print(f"PASS: {checks} assertions against eight production pump sprites, rig anchors and import metadata.")


if __name__ == "__main__":
    main()
