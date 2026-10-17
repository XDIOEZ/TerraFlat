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
    { size = 5, aspect = 1 }, { size = 20, aspect = 2.2 },
    { size = 40, aspect = 0.6, flipped = true }, { size = 55, aspect = 1.8 },
    { size = 60, aspect = 2.2 }, { size = 90, aspect = 1 }
}) do
    test.system("rendering", "zoom_cloud_mask", args, function(data)
        assert(data.clearRadius == 8 and data.opaqueRadius == 28, "拉远或改变宽高比后雾圈世界半径变化")
        assert(data.noiseScale == data.expectedNoiseScale, "缩放改变了云纹的世界尺寸")
        assert(math.abs(data.viewportHeight - data.expectedHeight) < 0.001, "云圈没有使用实际镜头世界投影")
        assert(math.abs(data.viewportWidth - data.expectedWidth) < 0.001, "宽高比改变了雾圈世界距离")
        assert(data.centerAligned, "雾圈圆心没有跟随玩家")
    end, "雾圈固定世界半径并随实际镜头投影")
end

for _, args in ipairs({
    { size = 20, expected = 0 }, { size = 55, expected = 0 }, { size = 60, expected = 0 },
    { size = 75, expected = 0.5 }, { size = 90, expected = 1 }, { size = 200, expected = 1 }
}) do
    test.system("rendering", "universe_view_transition", args, function(data)
        assert(math.abs(data.opacity - args.expected) < 0.0001, "宇宙画面没有在原视野区间直接淡入")
    end, "固定雾圈后的宇宙视图直接淡入")
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
