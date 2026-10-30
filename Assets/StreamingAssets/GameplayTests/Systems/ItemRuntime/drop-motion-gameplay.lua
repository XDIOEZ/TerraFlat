test.check("player_ready", "掉落物拾取专项的隔离玩家就绪")

for _, case in ipairs({
    {name = "部分拾取、物态推进与腾空间续捡", args = {retry = true}, amount = 3, remaining = 7},
    {name = "零容量保留原堆", args = {maxVolume = 0}, amount = 0, remaining = 10}
}) do
    test.runtime("inventory", "dropped_partial_pickup", case.args, function(data)
        assert(data.pickerWorldMatchesScene, "拾取器没有登记到掉落物所属世界")
        for _, sample in ipairs({data.first, data.afterMatter}) do
            assert(sample.inventoryAmount == case.amount and sample.remaining == case.remaining and
                sample.inventoryAmount + sample.remaining == 10, "运动或物态推进后拾取数量不守恒")
            assert(sample.worldRecords == 1 and sample.sameGuid and sample.unitWeight == 3 and sample.unitVolume == 12,
                "部分拾取后实物身份、快照或正式原木定义改变")
        end
        if case.args.retry then
            assert(data.first.hotbarAmount == 2 and data.first.bagAmount == 1,
                "没有按正式库存顺序分摊接收数量")
            assert(data.inPlaceInvalidated and data.removed == 2 and data.afterFreeCapacity.inventoryAmount == 3 and
                data.afterFreeCapacity.remaining == 5 and data.afterFreeCapacity.inventoryAmount +
                data.afterFreeCapacity.remaining + data.removed == 10, "原地腾容量重试丢失或重复生成掉落物")
        end
    end, "紧凑掉落物运行时真实拾取：" .. case.name)
end
