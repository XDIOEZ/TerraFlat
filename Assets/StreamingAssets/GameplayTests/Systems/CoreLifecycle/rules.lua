test.system("core_lifecycle", "persistent_object_hierarchy", {}, function(data)
    assert(data.available, "常驻分类规则需要编辑器预览场景")
    assert(data.grouped and data.idempotent and data.unitCategory, "常驻分类没有复用唯一的单位变换节点")
    assert(data.posePreserved and data.inactivePreserved and data.recategorizedWithoutCloning,
        "分类改变了对象姿态、激活状态或对象身份")
    assert(data.ownerTreePreserved and data.childCategoryResolved and data.childCannotBeStolen,
        "分类拆散了业务宿主和子对象")
    assert(data.structuralNodesRejected and data.foreignSceneRejected and data.invalidCategoryRejected,
        "分类未拒绝结构节点、其它场景对象或非法类别")
    assert(data.editModeCannotPromoteScene, "编辑态分类意外提升了场景对象生命周期")
    assert(data.destroyedCategoryRecreated and data.ownerReleaseDoesNotDestroyHierarchy and data.otherSceneUnaffected,
        "销毁业务对象或分类节点影响了其它所有者")
end, "常驻层级分类、所有权隔离与重复挂接")

local function near(actual, expected, label)
    assert(type(actual) == "number" and math.abs(actual - expected) < .0001, label .. "错误")
end

