for _, concurrent in ipairs({ true, false }) do
    for _, cancel in ipairs({ true, false }) do
        test.system("core_lifecycle", "startup_execution_timing", { concurrent = concurrent, cancel = cancel }, function(data)
            assert(data.workMeasured and data.longestBounded and data.slices > 0, "执行耗时或等待统计丢失")
            assert(data.released == 1 and data.failure == nil and data.completed == (cancel and 0 or 1),
                "计时包装改变了取消、完成或嵌套清理")
            assert(data.waitMeasured == not cancel, "帧间等待错误计为实际推进，或未恢复的取消被记为调度")
        end, "顺序/并行加载的真实推进、调度间隔与取消")
    end
end

test.system("core_lifecycle", "startup_definition_default_copies", {}, function(data)
    assert(data.shellModulesNotCopied and data.shellSubtypePreserved, "外壳仍复制废弃模块或丢失扩展类型字段")
    assert(data.sourceUntouched and data.shellFieldsIndependent and data.sourceOwnerStillBound,
        "复制外壳修改了原型、共享可变字段或解绑了原型模块所有权")
    assert(data.factoryReused and data.unboundDefaultsPreserved and data.moduleValuesIndependent,
        "默认数据工厂重复构建、拒绝空模板身份或共享了实例状态")
    assert(data.sessionSnapshotIsolated and data.stockIndependent and data.binaryPayloadIndependent,
        "跨会话复用了旧默认值或采集库存/二进制载荷相互污染")
    assert(data.extensionPreserved and data.prototypeReferenceIdentity and data.rawNullsNotNormalized and data.nullRejected,
        "扩展多态字段丢失、原始空配置被归一化或接受空原型")
end, "定义外壳免废弃模块拷贝，原型工厂复用与实例状态隔离")

test.system("core_lifecycle", "startup_resident_sprites", {}, function(data)
    if not data.available then assert(data.available == false, "运行包不使用 Editor 贴图入口"); return end
    assert(data.resident and data.noRepeatedRequest and data.atlasDeduplicated,
        "已驻留贴图重复进入异步预读或同图集请求未去重")
    assert(data.invalidSliceRejected, "复用贴图放过了不存在的具名子图")
end, "驻留贴图免重复预读，保留图集去重与子图校验")

test.system("core_lifecycle", "startup_small_configuration", {}, function(data)
    assert(data.parsedEachTime and data.tokenIndependent and data.noDiskCache,
        "小配置仍额外读写缓存或共享了可变 DTO")
    assert(data.invalidRejected and data.nullPreserved and data.largeConfigurationCached,
        "直接解析丢失严格错误、空值或大型配置缓存边界")
end, "小配置无额外落盘、独立 DTO、大配置缓存与严格解析")

test.system("core_lifecycle", "startup_production_dependencies", {}, function(data)
    local function requires(stage, dependency)
        for _, name in ipairs(data[stage]) do if name == dependency then return true end end
        return false
    end
    assert(data.noSessionStarted, "依赖诊断启动了实际资源会话")
    assert(requires("prefabs", "startup-ui") and requires("items", "skills"), "通用资源抢先重复载入菜单或缺少技能")
    assert(requires("recipes", "actors") and requires("quests", "actors"), "目录校验读取了尚未发布的生物身份")
    assert(requires("liquids", "items") and requires("liquids", "buffs") and requires("liquids", "actors"),
        "液体饮用效果或物品引用没有等待完整依赖")
    assert(requires("buffs", "addressables"), "Buff 图标没有等待资源地址目录")
end, "正式启动图中的菜单去重、物品身份与液体 Buff 依赖")

test.system("core_lifecycle", "startup_eager_data_request", {}, function(data)
    assert(data.startedImmediately and data.unpublishedBeforeAdvance, "后台请求没有立即发出或提前发布了结果")
    assert(data.workerIsBackground and data.callbackIsMainThread and data.value == 17,
        "私有数据没有在线程池处理或回调离开主线程")
    assert(data.errors == 1 and data.lazyPreserved and data.cancelledWithoutPublication,
        "后台异常未被捕获、旧延迟入口变了语义或取消后仍发布结果")
end, "后台任务立即发出、主线程交接、失败与取消边界")

