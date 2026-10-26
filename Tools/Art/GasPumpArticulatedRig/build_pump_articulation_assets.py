"""从AI分层母版裁取原生像素零件，并输出不参与Unity运行的机械合成预览。"""

from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
import re
import uuid

from PIL import Image, ImageChops, ImageDraw


ROOT = Path(__file__).resolve().parents[3]
ART = ROOT / "Assets/6_Art/Generated/Chemistry/GasPump_Mechanical"
SOURCE = ART / "Source"
PPU = 1475.294118
CANVAS = 1254
CRANK_AXIS = (630.0, 590.0)
CRANK_RADIUS = 36.0
ROD_LENGTH = 218.0


def keep_source_alpha_without_background_noise(image: Image.Image) -> Image.Image:
    # 只按透明度提取主体，不改动像素颜色、不缩图、不重绘零件。
    result = image.convert("RGBA")
    alpha = result.getchannel("A")
    result.putalpha(alpha.point(lambda value: value if value > 32 else 0))
    return result


def texture_meta(path: Path, pivot: tuple[float, float], source: bool = False) -> None:
    template = (ART / "GasPump_Mechanical_Idle.png.meta").read_text(encoding="utf-8")
    meta_path = Path(str(path) + ".meta")
    old = meta_path.read_text(encoding="utf-8") if meta_path.exists() else ""
    match = re.search(r"^guid: ([0-9a-f]{32})$", old, re.MULTILINE)
    guid = match.group(1) if match else uuid.uuid4().hex
    template = re.sub(r"^guid: .*", f"guid: {guid}", template, flags=re.MULTILINE)
    template = template.replace("spriteGenerateFallbackPhysicsShape: 1", "spriteGenerateFallbackPhysicsShape: 0")
    template = re.sub(r"spritePivot: \{.*\}", f"spritePivot: {{x: {pivot[0]:.12g}, y: {pivot[1]:.12g}}}", template)
    sprite_match = re.search(r"spriteID: ([0-9a-f]{32})", old)
    sprite_id = sprite_match.group(1) if sprite_match else uuid.uuid4().hex
    template = re.sub(r"spriteID: .*", f"spriteID: {sprite_id}", template)
    if source:
        template = template.replace("textureType: 8", "textureType: 0").replace("spriteMode: 1", "spriteMode: 0")
    meta_path.write_text(template, encoding="utf-8", newline="\n")


def folder_meta(path: Path) -> None:
    meta = Path(str(path) + ".meta")
    if not meta.exists():
        meta.write_text(f"fileFormatVersion: 2\nguid: {uuid.uuid4().hex}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n", encoding="utf-8")


