-- 稳定身份规则无需启动本地化加载，也不切换玩家语言。
for _, row in ipairs({ { tag = "resource.wood", stable = true }, { tag = "example:rare-ore_1", stable = true }, { tag = "木材", stable = false }, { tag = "resource wood", stable = false }, { tag = "", stable = false } }) do
    test.system("localization", "stable_identity", row, function(data)
        assert(data.stable == row.stable, "语言无关 Tag 身份校验错误")
        assert(data.itemNameKey == "item.Stick_Wood.name" and data.itemDescriptionKey == "item.Stick_Wood.description", "物品翻译键未规范化")
        assert(data.uiKey == data.paddedUiKey and data.uiKey ~= data.otherUiKey, "UI 文字键不稳定或忽略内容变化")
        assert(string.sub(data.uiKey, 1, 8) == "ui.text.", "UI 翻译键命名空间错误")
    end, "本地化稳定 ID 与文本键：" .. row.tag)
end
