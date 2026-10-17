-- 渲染参数由正式 IsValid 规则判定，图像效果需场景验收。
test.system("rendering", "fog_config", {}, function(data)
    assert(data.valid, "合法雾配置被拒绝")
end, "大雾合法配置")
for _, args in ipairs({ { clearRadius = 0 }, { opaqueRadius = 10 }, { transitionSeconds = -1 }, { noiseScale = 0 }, { driftSpeed = -1 }, { noiseStrength = 0.21 }, { red = 1.1 } }) do
    test.system("rendering", "fog_config", args, function(data)
        assert(not data.valid, "大雾接受非法半径、时间、噪声或颜色")
    end, "大雾配置拒绝非法边界")
end

for _, args in ipairs({
    { strength = 1, clear = 10, opaque = 20 },
    { strength = 0.5, clear = 20, opaque = 30 },
    { strength = 0.25, clear = 40, opaque = 50 },
    { strength = 0.0005, clear = 20000, opaque = 20010 },
    { strength = 2, clear = 10, opaque = 20 },
    { strength = 0.5, clearRadius = 5, opaqueRadius = 10, clear = 10, opaque = 15 }
}) do
    test.system("rendering", "fog_config", args, function(data)
        assert(data.visible, "非零雾强度没有生成视野圆")
        assert(math.abs(data.clearRadius - args.clear) < 0.01, "雾强度没有按反比改变清晰圆半径")
        assert(math.abs(data.opaqueRadius - args.opaque) < 0.01, "雾圈柔边宽度随强度改变")
    end, "天气雾强度只收缩视野圆并保留固定柔边")
end
for _, strength in ipairs({ 0, -1 }) do
    test.system("rendering", "fog_config", { strength = strength }, function(data)
        assert(not data.visible, "无雾时仍生成遮挡圈")
    end, "无雾和负强度关闭天气雾圈")
end

for _, args in ipairs({
    { size = 5, aspect = 1 }, { size = 20, aspect = 2.2 },
    { size = 40, aspect = 0.6, flipped = true }, { size = 55, aspect = 1.8 },
    { size = 60, aspect = 2.2 }, { size = 90, aspect = 1 }
}) do
    test.system("rendering", "zoom_cloud_mask", args, function(data)
        assert(data.clearWidth == 80 and data.clearHeight == 80, "镜头缩放或宽高比改变了三圈活动区块的方形清晰窗")
        assert(data.opaqueWidth == 112 and data.opaqueHeight == 112, "不透明边界没有落在额外表现预加载圈外沿")
        assert(data.clearMinX == -32 and data.clearMaxX == 48 and data.clearMinY == -48 and data.clearMaxY == 32,
            "清晰窗没有对齐玩家所在区块的实际格边")
        assert(data.windowCenterOffsetX == -5 and data.windowCenterOffsetY == -1, "方窗中心错误地锁在玩家脚下而没有对齐区块")
        assert(data.noiseScale == data.expectedNoiseScale, "缩放改变了云纹的世界尺寸")
        assert(math.abs(data.viewportHeight - data.expectedHeight) < 0.001, "云圈没有使用实际镜头世界投影")
        assert(math.abs(data.viewportWidth - data.expectedWidth) < 0.001, "宽高比改变了雾圈世界距离")
        assert(data.playerProjectionCentered, "雾层世界投影中点没有对齐玩家位置")
    end, "方形雾边界随加载窗口变化并独立于镜头缩放")
end

for _, args in ipairs({
    { playerX = 15.75, playerY = -0.25, minX = -32, maxX = 48, minY = -48, maxY = 32 },
    { playerX = -0.25, playerY = -16.25, minX = -48, maxX = 32, minY = -64, maxY = 16 },
    { playerX = 16, playerY = 0, minX = -16, maxX = 64, minY = -32, maxY = 48 }
}) do
    test.system("rendering", "zoom_cloud_mask", args, function(data)
        assert(data.clearMinX == args.minX and data.clearMaxX == args.maxX and
            data.clearMinY == args.minY and data.clearMaxY == args.maxY, "区块内移动、负坐标或跨区块后雾窗边界错位")
        assert(data.clearWidth == 80 and data.clearHeight == 80, "移动改变了玩家设置的窗口大小")
    end, "方形雾窗对齐正负坐标区块边界")
end

