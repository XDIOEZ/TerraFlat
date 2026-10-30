test.check("world_ready", "多世界隔离测试世界就绪")
test.check("player_ready", "多世界隔离测试玩家就绪")
test.runtime("networking", "world_scope", {}, function(data)
    assert(data.scope == "live-isolated-world", "必须在正式隔离存档采样")
    assert(data.firstWorld ~= data.secondWorld, "没有准备两个真实世界")
    local scopes = data.executionScopes
    assert(scopes.outerWorld and scopes.innerWorld and scopes.innerMachineWorld and scopes.nestedRestored
        and scopes.exceptionRestored and scopes.repeatedDisposePreservedContext and scopes.contextRestored
        and scopes.machineContextRestored and scopes.scenePreserved,
        "世界作用域嵌套、异常退出或重复释放没有恢复对应世界、机器上下文与活动场景")
    assert(scopes.stable and scopes.iterations == 1024 and scopes.allocatedBytes == 0,
        "预热后连续切换真实地表/洞穴仍产生GC或串用了世界上下文")
    assert(data.firstOccupied and data.secondOccupied and data.secondStillOccupied,
        "同坐标建筑占地串世界，或删除甲世界影响乙世界")
    assert(data.distinctEcsWorlds and data.distinctResourceHandles,
        "不同世界仍共享自然实体 ECS World 或句柄")
    assert(data.firstResourceRemoved and data.secondResourceAlive,
        "移除甲世界自然实体误删乙世界同GUID实体")
    assert(data.firstPhysicsOwn and data.secondPhysicsOwn
        and not data.firstPhysicsOther and not data.secondPhysicsOther,
        "独立PhysicsScene查询未命中自身，或穿越世界命中另一世界")
    assert(data.distinctWeatherManagers, "不同世界仍共享天气引擎与队列所有者")
    assert(data.weatherPersistenceWarnings == 0, "子天气仍对非根节点调用DontDestroyOnLoad")
    assert(data.weatherChildRetainsOwner and data.weatherChildDestroyedWithOwner,
        "子天气脱离所属父节点、错误保留整个场景层级，或父级销毁后残留")
    assert(data.weatherRootPersists and data.weatherRootSurvivesOwner and data.weatherFixtureReleased,
        "独立根天气没有进入持久场景，或生命周期夹具未清理")
    assert(data.weatherOwnersUnchanged, "临时天气夹具替换或清理了正式世界的天气所有者")
    assert(data.sceneUnchanged and data.localPlayerUnchanged,
        "后台准备世界切换了主机活动场景或替换本地主角")
end, "同坐标两世界：建筑、自然实体、物理查询与天气所有权")
