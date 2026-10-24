-- 调用正式行程、库存和船体规则，当前世界和玩家不参与这些样本。
local function near(actual, expected, label)
    assert(type(actual) == "number" and actual == actual and math.abs(actual - expected) < 0.000001, label)
end

local function scope(data, operation)
    assert(data.scope == "production-domain" and data.operation == operation, "缺少正式电梯规则采样")
    assert(type(data.limitations) == "string" and #data.limitations > 0, "必须说明真实场景覆盖缺口")
end

local function endpoint(sample, cabin, travelling, remaining, ground_access, station_access)
    assert(sample.cabin == cabin and sample.travelling == travelling, "货舱停靠端或运行状态错误")
    near(sample.remaining, remaining, "电梯剩余时间错误")
    assert(sample.groundAccess == ground_access and sample.stationAccess == station_access,
        "途中或远端错误开放货舱")
end

test.system("space", "elevator_authority", {}, function(data)
    scope(data, "elevator_authority")
    assert(data.offlineLocal and data.localHost, "单人内部启动本机 Host 后不能被当作远程客户端拒绝")
    assert(not data.remoteClient and not data.remoteActorOnServer and not data.noAuthorityOrActor,
        "原型只能由有状态权威的本机玩家操作")
end, "电梯本机Host与离线资格、远程客户端及他人角色拒绝")

test.system("space", "elevator_geography", {}, function(data)
    scope(data, "elevator_geography")
    local expected = {center = true, lower_edge = true, upper_edge = true, below_band = false,
        above_band = false, temperate = false, polar = false, wrapped_x = true, wrapped_y = true,
        unbounded = false, disabled_climate = true}
    assert(#data.samples == 11, "缺少赤道带边缘、外部或环世界边界")
    local seen = {}
    for _, sample in ipairs(data.samples) do
        assert(expected[sample.name] ~= nil and not seen[sample.name], "未知或重复赤道资格样本")
        assert(sample.accepted == expected[sample.name], "赤道地理资格错误：" .. sample.name)
        seen[sample.name] = true
    end
    assert(#data.shiftedBands == 5, "缺少不同X位置的真实赤道带采样")
    local first_center, varied
    for _, band in ipairs(data.shiftedBands) do
        assert(band.wrapStable, "同一格环绕世界后赤道资格改变")
        assert(band.count >= 40 and band.count <= 41 and band.last - band.first + 1 == band.count,
            "赤道带宽度错误或同一列出现断裂")
        assert(band.first >= -30 and band.last <= 30, "赤道带偏移超过正式地理带允许范围")
        local center = band.first + band.last
        if first_center == nil then first_center = center elseif center ~= first_center then varied = true end
    end
    assert(varied, "赤道带忽略了随X变化的正式边界偏移")
    assert(#data.anchors == 5, "缺少赤道X位置映射样本")
    local previous_x, previous_y
    for _, anchor in ipairs(data.anchors) do
        near(anchor.x + anchor.y, 0, "平台没有位于指定倾斜赤道线上")
        assert(anchor.x * anchor.x + anchor.y * anchor.y < 1000000, "平台仍被放到星球圆周之外")
        if previous_x ~= nil then
            assert(anchor.x > previous_x and anchor.y < previous_y, "不同地表X没有沿赤道带依次分开")
        end
        previous_x, previous_y = anchor.x, anchor.y
    end
    near(data.anchors[3].x, 0, "赤道中间X没有映射到星球中心")
    near(data.anchors[3].y, 0, "赤道中间X的纵向偏移错误")
    near(data.anchors[1].x + data.anchors[5].x, 0, "赤道两端映射不对称")
    local clearance = {same_longitude = false, near_longitude = false, separate_longitude = true,
        below_clearance = false, at_clearance = true, invalid = false, expanded_overlap = false,
        expanded_below_gap = false, expanded_at_gap = true, expanded_invalid = false}
    assert(#data.stationClearance == 10, "缺少站台最小间距或扩建范围边界")
    local checked = {}
    for _, sample in ipairs(data.stationClearance) do
        assert(clearance[sample.name] ~= nil and not checked[sample.name] and
            sample.accepted == clearance[sample.name], "多线路站台间距判断错误：" .. sample.name)
        checked[sample.name] = true
    end
end, "正式赤道带含边缘、X边界起伏、环绕与星球内部斜线位置映射")

test.system("space", "elevator_journey", {}, function(data)
    scope(data, "elevator_journey")
    endpoint(data.initial, "Ground", false, 0, true, false)
    assert(data.started and not data.repeated and data.repeatedUnchanged,
        "开始行程失败，或重复发车覆盖了正在进行的行程")
    assert(type(data.repeatedReason) == "string" and #data.repeatedReason > 0, "重复发车必须说明原因")
    endpoint(data.departed, "Ground", true, 12, false, false)
    assert(#data.invalidSteps == 4, "缺少零、负值和非有限时间边界")
    for _, sample in ipairs(data.invalidSteps) do
        assert(not sample.arrived and sample.unchanged, "非法步长改变了行程")
    end
    assert(not data.earlyArrival and data.arrival and not data.arrivalRepeated, "到站必须只触发一次")
    endpoint(data.during, "Ground", true, 7, false, false)
    endpoint(data.station, "Station", false, 0, false, true)
    assert(data.returnStarted and not data.returnEarlyArrival and data.returnArrival, "回程没有按正式时间到站")
    endpoint(data.returnDuring, "Station", true, 3, false, false)
    endpoint(data.returned, "Ground", false, 0, true, false)
    assert(#data.invalidStarts == 7 and not data.missingAccess, "发车拒绝或空线路访问样本缺失")
    local seen = {}
    for _, sample in ipairs(data.invalidStarts) do
        assert(not seen[sample.name] and not sample.accepted and sample.unchanged,
            "非法发车没有拒绝或修改了原状态：" .. sample.name)
        assert(type(sample.reason) == "string" and #sample.reason > 0, "非法发车缺少原因：" .. sample.name)
        seen[sample.name] = true
    end
    assert(data.pendingRecallStarted and data.pendingRecalled and data.pendingRideStarted and data.pendingRode,
        "目的仓库满时无法召回或乘坐电梯去腾出空位")
    endpoint(data.pendingRecall, "Ground", false, 0, true, false)
    endpoint(data.pendingRide, "Station", false, 0, false, true)
    assert(data.pendingRecall.deliveryPending and data.pendingRide.deliveryPending and data.pendingCargoUnchanged and
        data.pendingRecall.cargoDestination == "Station" and data.pendingRide.cargoDestination == "Station",
        "客运移动了待入库货物或更改了货物目的仓库")
end, "电梯两端往返、到站一次、途中封舱和无效请求不改状态")

local function amount(slots)
    local sum = 0
    for _, slot in ipairs(slots) do sum = sum + slot.amount end
    return sum
end

local function same_slots(before, after, name)
    assert(#before == #after, "拒绝转移后槽数变化：" .. name)
    for index, slot in ipairs(before) do
        local current = after[index]
        assert(slot.id == current.id and slot.guid == current.guid and slot.special == current.special,
            "拒绝转移后物品身份变化：" .. name)
        near(current.amount, slot.amount, "拒绝转移后数量变化：" .. name)
    end
end

test.system("space", "elevator_cargo", {}, function(data)
    scope(data, "elevator_cargo")
    local expected = {
        ground_to_cabin = {true, 0, 7}, cabin_to_station = {true, 0, 7},
        station_to_cabin = {true, 0, 7}, cabin_to_ground = {true, 0, 7},
        weight_partial = {true, 4, 3}, weight_full = {false, 4, 3},
        volume_partial = {true, 2, 2}, deposit_blocked = {false, 2, 0},
        no_slots = {false, 2, 0}, same_inventory = {false, 2, 2},
        bad_index = {false, 2, 0}, missing_inventory = {false, 0, 0},
        equipment_identity = {true, 0, 1}
    }
    assert(#data.samples == 13, "库存往返与拒绝边界样本数量错误")
    local seen = {}
    for _, sample in ipairs(data.samples) do
        local target = expected[sample.name]
        assert(target and not seen[sample.name], "未知或重复货物样本：" .. sample.name)
        seen[sample.name] = true
        assert(sample.accepted == target[1], "货物转移结果错误：" .. sample.name)
        assert(type(sample.reason) == "string" and #sample.reason > 0, "转移结果缺少反馈：" .. sample.name)
        near(amount(sample.source), target[2], "来源剩余数量错误：" .. sample.name)
        near(amount(sample.target), target[3], "目标数量错误：" .. sample.name)
        near(amount(sample.source) + amount(sample.target), amount(sample.beforeSource) + amount(sample.beforeTarget),
            "运输产生或吞掉物资：" .. sample.name)
        if not sample.accepted then
            same_slots(sample.beforeSource, sample.source, sample.name)
            same_slots(sample.beforeTarget, sample.target, sample.name)
        end
        if sample.name == "equipment_identity" then
            assert(sample.target[1].guid == 35791 and sample.target[1].special == "elevator-instance-marker",
                "不可堆叠设备丢失实例身份或自定义状态")
        end
    end
    assert(data.sameEquipmentInstance, "整件设备应移动原实例，不能生成模板副本")
end, "真实货舱数量守恒、重量体积容量、拒绝不扣物品和设备身份")

test.system("space", "elevator_warehouses", {}, function(data)
    scope(data, "elevator_warehouses")
    assert(data.packed, "地面仓库未能整仓打包")
    assert(#data.groundAfterPack == 3 and #data.transitAfterPack == 3 and #data.stationAfterPack == 3,
        "打包改变了仓库或在途库存的固定槽数")
    near(amount(data.groundAfterPack), 0, "打包后源仓库未清空")
    near(amount(data.transitAfterPack), 8, "打包货物未进入独立在途库存")
    same_slots(data.stationBeforePack, data.stationAfterPack, "打包不能改变太空仓库")
    assert(data.packedEquipmentInstance, "打包复制了不可堆叠设备的实例")
    assert(data.groundRefilled, "发货后地面仓库无法继续收货")
    near(amount(data.groundWhileInTransit), 2, "地面新增物品丢失")
    same_slots(data.transitAfterPack, data.transitAfterRefill, "在途货物被地面新货改写")
    assert(not data.busyPackAccepted and data.busySourceUnchanged and data.busyTransitUnchanged and
        type(data.busyPackReason) == "string" and #data.busyPackReason > 0, "重复打包覆盖在途货物或扣除了新货")
    assert(data.delivered and data.deliveredEquipmentInstance, "自动卸货未完成或设备实例被替换")
    near(amount(data.groundAfterDelivery), 2, "太空收货误动了地面仓库")
    near(amount(data.stationAfterDelivery), 11, "太空仓库未保留原货并接收全部到站货物")
    near(amount(data.transitAfterDelivery), 0, "收货后在途库存未清空")
    assert(data.returnPacked and data.returnDelivered and data.returnedEquipmentInstance, "太空端返程打包或接收失败")
    near(amount(data.groundReturned), 13, "两端仓库往返后物资不守恒")
    near(amount(data.stationReturned), 0, "返程打包没有清空太空仓库")
    near(amount(data.transitReturned), 0, "返程到站后仍残留在途物品")
    assert(not data.fullDelivered and data.fullUnchanged and data.pendingUnchanged, "太空仓库满时丢失货物或改写原库存")
    assert(not data.partialDelivered, "仍有余货时不能宣称完成收货")
    near(amount(data.partialWarehouse), 7, "腾出一个空槽后没有接收到可容纳货物")
    near(amount(data.partialTransit), 1, "部分接收吞掉了无法入库的设备")
    assert(data.remainingDelivered and data.pendingEquipmentInstance, "再次腾空后未能接收保留的原设备")
    near(amount(data.remainingTransit), 0, "余货接收完成后在途没有清空")
    near(amount(data.remainingWarehouse), 1, "最后一件设备入库数量错误")
    assert(data.remainingWarehouse[1].guid == 86420, "满仓等待后设备身份丢失")
    local expected = {weight_limit = true, volume_limit = true, deposit_blocked = true, slot_limit = true,
        empty_source = true, same_inventory = true, missing_source = true, missing_target = true,
        fractional_amount = true, overflow_amount = true}
    assert(#data.rejectedPacks == 10, "缺少整仓打包拒绝边界")
    local seen = {}
    for _, sample in ipairs(data.rejectedPacks) do
        assert(expected[sample.name] and not seen[sample.name] and not sample.accepted and
            sample.sourceUnchanged and sample.targetUnchanged, "无效打包没有原子拒绝：" .. sample.name)
        assert(type(sample.reason) == "string" and #sample.reason > 0, "拒绝打包缺少原因：" .. sample.name)
        seen[sample.name] = true
    end
end, "两端仓库独立、整仓打包、在途隔离、自动接收与满仓保留原实例")

test.system("space", "elevator_drag_guard", {}, function(data)
    scope(data, "elevator_drag_guard")
    assert(data.split and data.halfPrepared and data.halfSourceIdentity and not data.halfSourceHidden,
        "已拆入手部的半组拖拽没有保留原来源或回退状态")
    near(data.halfAmount, 4, "访问守卫改写了实际拖动数量")
    near(amount(data.halfWarehouse), 3, "半组拆分错误改写了源仓库剩余数量")
    near(amount(data.halfHand), 4, "守卫从仓库重新拿取而未使用真实手部半组")
    assert(data.halfProviders == 1 and data.halfReleases == 1, "半组真实来源未透传或完成时重复释放")
    assert(#data.denied == 2 and not data.denied[1].prepared and data.denied[2].prepared,
        "缺少普通拖拽或已准备手部回退的半拖边界")
    for _, sample in ipairs(data.denied) do
        assert(not sample.dropped and not sample.fallback and not sample.consumed,
            "离开终端后仍能拖放、准备回退或消费仓库来源")
        assert(sample.dropCalls == 0 and sample.prepareCalls == 0 and sample.providerCalls == 0 and sample.consumeCalls == 0,
            "失效守卫仍调用了底层转移或消费逻辑")
        assert(sample.releases == 1, "失效或多次关闭拖拽没有只释放一次")
    end
    assert(data.firstGuardDenied and data.secondGuardDenied and data.compositeDropped and not data.compositeDuplicate and
        data.compositeDrops == 1 and data.compositeReleases == 1 and data.compositeInstance,
        "组合守卫绕过其中一个条件、复制物品或重复提交")
    assert(data.fallbackPrepared and data.fallbackDropped and not data.fallbackSourceHidden and data.fallbackPrepares == 1 and
        data.directDrops == 0 and data.fallbackDrops == 1 and data.fallbackReleases == 1,
        "有效回退未切换原事务来源，或重复准备/释放")
end, "仓库拖拽守卫保留真实半组来源、失效拒绝全部入口和单次释放")

test.system("space", "elevator_snapshot", {}, function(data)
    scope(data, "elevator_snapshot")
    assert(data.stableRoundtrip and data.elevatorCount == 1 and data.shipCount == 1, "会话快照增减了线路或站台")
    endpoint(data.beforeResume, "Ground", true, 7, false, false)
    local ids = {"saved-elevator", "ground-terminal", "station-terminal", "saved-station", "world:surface:elevator"}
    assert(#data.identities == #ids, "电梯连接身份样本缺失")
    for index, id in ipairs(ids) do assert(data.identities[index] == id, "电梯连接身份没有保存") end
    near(data.groundX, -15, "地面负坐标丢失")
    near(data.groundY, 26, "地面坐标丢失")
    assert(data.cargoSlots == 2 and data.emptySlot == "" and data.cargoBytesUnchanged,
        "货舱槽位或正式物品快照被改写")
    assert(data.cargoId == "probe:elevator-snapshot" and data.cargoGuid == 24681 and data.cargoAmount == 7 and
        data.cargoSpecial == "elevator-persisted-instance", "正式物品封包恢复后数量或实例状态丢失")
    assert(data.cargoDestination == "Station", "保存后在途货物的目的端丢失")
    assert(data.groundWarehouseBytesUnchanged and data.stationWarehouseBytesUnchanged and data.warehousesDistinct,
        "保存后两端仓库互相覆盖或改写物品封包")
    assert(#data.groundWarehouse == 2 and data.groundWarehouse[1].id == "" and
        data.groundWarehouse[2].id == "probe:elevator-ground-snapshot" and data.groundWarehouse[2].guid == 13579 and
        data.groundWarehouse[2].amount == 4 and data.groundWarehouse[2].special == "ground-warehouse-instance",
        "地面仓库的空槽、数量或实例数据未保存")
    assert(#data.stationWarehouse == 2 and data.stationWarehouse[2].id == "" and
        data.stationWarehouse[1].id == "probe:elevator-station-snapshot" and data.stationWarehouse[1].guid == 86421 and
        data.stationWarehouse[1].amount == 9 and data.stationWarehouse[1].special == "station-warehouse-instance",
        "太空仓库的空槽、数量或实例数据未保存")
    assert(data.pendingRestored, "到站满仓等待状态或在途余货未保存")
    assert(not data.riderRestored, "临时乘坐预约不能跨读档强制传送玩家")
    assert(data.anchorBody == "anchor-body" and data.anchorX == -100 and data.anchorY == 200, "站台锚定信息丢失")
    assert(data.resumedArrival, "恢复后的剩余行程无法完成")
    endpoint(data.afterResume, "Station", false, 0, false, true)
end, "电梯连接、行程和真实物品封包内存快照恢复")

test.system("space", "elevator_station", {}, function(data)
    scope(data, "elevator_station")
    assert(data.expandedGroups == 1 and data.expandedPieces == 3 and data.anchorRetained and not data.expandedWreck,
        "连续扩建错误拆分站台、丢失锚点或变成残骸")
    near(data.originAfter[1], data.originBefore[1], "扩建改变重心后站台原点横向漂移")
    near(data.originAfter[2], data.originBefore[2], "扩建改变重心后站台原点纵向漂移")
    assert(not data.anchorDamageAccepted and data.anchorHealth == 100, "电梯保护核心被结构伤害拆掉")
    assert(data.bridgeDamageAccepted and #data.fragments == 2, "普通扩建部件无法损坏或断桥未分裂")
    local anchored, detached = 0, 0
    for _, fragment in ipairs(data.fragments) do
        if fragment.anchored then
            anchored = anchored + 1
            assert(fragment.id == "elevator-station" and not fragment.wreck and #fragment.pieces == 1 and
                fragment.pieces[1] == "station-core", "保留锚点的部分不是原站台核心")
        else
            detached = detached + 1
            assert(fragment.id ~= "elevator-station" and fragment.wreck and #fragment.pieces == 1 and
                fragment.pieces[1] == "station-extension", "断开的扩建部分错误保留原身份或锚点")
        end
    end
    assert(anchored == 1 and detached == 1, "分裂复制了固定锚点或丢失脱落部分")
end, "站台自由扩建、原点稳定、核心保护和断桥仅保留一个锚点")