test.system("core_lifecycle", "fixed_step_scheduler", {}, function(data)
    local base = { "first", "time_peer", "world", "navigation", "gameplay_first", "gameplay_second",
        "prepare_physics", "space", "physics", "after_physics" }
    local function check_step(sample, tick, count, delta, expected)
        assert(sample.tick == tick and sample.registered == count, "固定步序号或已登记系统数量错误")
        assert(#sample.calls == #expected, "固定步漏执行、重复执行或执行了不应运行的系统")
        for index, name in ipairs(expected) do
            assert(sample.calls[index].name == name, "固定步阶段或同阶段注册顺序错误：" .. index)
            near(sample.calls[index].delta, delta, "固定步传入时间")
        end
    end
    assert(data.beforeTick == 0 and data.beforeRegistered == 14, "初始固定步调度状态错误")
    check_step(data.first, 1, 14, .125, base)
    check_step(data.disabled, 2, 14, .25, { "first", "time_peer", "world", "navigation", "gameplay_first",
        "gameplay_second", "late", "reregistered", "prepare_physics", "space", "physics", "after_physics" })
    check_step(data.reenabled, 3, 14, .5, { "first", "time_peer", "world", "navigation", "gameplay_first",
        "gameplay_second", "late", "reregistered", "prepare_physics", "space", "physics", "disabled", "inactive", "after_physics" })
    check_step(data.unregistered, 4, 10, .75, base)
    local one, two = data.oneStepPerGroup, data.twoStepsPerGroup
    assert(one.groups == 60 and two.groups == 30 and one.ticks == 60 and two.ticks == 60 and
        one.calls == 60 and two.calls == 60, "手动推进次数被渲染分组改变")
    near(one.elapsed, 1, "单步分组的模拟时长")
    near(two.elapsed, 1, "双步分组的模拟时长")
    assert(one.startFrame == one.endFrame and two.startFrame == two.endFrame,
        "独立规则采样意外依赖真实渲染帧")

    local rates = {60, 30, 20, 10, 5}
    local function check_tiered(sample, multiplier, label, baseRate)
        baseRate = baseRate or 60
        assert(sample.ticks == baseRate * multiplier and sample.uniqueRegistrations == 5,
            label .. "重复注册、物理步次数或时间倍率错误")
        assert(#sample.tiers == #rates, label .. "缺少分类 TPS 档位")
        for i, rate in ipairs(rates) do
            local tier = sample.tiers[i]
            local expectedCalls = (rate == 60 and baseRate or rate) * multiplier
            assert(tier.rate == rate and tier.calls == expectedCalls,
                label .. "的 " .. rate .. " TPS 执行次数错误")
            near(tier.seconds, multiplier, label .. "的 " .. rate .. " TPS 累计时间")
            local expectedLargest = rate == 60 and 1 / baseRate or math.ceil(baseRate / rate - .000001) / baseRate
            near(tier.largestStep, expectedLargest, label .. "的 " .. rate .. " TPS 步长")
        end
    end
    check_tiered(data.tiered60Fps, 1, "60FPS")
    check_tiered(data.tiered30Fps, 1, "30FPS 每帧双步")
    check_tiered(data.tiered15Fps, 1, "15FPS 每帧四步")
    check_tiered(data.tieredBase30Tps, 1, "基础 30 TPS", 30)
    check_tiered(data.tieredBase120Tps, 1, "基础 120 TPS", 120)
    check_tiered(data.tieredDoubleSpeed, 2, "2 倍速")
    check_tiered(data.tieredPaused, 1, "暂停恢复")
    local before = data.tieredPaused.beforeResume
    assert(before.ticks == 30, "暂停前固定步计数错误")
    for i, count in ipairs({30, 15, 10, 5, 2}) do
        assert(before.tiers[i].calls == count, "暂停前分级执行次数错误")
        near(before.tiers[i].seconds, count / rates[i], "暂停前各档时间")
    end
    assert(data.tieredHitch.callsAfterHitch == 1 and data.tieredHitch.callsAfterNextStep == 1 and
        data.tieredHitch.registered == 1 and data.tieredHitch.physicsRejectsLowRate,
        "卡顿后出现同帧重复补算、重复登记或物理误降频")
    near(data.tieredHitch.deliveredSeconds, .35, "卡顿累计时间")
    assert(data.tieredDisabled.beforeDue == 0 and data.tieredDisabled.calls == 1,
        "禁用期间累计了欠债，或恢复后提前执行")
    near(data.tieredDisabled.seconds, .05, "重新启用后低频时间")
    assert(data.tieredReadiness.prematureCalls == 0 and data.tieredReadiness.calls == 1,
        "世界未就绪期间的旧会话时间混入了新会话")
    near(data.tieredReadiness.seconds, .05, "世界重新就绪后的累计时间")
    assert(data.tickRatePreference.rejectedInvalid, "非法 TPS 选项未被拒绝")
    local rates = {30, 60, 120}
    for i, sample in ipairs(data.tickRatePreference.samples) do
        local rate = rates[i]
        assert(sample.accepted and sample.selectedIndex == i - 1 and
            sample.effectiveRate == rate and sample.savedRate == rate,
            "TPS 设置没有保存或立即生效：" .. rate)
        near(sample.fixedStep, data.tickRatePreference.samples[1].fixedStep, "Unity FixedUpdate 不应跟随 TPS")
        near(sample.maximumStep, data.tickRatePreference.samples[1].maximumStep, "Unity 最大时间预算不应跟随 TPS")
    end
    local clock = data.independentFrameClock
    assert(clock.defaultTickRate == 30, "游戏时钟默认 TPS 不是30")
    for _, sample in ipairs({clock.normal, clock.lowFps}) do
        assert(sample.ticks == 30 and sample.tickRate == 30 and sample.maximumGroup <= 4,
            "独立时钟 1倍速未维持30 TPS")
        near(sample.seconds, 1, "独立时钟一秒总游戏时间")
        near(sample.clockSeconds, 1, "独立时钟累计秒数")
    end
    assert(clock.doubleSpeed.ticks == 60 and clock.doubleSpeed.maximumGroup <= 4 and
        clock.highRate.ticks == 120 and clock.highRate.tickRate == 120,
        "独立时钟倍速或120 TPS执行错误")
    near(clock.doubleSpeed.clockSeconds, 2, "两倍速游戏时间")
    near(clock.highRate.clockSeconds, 1, "120 TPS游戏时间")
    assert(clock.overloaded.ticks == 20 and clock.overloaded.maximumGroup == 4 and
        clock.hitchTicks == 4 and clock.afterHitchTicks == 0,
        "低帧率或卡顿未按最大4 Tick预算丢弃超额时间")
    near(clock.hitchSeconds, 4 / 30, "卡顿追赶推进时长")
    assert(clock.pausedTicks == 0 and clock.resumedTicks == 1 and
        clock.changedTicks == 3 and clock.changedRate == 120,
        "暂停恢复或动态切换 TPS 没有刷新独立时钟")
    near(clock.changedSeconds, 1 / 60 + 1 / 120, "切档后必须保留已累计的真实游戏秒")
end, "固定步阶段顺序、停用注销、步内注册及不同渲染分组")

test.system("core_lifecycle", "scene_object_hierarchy", {}, function(data)
    assert(data.available, "场景分类规则需要编辑器现场")
    assert(data.rootHideFlags == 0, "场景分类根使用了会脱离场景并阻止卸载的 HideFlags")
    assert(data.reused and data.sharedRoot and data.oneSceneRoot, "同场景分类重复创建或没有共用根节点")
    assert(data.sceneIsolation and data.otherSceneSurvives, "不同场景的分类互相干扰")
    assert(data.identityTransform and data.posePreserved and data.migrationPosePreserved, "分类改变了物品位置、旋转或缩放")
    assert(data.itemGrouped and data.explicitOwnerPreserved, "自由物品没有分类或覆盖了显式宿主")
    assert(data.migrationGrouped and data.inactivePreserved, "跨场景归类错误或激活了准备中的物品")
    assert(data.categoryRecreated and data.rootRecreated, "销毁分类节点后仍使用旧缓存")
end, "运行时场景分类：复用、独立场景、显式宿主、迁移和回收")

test.system("core_lifecycle", "editor_asset_loading", {}, function(data)
    if not data.available then
        assert(data.available == false, "运行包不应使用编辑器资源入口")
        return
    end
    assert(data.sourceUnchanged and data.separateProvider, "编辑器加速覆盖了原 Provider 或仍命中原缓存")
    assert(data.identityPreserved and data.dependenciesPreserved, "加速位置丢失资源身份、子图、数据或依赖")
    assert(data.oneProvider, "重复请求重复注册编辑器 Provider")
    assert(data.customPreserved and data.bundlePreserved, "编辑器入口接管了自定义 Provider 或 Bundle 资源")
    assert(data.completedImmediately and data.loadedExpectedPrefab and data.immediateRequestReleased,
        "编辑器已准备资源仍额外排队等待、加载错误 Prefab，或释放后遗留请求")
    assert(data.directSpriteMatches and data.slicedSpriteMatches, "编辑器普通 Sprite 或子图资源身份错误")
end, "编辑器默认资源请求路由、缓存隔离和依赖保留")

for _, row in ipairs({
    {startupReady = false, enteringWorld = false, background = false},
    {startupReady = true, enteringWorld = false, background = true},
    {startupReady = true, enteringWorld = true, background = false},
    {startupReady = false, enteringWorld = true, background = false}
}) do
    test.system("core_lifecycle", "resource_loading_priority", row, function(data)
        assert(data.background == row.background, "加载页仍使用菜单后台预算，或菜单没有恢复后台限制")
    end, "启动、菜单和等待进入世界的资源前后台预算")
end

test.system("core_lifecycle", "binary_content_cache", {}, function(data)
    assert(data.coldMiss and data.stored and data.warmHit and data.count == 2, "首次解析没有生成可跨会话恢复的二进制对象")
    near(data.inheritedWeight, 3, "跨文件继承结果")
    assert(data.parametersEqual and data.actorSourcesEqual and data.actorDefinitions == 2, "扩展参数树或 Actor 继承来源丢失")
    for index, value in ipairs(data.defense) do near(value, ({4, 2, 1})[index], "分类防御值") end
    assert(#data.defense == 3 and data.gridX == -2 and data.nullGridCell, "防御或占地空值往返丢失")
    near(data.scaleZ, 4, "Unity 向量值")
    near(data.surfaceWidth, .3, "液面矩形")
    near(data.transitionTemperature, 100, "物质变化配置")
    assert(data.independentInteger == "9007199254740993", "修改恢复对象污染了下次读取或整数精度丢失")
    assert(data.recipePackWritten and data.recipePackRestored and data.buffPackWritten and data.buffPackRestored,
        "配方或 Buff 首次二进制配置包未生成，或跨会话无法恢复与正常构建")
    assert(data.recipeChangedMiss and data.buffChangedMiss, "玩家修改配方或 Buff JSON 后仍使用旧二进制配置包")
    assert(data.contractMiss and data.sameLengthChangedMiss and data.manifestMiss and data.missingSourceMiss,
        "代码、同大小同时间戳 JSON、清单或缺失依赖未使缓存失效")
    assert(data.rebuilt and data.updatedWeight == 4, "父定义更新后仍恢复旧继承结果")
    assert(data.corruptMiss and data.truncatedMiss and data.repaired, "损坏缓存未回退或不能原子重建")
    assert(data.changedDuringParseRejected and data.writeFailureFallback and data.noTemporaryFiles,
        "读取期间变更或写盘失败污染了缓存/JSON 结果，或遗留临时文件")
end, "启动实体二进制缓存、内容更新、继承、损坏与写入失败回退")

test.system("core_lifecycle", "json_configuration_cache", {}, function(data)
    assert(data.parametersSkipDiskCache, "小参数树仍逐个读写磁盘缓存或触发首次构建提示")
    assert(data.parameterObjectsIndependent and data.parameterInteger == "9007199254740993" and
        data.updatedParameterInteger == "9007199254740994", "参数树对象共享状态、丢失整数精度或更新未生效")
    assert(data.parameterArray[1] == nil and data.parameterArray[2] == 1.25 and data.invalidParameterParses == 2,
        "参数数组空值/小数改变，或无效 JSON 没有保留原解析异常")
    assert(data.coldBuildsHint and data.warmDoesNotBuildHint, "首次构建提示没有跟随真实缓存构建，或热命中仍触发首次提示")
    assert(data.libraryIgnoreCase and data.generatorIgnoreCase and data.atmosphereIgnoreCase,
        "文字库、生成器或星球大气的字典大小写规则改变")
    assert(data.independentValue == "甲", "修改调用方对象污染了磁盘缓存")
    near(data.timeScale, 2.5, "时间配置嵌套对象")
    near(data.playerStamina, 100, "玩家模板嵌套默认字段")
    assert(data.themeHex == "#FFFFFF" and data.renderingPreset == 3 and data.weatherAssociation == "rain",
        "跨程序集 UI、渲染或天气配置未完整恢复")
    assert(data.tileParameter == 5 and data.modPayload and data.derivedMembersExcluded,
        "地块/MOD 扩展参数丢失或结构展开结果混入原配置")
    assert(data.changedParses == 1 and data.updatedVersion == 9, "JSON 更新后仍读取旧对象")
    assert(data.invalidParses == 2 and data.invalidNotCached, "无效配置被缓存或吞掉了原解析器异常")
end, "全部启动配置的统一缓存、字段完整性、原解析错误及首次构建提示")

for level = 0, 20 do
    local expectedDrain = level == 0 and .5 or 1 + level * .04
    local expectedReceived = level == 0 and .5 or 1
    test.system("core_lifecycle", "difficulty_rules", {level = level}, function(data)
        near(data.hungerDrain, expectedDrain, "食物与水分共用的消耗倍率")
        near(data.received, expectedReceived, "怪物攻击玩家的受伤倍率")
        near(data.environmentalDamage, 1 + level * .04, "环境伤害倍率保持原规则")
        near(data.creatureAttack, 1 + level * .06, "怪物自身攻击倍率保持原规则")
        near(data.playerAttack, 1 - level * .0175, "玩家攻击倍率保持原规则")
        near(data.creatureHealth, 1 + level * .075, "怪物生命倍率")
        near(data.playerDamageTaken, expectedReceived, "玩家受伤倍率")
        near(data.staminaConsumption, 1 + level * .035, "体力消耗倍率")
        near(data.staminaRecovery, 1 - level * .0175, "体力恢复倍率")
        near(data.healing, 1 - level * .015, "治疗倍率")
        near(data.timeSpeed, 1 + level * .0125, "时间倍率")
        near(data.spawnFrequency, 1 + level * .04, "生成频率倍率")
        near(data.spawnPopulation, 1 + level * .035, "生成数量倍率")
        near(data.lootAmount, 1 - level * .0175, "战利品倍率")
        near(data.cropGrowth, 1 - level * .015, "作物生长倍率")
        near(data.fuelConsumption, 1 + level * .03, "燃料消耗倍率")
        near(data.craftingOutput, 1 - level * .01, "制作产出倍率")
        assert(data.deathDrop == (level >= 10), "死亡掉落门槛改变")
        near(data.damage, 7 * expectedReceived, "受伤减半在扣除防御之后")
    end, "预设难度生存与战斗倍率：" .. level)
end

test.system("core_lifecycle", "difficulty_rules", { custom = true, customValues = {
    DropAllCarriedItems = true, PlayerAttackMultiplier = .2, CreatureAttackMultiplier = 1.3,
    CreatureHealthMultiplier = 0, HungerDrainMultiplier = 5, StaminaConsumptionMultiplier = -1,
    StaminaRecoveryMultiplier = .8, HealingMultiplier = 1.6, EnvironmentalDamageMultiplier = 1.9,
    TimeSpeedMultiplier = 2.1, SpawnFrequencyMultiplier = 2.2, SpawnPopulationMultiplier = 0,
    LootAmountMultiplier = 2.4, CropGrowthMultiplier = 2.5, FuelConsumptionMultiplier = 2.6,
    CraftingOutputMultiplier = 0
} }, function(data)
    for key, expected in pairs({ playerAttack = .2, creatureAttack = 1.3, creatureHealth = .1,
        hungerDrain = 4, staminaConsumption = 0, staminaRecovery = .8, healing = 1.6,
        environmentalDamage = 1.9, timeSpeed = 2.1, spawnFrequency = 2.2, spawnPopulation = .25,
        lootAmount = 2.4, cropGrowth = 2.5, fuelConsumption = 2.6, craftingOutput = .25,
        playerDamageTaken = 1 }) do
        near(data[key], expected, "自定义难度规则 " .. key)
    end
    assert(data.deathDrop and data.customInputUnchanged and data.definitionIsolated,
        "自定义难度掉落或输入/定义隔离改变")
    near(data.frozenAttack, 1.25, "难度定义战斗值的独立副本")
    near(data.frozenTime, 1.5, "难度定义世界值的独立副本")
    assert(data.frozenDrop, "更改可写规则候选污染了难度定义")
end, "自定义难度：完整倍率、上下限与定义隔离")

test.system("core_lifecycle", "difficulty_rules", { custom = true, nonFiniteCustom = true }, function(data)
    near(data.playerAttack, 1, "自定义 NaN 回退")
    near(data.creatureHealth, 1, "自定义负无穷回退")
    near(data.spawnPopulation, 1, "自定义正无穷回退")
    assert(data.definitionIsolated, "自定义异常值或源对象修改污染难度定义")
end, "自定义难度：非有限值回退")

for _, case in ipairs({
    {name = "玩家攻击", args = {sourcePlayer = true}, damage = 7, received = 1},
    {name = "非玩家目标", args = {targetPlayer = false}, damage = 7, received = 1},
    {name = "环境攻击", args = {environmental = true}, damage = 7, received = 1},
    {name = "未破防", args = {defense = 12}, damage = 0, received = .5},
    {name = "自定义难度", args = {custom = true}, damage = 7, received = 1}
}) do
    test.system("core_lifecycle", "difficulty_rules", case.args, function(data)
        near(data.received, case.received, "受伤倍率适用范围")
        near(data.damage, case.damage, "难度减伤与防御边界")
        if case.args.custom then near(data.hungerDrain, 1, "自定义消耗保持原规则") end
    end, "难度 0 减伤边界：" .. case.name)
end

test.system("core_lifecycle", "rule_pipeline", {mode = "compose"}, function(data)
    for _, sample in ipairs({data.composed, data.immutable, data.replaced}) do
        near(sample.loot, 12, "独立来源组合与替换不重复累乘")
        near(sample.fuel, .25, "原始配置先经过外部修正再发布")
        near(sample.growth, 6, "通用配置允许大于难度界面上限的倍率")
        near(sample.time, 2.5, "多个业务域共享同一完成快照")
        assert(not sample.deathDrop, "后执行来源没有覆盖死亡开关")
    end
    near(data.removedSecond.loot, 3, "撤销一个来源保留其它来源")
    near(data.removedSecond.fuel, 1, "撤销来源从原始燃料系数重建")
    assert(data.removedSecond.deathDrop, "撤销覆盖没有恢复默认开关")
    for _, sample in ipairs({data.original, data.reset}) do
        near(sample.loot, 2, "基础配置没有被外逸引用或重复修正污染")
        near(sample.fuel, .5, "会话结束恢复配置原值")
        near(sample.time, 1.25, "会话撤销保留独立基础配置")
        assert(not sample.deathDrop, "会话结束遗留机制覆盖")
    end
    assert(not data.removedMissing and data.missingPreservedVersion, "撤销不存在来源仍发布新配置")
    assert(not data.stableOrderDrop, "替换同优先级来源改变了初次注册顺序")
end, "业务配置独立来源组合、撤销、冻结与基础值保护")

test.system("core_lifecycle", "rule_pipeline", {mode = "rollback"}, function(data)
    assert(data.callbackFailure == "InvalidOperationException" and data.callbackKeptSnapshot, "来源构建失败污染已发布快照")
    near(data.afterFailureRebuild.loot, 2, "失败替换污染了原来源注册")
    near(data.afterFailureRebuild.fuel, 3, "其它合法来源没有继续工作")
    assert(#data.invalid == 3, "非有限值与负倍率检查不完整")
    for _, sample in ipairs(data.invalid) do
        assert(sample.failure == "ArgumentOutOfRangeException" and sample.preserved, "非法倍率未被原子拒绝")
    end
    assert(data.reentryFailure == "InvalidOperationException" and data.reentryPreserved, "构建回调重入污染来源表")
    near(data.aboveFour, 8, "通用规则错误继承难度界面的四倍限制")
end, "业务规则候选失败、非法倍率和构建重入回滚")

test.system("core_lifecycle", "rule_pipeline", {mode = "notification"}, function(data)
    assert(data.callerFailure == nil, "监听异常被错误报告为已经提交的事务失败")
    assert(data.firstCalled == 1 and data.lastCalled == 1, "一个监听失败阻断其它业务更新")
    assert(#data.failures == 2 and data.failures[1] == "InvalidOperationException" and data.failures[2] == "InvalidOperationException", "监听异常或通知重入没有交给诊断出口")
    assert(data.versionDelta == 1 and data.notifiedVersion == data.currentVersion and data.sameSnapshot, "通知发生嵌套发布或版本错配")
    near(data.loot, 3, "通知期间重入改写最终配置")
end, "业务规则通知隔离、重入拒绝与版本一致")

test.system("core_lifecycle", "rule_pipeline", {mode = "session"}, function(data)
    near(data.first.hunger, 1.8, "加载世界时发布存档难度来源")
    assert(data.first.deathDrop, "加载时未发布默认死亡规则")
    near(data.switched.hunger, .5, "运行中切换重建最终配置")
    near(data.switched.growth, 2, "难度切换丢失其它来源")
    assert(not data.switched.deathDrop, "难度覆盖了随后执行的外部机制开关")
    near(data.rebound.hunger, 1.7, "换档没有采用新自定义参数")
    near(data.rebound.growth, 1.5, "新世界继承了旧世界修正")
    near(data.rebound.fuel, .75, "自定义燃料配置未发布")
    near(data.rebound.time, 2, "自定义时间配置未发布")
    assert(data.rebound.deathDrop and data.bindNotifications == 1 and data.previousSourceRemoved, "换档发布了中间空规则或遗留旧来源")
    near(data.ended.hunger, 1, "退出后恢复中性业务配置")
    assert(not data.ended.deathDrop, "退出后遗留死亡规则")
    local rejected = data.rejected
    assert(not rejected.presetAccepted and not rejected.customAccepted, "后续来源失败仍报告难度切换成功")
    assert(type(rejected.presetError) == "string" and type(rejected.customError) == "string", "失败没有返回具体原因")
    assert(rejected.difficulty == 0 and rejected.customHunger == 1 and not rejected.customDrop, "切换失败留下已修改的存档字段")
    assert(rejected.sameSnapshot and rejected.sameVersion, "切换失败污染已发布规则")
end, "世界装配、外部覆盖、换档清理与难度切换失败回滚")

test.system("core_lifecycle", "startup_prefabs", {}, function(data)
    local present = {}
    for _, key in ipairs(data.keys) do
        assert(type(key) == "string" and #key > 0, "启动 UI 清单存在空键")
        assert(not present[key], "启动 UI 清单重复加载：" .. key)
        present[key] = true
    end
    assert(data.networkPanelKey == "UI_NetworkMode", "联机面板资源键偏离正式 Prefab 名称")
    for _, key in ipairs({
        "UI_SliderControl", "UI_TabButton", "UI_MainMenuSettings", "UI_MainMenuExitConfirmation",
        "UI_ModManager", "UI_InputBindingRow", "UI_WorldLoading", "UI_NetworkMode",
        "UI_MobileControls", "UI_MobileControlLayoutEditor"
    }) do
        assert(present[key], "菜单提前就绪时缺少可立即访问的 UI：" .. key)
    end
end, "启动 UI 包含联机与手机面板且资源键唯一")

-- 只执行独立资源计划，阶段依赖和失败清理由生产管线处理。
test.system("core_lifecycle", "resource_plan", { mode = "ordered" }, function(data)
    assert(data.failure == nil, "合法资源依赖被拒绝")
    assert(#data.order == 2 and data.order[1] == "startup" and data.order[2] == "content", "资源没有按依赖执行")
    assert(data.finished == 2, "正常阶段没有释放迭代器")
    local previous = -1
    for _, value in ipairs(data.progress) do
        assert(value >= previous and value >= 0 and value <= 1, "加载进度倒退或越界")
        previous = value
    end
    assert(previous == 1, "全部阶段完成后进度没有达到一")
end, "资源依赖顺序与单调进度")

for _, mode in ipairs({ "missing", "cycle" }) do
    test.system("core_lifecycle", "resource_plan", { mode = mode }, function(data)
        assert(data.failure == "InvalidOperationException" and #data.order == 0, "非法依赖没有在执行前拒绝")
        assert(data.finished == 0, "错误依赖仍执行了资源阶段")
    end, "资源拒绝非法依赖：" .. mode)
end

test.system("core_lifecycle", "resource_plan", { mode = "throw" }, function(data)
    assert(data.failure == "InvalidOperationException" and data.stage == "content", "没有保留失败阶段和异常")
    assert(#data.order == 2 and data.finished == 2, "异常阶段没有执行 finally 清理")
end, "资源阶段异常与清理")

for _, mode in ipairs({ "complete", "busy", "busy_interactive", "failed", "cancel", "nested_throw", "nested_cancel", "consume_twice" }) do
    test.system("core_lifecycle", "resource_prefetch", { mode = mode }, function(data)
        if mode ~= "busy_interactive" then
            assert(data.startedBeforeStartupFinished, "配置预取没有与启动阶段重叠")
        end
        assert(data.disposed == 1, "预取结束、失败或取消后没有释放迭代器")
        if mode == "complete" or mode == "busy" then
            assert(data.value == 42 and data.failure == nil and data.prefetchFinishedBeforeContent,
                "完成的预取没有被依赖阶段正确接收")
        elseif mode == "busy_interactive" then
            assert(not data.startedBeforeStartupFinished and data.value == 42 and data.failure == nil,
                "交互期间的预取超过后台预算或未正确完成")
        elseif mode == "failed" or mode == "nested_throw" then
            assert(data.failure == "InvalidOperationException" and data.stage == "content" and data.value == 0,
                "预取失败没有在依赖阶段终止资源会话")
        elseif mode == "consume_twice" then
            assert(data.failure == "InvalidOperationException" and data.value == 42, "预取结果被重复交接")
        else
            assert(data.value == 0 and not data.published, "取消后仍发布了预取结果")
        end
        if mode == "nested_throw" or mode == "nested_cancel" then
            assert(data.nestedDisposed == 1, "嵌套预取异常或取消后没有释放内部迭代器")
        end
    end, "资源配置预取、错误与取消：" .. mode)
end

test.system("core_lifecycle", "resource_prefetch_fairness", {}, function(data)
    assert(data.foregroundSteps == 5, "预取消耗预算阻塞了当前加载阶段")
    assert(data.secondStartedBeforeFirstFinished, "预取没有轮流获得执行机会")
    assert(not data.repeatedSameFrame and data.firstValue == 7 and data.secondValue == 9,
        "预取同帧重复推进或结果错误")
    assert(data.disposed == 2 and data.failure == nil, "并行预取没有完整收尾")
end, "资源预取与前台阶段的帧预算和公平性")

for _, mode in ipairs({ "complete", "failed", "cancel" }) do
    test.system("core_lifecycle", "resource_prefetch_candidate", { mode = mode }, function(data)
        assert(data.candidateContext and data.officialBetweenFrames and data.disposed == 1,
            "候选预取污染正式目录或没有释放")
        if mode == "complete" then
            assert(data.committed and data.result == 24 and data.officialProgressPreserved,
                "候选预取没有正确发布或覆盖了正式运行状态")
        else
            assert(not data.committed and data.officialProgressPreserved,
                "失败或取消的候选预取改变了正式目录")
            if mode == "failed" then
                assert(data.failure == "InvalidOperationException" and data.stage == "content", "候选预取失败阶段错误")
            end
        end
    end, "候选资源预取的隔离、失败与取消：" .. mode)
end

test.system("core_lifecycle", "world_liquid_resource_plan", {}, function(data)
    assert(data.spriteCount == 2 and data.materialCount == 1, "重复液体外观地址没有合并")
    assert(data.sharedSpriteOwner == "first" and data.sharedMaterialOwner == "first", "共享资源诊断丢失首个来源")
    assert(data.invalidRejected, "缺少世界外观的液体未被拒绝")
end, "世界液体外观按资源类型与地址合并")

test.system("core_lifecycle", "actor_catalog_inputs", {}, function(data)
    assert(data.count == 2 and data.childWeight == 5 and data.inheritedPrefab == "Module_Example",
        "Actor 分包合并或跨文件继承结果不正确")
    assert(data.invalidRejected, "错误的 Actor 分包没有被拒绝")
end, "Actor 分包保留跨文件继承和格式校验")

test.system("core_lifecycle", "catalog_package_deserialization", {}, function(data)
    assert(data.recipeId == "recipe:test" and data.buffId == "buff:test", "本体分包解析结果错误")
    assert(data.noNestedCacheBuild and data.strictBuffRejected, "分包重复写缓存或丢失 Buff 严格解析")
end, "整包缓存内的配方和 Buff 分包直接解析")

for _, mode in ipairs({ "complete", "busy", "completion", "budget", "failed", "cancel" }) do
    test.system("core_lifecycle", "item_sprite_compilation_pipeline", { mode = mode }, function(data)
        assert(data.started == 2 and data.disposed == 1, "图片请求未并行发出或未释放")
        assert(#data.addresses == 2 and data.addresses[1] == "A" and data.addresses[2] == "B",
            "图片请求没有按首次出现顺序去重，或包含抽象定义")
        assert(data.readinessCaseSensitive, "就绪依赖索引错误合并了大小写不同的地址")
        assert(not data.builtWithoutDependencies, "物品在所需主图或状态图就绪前编译")
        if mode == "complete" or mode == "busy" or mode == "completion" or mode == "budget" then
            assert(data.beforeAllSprites >= 2 and not data.pendingSprites,
                "图片等待期间没有编译可用定义，或共享图片依赖未完成")
            assert(#data.order == 4 and data.order[1] == 0 and data.order[2] == 2
                and data.order[3] == 1 and data.order[4] == 3,
                "无图片、共享图片或状态图的编译顺序不符合就绪顺序")
            assert(#data.published == 4 and data.published[1] == 0 and data.published[2] == 1
                and data.published[3] == 2 and data.published[4] == 3,
                "编译乱序后没有按原定义顺序发布")
            if mode == "budget" then assert(data.maxWorkPerFrame == 1, "图片等待时超出帧预算") end
            if mode == "completion" then assert(data.compiledInQueue > 0, "图片完成时没有及时推进就绪编译") end
        else
            assert(#data.published == 0 and data.pendingSprites, "失败或取消时发布了不完整定义")
            assert((mode == "failed") == data.failed, "流水失败状态不正确")
        end
    end, "图片加载与物品编译流水：" .. mode)
end

for _, mode in ipairs({ "slow", "instant", "budget", "empty", "failed_start", "failed_complete", "cancel", "optional_missing" }) do
    test.system("core_lifecycle", "resource_queue", { mode = mode }, function(data)
        assert(data.maximum <= 4, "资源在途请求超过四个")
        assert(data.ownedBeforeDispose == data.started, "请求发出后没有立即登记会话所有权")
        assert(data.ownedAfterDispose == 0 and data.validAfterDispose == 0, "成功、失败或取消后遗留资源请求")
        if mode == "failed_start" or mode == "failed_complete" then
            assert(data.failure == "InvalidOperationException" and data.stage == "queue", "没有保留队列失败阶段和异常")
            if mode == "failed_start" then assert(data.started == 2, "发出失败后仍继续创建请求") end
        else
            assert(data.failure == nil, "合法资源队列执行失败")
            if mode == "empty" then
                assert(data.started == 0 and data.yielded == 0, "空队列仍发出请求或空等一帧")
            elseif mode == "cancel" then
                assert(data.started == 4 and #data.progress == 0, "取消后仍补发请求或收尾")
            elseif mode == "optional_missing" then
                assert(data.started == 5 and #data.progress == 6, "可选资源缺失阻止了其他请求或导致进度遗漏")
                for i = 1, 6 do
                    assert(data.results[i] == (i == 3 and -1 or i - 1), "可选资源回退或其他结果顺序错误")
                    assert(data.progress[i] == i, "可选资源缺失导致进度倒退或跳过")
                end
            else
                assert(data.started == 6 and #data.progress == 6 and data.progress[6] == 6, "队列遗漏请求或完成进度")
                for i = 1, 6 do
                    assert(data.results[i] == i - 1, "乱序完成改变了资源结果顺序")
                    assert(data.progress[i] == i, "队列完成进度倒退或跳过")
                end
                if mode == "slow" then
                    assert(data.maximum == 4 and data.startedAt[5] < data.completedAt[1], "慢请求堵住已释放的在途槽位")
                elseif mode == "instant" then
                    assert(data.yielded == 0, "已完成资源仍按批次空等下一帧")
                else
                    assert(data.yielded > 0, "请求队列没有归还主线程预算")
                end
            end
        end
    end, "资源队列并发、补槽、顺序与释放：" .. mode)
end

for _, row in ipairs({ { fail = false, commit = true }, { fail = true, commit = true }, { fail = false, commit = false } }) do
    test.system("core_lifecycle", "resource_candidate", row, function(data)
        assert(#data.betweenFrames == 1 and data.betweenFrames[1], "候选加载让出帧时没有恢复正式目录")
        assert(data.disposed == 1 and data.preservedBeforeCommit and data.officialPreserved, "候选释放或正式目录隔离错误")
        if row.fail then
            assert(data.error == "InvalidOperationException" and not data.committed and not data.hasCandidate, "失败候选仍被发布")
        elseif row.commit then
            assert(data.error == nil and data.committed and data.hasCandidate and data.hasSecond and data.hasCleanup, "完成候选没有完整发布或 comparer 丢失")
        else
            assert(data.error == nil and not data.committed and not data.hasCandidate, "尚未提交的候选污染正式目录")
        end
    end, "资源候选跨帧恢复、释放与原子发布")
end
