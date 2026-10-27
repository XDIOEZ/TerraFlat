-- 正式检测仪按根画布 Expand 规则检查桌面和手机的物理尺寸及内部内容。
local function analyzer_bounds_inside(child, parent)
    return child.left >= parent.left - 0.2 and child.right <= parent.right + 0.2
        and child.bottom >= parent.bottom - 0.2 and child.top <= parent.top + 0.2
end

for _, size in ipairs({
    {2560, 1440, 1.5}, {1920, 1080, 1.0}, {1920, 1080, 1.5},
    {1600, 900, 1.5}, {1280, 720, 1.5},
    {1080, 1920, 1.0}, {1080, 1920, 1.5}, {720, 1600, 1.5}, {720, 1600, 3.25}
}) do
    local width, height, user_scale = size[1], size[2], size[3]
    test.system("ui_settings", "atmosphere_analyzer_ui",
        {width = width, height = height, userScale = user_scale}, function(data)
        local resolution = width .. "x" .. height .. " UI倍率 " .. user_scale
        assert(analyzer_bounds_inside(data.frame, data.canvas), "检测仪越出安全画布：" .. resolution)
        assert(analyzer_bounds_inside(data.screen, data.frameLocalBounds)
            and analyzer_bounds_inside(data.close, data.frameLocalBounds),
            "显示屏或关闭键越出仪器：" .. resolution)
        local width_ratio = data.frame.width / data.canvas.width
        if width < height and user_scale <= 1.5 then
            assert(width_ratio >= .75 and width_ratio <= .84, "竖屏仪器没有合理利用屏宽：" .. resolution)
        end
        for _, text in ipairs(data.texts) do
            assert(analyzer_bounds_inside(text.bounds, data.screenLocalBounds),
                "仪器文字越过显示屏：" .. text.name .. " " .. resolution)
            assert(text.auto and not text.raycast, "仪器文字未限幅或阻挡输入：" .. text.name)
        end
        assert(data.artworkBound and data.dragUsesFrame and data.closeIsButton
            and data.closeRaycast and data.closeAlpha == 0,
            "仪器贴图、拖动范围或关闭键引用缺失：" .. resolution)
        assert(data.vertical and not data.horizontal and data.hasMask and data.layoutControlsHeight
            and data.contentFitsHeight, "检测仪气体列表没有正确配置遮罩和纵向布局")
        assert(data.contentHeight > data.viewportHeight and data.bottomPosition > data.topPosition + 1,
            "多种气体不能滚动查看：" .. resolution)
        assert(data.count == 16 and #data.rows == 16 and data.planet == "蓝星",
            "检测仪遗漏成分或星球名称")
        assert(data.status == data.expectedStatus and not data.total:find("[eE]")
            and data.total:find(",") and data.fullInteger == "1,000,000,000,000,000"
            and data.fullSmallAmount == "0.000000001",
            "实时状态或大数读数显示错误")
        assert(data.rows[1].fraction > .77 and data.rows[1].fraction < .79,
            "主要气体比例错误")
        for _, row in ipairs(data.rows) do
            assert(row.active and not row.fillRaycast and row.fraction > 0 and row.fraction <= 1
                and math.abs(row.fill - row.fraction) < .0001,
                "气体行比例条与真实成分不一致：" .. row.id)
        end
        assert(data.rows[#data.rows].percentage == "<0.001%", "微量气体被显示成零")
        assert(data.vacuum.status == data.vacuum.expectedStatus and data.vacuum.planet == "真空星球"
            and data.vacuum.total == "0" and data.vacuum.empty == data.vacuum.expectedEmpty
            and data.vacuum.emptyVisible and data.vacuum.visibleCount == 0 and data.vacuum.activeRows == 0,
            "真空星球未清除旧气体或未显示零总量")
        assert(data.unavailable.status == data.unavailable.expectedStatus
            and data.unavailable.planet == data.unavailable.expectedUnavailablePlanet
            and data.unavailable.total == "—" and data.unavailable.empty == data.unavailable.expectedEmpty
            and data.unavailable.emptyVisible and data.unavailable.visibleCount == 0
            and data.unavailable.activeRows == 0, "无法测量时残留旧星球或气体读数")
        assert(data.rowsReused and data.restoredCount == 16, "切换测量状态后没有复用并恢复气体行")
    end, "大气检测仪屏幕布局、滚动与读数 " .. width .. "×" .. height .. " / " .. user_scale)
end
