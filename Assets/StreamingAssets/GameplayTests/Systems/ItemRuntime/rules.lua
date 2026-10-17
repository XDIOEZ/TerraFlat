-- 检查模块结构版本和正式物品工厂的可变状态隔离。
test.system("item_runtime", "module_structure", { stock = 2 }, function(data)
    assert(data.added == data.before + 1, "新增模块没有发布结构变化")
    assert(data.same == data.added, "原引用赋值误增结构版本")
    assert(data.disabled == data.same + 1, "禁用模块没有失效调度")
    assert(data.payload == data.disabled and data.stock == 2, "库存载荷变化被当成模块结构变化")
    assert(data.removed and data.remaining == 0 and data.afterRemove == data.payload + 1, "移除模块没有更新结构")
    assert(data.afterDetachedEnable == data.afterRemove, "被移除模块仍回写宿主结构")
end, "模块增删启停与载荷通知边界")

for _, row in ipairs({ { amount = 3, weight = 2, volume = 4, stock = 7 }, { amount = 0, weight = 0.5, volume = 1.5, stock = 0 } }) do
    test.system("item_runtime", "clone_runtime", row, function(data)
        assert(data.guid == data.sourceGuid, "复制运行态改变了持久身份")
        assert(data.independentStack and data.independentModules, "复制后共享可变库存或模块")
        assert(data.initialAmount == row.amount and data.sourceAmount == row.amount, "修改副本污染原数量")
        assert(data.initialStock == row.stock and data.sourceStock == row.stock, "修改副本污染原模块库存")
        assert(not data.sourceHasAddedTag, "副本标签污染原配置")
        assert(data.sourceWeight == row.amount * row.weight and data.sourceVolume == row.amount * row.volume, "重量体积计算错误")
    end, "物品独立运行态：数量 " .. row.amount)
end

