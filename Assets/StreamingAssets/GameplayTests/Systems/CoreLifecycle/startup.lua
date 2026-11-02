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

test.system("core_lifecycle", "startup_private_text_parser", {}, function(data)
    assert(data.startedImmediately and data.notPublishedEarly and data.workerIsPrivate and data.callbackIsMain,
        "读取解析没有合成后台任务，或提前/跨线程发布")
    assert(data.value == 17 and data.errors == 3, "BOM 文本读取、解析错误、缺文件或空路径语义丢失")
end, "文本读取与纯解析单任务、主线程交接及错误边界")

for _, kind in ipairs({ "tiles", "skills" }) do
    test.system("core_lifecycle", "startup_supplemental_inputs", { kind = kind }, function(data)
        assert(data.coldBuilt and data.warmRead and data.corruptFallback and data.invalidRejected and data.missingRejected,
            "补充目录的后台冷读、命中、坏缓存回退或严格拒绝错误")
    end, "地块及技能的正式后台输入：" .. kind)
end

for _, mode in ipairs({ "complete", "failed", "null", "cancel" }) do
    test.system("core_lifecycle", "startup_configuration_publication", { mode = mode, deferredBinding = true }, function(data)
        assert(data.disposed == 1 and not data.publishedTooEarly, "异步绑定在依赖完成前发布或未回收输入")
        if mode == "complete" then
            assert(data.parsedBeforeInitialization and data.publications == 1 and data.failure == nil, "异步绑定没有复用已准备输入")
        else
            assert(data.publications == 0, "取消或失败的输入仍被绑定")
            if mode ~= "cancel" then assert(data.failure ~= nil and data.stage == "configuration", "绑定错误归属丢失") end
        end
    end, "纯输入提前准备、绑定等待依赖：" .. mode)
end

test.system("core_lifecycle", "startup_editor_sprite_request_route", {}, function(data)
    if not data.available then assert(data.available == false, "运行包不能走 Editor 入口"); return end
    assert(data.initialUsesSingleRequest and data.reloadKeepsAsyncPreparation and data.customNotOverridden and data.missingProviderRejected,
        "首次贴图仍重复排队，或接管了原位更新、自定义/缺失 Provider")
end, "首次 Editor 贴图单请求与交互重载边界")

for _, mode in ipairs({ "complete", "failed", "cancel", "invalid_graph" }) do
    test.system("core_lifecycle", "startup_request_batch", { mode = mode }, function(data)
        assert(data.diagnosticIsLazy and not data.publishedEarly and data.noCallbackAfterDispose,
            "读取依赖图提前发出请求、未等依赖发布或取消后仍回调")
        if mode == "invalid_graph" then
            assert(data.requests == 0 and data.failure ~= nil, "非法依赖图仍启动了后台请求")
        else
            assert(data.requests == 2 and data.beganAtFirstWork == 2, "独立请求没有在首个前台阶段前发出")
            if mode == "complete" then
                assert(data.publications == 14 and data.callbacks == 2 and data.failure == nil, "结果漏交接或重复交接")
            else
                assert(data.publications == 0, "失败或取消结果污染正式目录")
                if mode == "failed" then assert(data.failure ~= nil and data.stage == "publish", "发出失败没有归属消费阶段") end
            end
        end
    end, "独立输入统一发出、错误归属与取消：" .. mode)
end

for _, kind in ipairs({ "recipes", "buffs", "actors" }) do
    test.system("core_lifecycle", "startup_catalog_input_transaction", { kind = kind }, function(data)
        assert(data.coldRead and data.stored and data.warmRead and data.sameLengthChangedRead,
            "后台事务冷读、缓存命中或同长度内容失效错误")
        assert(data.corruptFallback and data.missingRejected and data.invalidRejected and data.escapeRejected and data.schemaRejected,
            "输入重构丢失坏缓存回退、缺包、非法JSON、越界或版本拒绝")
        if kind == "actors" then
            assert(data.reloadBypassesCache and data.sharedTreeAttached, "Actor重载误用缓存或丢失共享行为树")
            assert(data.runtimeKeepsReferenceClosure and data.completeSourceIsolated and data.invalidUsedTreeRejected,
                "行为树压缩丢失传递引用、污染完整来源或放过非法节点")
        end
    end, "配置输入完整后台事务：" .. kind)
end

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
    assert(requires("actor-code-prewarm", "machine-prewarm"), "完整资源 Ready 前没有等待生物出生代码预热")
