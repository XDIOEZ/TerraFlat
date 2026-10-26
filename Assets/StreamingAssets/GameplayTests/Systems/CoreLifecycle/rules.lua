local function near(actual, expected, label)
    assert(type(actual) == "number" and math.abs(actual - expected) < .0001, label .. "错误")
end

test.system("core_lifecycle", "editor_asset_loading", {}, function(data)
    if not data.available then
        assert(data.available == false, "运行包不应使用编辑器资源入口")
        return
    end
    assert(data.sourceUnchanged and data.separateProvider, "编辑器加速覆盖了原 Provider 或仍命中原缓存")
    assert(data.identityPreserved and data.dependenciesPreserved, "加速位置丢失资源身份、子图、数据或依赖")
    assert(data.oneProvider, "重复请求重复注册编辑器 Provider")
    assert(data.customPreserved and data.bundlePreserved, "编辑器入口接管了自定义 Provider 或 Bundle 资源")
end, "编辑器默认资源请求路由、缓存隔离和依赖保留")

test.system("core_lifecycle", "binary_content_cache", {}, function(data)
    assert(data.coldMiss and data.stored and data.warmHit and data.count == 2, "首次解析没有生成可跨会话恢复的二进制对象")
    near(data.inheritedWeight, 3, "跨文件继承结果")
    assert(data.parametersEqual and data.actorSourcesEqual and data.actorDefinitions == 2, "扩展参数树或 Actor 继承来源丢失")
    for index, value in ipairs(data.defense) do near(value, index, "四种独立防御值") end
    assert(#data.defense == 4 and data.gridX == -2 and data.nullGridCell, "防御或占地空值往返丢失")
    near(data.scaleZ, 4, "Unity 向量值")
    near(data.surfaceWidth, .3, "液面矩形")
    near(data.transitionTemperature, 100, "物质变化配置")
    assert(data.independentInteger == "9007199254740993", "修改恢复对象污染了下次读取或整数精度丢失")
    assert(data.contractMiss and data.sameLengthChangedMiss and data.manifestMiss and data.missingSourceMiss,
        "代码、同大小同时间戳 JSON、清单或缺失依赖未使缓存失效")
    assert(data.rebuilt and data.updatedWeight == 4, "父定义更新后仍恢复旧继承结果")
    assert(data.corruptMiss and data.truncatedMiss and data.repaired, "损坏缓存未回退或不能原子重建")
    assert(data.changedDuringParseRejected and data.writeFailureFallback and data.noTemporaryFiles,
        "读取期间变更或写盘失败污染了缓存/JSON 结果，或遗留临时文件")
end, "启动实体二进制缓存、内容更新、继承、损坏与写入失败回退")

test.system("core_lifecycle", "json_configuration_cache", {}, function(data)
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
        near(data.damage, 7 * expectedReceived, "受伤减半在扣除防御之后")
    end, "预设难度生存与战斗倍率：" .. level)
end

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