for _, mode in ipairs({ "complete", "budget", "failed", "cancel", "unsupported" }) do
    test.system("core_lifecycle", "startup_concurrent_stages", { mode = mode }, function(data)
        assert(data.overlapped and not data.dependencyViolated, "独立阶段仍串行等待或依赖未就绪就执行")
        assert(data.progressMonotonic and data.nestedDisposed == 2 and #data.disposed == 2,
            "并行进度倒退、越界或嵌套迭代器未恰好回收一次")
        if mode == "complete" or mode == "budget" then
            assert(data.joined and data.progress == 1 and data.failure == nil, "全部阶段完成后没有正确汇合")
            if mode == "budget" then assert(data.maxWorkPerFrame == 1, "并行阶段各自重置了同帧预算") end
        else
            assert(not data.joined and data.progress < 1, "失败或取消后仍执行依赖阶段或发布完成")
            assert(data.disposed[1] == "b" and data.disposed[2] == "a", "取消没有按阶段启动顺序逆序回收")
            if mode == "cancel" then assert(data.failure == nil, "主动取消被误报失败")
            else assert(data.failure ~= nil and data.stage == "b", "并行失败没有保留实际故障阶段") end
        end
    end, "依赖阶段并行、共享预算、进度与失败取消：" .. mode)
end

test.system("core_lifecycle", "startup_module_parameter_templates", {}, function(data)
    assert(data.compiledOnce and data.independentBindings, "重复参数没有复用编译结果或共用了定义身份")
    assert(data.customValidationPreserved and data.currentValidatorRejected, "复用参数跳过了当前模块约束或丢失错误身份")
    assert(data.independentValues and data.prototypeSeparated and data.contentSeparated, "参数模板污染实例或混合不同原型/配置")
    assert(data.fieldSnapshotsIsolated, "读取配置字段时泄漏了共享参数树、大小写不匹配或复制了无关字段")
    assert(data.currentStructureRejected and data.newSessionEmpty, "复用漏检结构变化或缓存越过资源会话")
end, "同原型参数仅编译一次，定义身份、实例状态与实时校验独立")

for _, initial in ipairs({ true, false }) do
    for _, menuReady in ipairs({ true, false }) do
        for _, enteringWorld in ipairs({ true, false }) do
            for _, reload in ipairs({ true, false }) do
                for _, testCache in ipairs({ true, false }) do
                    local row = { initial = initial, menuReady = menuReady,
                        enteringWorld = enteringWorld, reload = reload, testCache = testCache }
                    test.system("core_lifecycle", "startup_loading_policy", row, function(data)
                        assert(data.background == (menuReady and not enteringWorld and not initial),
                            "首次资源加载被主菜单降速，或后续菜单重载丢失交互保护")
                        local explicit = testCache or (initial and not reload)
                        assert(data.explicitBudget == explicit, "首次启动、原位重载和测试预算串用")
                        local expectedBudget = testCache and 8 or (initial and not reload and 32 or -1)
                        assert(data.budgetMs == expectedBudget, "每帧工作预算错误")
                        assert(data.targetMs == 10000, "完整资源加载的十秒验收目标被改变")
                    end, "首次启动、菜单、入世及原位更新的独立调度策略")
                end
            end
        end
    end
end

test.system("core_lifecycle", "startup_local_item_catalog", {}, function(data)
    assert(data.coldBuilt and data.inheritedWeight == 3 and data.warmIndependent,
        "后台目录未构建缓存、跨定义继承错误或恢复对象互相污染")
    assert(data.contentRebuilt and data.corruptRecovered and data.writeFailureFallback,
        "内容改动、损坏缓存或写入失败没有沿正式 JSON 路径恢复")
    assert(data.missingRejected and data.escapeRejected and data.schemaRejected and data.cycleRejected,
        "后台读取放过了缺失分包、路径越界、非法版本或继承循环")
end, "本地后台目录冷读、热读、内容变化、缓存损坏与严格失败边界")

test.system("core_lifecycle", "startup_behavior_tree_templates", {}, function(data)
    assert(data.equivalentDefinitionReused and data.inputsIsolated, "共享行为模板没有复用或可被外部修改污染")
    assert(data.changedLibraryRejected and data.failedCandidatePreservedActive,
        "共享库改动仍复用旧模板，或失败候选破坏正式模板")
    assert(data.newLibraryRecompiled and data.runtimeStillValid, "共享库发布后未重新编译或树无法正常执行")
end, "行为树模板复用、共享库失效及候选隔离")

for _, mode in ipairs({ "complete", "failed", "null", "cancel" }) do
    test.system("core_lifecycle", "startup_configuration_publication", { mode = mode }, function(data)
        assert(data.disposed == 1 and not data.publishedTooEarly,
            "配置预取未释放或在依赖阶段完成前污染了正式目录")
        if mode == "complete" then
            assert(data.parsedBeforeInitialization and data.publications == 1 and data.failure == nil,
                "配置没有与初始化重叠读取，或没有在正式阶段发布一次")
        elseif mode == "cancel" then
            assert(data.publications == 0 and data.failure == nil, "取消后仍发布配置或误报读取失败")
        else
            assert(data.parsedBeforeInitialization and data.publications == 0,
                "失败或空配置进入正式目录")
            assert(data.failure ~= nil and data.stage == "configuration", "失败没有归属实际消费阶段")
        end
    end, "配置预取的完成、失败、空结果与取消边界")
end
