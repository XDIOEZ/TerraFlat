-- 独立布局规则，避免 UI 综合清单超过每份 Lua 的 256 步预算。
local function scroll_near(actual, expected, message)
    assert(type(actual) == "number" and math.abs(actual - expected) < 0.001, message)
end

-- 公共窗口在父级缩放和全屏透明根下都按真实可见边界拖动。
for _, scale in ipairs({0.5, 1, 1.75}) do
    for _, child in ipairs({false, true}) do
        test.system("ui_settings", "window_drag_bounds", {parentScale = scale, childWindow = child,
            steps = {{x=100,y=-40},{x=100,y=-40},{x=2000,y=2000},{x=-2000,y=2000},
                {x=-2000,y=-2000},{x=2000,y=-2000},{x=0,y=0}}}, function(data)
            assert(data.released and data.revision > 0 and #data.samples == 7, "窗口拖动未完成或漏采样")
            for _, sample in ipairs(data.samples) do assert(sample.inside, "窗口拖动后跑出安全区") end
            for _, key in ipairs({"left", "right"}) do
                scroll_near(data.samples[1][key] - data.before[key], 100, "缩放父级下窗口没有跟随指针水平移动")
            end
            scroll_near(data.samples[1].top - data.before.top, -40, "缩放父级下窗口没有跟随指针垂直移动")
            for _, key in ipairs({"left", "right", "top", "bottom"}) do
                scroll_near(data.samples[1][key], data.samples[2][key], "相同指针坐标重复拖动导致位置漂移")
                scroll_near(data.samples[7][key], data.before[key], "拖回起点后窗口未回到原位置")
            end
            assert(data.resized.inside, "可用区域缩小后窗口未回收")
            if not child then
                assert(data.animation and data.animation.grewOutside and data.animation.ended.inside,
                    "窗口打开动画放大后未按最终尺寸重新限界")
            end
        end, "窗口拖动边界 / " .. scale .. " / " .. tostring(child))
    end
end

-- 同一个屏幕坐标重复到达不能反向缩回；八个方向均固定对侧边，并覆盖伸展锚点与尺寸上下限。
for _, edge in ipairs({{-1,0},{1,0},{0,-1},{0,1},{-1,-1},{-1,1},{1,-1},{1,1}}) do
    for _, stretch in ipairs({false, true}) do
        local x, y = edge[1], edge[2]
        test.system("ui_settings", "window_resize_pointer", {edgeX=x, edgeY=y,
            pivotX=stretch and 0.2 or 0.5, pivotY=stretch and 0.8 or 0.5, stretch=stretch,
            steps={{x=x*100,y=y*60},{x=x*100,y=y*60},{x=x*1000,y=y*1000},
                {x=-x*1000,y=-y*1000},{x=x*100,y=y*60}}}, function(data)
            assert(#data.samples == 5, "窗口缩放漏采样")
            local expected = {{x==0 and 360 or 460,y==0 and 240 or 300},
                {x==0 and 360 or 460,y==0 and 240 or 300},
                {x==0 and 360 or 600,y==0 and 240 or 450},
                {x==0 and 360 or 160,y==0 and 240 or 100},
                {x==0 and 360 or 460,y==0 and 240 or 300}}
            for i, sample in ipairs(data.samples) do
                scroll_near(sample.width, expected[i][1], "窗口宽度漂移或未遵守上下限")
                scroll_near(sample.height, expected[i][2], "窗口高度漂移或未遵守上下限")
                for _, key in ipairs({x<0 and "right" or "left", y<0 and "top" or "bottom"}) do
                    scroll_near(sample[key], data.before[key], "缩放时未抓住的对侧边发生错位")
                end
            end
            for _, key in ipairs({"left", "right", "top", "bottom"}) do
                scroll_near(data.samples[1][key], data.samples[2][key], "重复缩放事件产生抖动")
                scroll_near(data.samples[1][key], data.samples[5][key], "反向缩放再返回后位置漂移")
            end
        end, "窗口八方向缩放 / " .. x .. "," .. y .. " / " .. tostring(stretch))
    end
end

for _, size in ipairs({{2560,1440},{1920,1080},{1600,900},{1280,720},{960,720},{720,1280}}) do
    for _, scale in ipairs({0.6, 0.85}) do
        test.system("ui_settings", "bag_hotbar_layout", {width = size[1], height = size[2], scale = scale}, function(data)
            assert(math.abs(data.gap) < 0.1 and math.abs(data.leftGap) < 0.1)
            assert(data.insideTop)
        end, "背包底边避让快捷栏 " .. size[1] .. "x" .. size[2] .. " / " .. scale)
    end
end

-- 主菜单四个入口与其它 UI 共用根画布倍率；自适应仅在空间不足时限幅。
for _, size in ipairs({{2560,1440},{1920,1080},{1600,900},{1280,720},{960,720},{720,1280}}) do
    local previous, halfScale
    for _, scale in ipairs({0.25,0.5,1,1.5,2,2.5,3.25}) do
        test.system("ui_settings", "main_menu_scale_layout",
            {width = size[1], height = size[2], scale = scale}, function(data)
            assert(data.buttons == 4, "正式主菜单缺少旅程入口")
            assert(data.insideSafeArea and data.brandCardSeparated and data.brandSettingsSeparated,
                "主菜单缩放后出现越界或标题与按钮重叠")
            assert(math.abs(data.menuToBrandRatio - 0.9) < 0.001 and
                math.abs(data.menuPrefabScaleX - 0.9) < 0.001 and
                math.abs(data.menuPrefabScaleY - 0.9) < 0.001,
                "四个主菜单按钮未在原0.6基准上放大50%或Prefab不同步")
            if previous then
                for _, key in ipairs({"buttonPixels","brandPixels","settingsPixels",
                    "leftMarginPixels","topMarginPixels"}) do
                    assert(data[key] >= previous[key] - 0.2,
                        "提高用户界面倍率却使主菜单局部变小：" .. key)
                end
            end
            if scale == 0.5 then halfScale = data end
            if scale == 1 then
                for _, key in ipairs({"buttonPixels","brandPixels","settingsPixels",
                    "leftMarginPixels","topMarginPixels"}) do
                    assert(math.abs(data[key] - halfScale[key] * 2) < 0.3,
                        "空间足够时主菜单没有跟随用户倍率整体缩放：" .. key)
                end
                if size[1] == 1920 then
                    assert(math.abs(data.buttonPixels - 93.6) < 0.1,
                        "1920x1080、100% UI倍率下主菜单入口高度未从62.4放大50%")
                end
            end
            previous = data
        end, "主菜单整体缩放 " .. size[1] .. "x" .. size[2] .. " / " .. scale)
    end
end

-- 从四个正式面板 Prefab 采样卡片基准和真实安全区缩放组件，验证放大/缩小不是被限幅抵消。
for _, modal in ipairs({{"save",2},{"new_game",3},{"network",2},{"mods",1}}) do
    for _, size in ipairs({{2560,1440},{1920,1080},{1600,900},{1280,720},{960,720},{720,1280}}) do
        local previous, halfScale, baseline
        for _, scale in ipairs({0.25,0.5,1,1.5,2,3.25}) do
            test.system("ui_settings", "main_menu_modal_scale_layout",
                {panel = modal[1], width = size[1], height = size[2], scale = scale}, function(data)
                assert(data.panel == modal[1] and data.targets == modal[2] and data.containsBounds,
                    "主菜单面板缺少共同缩放目标：" .. modal[1])
                assert(math.abs(data.baseScaleX - 0.9) < 0.001 and
                    math.abs(data.baseScaleY - 0.9) < 0.001,
                    "主菜单面板未在原0.6基准上放大50%：" .. modal[1])
                assert(data.presentationScale == 1 and data.insideSafeArea,
                    "面板叠乘表现倍率或超出安全区：" .. modal[1])
                assert(data.widthPixels > 0 and data.heightPixels > 0,
                    "面板没有有效显示尺寸：" .. modal[1])
                -- 放大后较早触及安全区上限；极端倍率下边距也会变宽，只核验不越界。
                if previous and scale <= 1.5 then
                    assert(data.widthPixels >= previous.widthPixels - 0.2 and
                        data.heightPixels >= previous.heightPixels - 0.2,
                        "提高全局 UI 倍率时主菜单面板反而缩小：" .. modal[1])
                end
                if scale == 0.5 then halfScale = data end
                if scale == 1 then
                    baseline = data
                    assert(math.abs(data.widthPixels - halfScale.widthPixels * 2) < 0.5 and
                        math.abs(data.heightPixels - halfScale.heightPixels * 2) < 0.5,
                        "空间足够时主菜单面板未跟随用户倍率放大：" .. modal[1])
                    if size[1] == 1920 then
                        local expectedWidth = modal[1] == "new_game" and 1350 or 1332
                        assert(math.abs(data.widthPixels - expectedWidth) < 0.5 and
                            math.abs(data.heightPixels - 756) < 0.5,
                            "1920x1080、100% UI倍率下面板基准尺寸未放大50%：" .. modal[1])
                    end
                end
                if scale == 3.25 then
                    assert(data.widthPixels > baseline.widthPixels and
                        data.heightPixels > baseline.heightPixels,
                        "高倍率时主菜单面板不应恢复到默认尺寸：" .. modal[1])
                end
                previous = data
            end, "主菜单 " .. modal[1] .. " 面板缩放 " ..
                size[1] .. "x" .. size[2] .. " / " .. scale)
        end
    end
end

-- 扫描包含嵌套控件的正式资产，防止关闭自动缩放、过高下限或 Overflow 再次让长翻译出框。
-- 正式库存分类控件在大倍率、安全区和缩放父级中展开、滚动、缩放及重开。
local function assert_inventory_category_popup_bounds(sample, label)
    assert(sample.hostIsSafeArea and sample.popupOnTop and sample.popupActive and sample.raycasts,
        label .. "分类弹层未在所属安全区顶层显示或遮罩射线丢失")
    assert(sample.left >= sample.safeLeft + 7.9 and sample.right <= sample.safeRight - 7.9
        and sample.bottom >= sample.safeBottom + 7.9 and sample.top <= sample.safeTop - 7.9,
        label .. "分类弹层越出安全区")
    assert(sample.scaleX > 0 and sample.scaleY > 0 and sample.scaleX <= sample.parentScaleX + 0.001
        and sample.scaleY <= sample.parentScaleY + 0.001
        and math.abs(sample.scaleX / sample.parentScaleX - sample.scaleY / sample.parentScaleY) < 0.001,
        label .. "分类弹层没有保留父级比例或被额外放大")
    assert(sample.rows == 14 and sample.viewportHeight > 0 and sample.contentHeight > sample.viewportHeight,
        label .. "分类列表丢行或失去有效滚动区")
end

for index, options in ipairs({
    {width = 1920, height = 1080, scale = 1.5},
    {width = 1920, height = 1080, scale = 3.25, oppositeCorner = true},
    {width = 1280, height = 720, scale = 3.25, left = 70, right = 28, top = 20, bottom = 32},
    {width = 360, height = 800, scale = 3.25, left = 24, right = 18, top = 38, bottom = 40},
    {width = 1920, height = 1080, scale = 1.5, parentScaleX = .65, parentScaleY = .85},
    {width = 1280, height = 720, scale = 3.25, parentScaleX = 1.3, parentScaleY = 1.3, oppositeCorner = true},
}) do
    test.system("ui_settings", "inventory_category_popup_layout", options, function(data)
        assert_inventory_category_popup_bounds(data.beforeResize, "首次展开：")
        assert_inventory_category_popup_bounds(data.afterResize, "展开期间安全区缩小：")
        assert_inventory_category_popup_bounds(data.reopened, "关闭重开：")
        assert(data.scrollMovement > 1, "分类长列表不能滚动到后半部分")
        assert(data.closed and data.selectionPreserved, "分类弹层关闭归属、原始尺寸或复选内容丢失")
    end, "库存分类弹层安全区布局 " .. index)
end
-- 背包沿用十列网格；缩小窗口至少保留一整行，过大倍率只缩整体显示。
local function bag_window_inside(child, parent, label)
    assert(child.left >= parent.left - 0.1 and child.right <= parent.right + 0.1
        and child.bottom >= parent.bottom - 0.1 and child.top <= parent.top + 0.1, label)
end

for _, row in ipairs({
    {width = 1920, height = 1080, scale = 1},
    {width = 1920, height = 1080, scale = 3.25},
    {width = 1280, height = 720, scale = 2.5, insetX = 110, insetY = 55},
    {width = 720, height = 1280, scale = 3.25, insetX = 80, insetY = 100},
    {width = 2560, height = 1440, scale = 0.5}
}) do
    test.system("ui_settings", "inventory_bag_window_layout", row, function(data)
        assert(data.scalesRoot and data.columns == 10, "背包缺少整体安全区缩放，或十列库存布局改变")
        assert(data.scrollbarBound and data.scrollbarAutomatic and data.scrollbarActive,
            "背包滚动条没有绑定正式列表或未由滚动区自动控制显隐")
        assert(data.verticalScroll and not data.horizontalScroll and data.verticalFit == 2,
            "背包纵向滚动或内容高度自适应丢失")
        assert(#data.samples == 3, "背包默认、最小、最大尺寸采样不完整")
        local neededWidth = data.columns * data.cellWidth + (data.columns - 1) * data.spacing + data.paddingX
        for _, sample in ipairs(data.samples) do
            bag_window_inside(sample.header, sample.root, "背包标题栏越过窗口")
            bag_window_inside(sample.inner, sample.root, "背包正文底板越过窗口")
            bag_window_inside(sample.footer, sample.root, "背包底栏越过窗口")
            bag_window_inside(sample.scroll, sample.inner, "背包滚动区越过正文")
            bag_window_inside(sample.viewport, sample.scroll, "背包视口越过滚动区")
            bag_window_inside(sample.bar, sample.scroll, "背包滚动条越过滚动区")
            assert(sample.viewport.right <= sample.bar.left + 0.1 and math.abs(sample.bar.width - 32) < 0.1,
                "背包滚动条与槽位视口重叠，或宽度没有统一")
            assert(sample.scroll.top <= sample.header.bottom + 0.1
                and sample.scroll.bottom >= sample.footer.top - 0.1,
                "背包缩小时正文挤入标题或底栏")
            assert(sample.viewportWidth >= neededWidth and sample.contentWidth >= neededWidth
                and math.abs(sample.contentWidth - sample.viewportWidth) < 0.1,
                "十列槽位超出背包内容宽度，或内容未跟随视口")
            assert(sample.viewportHeight >= data.cellHeight + data.paddingY - 0.1,
                "背包最小高度无法显示完整一行槽位")
            bag_window_inside(sample.scaledBounds, data.available, "背包在高 UI 倍率或窄安全区中出屏")
            assert(sample.scaleX > 0 and sample.scaleX <= 1.001
                and math.abs(sample.scaleX - sample.scaleY) < 0.001,
                "背包安全区适配失去等比缩小规则")
        end
        assert(math.abs(data.samples[1].root.width - 1004) < 0.1
            and math.abs(data.samples[1].root.height - 560) < 0.1,
            "背包默认设计尺寸改变")
        if row.scale == 1 and row.width == 1920 then
            assert(math.abs(data.samples[1].scaleX - 1) < 0.001,
                "空间足够时背包未保留原始显示比例")
        end
    end, "背包可缩放窗口与安全区 " .. row.width .. "×" .. row.height .. " / " .. row.scale)
end