end, "正式启动图中的菜单去重、物品身份与液体 Buff 依赖")

test.system("core_lifecycle", "startup_actor_code_prewarm", {}, function(data)
    assert(data.productionRoots and data.capabilitiesAndNodes, "出生、行为工厂或可扩展能力节点没有进入代码预热")
    assert(data.capabilityFactoryHelpers and data.closedCapabilityFactory and data.closedCapabilityClosure,
        "陆地能力工厂或其泛型闭包仍留到首只动物出生时编译")
    assert(data.invalidCapabilityRejected, "非具体能力类型错误地进入泛型工厂预热")
    assert(data.unique and data.constructorsAndClosures, "代码预热重复收集或遗漏构造函数和闭包")
    assert(data.noStaticConstructors and data.openGenericRejected and data.abstractRejected,
        "预热包含静态初始化、开放泛型或抽象方法")
    assert(data.discoveryDoesNotExecute and data.preparationDoesNotExecute, "预编译执行了构造函数、静态初始化或玩法方法")
    if data.available then
        assert(data.prepared and data.reused, "托管方法未完成代码预备或同一方法重复预热")
    end
end, "生物首次创建代码预热：真实入口、闭包、构造、去重及不执行玩法")

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
                        local background = menuReady and not enteringWorld and not reload
                        assert(data.background == background,
                            "菜单背景加载未降速，或入世及原位更新错误使用菜单限速")
                        local explicit = testCache or background or (initial and not reload)
                        assert(data.explicitBudget == explicit, "首次启动、原位重载和测试预算串用")
                        local expectedBudget = testCache and 8 or (background and 1 or (initial and not reload and 32 or -1))
                        assert(data.budgetMs == expectedBudget, "每帧工作预算错误")
                        assert(data.targetMs == 10000, "完整资源加载的十秒验收目标被改变")
                    end, "首次启动、菜单、入世及原位更新的独立调度策略")
                end
            end
        end
    end
end

for _, idle in ipairs({ true, false }) do
    for _, inWorld in ipairs({ true, false }) do
        for _, enteringWorld in ipairs({ true, false }) do
            for _, reloading in ipairs({ true, false }) do
                local row = { idle = idle, inWorld = inWorld, enteringWorld = enteringWorld, reloading = reloading }
                test.system("core_lifecycle", "startup_menu_preload", row, function(data)
                    assert(data.request == (idle and not inWorld and not enteringWorld and not reloading),
                        "菜单预加载未启动，或世界运行、入世及重载期间重复启动")
                end, "主菜单预热只在空闲时启动：" .. tostring(idle) .. "/" .. tostring(inWorld)
                    .. "/" .. tostring(enteringWorld) .. "/" .. tostring(reloading))
            end
        end
    end
end

for _, mode in ipairs({ "startup", "world", "reload", "missing" }) do
    test.system("core_lifecycle", "startup_menu_snapshot_reuse", { mode = mode }, function(data)
        if mode == "world" then
            assert(data.loads == 0 and data.publishes == 1 and data.captures == 0
                and data.published == "menu" and data.failure == nil, "完整世界重复读取了已就绪的菜单配置")
        elseif mode == "startup" then
            assert(data.loads == 1 and data.publishes == 1 and data.captures == 1
                and data.published == "fresh" and data.failure == nil, "初次菜单没有加载并保存必要配置")
        elseif mode == "reload" then
            assert(data.loads == 1 and data.publishes == 1 and data.captures == 0
                and data.published == "fresh" and data.failure == nil, "原位更新没有读取独立的新配置")
        else
            assert(data.loads == 0 and data.publishes == 0 and data.captures == 0
                and data.failure ~= nil, "缺失的菜单快照错误地发布了空配置")
        end
    end, "菜单配置快照复用与原位更新：" .. mode)
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
test.system("core_lifecycle", "baked_configuration_catalog", {}, function(data)
    assert(data.entryCount == 1 and data.duplicateRejected, "烘焙目录重复键未去重或冲突覆盖了已有配置")
    assert(data.independent, "二进制配置恢复共享了可变实例")
    assert(data.mismatchRejected and data.invalidPreserved, "错误契约或损坏包覆盖了完整目录")
    assert(data.deferred and data.asyncValue == 41 and data.parserCalls == 0, "二进制命中仍读取JSON或提前发布异步结果")
    assert(data.sourcesDisabled and data.nestedPreserved and data.sourcesRestored and data.catalogRestored,
        "源配置重载租约或临时目录没有完整恢复")
end, "构建期目录直接读取、独立实例、契约拒绝与F5恢复")

