-- 直接驱动正式树执行器，断言优先级抢占先退出旧动作再进入新动作。
test.system("ai", "behavior_tree_priority", {}, function(data)
    assert(data.initialStatus == "Running" and data.initialAction == "test_low",
        "无威胁时没有持续执行低优先级动作")
    assert(data.threatStatus == "Running" and data.threatAction == "test_high",
        "威胁出现后高优先级分支没有立即抢占")
    assert(data.clearedStatus == "Running" and data.clearedAction == "test_low",
        "威胁解除后没有恢复低优先级动作")
    assert(table.concat(data.events, ",") ==
        "low.enter,low.tick,low.exit,high.enter,high.tick,high.exit,low.enter,low.tick,low.exit",
        "抢占、恢复或重复中止时的动作进入/退出顺序错误")
    assert(table.concat(data.actionEvents, ",") ==
        "->test_low,test_low->,->test_high,test_high->,->test_low,test_low->" and data.afterAbortAction == "",
        "动作结束时没有通知宿主或重复中止残留当前动作")
end, "行为树优先级抢占及中止顺序")

-- Sequence 续跑时保留已完成动作，但条件失效必须取消正在执行的动作。
test.system("ai", "behavior_tree_sequence", {}, function(data)
    assert(data.firstStatus == "Running" and data.firstAction == "test_waiting" and
        data.completedStatus == "Success" and data.completedAction == "", "Sequence 没有持续等待并在完成后继续")
    assert(table.concat(data.completionEvents, ",") ==
        "once.enter,once.tick,once.exit,waiting.enter,waiting.tick,waiting.tick,waiting.exit,last.enter,last.tick,last.exit",
        "Sequence 重复执行已完成动作或没有按声明顺序继续")
    assert(data.invalidatedStatus == "Failure" and data.invalidatedAction == "" and
        table.concat(data.invalidationEvents, ",") == "waiting.enter,waiting.tick,waiting.exit",
        "前置条件失效后仍运行或未退出当前动作")
end, "Sequence 续跑和前置条件重新检查")

-- 两个子树引用及两个运行实例各自持有动作状态，参数替换不能污染源配置。
test.system("ai", "behavior_tree_subtrees", {}, function(data)
    local expected = "alpha.enter,alpha.tick,alpha.tick,alpha.exit,beta.enter,beta.tick,beta.tick,beta.exit"
    assert(table.concat(data.firstEvents, ",") == expected and table.concat(data.secondEvents, ",") == expected,
        "共享子树的调用位置或不同运行实例共享了动作状态")
    assert(data.firstStatus == "Success" and data.secondStatus == "Success" and data.sourceUnchanged,
        "嵌套参数化子树没有完成，或编译时修改了源定义")
    assert(data.defaultParameter == "default" and data.overrideParameter == "override",
        "共享子树默认参数或引用处覆盖没有生效")
    assert(data.firstInitialAction == "alpha" and data.secondInitialAction == "alpha" and
        data.firstNextAction == "beta" and data.secondNextAction == "beta" and
        data.defaultAction == "default" and data.overrideAction == "override",
        "共享子树动作 ID 没有按调用参数独立展开")
end, "嵌套共享子树参数、调用位置与实例隔离")

-- 非法定义由生产编译器拒绝，反转节点使用实际 Success/Failure。
test.system("ai", "behavior_tree_validation", {}, function(data)
    assert(data.unknownActionRejected and data.unknownConditionRejected and
        data.missingSubtreeRejected and data.cycleRejected and data.badChildrenRejected,
        "行为树接纳了未知节点、缺失/循环子树或错误的节点结构")
    assert(data.invertedSuccess == "Failure" and data.invertedFailure == "Success",
        "反转节点没有正确翻转完成结果")
end, "行为树结构拒绝和反转节点")

-- 后台服务每帧执行但不能成为当前动作，抢占与正常完成分别清理运行节点。
test.system("ai", "behavior_tree_service", {}, function(data)
    assert(data.firstStatus == "Running" and data.secondStatus == "Running" and
        data.firstAction == "test_waiting" and data.secondAction == "test_waiting",
        "服务节点抢占了主动作身份")
    assert(table.concat(data.actionEvents, ",") == "->test_waiting,test_waiting->",
        "服务节点反复触发动作变化，或主动作中止后没有通知退出")
    assert(table.concat(data.abortedEvents, ",") ==
        "service.enter,service.tick,service.exit,main.enter,main.tick,service.enter,service.tick,service.exit,main.tick,main.abort,main.exit",
        "服务没有每帧执行、正常完成被误判中止，或主动作中止没有清理")
    assert(data.completedStatus == "Success" and table.concat(data.completedEvents, ",") ==
        "service.enter,service.tick,main.enter,main.tick,main.exit,service.abort,service.exit",
        "主树完成时没有中止持续服务，或完成的主动作被误判为中止")
end, "后台服务、动作身份与中止语义")

-- 真实单图动画先命中指定状态；缺失状态按移动或静止角色回退。
test.system("ai", "animal_tree_animation", {}, function(data)
    assert(data.explicitState and data.stoppedFallback and data.movingFallback and data.actualNode,
        "行为树动画没有命中指定状态、按角色回退或被正式动作节点正确驱动")
end, "行为树动作的动画命中与回退")

