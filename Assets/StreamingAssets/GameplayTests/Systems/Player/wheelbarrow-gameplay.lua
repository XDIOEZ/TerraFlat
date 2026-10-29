test.check("world_ready", "隔离世界就绪")
test.check("terrain_ready", "玩家脚下地形就绪")
test.runtime("player", "wheelbarrow_setup", {}, function(data)
    assert(data.placed and data.available and data.slots == 12, "正式放置没有建立可用独轮车和 12 格货仓")
    assert(data.weight == 120 and data.volume == 300, "真实货仓容量不是 120 公斤、300 升")
    assert(data.exactCapacity and not data.overweight and not data.overvolume, "货仓重量或体积上限没有生效")
    assert(data.twelveAccepted and not data.thirteenthAccepted, "货仓格数没有限制为 12 格")
end, "独轮车正式放置及真实库存容量")

local function prepare(side)
    test.runtime("player", "wheelbarrow_prepare", {side = side}, function(data)
        assert(data.ready and not data.rider and not data.carrierSource, "车体或角色状态不适合本轮推动")
    end, "准备独轮车" .. side .. "方向接触")
end
local function sample(label, shouldMove, shouldCrossCell)
    test.runtime("player", "wheelbarrow_sample", {}, function(data)
        assert(data.frames > 0 and not data.rider and not data.carrierSource, "没有实际物理步或玩家误被当成乘员")
        if shouldMove then
            assert(data.unlockedWhilePushing and data.peakDrivenSpeed > .05,
                "已接收推动意图，但车体没有解除位置锁或提交实际驱动速度")
            assert(data.everPushed and data.distance > .05 and data.peakSpeed > .05, "车尾真实碰撞没有推动独轮车")
            assert(data.ratio > .64 and data.ratio < .66, "实际推动速度不是玩家意图速度的 65%")
            assert(data.positionTraversable and not data.cartCellOccupied, "独轮车重新登记占地并挡住自己")
            if shouldCrossCell then assert(data.crossedCell, "独轮车没有跨过格子边界") end
        else
            assert(not data.everPushed and data.distance < .05, "不符合条件的碰撞仍推动了车体")
        end
        assert(data.stopped and not data.pushAfterRelease, "松开输入后车体或推源没有清理")
    end, label)
end