test.system("core_lifecycle", "baked_module_parameters", {}, function(data)
    assert(data.prototypeUnchanged and data.moduleHit and data.validatorRan,
        "模块烘焙改变了原型、没有命中二进制，或跳过当前原型的自有校验")
    assert(data.independentValues and data.specialApplied and data.reapplyRestored,
        "烘焙参数在实例间串用，或 Transform/Collider 及重复应用不正确")
    assert(data.modFallback and data.sourceReloadFallback,
        "未烘焙的 MOD 参数或 F5 源配置没有走原编译路径")
    assert(data.missingBuiltInRejected and data.builtInColliderRejected,
        "内置模块缺少烘焙键或碰撞原型变化时没有拒绝加载")
    assert(data.replacementFallback and data.replacementInvalidRejected,
        "替换碰撞体原型时没有重新校验，或错误拒绝合法参数")
end, "模块烘焙命中、原型与实例隔离、MOD 和 F5 回源")

test.system("core_lifecycle", "baked_behavior_tree", {}, function(data)
    assert(data.bakeDidNotRunFactory and data.sourceUnchanged and data.treeHit,
        "行为树烘焙执行了运行时工厂、修改源定义或没有命中二进制")
    assert(data.factoryRebound and data.independentRuntime and data.actionId == "alpha" and data.abortCleared,
        "行为树没有绑定当前工厂、隔离运行实例或保留展开后的动作身份")
    assert(data.sharedChangeFallback and data.failedTreeRecovered,
        "共享子树变更后仍命中旧包，或无效节点破坏后续正常编译")
    assert(data.hostFactoryRollback and data.hostInitializationRollback,
        "行为树宿主在节点工厂或后续初始化失败后残留运行态或检测范围")
end, "行为树烘焙还原、运行时工厂绑定与共享子树回源")

test.system("core_lifecycle", "baked_sprite_reference_catalog", {}, function(data)
    assert(data.completed and data.reused and data.duplicateRejected,
        "Sprite 批量请求没有完成或去重，或目录接受了重复地址")
    assert(data.preserved and data.released and data.sourcePreserved and data.nativeRequests == 0,
        "Sprite 引用、释放或源资源状态不正确，或重复发起了原生请求")
    assert(data.lateAfterDisposeFailed, "Scope 释放后晚到回调不得伪装成成功-null")
end, "Sprite批量真实引用、请求去重、目录拒绝与释放")

test.system("core_lifecycle", "animal_detection_radius_contributions", {}, function(data)
    assert(data.threat == 30 and data.hunting == 50 and data.territory == 65 and data.pack == 80,
        "行为树能力没有按各自配置汇总检测范围")
    assert(data.afterHighRemoved == 45 and data.afterSourceReduced == 30 and
        data.afterOtherRemoved == 30 and data.whileLowRemains == 30,
        "高范围来源退出或更新时覆盖了其他来源")
    assert(data.afterAllRemoved == 14 and data.baseValue == 14,
        "移除行为树能力后没有恢复最新基础检测半径")
    assert(data.staleRejected and data.unchangedNotQueued and data.overlappingReloadInvalidated,
        "范围变化未拒绝旧异步结果，或相同来源重复更新产生无效请求")
    assert(data.factoryFailed and data.duringFailure == 90 and data.afterFailure == 14,
        "行为能力工厂失败后残留检测范围")
end, "行为树检测范围的来源聚合、高低替换、移除与异步失效")


test.system("core_lifecycle", "sprite_atlas_rectangle_packing", {}, function(data)
    assert(data.sameNamePages == 1 and data.sameNameCount == 32, "同名 Sprite 仍被强制拆成小图集")
    assert(data.stable and data.paddedPages == 2 and data.paddedLargestPage == 9,
        "分桶不稳定或没有按真实边距和二维尺寸限制装箱")
    assert(data.filledEarlierPage and data.usagePages == 3 and data.usagesSeparated,
        "没有回填旧页空间或把不同用途混装")
    assert(data.menuSharingIsolated, "菜单共享图片混进了世界专用 Shared 用途包")
end, "图集同名不拆页、二维边界、旧页回填与用途隔离")
