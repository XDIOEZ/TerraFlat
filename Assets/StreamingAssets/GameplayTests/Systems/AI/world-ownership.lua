-- 迁移到尚未就绪的逻辑世界时，AI 不能留在旧地址或错误进入当前场景世界。
test.system("ai", "world_ownership", {}, function(data)
    assert(data.sameScene and data.oldWorldCleared and data.oldWorldDirty,
        "同场景迁移没有清除原 AI 索引或标记旧地址存档")
    assert(data.targetPending and data.singleOwner and data.noWorldCreated,
        "未就绪目标错误创建世界、回退场景或出现多个 AI 归属")
    assert(data.unregistered, "注销后 AI 归属索引仍保留旧物品")
end, "AI 归属按逻辑世界迁移并保留旧地址删除标记")
