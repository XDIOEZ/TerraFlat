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