def extract_parts() -> dict:
    base_source = SOURCE / "GasPump_ArticulatedBase_Source.png"
    sheet_source = SOURCE / "GasPump_ArticulatedParts_Source.png"
    base = keep_source_alpha_without_background_noise(Image.open(base_source))
    sheet = keep_source_alpha_without_background_noise(Image.open(sheet_source))
    assert base.size == sheet.size == (CANVAS, CANVAS)
    folder_meta(SOURCE)
    texture_meta(base_source, (.5, .5), source=True)
    texture_meta(sheet_source, (.5, .5), source=True)

    front_mask = Image.new("L", base.size)
    draw = ImageDraw.Draw(front_mask)
    draw.rectangle((0, 0, CANVAS, 333), fill=255)
    draw.rectangle((0, 547, CANVAS, CANVAS), fill=255)
    for left, right in ((233, 319), (449, 501), (745, 817), (938, 1007)):
        draw.rectangle((left, 313, right, 547), fill=255)
    front = base.copy()
    front.putalpha(ImageChops.multiply(base.getchannel("A"), front_mask))
    back = base.copy()
    back.putalpha(ImageChops.multiply(base.getchannel("A"), ImageChops.invert(front_mask)))

    manifest = {
        "schemaVersion": 1,
        "canvasSize": [CANVAS, CANVAS],
        "sourceSheetSize": [CANVAS, CANVAS],
        "sourceAlphaDiscardAtOrBelow": 32,
        "pixelsPerUnit": PPU,
        "coordinateConvention": "Top-left pixel coordinates; native transparent crops. pivotSourcePixels belongs to its source sheet, pivotCanvasPixels is the assembled target. Positive sourceRestAngleUnityRadians is counter-clockwise in Unity Y-up.",
        "sourceOriginal": "GasPump_Mechanical_Idle.png",
        "generatedSourceBase": "Source/GasPump_ArticulatedBase_Source.png",
        "generatedSourceParts": "Source/GasPump_ArticulatedParts_Source.png",
        "crankAxisCanvasPixels": list(CRANK_AXIS),
        "crankRadiusPixels": CRANK_RADIUS,
        "connectingRodLengthPixels": ROD_LENGTH,
        "sliderAxisCanvasPixels": [454.0, 806.0],
        "layerOrder": ["articulatedBack", "articulatedPistonLeft", "articulatedPistonRight", "articulatedFront", "articulatedCrank", "articulatedRodLeft", "articulatedRodRight", "articulatedNeedle"],
        "parts": [],
    }

    def export(key, suffix, image, crop, pivot_source, pivot_canvas, scale=1.0, endpoint=None):
        x0, y0, x1, y1 = crop
        cropped = image.crop(crop)
        file = f"GasPump_Articulated{suffix}.png"
        destination = ART / file
        cropped.save(destination)
        pivot = ((pivot_source[0] - x0) / cropped.width, 1.0 - (pivot_source[1] - y0) / cropped.height)
        texture_meta(destination, pivot)
        entry = {
            "stateKey": key,
            "file": file,
            "source": manifest["generatedSourceBase"] if suffix in ("Back", "Front") else manifest["generatedSourceParts"],
            "cropRect": [x0, y0, x1 - x0, y1 - y0],
            "pivotSourcePixels": list(pivot_source),
            "pivotCanvasPixels": list(pivot_canvas),
            "spritePivotNormalized": list(pivot),
            "scale": scale,
            "nativeSize": list(cropped.size),
        }
        if endpoint:
            dx, dy = endpoint[0] - pivot_source[0], endpoint[1] - pivot_source[1]
            entry["endpointSourcePixels"] = list(endpoint)
            entry["sourceEndpointLengthPixels"] = math.hypot(dx, dy)
            entry["sourceRestAngleUnityRadians"] = math.atan2(-dy, dx)
        manifest["parts"].append(entry)

    export("articulatedBack", "Back", back, (0, 0, CANVAS, CANVAS), (627, 627), (627, 627))
    export("articulatedFront", "Front", front, (0, 0, CANVAS, CANVAS), (627, 627), (627, 627))
    export("articulatedPistonLeft", "PistonLeft", sheet, (80, 96, 385, 567), (321, 489), (454, 480), .55)
    export("articulatedPistonRight", "PistonRight", sheet, (450, 96, 742, 567), (508, 489), (806, 480), .55)
    export("articulatedRodLeft", "RodLeft", sheet, (40, 690, 590, 824), (522, 758), CRANK_AXIS, ROD_LENGTH / 415, (107, 758))
    export("articulatedRodRight", "RodRight", sheet, (666, 690, 1214, 824), (733, 758), CRANK_AXIS, ROD_LENGTH / 414, (1147, 758))
    crank_source_axis = (1018, 357)
    crank_source_pin = (1097.5, 284.5)
    crank_source_radius = math.dist(crank_source_axis, crank_source_pin)
    export("articulatedCrank", "Crank", sheet, (846, 171, 1196, 528), crank_source_axis, CRANK_AXIS, CRANK_RADIUS / crank_source_radius, crank_source_pin)
    export("articulatedNeedle", "Needle", sheet, (566, 994, 727, 1149), (605, 1108.5), (624, 798), .5, (714, 1006))

    rig_path = ART / "GasPump_ArticulationRig.json"
    rig_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    rig_meta = Path(str(rig_path) + ".meta")
    if not rig_meta.exists():
        rig_meta.write_text(f"fileFormatVersion: 2\nguid: {uuid.uuid4().hex}\nTextScriptImporter:\n  externalObjects: {{}}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n", encoding="utf-8")
    return manifest


