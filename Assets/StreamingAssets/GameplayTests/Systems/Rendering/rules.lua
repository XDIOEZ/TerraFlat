-- 渲染参数由正式 IsValid 规则判定，图像效果需场景验收。
test.system("rendering", "fog_config", {}, function(data)
    assert(data.valid, "合法雾配置被拒绝")
end, "大雾合法配置")
for _, args in ipairs({ { clearRadius = 0 }, { opaqueRadius = 10 }, { transitionSeconds = -1 }, { noiseScale = 0 }, { driftSpeed = -1 }, { noiseStrength = 0.21 }, { red = 1.1 } }) do
    test.system("rendering", "fog_config", args, function(data)
        assert(not data.valid, "大雾接受非法半径、时间、噪声或颜色")
    end, "大雾配置拒绝非法边界")
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
