test.runtime("world_streaming", "natural_item_decoupling", {}, function(data)
    assert(data.realFoodModules and data.logicOutsideChunkView and data.physicsOutsideLogic and data.pureVisibleView,
        "真实Leaf的食物模块、原生碰撞和视觉仍混在同一归属层")
    assert(data.viewUnbindKeepsLogic and data.viewUnbindKeepsPhysics and data.rebindDoesNotClone,
        "ChunkView解绑回收了自然物逻辑/碰撞，或重新绑定生成了第二份实例")
end, "真实Leaf的逻辑、物理、视觉分离及整块视图重绑")
