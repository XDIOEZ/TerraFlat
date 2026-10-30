-- 物品运行、MOD Lua和网络副本共用正式资源与自有运行实例。
test.check("world_ready", "正式隔离世界已就绪")
test.runtime("item_runtime", "shell_final_configuration", {}, function(d)
    assert(d.initialInventoryTemplates == 0, "旧测试库存清理后仍加载了本体库存模板")
    local jsonOnlyShells = {
        Apple = "Prop", Egg = "Prop", Meat = "Prop", Ore_Copper = "Prop",
        WorkBench = "BuildingBodyShell", MineEntrance = "BuildingBodyShell",
        CaveExit = "BuildingBodyShell", Wall_Stone_Summoner = "BuildingSummonerShell"
    }
    for id, shell in pairs(jsonOnlyShells) do
        local row = d.jsonOnlyShells[id]
        assert(row and row.itemId == id and row.shell == shell,
            "清理旧副本后无法从 JSON 和通用壳创建物品：" .. id)
    end
    assert(d.rawModuleCount == d.templateModuleCount and d.rawKeptTemplateIdentity,
        "冷外壳工厂提前创建了临时定义数据或装配了额外模块")
    assert(d.firstDataBound and d.configuredModuleCount == d.expectedModuleCount,
        "最终装配没有直接绑定传入数据，或遗漏、重复创建了模块")
    assert(d.poolAccepted and d.samePooledInstance and d.coldCreates == 1 and d.sameConfiguredComponents,
        "对象池没有复用同一外壳和已装配组件")
    assert(d.latestDataBound and d.allModulesBoundToLatestData,
        "池对象重取后仍引用旧实例数据或旧模块状态")
    assert(d.nestedExcluded and d.duplicateRejected,
        "模块槽位索引跨入子物品，或放过同一物品的重复稳定槽位")
    assert(d.publicFactoryStillConfigured, "公共Prefab工厂不再按原约定提供已配置实例")
end, "冷外壳、最终数据装配、同池实例更新与模块所有权边界")

test.runtime("item_runtime", "center_spatial_query", {}, function(d)
    assert(d.registered and d.fixtureRemoved, "中心查询夹具未走正式登记或卸载后残留索引")
    assert(d.zeroRadius == 1 and d.boundary == 1 and d.outside == 0,
        "中心半径查询遗漏零半径同点或边界，或接收半径外物品")
    assert(d.excluded == 0 and d.masked == 0 and d.inactive == 0,
        "中心查询未排除指定对象、屏蔽图层或停用对象")
    assert(d.negativeRadius == 0 and d.invalidRadius == 0, "非法半径进入空间桶遍历")
    assert(d.wrapped and d.seamBoundary == 1 and d.seamOutside == 0 and d.seamDeduplicated == 1,
        "循环接缝遗漏真实物品、超范围命中或重复返回镜像")
    assert(d.topologyRestored, "循环接缝测试没有恢复原地表拓扑")
    assert(d.atmosphereIdentityPreserved and d.atmosphereReadBytes <= 32768,
        "只读大气查询未复用真实状态或仍反复分配完整校验集合")
end, "真实物品中心查询、半径边界、循环去重及大气只读分配")

test.runtime("item_runtime", "loaded_lifecycle", {}, function(d)
    for _, state in ipairs({d.spawned, d.afterFrame, d.second}) do
        assert(state.initialized and state.active and state.registered and state.indexed and
            not state.destructionHandled and not state.inPool,
            "实际物品没有完整加载并登记，或已进入回收流程")
        assert(state.inHand and state.ownedByPlayer,
            "跨帧小刀没有通过正式手持入口归属隔离玩家")
        assert(state.guid ~= 0 and state.generation > 0, "运行身份未建立")
        assert(state.moduleDataCount > 0 and state.modules > 0 and
            state.enabledModules == state.loadedModules, "正式模块数据或启用模块没有进入加载态")
        assert(state.stableNamesUnique, "模块稳定名字重复")
    end
    assert(d.saveKeptIdentity, "ModuleSave改写了数据身份或运行代际")
    assert(d.registeredEvents == 1 and d.unregisteredEvents == 1, "真实加载/卸载事件没有一一对应")
    assert(d.firstRemoved and d.secondRemoved and d.newGeneration and d.independentData, "回收/再次加载残留索引或旧状态")
end, "真实物品加载、跨帧存活、卸载和再次加载")

test.runtime("modding", "live_lua_module", {}, function(d)
    assert(d.maximum > 0, "正式小刀耐久不足以完成生命周期夹具")
    assert(d.mainLoaded == "loaded" and d.mainState == "restored:probe.event", "MOD主Lua加载/恢复/事件/保存链错误")
    assert(d.mainAfterReplacement == "restored:probe.event:replacement:probe.second",
        "MOD主Lua热替换回调后仍调用旧函数")
    assert(math.abs(d.afterLoad - (d.maximum * .8)) < .0001, "MOD物品OnLoad未实际扣耐久")
    assert(math.abs(d.afterUpdate - (d.maximum * .7)) < .0001, "MOD物品OnUpdate未使用传入步长")
    assert(math.abs(d.afterReplacedUpdate - (d.maximum * .65)) < .0001,
        "MOD物品Lua替换OnUpdate后没有调用最新函数")
    assert(d.missingHandlerState == "unchanged", "MOD缺失函数改写了原状态")
    assert(math.abs(d.afterUnload - (d.maximum * .7)) < .0001, "MOD物品OnUnload未恢复耐久")
    assert(d.state == "loaded:Knife_Flint:updated:updated_again:unloaded", "物品Lua状态未按顺序传递")
    assert(d.registeredAfterCallbacks and d.packageRemoved, "Lua生命周期破坏了实例登记或残留测试包")
end, "MOD Lua实际调用正式物品API并传递生命周期状态")

test.runtime("networking", "live_replica", {}, function(d)
    assert(d.wearAccepted and d.sourceDurability == d.durabilityBeforeWear - 1, "物品实际磨损没有提交到权威实例")
    assert(d.identityRead and d.bytes > 4 and d.payloadGuid == d.sourceGuid and d.itemId == "Knife_Flint", "实际物品网络负载身份错误")
    assert(d.sourcePreserved and d.applied and d.replicaGuidPreserved, "捕获/合并破坏权威或副本身份")
    assert(math.abs(d.replicaDurability - d.sourceDurability) < .0001 and d.specialData == '{"luaReplica":42}', "运行副本没有收到实例状态")
    assert(d.replicaActive and not d.replicaRegistered and not d.replicaIndexed, "远程视觉副本误入本地权威循环")
    assert(not d.corruptedApplied and d.afterRejectedGuid == d.expectedReplicaGuid, "损坏包未被拒绝或改写身份")
    assert(math.abs(d.afterRejectedDurability - d.replicaDurability) < .0001, "损坏包改写了副本状态")
end, "真实网络快照合并、远程副本隔离与损坏包拒绝")