for _, args in ipairs({
    { distance = 3, nextDistance = 5, initial = 80, clear = 144, opaque = 176, expectedDistance = 5 },
    { distance = 5, nextDistance = 2, initial = 144, clear = 48, opaque = 80, expectedDistance = 2 },
    { distance = 2, nextDistance = 1, initial = 48, clear = 16, opaque = 48, expectedDistance = 1 },
    { distance = 0, initial = 16, clear = 16, opaque = 48, expectedDistance = 1 }
}) do
    test.system("rendering", "zoom_cloud_mask", args, function(data)
        assert(data.initialWidth == args.initial and data.loadDistance == args.expectedDistance, "正式加载器没有应用范围设置或最小一圈约束")
        assert(data.clearWidth == args.clear and data.clearHeight == args.clear, "修改加载范围后清晰方窗没有同步扩大或缩小")
        assert(data.opaqueWidth == args.opaque and data.opaqueHeight == args.opaque, "修改加载范围后柔雾外边界没有同步")
        assert(data.featherX == 16 and data.featherY == 16, "柔雾带没有复用额外预绑定的一圈")
    end, "玩家加载范围动态扩大缩小与最小值")
end

test.system("rendering", "zoom_cloud_mask", { distance = 2, chunkWidth = 32, chunkHeight = 8 }, function(data)
    assert(data.clearWidth == 96 and data.clearHeight == 24 and data.opaqueWidth == 160 and data.opaqueHeight == 40,
        "非正方形区块没有按实际宽高生成加载窗和柔雾带")
    assert(data.clearMinX == -32 and data.clearMaxX == 64 and data.clearMinY == -16 and data.clearMaxY == 8,
        "非正方形区块边界错位")
end, "雾窗兼容非正方形区块")

test.system("rendering", "zoom_cloud_mask", { distance = 3, preload = false }, function(data)
    assert(data.opaqueWidth == 80 and data.opaqueHeight == 80, "没有表现预加载圈时暴露了窗口外未加载地形")
    assert(data.clearWidth == 78 and data.clearHeight == 78 and data.featherX == 1 and data.featherY == 1,
        "没有表现预加载圈时未在已加载窗口内保留一格柔边")
end, "雾窗缺少额外表现圈时安全内收柔边")

test.system("rendering", "zoom_cloud_mask", { distance = 1, chunkWidth = 1, chunkHeight = 1, preload = false }, function(data)
    assert(data.clearWidth == 0 and data.clearHeight == 0 and data.opaqueWidth == 1 and data.opaqueHeight == 1,
        "极小窗口产生负数清晰区或越出已加载范围")
    assert(data.featherX == 0.5 and data.featherY == 0.5, "极小窗口的柔边退化为零宽度")
end, "极小雾窗保持有限柔边")

for _, args in ipairs({
    { size = 20, expected = 0 }, { size = 55, expected = 0 }, { size = 60, expected = 0 },
    { size = 75, expected = 0.5 }, { size = 90, expected = 1 }, { size = 200, expected = 1 }
}) do
    test.system("rendering", "universe_view_transition", args, function(data)
        assert(math.abs(data.opacity - args.expected) < 0.0001, "宇宙画面没有在原视野区间直接淡入")
    end, "动态加载雾窗下的宇宙视图直接淡入")
end

test.system("rendering", "group_projection", { outerOrder = 77, innerOrder = 50, spriteOrder = 31 }, function(data)
    assert(data.outerOrder == 77 and data.outerLayerMatches, "代理没有继承最外层启用分组的排序键")
    assert(data.innerOrder == 50 and data.innerLayerMatches, "外层分组停用后没有使用内层排序键")
    assert(data.spriteOrder == 31 and data.spriteLayerMatches, "所有分组停用后没有恢复精灵自身排序键")
end, "独立渲染层级与外部代理排序投影")

test.system("rendering", "universe_view_config", {}, function(data)
    assert(data.valid, "合法宇宙视野配置被拒绝")
end, "宇宙视野合法配置")
for _, args in ipairs({ { start = 0 }, { opaque = 10 }, { maximum = 19 }, { zoom = 1 }, { transitionSeconds = -1 }, { entry = 0 }, { pixels = -1 } }) do
    test.system("rendering", "universe_view_config", args, function(data)
        assert(not data.valid, "宇宙视野接受非法层次、缩放或过渡参数")
    end, "宇宙视野配置拒绝非法边界")
end

for _, args in ipairs({
    { bx = 3, expected = true, label = "相交近光" },
    { bx = 10, expected = false, label = "远光" },
    { bx = 4, expected = true, label = "相切边界" },
    { bx = 4.0005, expected = true, label = "边界保守余量" },
    { bx = 4.01, expected = false, label = "超出边界余量" },
    { bz = 3, expected = true, label = "Z轴仍相交" },
    { bz = 5, expected = false, label = "Z轴分离" },
    { ar = 0, br = 0, expected = true, label = "同点零半径" },
    { ar = 0, br = 0, bx = 1, expected = false, label = "分离零半径" }
}) do
    test.system("rendering", "cave_shadow_spheres", args, function(data)
        assert(data.intersects == args.expected, "洞穴墙阴影球形筛选错误：" .. args.label)
        assert(data.reverseIntersects == data.intersects, "洞穴墙阴影球形筛选不满足对称性：" .. args.label)
    end, "洞穴墙阴影保守球形筛选：" .. args.label)
end
