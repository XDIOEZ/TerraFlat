test.check("world_ready", "机仆使用真实隔离世界")
test.check("terrain_ready", "机仆工作现场已加载")
test.runtime("ai", "servitor_work", {}, function(data)
    assert(data.loaded and data.terminalOpened, "正式机仆未装配或对象终端未打开")
    assert(data.destinationAssigned and data.radiusButtonWorked and data.haulButtonWorked and data.standbyButtonWorked,
        "终端按钮未提交实际工作命令")
    assert(data.rechargeButtonWorked and data.wireCharged and data.movedToWire and data.gridEnergySpent,
        "机仆未接近通电电线或没有真实取电")
    assert(data.deliveredWood == 7 and data.cargoAfterDelivery == 0 and data.workContinuesWhenClosed,
        "搬运数量不守恒或关闭终端取消了工作")
    assert(data.lowBatteryAction == "servitor.recharge" and data.interruptedCargoBefore > 0 and
        data.interruptedCargoAfter == data.interruptedCargoBefore, "低电量未抢占工作或中断丢失货物")
    assert(data.restoredCargo == data.interruptedCargoAfter and math.abs(data.restoredBattery - data.savedBattery) < .01 and
        data.restoredDestination and data.restoredRechargeIntent, "机仆读档未恢复货物、电量、订单或回充意图")
    assert(data.foreignOrderCleared and data.foreignCargoRetained == data.interruptedCargoAfter and
        math.abs(data.foreignBatteryRetained - data.savedBattery) < .01, "异世界订单读入没有清退或丢失货物与电量")
    assert(data.remoteCommandRejected, "远距离直接控制机仆绕过对象终端距离限制")
    assert(data.automaticallyResumed, "机仆读档回充后没有自动继续搬运")
end, "机仆终端、真实搬运、电线充电、抢占和读档")

test.runtime("ai", "servitor_cooperation", {}, function(data)
    assert(data.interruptedReservationReleased, "机仆取消任务没有释放实物预约")
    assert(data.cooperativeStored == 30 and data.cooperativeCargo == 0, "两台机仆搬运同一堆物品出现重复或遗失")
    assert(data.emptyStopped and data.emptyStillAlive and data.emptyAction == "servitor.recharge",
        "耗尽机仆仍有主动移动指令或不能等待救援充电")
    assert(data.emptyRecharged > .1 and data.rescueGridSpentEnergy, "耗尽后身边接线没有真实消耗电网能量唤醒")
end, "两台机仆实物预约、数量守恒及耗尽补线唤醒")