prepare("rear")
test.profiler_start(10, "采集独轮车车尾推动")
test.move(0, -1, 1.0)
test.profiler_stop("FlatWorld.Wheelbarrow.StepCarrierMotion", function(data)
    assert(data.selectedTreeNodeCount > 0 and data.selectedMarkerCalls > 0 and data.settingsRestored,
        "推动阶段没有导出实际调用树，或性能采集设置未恢复")
    local captured = data.screenshotStatus == "captured" and data.screenshotCaptured and
        data.screenshotWidth > 0 and data.screenshotHeight > 0
    local minimized = data.screenshotStatus == "failed" and not data.screenshotCaptured and
        data.screenshotWidth == 0 and data.screenshotHeight == 0 and type(data.screenshotError) == "string" and
        string.find(data.screenshotError, "Unity 主窗口已最小化", 1, true)
    assert(captured or minimized, "性能 JSON 的 Unity 窗口截图缺失且没有明确的最小化原因")
end, "导出车尾推动阶段性能 JSON")
sample("车尾真实碰撞跨格推动并在松手后停止", true, true)
test.runtime("player", "wheelbarrow_carrier_occupancy_lifecycle", {}, function(data)
    assert(not data.occupiedBefore and data.unloaded and data.occupiedWhileUnloaded and not data.occupiedByOther,
        "单独卸载载具模块没有恢复车体的建筑占地")
    assert(data.reloaded and not data.occupiedAfterReload and data.traversableAfterReload,
        "重载载具并经过物理步后没有持续释放车体占地")
end, "载具模块停用与重载的占地恢复")
prepare("rear")
test.move(0, -1, .7)
sample("载具重载后仍能从车尾推动", true)
prepare("front")
test.move(0, 1, .6)
sample("持续正面碰撞不能推车", false)
prepare("side")
test.move(-1, 0, .6)
sample("持续侧面碰撞不能推车", false)
prepare("obstacle")
test.move(0, -1, .9)
test.runtime("player", "wheelbarrow_sample", {}, function(data)
    assert(data.everPushed and data.distance > .05 and data.wallBlocked,
        "车尾推动遇到固定障碍时没有停在障碍前")
    assert(data.stopped and not data.pushAfterRelease, "障碍前松开输入仍残留车速或推源")
end, "车尾推动遇到固定障碍不能穿越")
prepare("building_obstacle")
test.move(0, -1, 1.2)
test.runtime("player", "wheelbarrow_sample", {}, function(data)
    assert(data.buildingPlaced and data.buildingOccupied and data.buildingBlocksTraversal,
        "正式帐篷没有提供建筑占地阻挡")
    assert(data.everPushed and data.distance > .05 and data.buildingBlockedWhilePushing and data.buildingNotCrossed,
        "车尾推动没有在正式建筑前停下")
    assert(not data.cartCellOccupied and data.stopped and not data.pushAfterRelease,
        "建筑前停推后车体重新占地或残留推速")
end, "车尾推动遇到正式建筑不能穿越")
prepare("turn")
test.move(-.5, -1, .7)
test.runtime("player", "wheelbarrow_sample", {}, function(data)
    assert(data.everPushed and data.distance > .05 and data.forwardX < -.1 and data.forwardY < 0,
        "从后侧斜推没有让车体按真实接触转向")
    assert(data.stopped and not data.pushAfterRelease and not data.rider and not data.carrierSource,
        "斜推松手后没有停下或错误建立乘坐关系")
end, "从车尾斜推并转向")

test.runtime("player", "wheelbarrow_open_cargo", {}, function(data)
    assert(data.fOpened and data.cargoOpen and not data.rider, "正式 F 交互没有打开独轮车货仓，或误进入乘坐")
    assert(data.deposited and data.withdrew and data.restoredDeposit and data.remaining == 1,
        "玩家背包与打开的货仓不能通过正式事务存取")
end, "F 打开车斗并从玩家背包存取")
prepare("open_cargo")
test.move(0, -1, .6)
sample("打开车斗时继续向前不能推车", false)
test.runtime("player", "wheelbarrow_open_player_bag", {}, function(data)
    assert(data.cargoClosed and data.bagOpen, "没有关闭车斗并打开玩家背包")
end, "切换到玩家主背包")
prepare("open_bag")
test.move(0, -1, .6)
sample("打开玩家背包时继续向前不能推车", false)

test.runtime("player", "wheelbarrow_roundtrip", {}, function(data)
    assert(data.repacked and data.summonerId == "Wheelbarrow" and data.repackedAmount == 1,
        "拆回召唤器时没有保留独轮车货仓")
    assert(data.serialized and data.placedAgain and data.restoredAmount == 1 and data.restoredId == "Twine",
        "保存、读取并再次放置后没有恢复货仓物品")
end, "独轮车拆装与存档快照恢复")
prepare("rear")
test.move(0, -1, .6)
test.runtime("player", "wheelbarrow_teleport", {}, function(data)
    assert(data.everPushed and data.teleported and data.stopped and not data.pushAfterRelease,
        "玩家传送后旧推源或车速仍然存在")
end, "传送后解除独轮车推动")
prepare("rear")
test.move(0, -1, .6)
test.runtime("player", "wheelbarrow_unload", {}, function(data)
    assert(data.everPushed and data.unloaded and data.stopped and not data.pushAfterRelease,
        "卸载独轮车模块没有立即清空车速和推动关系")
end, "运行中卸载独轮车立即停止")
test.runtime("player", "wheelbarrow_display", {}, function(data)
    assert(data.opened and data.amount == 1 and not data.rider, "重载后无法恢复带货物的独轮车现场")
end, "保留重载后的独轮车和打开的货仓供结束截图")