def render_part(canvas, entry, position, angle):
    image = Image.open(ART / entry["file"]).convert("RGBA")
    pivot = (entry["spritePivotNormalized"][0] * image.width, (1.0 - entry["spritePivotNormalized"][1]) * image.height)
    radius = math.ceil(math.hypot(max(pivot[0], image.width - pivot[0]), max(pivot[1], image.height - pivot[1]))) + 2
    tile = Image.new("RGBA", (radius * 2, radius * 2))
    tile.alpha_composite(image, (round(radius - pivot[0]), round(radius - pivot[1])))
    side = max(1, round(tile.width * entry["scale"]))
    tile = tile.resize((side, side), Image.Resampling.NEAREST).rotate(math.degrees(angle), Image.Resampling.NEAREST)
    canvas.alpha_composite(tile, (round(position[0] - side / 2), round(position[1] - side / 2)))


def render_frame(manifest, phase):
    parts = {part["stateKey"]: part for part in manifest["parts"]}
    frame = Image.new("RGBA", (CANVAS, CANVAS), (58, 58, 53, 255))
    pin = (CRANK_AXIS[0] + CRANK_RADIUS * math.cos(phase), CRANK_AXIS[1] - CRANK_RADIUS * math.sin(phase))
    slider_left = (454.0, pin[1] - math.sqrt(ROD_LENGTH**2 - (pin[0] - 454.0) ** 2))
    slider_right = (806.0, pin[1] - math.sqrt(ROD_LENGTH**2 - (pin[0] - 806.0) ** 2))
    for key in manifest["layerOrder"]:
        part = parts[key]
        position, angle = part["pivotCanvasPixels"], 0.0
        if key == "articulatedPistonLeft": position = slider_left
        elif key == "articulatedPistonRight": position = slider_right
        elif key == "articulatedCrank": angle = phase - part["sourceRestAngleUnityRadians"]
        elif key in ("articulatedRodLeft", "articulatedRodRight"):
            slider = slider_left if key.endswith("Left") else slider_right
            position = pin
            angle = math.atan2(pin[1] - slider[1], slider[0] - pin[0]) - part["sourceRestAngleUnityRadians"]
        elif key == "articulatedNeedle": angle = .14 * math.sin(phase) + .15
        render_part(frame, part, position, angle)
    return frame


def previews(manifest):
    preview_dir = ROOT / "Logs/GasPumpArticulationPreview"
    preview_dir.mkdir(parents=True, exist_ok=True)
    frames = [render_frame(manifest, index * math.tau / 32) for index in range(32)]
    frames[0].save(preview_dir / "pump-articulation-frame.png")
    frames[0].resize((627, 627), Image.Resampling.NEAREST).save(preview_dir / "pump-articulation-preview.gif", save_all=True, append_images=[frame.resize((627, 627), Image.Resampling.NEAREST) for frame in frames[1:]], duration=70, loop=0, disposal=2)
    contact = Image.new("RGB", (4 * 418, 3 * 448), (45, 45, 43))
    draw = ImageDraw.Draw(contact)
    original = Image.new("RGBA", (CANVAS, CANVAS), (58, 58, 53, 255))
    original.alpha_composite(Image.open(ART / "GasPump_Mechanical_Idle.png").convert("RGBA"))
    original = original.convert("RGB").resize((418, 418), Image.Resampling.NEAREST)
    contact.paste(original, (0, 30))
    draw.text((12, 8), "Original icon (unchanged)", fill="white")
    for slot in range(11):
        index = (slot * 3) % 32
        x, y = ((slot + 1) % 4) * 418, ((slot + 1) // 4) * 448
        contact.paste(frames[index].convert("RGB").resize((418, 418), Image.Resampling.NEAREST), (x, y + 30))
        draw.text((x + 12, y + 8), f"OFFLINE phase {index}/32", fill="white")
    contact.save(preview_dir / "pump-articulation-contact.png")
    print(preview_dir)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--preview", action="store_true")
    args = parser.parse_args()
    manifest = extract_parts()
    if args.preview: previews(manifest)
    print(json.dumps({part["stateKey"]: {"nativeSize": part["nativeSize"], "pivot": part["spritePivotNormalized"], "scale": part["scale"]} for part in manifest["parts"]}, indent=2))


if __name__ == "__main__":
    main()