-- 默认模块参数继续继承，明确要求完整替换时只替换指定模块。
test.system("ai", "animal_tree_inheritance", {}, function(data)
    assert(data.mergedOld == 1 and data.mergedNestedOld == 2 and data.mergedNestedNew == 3 and data.mergedShared == "child",
        "普通模块继承不再保留父参数或子参数覆盖")
    assert(not data.replacedOldPresent and not data.replacedNestedOldPresent and data.replacedNestedNew == 3 and
        data.replacedShared == "child" and data.replacedPrefab == "SharedController" and data.otherModuleRetained,
        "独立模块定义仍混入父参数，或错误删除了其他继承模块")
end, "模块默认继承与显式整体替换")

-- ECS 参数映射读取正式继承树和独立 ECS 覆盖，不按物种名称推算规则。
test.system("ai", "aiecs_tree_parameters", {}, function(data)
    local function near(a, b) return math.abs(a - b) < .0001 end
    for id, expected in pairs({
        Wolf = {sense = 28, chase = 44, attack = 2.1, windup = .25, active = 1 / 3, recovery = .35, cooldown = 2},
        SnowLeopard = {sense = 24, chase = 30, attack = 1.2, windup = .35, active = .25, recovery = .25, cooldown = 1.2},
        Bear = {sense = 30, chase = 30, attack = 1.8, windup = .4, active = .2, recovery = .3, cooldown = 1.8}
    }) do
        local actual = assert(data[id], "缺少正式 ECS 参数采样：" .. id)
        for key, value in pairs(expected) do
            assert(near(actual[key], value), "ECS 映射未保留正式树参数：" .. id .. "." .. key)
        end
        assert(near(actual.retry, 3), "ECS 追击重试没有读取正式能力：" .. id)
    end
    assert(near(data.Chicken.eggDays, 2) and data.Chicken.eggItem == "Egg", "鸡的正式产蛋配置没有映射到 ECS")
    local snowDamage = data.SnowLeopard.damage
    assert(snowDamage.cutting == 10 and snowDamage.piercing == 10 and snowDamage.chopping == 1 and snowDamage.blunt == 1 and
        snowDamage.physical == 22 and snowDamage.bleedingAt83 == 3 and snowDamage.bleedingAt85 == 0,
        "ECS 合并模块参数时改变了雪豹伤害四槽、总量或出血资格")
    assert(near(data.WildBoar.fleeHealth, .2), "野猪的共享子树低血阈值没有映射到 ECS")
    assert(near(data.override.sense, 73) and near(data.override.speed, 7.25) and
        near(data.override.eggDays, 9) and data.override.eggItem == "OverrideEgg",
        "显式 ECS 配置没有优先于共享树和模块参数")
    assert(near(data.senseOverride.sense, 91), "运行时感知覆盖没有优先于 ECS 配置")
end, "正式行为树到 ECS 参数映射与覆盖优先级")

-- 正式继承目录和外壳按生产模块身份装配并加载树，检查所有物种及共用子树。
test.system("ai", "animal_tree_catalog", {}, function(data)
    local expected = {
        Bear = true, Bird = true, Seagull = true, Bee = true, Chicken = true,
        WildBoar = true, Wolf = true, Ghost = true, Zombie = true, SmallFish = true,
        Sheep = true, Rabbit = true, SnowLeopard = true
    }
    local seen, references, sharedUsed = {}, {}, false
    assert(#data.actors == 13, "正式生物目录数量不符")
    for _, actor in ipairs(data.actors) do
        assert(expected[actor.id] and not seen[actor.id], "生物目录缺失或重复：" .. actor.id)
        seen[actor.id] = true
        assert(actor.declaredId == "AI" and actor.prefabId == "Module_AnimalBehaviorTree" and
            actor.enabled and actor.identityBound and actor.treeLoaded,
            "正式生物没有装配并加载唯一行为树：" .. actor.id)
        assert(actor.shellActors == 1 and actor.missingScripts == 0 and actor.hasSharedSubtrees,
            "正式外壳残留重复 AI、失效脚本或缺少共享子树：" .. actor.id)
        for _, name in ipairs(actor.subtreeReferences) do
            if references[name] then sharedUsed = true end
            references[name] = true
        end
        if actor.id == "Wolf" then assert(actor.hasPack, "狼的正式树没有绑定群体能力") end
        if actor.id == "Bear" then assert(actor.hasTerritory and not actor.hasPack, "熊的树能力不符合领地规则") end
        if actor.id == "Rabbit" then
            assert(not actor.hasProduction and not actor.hasProductionModule, "兔子继承了产蛋能力或生产模块")
        end
        if actor.id == "Bird" or actor.id == "Seagull" or actor.id == "Bee" then
            assert(actor.hasFlight, "飞行生物缺少飞行能力：" .. actor.id)
        end
        if actor.id == "Bee" then assert(actor.hasColony, "蜜蜂缺少群落能力") end
        if actor.id == "SmallFish" then assert(actor.hasAquatic, "小鱼缺少水生能力") end
        if actor.id == "Ghost" then assert(actor.hasLight, "幽灵缺少光照导航能力") end
        if actor.id == "SnowLeopard" then
            assert(not actor.hasPack, "雪豹继承了狼群能力配置")
            local damage = actor.damage
            assert(damage.cutting == 10 and damage.piercing == 10 and damage.chopping == 1 and damage.blunt == 1 and
                damage.physical == 22 and damage.cutting + damage.piercing + damage.chopping == 21,
                "雪豹实际伤害模块丢失四槽或改变物理与刃伤总量")
            assert(damage.bleedingAt83 == 3 and damage.bleedingAt85 == 0,
                "雪豹刃伤没有按正式出血概率与等级计算")
        end
    end
    assert(sharedUsed, "没有两个正式物种引用同一共享子树")
end, "全部正式生物的树加载、能力身份与共享子树")
