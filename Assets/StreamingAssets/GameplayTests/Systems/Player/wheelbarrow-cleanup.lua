test.runtime("player", "wheelbarrow_cleanup_check", {}, function(data)
    assert(data.removed and data.playerRestored and data.bagRestored, "独轮车自动收尾没有归还车体、玩家或背包")
end, "上一独轮车案例结束截图后的自动清理")
