"""BUFF Wiki 写入校验；与 BuffDefinitionFactory 的本体 JSON 契约保持一致。"""

import math


FIELDS = set("id displayName category description labelKey descriptionKey durationSeconds tickIntervalSeconds stackMode maxStacks visualBaseScale visualScalePerStack waterStackIntervalSeconds waterStacksPerDepthLevel drinkDurationExtensionSeconds effects".split())
EFFECT_FIELDS = set("phase typeId targetId requiredTag value upperLimit scaleWithStacks".split())
MULTIPLIERS = {f"core:{name}" for name in ("move_speed_multiplier", "food_consume_speed_multiplier", "water_consume_speed_multiplier", "temperature_cooling_multiplier", "damage_taken_multiplier")}
TRAUMA = {f"core:trauma_{name}" for name in ("move", "attack", "confusion", "blur")}
TYPES = MULTIPLIERS | TRAUMA | {f"core:{name}" for name in ("temperature_warming", "heal", "max_health_percent_heal", "stamina_change", "nutrition_change", "true_damage", "max_health_percent_true_damage", "body_durability_restore")}
BODY_PARTS = {"Head", "Chest", "Abdomen", "Pelvis", "LeftHand", "RightHand", "LeftLeg", "RightLeg"}


def number(value, label, minimum=None, maximum=None, integer=False):
    """拒绝布尔值、非有限值和不能由游戏 float 表达的数值。"""
    if type(value) not in (int, float) or abs(value) > 3.402823466e38 or not math.isfinite(value):
        raise ValueError(f"{label} 必须是有限数字")
    if integer and type(value) is not int:
        raise ValueError(f"{label} 必须是整数")
    if minimum is not None and value < minimum or maximum is not None and value > maximum:
        raise ValueError(f"{label} 超出有效范围")
    return value


def text(value, label, optional=False):
    if value is None and optional:
        return ""
    if not isinstance(value, str):
        raise ValueError(f"{label} 必须是文本")
    return value.strip()


def validate_definition(source):
    """校验字段、层数、生命周期与缓存效果能够执行的参数组合。"""
    if not isinstance(source, dict) or set(source) - FIELDS:
        raise ValueError("BUFF 定义包含未知字段或不是对象")
    buff_id = text(source.get("id"), "BUFF id")
    if not buff_id:
        raise ValueError("BUFF id 不能为空")
    for field in ("displayName", "description", "labelKey", "descriptionKey"):
        text(source.get(field), f"{buff_id}.{field}", optional=True)
    category = text(source.get("category", "general"), "category", optional=True).lower()
    if category not in ("", "general", "blood_loss"):
        raise ValueError("category 必须是 general 或 blood_loss")
    mode = text(source.get("stackMode", "ignore"), "stackMode").lower()
    if mode not in ("ignore", "extend_duration", "refresh_duration", "add_stacks"):
        raise ValueError("未知的 stackMode")
    duration = source.get("durationSeconds")
    if duration is not None:
        number(duration, "durationSeconds", 0)
    if mode in ("extend_duration", "refresh_duration") and (duration is None or duration <= 0):
        raise ValueError("续期模式必须配置正持续时间")
    if mode == "add_stacks" and duration == 0:
        raise ValueError("叠层 BUFF 持续时间必须为正数或 null")
    maximum = number(source.get("maxStacks", 1), "maxStacks", 1, 1000, integer=True)
    base = number(source.get("visualBaseScale", 1), "visualBaseScale", 0)
    step = number(source.get("visualScalePerStack", 0), "visualScalePerStack", 0)
    if base <= 0:
        raise ValueError("visualBaseScale 必须大于零")
    number(base + (maximum - 1) * step, "最高层特效倍率", 0)
    interval = number(source.get("tickIntervalSeconds", 0), "tickIntervalSeconds", 0)
    water_interval = number(source.get("waterStackIntervalSeconds", 0), "waterStackIntervalSeconds", 0)
    water_per_depth = number(source.get("waterStacksPerDepthLevel", 0), "waterStacksPerDepthLevel", 0, 1000, integer=True)
    if bool(water_interval) != bool(water_per_depth):
        raise ValueError("水体周期与每级层数必须同时启用或同时为零")
    if water_interval and (buff_id != "潮湿" or mode != "add_stacks"):
        raise ValueError("水体叠层参数只用于 add_stacks 潮湿定义")
    extension = number(source.get("drinkDurationExtensionSeconds", 0), "drinkDurationExtensionSeconds", 0)
    if duration is None and extension > 0:
        raise ValueError("永久 BUFF 不能配置饮水延时")
    effects = source.get("effects")
    if not isinstance(effects, list):
        raise ValueError("effects 必须是数组")
    for effect in effects:
        if not isinstance(effect, dict) or set(effect) - EFFECT_FIELDS:
            raise ValueError("BUFF 效果包含未知字段或不是对象")
        phase = text(effect.get("phase"), "phase").lower()
        kind = text(effect.get("typeId"), "typeId").lower()
        if phase not in ("start", "tick", "stop") or kind not in TYPES:
            raise ValueError("未知效果阶段或 typeId")
        if phase == "tick" and interval <= 0:
            raise ValueError("周期效果的 tickIntervalSeconds 必须大于零")
        value = number(effect.get("value", 0), "effect.value")
        upper = effect.get("upperLimit")
        if upper is not None:
            number(upper, "upperLimit")
        target = text(effect.get("targetId"), "targetId", optional=True)
        text(effect.get("requiredTag"), "requiredTag", optional=True)
        scaled = effect.get("scaleWithStacks", False)
        if type(scaled) is not bool or scaled and (phase != "tick" or kind != "core:true_damage"):
            raise ValueError("scaleWithStacks 只支持周期真实伤害，且必须是布尔值")
        if kind in MULTIPLIERS and value <= 0 or kind in TRAUMA and (phase == "tick" or value <= 0):
            raise ValueError("倍率/创伤效果参数无效")
        if kind in {"core:heal", "core:true_damage"} and value < 0:
            raise ValueError("伤害或治疗值不能为负")
        if kind in {"core:max_health_percent_heal", "core:max_health_percent_true_damage"} and not 0 <= value <= 1:
            raise ValueError("生命比例必须在 0..1 之间")
        if kind == "core:nutrition_change" and target.lower() not in {"carbohydrates", "fat", "protein", "water", "vitamins"}:
            raise ValueError("营养 targetId 无效")
        if kind == "core:temperature_warming" and (phase == "tick" or phase == "start" and (value <= 0 or upper is None)):
            raise ValueError("临时增温必须使用 start/stop，start 要求正 value 与 upperLimit")
        if kind == "core:body_durability_restore" and (phase == "start" or value <= 0 or target not in BODY_PARTS):
            raise ValueError("部位恢复参数无效")


def validate_catalogs(roots_by_package, packages):
    """检查全部启用分包，禁止同名 BUFF 跨分包重复。"""
    ids = set()
    for package in packages:
        root = roots_by_package[package["id"]]
        if set(root) - {"schemaVersion", "buffs"} or type(root.get("schemaVersion")) is not int or root["schemaVersion"] != 1 or not isinstance(root.get("buffs"), list):
            raise ValueError(f"BUFF 分包 {package['id']} 结构无效")
        for source in root["buffs"]:
            validate_definition(source)
            key = source["id"].strip().casefold()
            if key in ids:
                raise ValueError(f"跨分包存在重复 BUFF ID：{source['id']}")
            ids.add(key)