-- 单图动画只摆动独立视觉层，物品根、刚体与原贴图局部姿态都必须保持原值。
local function checkAnimationBoundary(data)
    assert(#data.samples > 0, "单图动画没有返回真实姿态采样")
    for _, sample in ipairs(data.samples) do
        assert(sample.rootPositionError < .00001 and sample.bodyPositionError < .00001,
            "单图动作修改了物品根或刚体位置")
        assert(sample.rootRotationError < .001 and sample.rootScaleError < .00001,
            "单图动作修改了物品根旋转或缩放")
        assert(sample.localPositionError < .00001 and sample.localRotationError < .001 and sample.localScaleError < .00001,
            "单图表现覆盖了原贴图节点的局部姿态")
        for _, field in ipairs({"x", "y", "z", "angle", "scaleX", "scaleY"}) do
            assert(type(sample[field]) == "number" and sample[field] == sample[field] and math.abs(sample[field]) < math.huge,
                "单图姿态含无效数值：" .. field)
        end
    end
end

local function checkAnimationRest(sample)
    assert(math.abs(sample.x) < .0002 and math.abs(sample.y) < .0002 and math.abs(sample.z) < .0002,
        "单图动作结束后没有回到原位置")
    assert(math.abs(sample.angle) < .005 and math.abs(sample.scaleX - 1) < .0002 and math.abs(sample.scaleY - 1) < .0002,
        "单图动作结束后没有恢复原角度和缩放")
    assert(not sample.attacking, "单图攻击结束后仍保留攻击状态")
end

for _, network in ipairs({false, true}) do
    local steps = {}
    for index = 1, 20 do steps[index] = {moving = true, deltaTime = .08} end
    for index = 21, 36 do steps[index] = {moving = false, deltaTime = .08} end
    test.system("item_runtime", "single_sprite_animation", {network = network, steps = steps}, function(data)
        checkAnimationBoundary(data)
        local left, right, lifted = false, false, false
        for index = 1, 20 do
            left = left or data.samples[index].angle < -.1
            right = right or data.samples[index].angle > .1
            lifted = lifted or data.samples[index].y > .005
        end
        assert(left and right and lifted, "主动走路没有表现左右摇摆与起伏")
        checkAnimationRest(data.samples[#data.samples])
    end, network and "单图远端移动与停止复位" or "单图本地主动移动与停止复位")
end

test.system("item_runtime", "single_sprite_animation", {steps = {
    {moving = false, velocityX = 4, deltaTime = .1}, {deltaTime = .1}, {deltaTime = .1}
}}, function(data)
    checkAnimationBoundary(data)
    for _, sample in ipairs(data.samples) do
        assert(sample.bodySpeed > 3, "被动移动夹具没有提供真实刚体速度")
        checkAnimationRest(sample)
    end
end, "单图被动漂移不误播迈步")

for _, direction in ipairs({
    {name = "右", x = 1, y = 0}, {name = "左", x = -1, y = 0},
    {name = "上", x = 0, y = 1}, {name = "下", x = 0, y = -1},
    {name = "镜像父级向右", x = 1, y = 0, mirrored = true}
}) do
    local steps = {{attack = true, x = direction.x, y = direction.y, deltaTime = .06}}
    for index = 2, 18 do steps[index] = {deltaTime = .08} end
    test.system("item_runtime", "single_sprite_animation", {mirrored = direction.mirrored == true, steps = steps}, function(data)
        checkAnimationBoundary(data)
        local first = data.samples[1]
        assert(first.attacking and first.x * direction.x + first.y * direction.y > .04,
            "单图攻击没有朝实际世界攻击方向前探")
        checkAnimationRest(data.samples[#data.samples])
    end, "单图攻击前探与归位：" .. direction.name)
end

test.system("item_runtime", "single_sprite_animation", {steps = {
    {jump = true, deltaTime = .105}, {deltaTime = .105}, {deltaTime = .105}, {deltaTime = .105},
    {deltaTime = .1}, {deltaTime = .1}, {deltaTime = .1}, {deltaTime = .1}
}}, function(data)
    checkAnimationBoundary(data)
    assert(data.samples[1].y > .01 and data.samples[2].y > data.samples[1].y,
        "单图跳跃没有向上起跳")
    assert(data.samples[2].y > .1 and data.samples[3].y < data.samples[2].y,
        "单图跳跃没有从最高点落下")
    checkAnimationRest(data.samples[#data.samples])
end, "单图跳跃起落与原地复位")

test.system("item_runtime", "single_sprite_animation", {steps = {
    {attack = true, jump = true, deltaTime = .08},
    {mode = "Animator", deltaTime = .08},
    {mode = "SingleSprite", moving = true, deltaTime = .08},
    {lifecycle = "disable"},
    {lifecycle = "enable", moving = false, deltaTime = 0},
    {attack = true, jump = true, deltaTime = .08},
    {lifecycle = "unload"}, {lifecycle = "unload"},
    {lifecycle = "load", deltaTime = 0}, {lifecycle = "load", deltaTime = 0},
    {attack = true, jump = true, deltaTime = .08}, {reset = true, deltaTime = 0},
    {mode = "Animator", deltaTime = 0}
}}, function(data)
    checkAnimationBoundary(data)
    assert(data.samples[1].attacking and data.samples[1].y > .01, "中断动作夹具没有先启动攻击和跳跃")
    for _, index in ipairs({2, 4, 5, 7, 8, 9, 10, 12, 13}) do checkAnimationRest(data.samples[index]) end
    for _, index in ipairs({2, 4, 7, 8, 13}) do
        assert(data.samples[index].parentRestored, "切模式或卸载后没有恢复原父级和同级顺序")
    end
    assert(not data.samples[7].loaded and not data.samples[8].loaded and data.samples[9].loaded and data.samples[10].loaded,
        "单图模块重复加载或卸载的生命周期不正确")
end, "单图中断切换、重复启停、层级恢复与清除残留动作")

test.system("item_runtime", "single_sprite_animation", {steps = {
    {moving = true, deltaTime = .08}, {externalY = 180, deltaTime = .08},
    {moving = false, mode = "Animator", deltaTime = 0},
    {mode = "SingleSprite", attack = true, deltaTime = .04}, {lifecycle = "unload"}
}}, function(data)
    checkAnimationBoundary(data)
    for index = 2, #data.samples do
        assert(data.samples[index].localFacingX < -.9, "切模式或释放单图层覆盖了外部最新翻面")
    end
    assert(data.samples[3].parentRestored and data.samples[5].parentRestored,
        "外部翻面后没有恢复原视觉层级")
end, "单图层释放保留外部最新Y轴翻面")

test.system("item_runtime", "single_sprite_animation", {withAnimator = true, steps = {
    {attack = true, jump = true, deltaTime = .08},
    {componentEnabled = false, attack = true, jump = true, hit = true},
    {attack = true, jump = true, hit = true},
    {componentEnabled = true, deltaTime = .08},
    {attack = true, deltaTime = .06},
    {componentEnabled = false, attack = true, jump = true, hit = true}
}}, function(data)
    checkAnimationBoundary(data)
    assert(data.samples[1].attacking and not data.samples[1].bodyAnimatorEnabled,
        "停用边界夹具没有先进入单图动作并暂停身体Animator")
    for _, index in ipairs({2, 3, 6}) do
        local sample = data.samples[index]
        checkAnimationRest(sample)
        assert(not sample.componentEnabled and sample.parentRestored and sample.bodyAnimatorEnabled,
            "停用动画组件后动作入口重新建立了单图层或卡住身体Animator")
    end
    checkAnimationRest(data.samples[4])
    assert(data.samples[5].componentEnabled and data.samples[5].attacking and data.samples[5].x > .04,
        "重新启用动画组件后无法正常播放新攻击")
end, "单图组件停用拒绝攻击跳跃受击且恢复身体Animator")
