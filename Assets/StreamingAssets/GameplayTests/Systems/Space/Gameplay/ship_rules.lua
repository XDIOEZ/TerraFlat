-- 同一主题集中编排，子检查保留独立标签和原始采样。

do
    -- 独立结构样本验证四邻支撑、真实伤害后的分裂和旋转占地，不代表已执行场景放置。
    test.space("ship_structure", function(data)
        assert(data.operation == "ship_structure" and data.scope == "production-domain", "缺少正式结构领域结果")
        assert(type(data.inputPieces) == "number" and #data.connected == 1, "连续地板必须属于同一艘船")
        assert(#data.detached == 1 and data.unsupportedDestroyed == true, "无地板支撑设备未被回收")
        assert(data.detached[1].reason == "lost-floor-support", "脱离设备缺少真实支撑原因")
        assert(data.connected[1].pieces == data.inputPieces - #data.detached, "重建后的结构数量错误")
        assert(data.removeAccepted == true and data.bridgeDestroyed == true, "桥梁实际伤害没有移除结构")
        assert(#data.split == 2, "断开桥梁后必须形成两艘独立船体")
        local identities, retained, total = {}, 0, 0
        for _, ship in ipairs(data.split) do
            assert(type(ship.id) == "string" and not identities[ship.id], "分裂船体身份缺失或重复")
            identities[ship.id] = true
            if ship.id == data.originalId then retained = retained + 1 end
            assert(ship.wreck == false and ship.pieces == 3, "两侧控制台、引擎与地板未保持各自归属")
            total = total + ship.pieces
        end
        assert(retained == 1 and total == data.connected[1].pieces - 1, "原船身份或移除后的部件守恒失败")
        assert(#data.rotatedFootprint == 6, "旋转矩形地板占地数量错误")
        local cells = {}
        for _, cell in ipairs(data.rotatedFootprint) do
            assert(cell.x >= -2 and cell.x <= 0 and cell.y >= -3 and cell.y <= -2, "旋转后的占地边界错误")
            local key = tostring(cell.x) .. ":" .. tostring(cell.y)
            assert(not cells[key], "旋转占地出现重复格")
            cells[key] = true
        end
        assert(data.noFloorGroups == 0 and data.noFloorWreck == true and data.noFloorOrphanDestroyed == true,
            "失去全部地板后应回收孤立设备并结束船体归属")
        return "覆盖领域四邻支撑、无支撑设备、毁桥分裂、身份保留、全部失支撑与旋转占地。"
    end, "船体结构支撑与断裂")
end

do
    -- 独立相向接口样本走双方确认和断开事务，场景按钮与多人请求由额外流程验收。
    test.space("ship_docking", function(data)
        assert(data.operation == "ship_docking" and data.scope == "production-domain", "缺少正式对接领域结果")
        assert(data.candidate == true and data.groupsBefore == 2, "相向贴合接口未产生候选或提前合船")
        assert(data.firstConfirm == true and data.connectedAfterFirst == false, "单方确认不能直接建立连接")
        assert(data.secondConfirm == true and data.connectedAfterSecond == true, "双方确认未建立实际连接")
        assert(data.connectedGroups == 1, "连接后应只有一个运动整体")
        assert(type(data.momentumAfter) == "number" and math.abs(data.momentumAfter) <= 0.0000001,
            "等质量反向低速对接没有守恒零总动量")
        assert(data.disconnectAccepted == true and data.connectedAfterDisconnect == false,
            "断开入口未撤销实际连接")
        assert(data.disconnectedGroups == 2, "断开后两个成员未恢复独立运动整体")
        assert(data.distantCandidateAccepted == false, "接口远离后不能继续生成对接候选")
        assert(#data.ships == 2 and data.ships[1].id ~= data.ships[2].id, "对接事务丢失独立船体身份")
        return "覆盖相向捕获、双方确认、运动整体合并、动量守恒、断开和远距离拒绝。"
    end, "飞船双方对接与断开")
end

do
    -- 舱气使用真实库存进出，门的通行状态与气密边界按生产规则分别断言。
    local function near(actual, expected, label)
        assert(type(actual) == "number" and actual == actual and math.abs(actual - expected) <= 0.00001, label)
    end
    test.space("ship_atmosphere", function(data)
        assert(data.operation == "ship_atmosphere" and data.scope == "production-domain", "缺少正式舱气领域结果")
        assert(data.rooms == 1, "完整五格方形边界应产生一个九格内舱")
        near(data.volumeLiters, 9 * 2.5 * 1000, "内舱实际容积错误")
        assert(type(data.plannedMoles) == "number" and data.plannedMoles > 0, "标准压强的计划气量无效")
        assert(data.oxygenAdded == true and data.nitrogenAdded == true, "真实氮氧库存未进入内舱")
        assert(data.filled.sealed == true and data.filled.floor == true and data.filled.canBreathe == true,
            "有地板的标准空气内舱应气密且可呼吸")
        near(data.filled.pressure, 101.325, "充气后的实际总压强错误")
        near(data.filled.oxygenPressure, data.filled.pressure * 0.21, "实际氧分压错误")
        near(data.filled.carbonDioxidePressure, 0, "样本没有二氧化碳却出现二氧化碳分压")
        near(data.beforeTakeMoles, data.plannedMoles, "氮氧入舱的气量没有守恒")
        assert(data.takeAccepted == true, "内舱真实抽氧失败")
        near(data.takenMoles, 10, "抽氧必须返回实际扣除的气量")
        assert(data.supplyPrepared == true and data.supplyAccepted == true, "独立真实供气罐未完成供气")
        near(data.receivedMoles, 5, "内舱没有收到指定的五摩尔供气")
        near(data.sourceBeforeMoles - data.sourceAfterMoles, data.receivedMoles, "供气未等量扣除源库存")
        assert(data.blocksPassageClosed == true and data.blocksPassageOpen == false, "舱门开关没有改变通行阻挡")
        assert(data.doorOpenEnvironment.sealed == true, "通行开关不应擅自修改独立气密边界")
        near(data.beforeLeakMoles, data.beforeTakeMoles - data.takenMoles + data.receivedMoles,
            "抽取、供气和开门后的舱气没有守恒")
        assert(data.breachAccepted == true and data.breachedRooms == 0, "真实边界摧毁后仍保留密闭舱")
        near(data.releasedMoles, data.beforeLeakMoles, "破舱释放没有交回实际气体库存")
        assert(data.repairedRooms == 1, "修补边界后没有恢复密闭拓扑")
        near(data.repairedMoles, 0, "曾经密闭的区域修补后不能免费生成舱气")
        return "覆盖真实充气、分压、呼吸条件、抽氧与供气守恒、门通行、破舱释放和修补不生成气体。"
    end, "密闭舱真实气量与舱门")
end

do
    -- 载荷改变只移动物理重心，船体上的世界点和点速度必须保持连续。
    local function near(actual, expected, label)
        assert(type(actual) == "number" and actual == actual and math.abs(actual - expected) <= 0.0000001, label)
    end
    test.space("ship_mass", function(data)
        assert(data.operation == "ship_mass" and data.scope == "production-domain", "缺少正式质量领域结果")
        near(data.initial.mass, data.floorInputMass, "初始结构质量错误")
        near(data.initial.centerX, 1, "两格矩形地板的横向重心错误")
        near(data.initial.centerY, 0.5, "一格高地板的纵向重心错误")
        near(data.initial.inertia, data.floorInputMass * (2 * 2 + 1) / 12, "矩形地板实际惯量错误")
        near(data.loaded.mass, data.floorInputMass + data.cargoInputMass, "相同货物来源被重复计重")
        local center = (data.initial.centerX * data.floorInputMass + 3 * data.cargoInputMass) / data.loaded.mass
        near(data.loaded.centerX, center, "真实货物没有按位置改变横向重心")
        near(data.loaded.centerY, data.initial.centerY, "同高度货物不应改变纵向重心")
        local inertia = data.initial.inertia + data.floorInputMass * data.cargoInputMass / data.loaded.mass
            * (3 - data.initial.centerX) ^ 2
        near(data.loaded.inertia, inertia, "新增货物的平行轴惯量错误")
        for _, field in ipairs({"x", "y", "velocityX", "velocityY"}) do
            near(data.pointAfter[field], data.pointBefore[field], "重心改变时世界点或点速度不连续：" .. field)
        end
        for _, field in ipairs({"mass", "centerX", "centerY", "inertia"}) do
            near(data.unloaded[field], data.initial[field], "卸载货物后物理属性未恢复：" .. field)
        end
        assert(data.invalidMassRejected == true, "负质量贡献必须被生产入口拒绝")
        return "覆盖部件质量、矩形惯量、来源去重、货物重心、点速度守恒、卸载恢复和负质量拒绝。"
    end, "船体质量重心与惯量")
end

do
    -- 领域推进只断言方向分配和惯性算法，供油输入不代表场景机器已经供油。
    local function near(actual, expected, label)
        assert(type(actual) == "number" and actual == actual and math.abs(actual - expected) <= 0.0000001, label)
    end
    test.space("ship_propulsion", function(data)
        assert(data.operation == "ship_propulsion" and data.scope == "production-domain", "缺少正式推进领域结果")
        assert(#data.cardinal == 4, "必须返回四个实际安装方向的推进结果")
        for direction, sample in ipairs(data.cardinal) do
            assert(#sample.throttle == 4, "四台喷嘴的出力索引丢失")
            for nozzle, throttle in ipairs(sample.throttle) do
                near(throttle, nozzle == direction and 1 or 0, "方向请求选择了错误的喷嘴")
            end
            near(sample.actualChange.x, sample.request.x, "四向推进横轴实际变化错误")
            near(sample.actualChange.y, sample.request.y, "四向推进纵轴实际变化错误")
        end
        near(data.partialOutputChange.x, 0, "没有横向供油时不能产生横向推进")
        near(data.partialOutputChange.y, 1.6 * 0.5, "纵向半供油未按比例限制推进")
        near(data.pureTurnTranslation.x, 0, "纯转向不应附加横向推进")
        near(data.pureTurnTranslation.y, 0, "纯转向不应附加纵向推进")
        near(data.turnAuthority, 1, "完整转向供油没有形成完整转向能力")
        near(data.coastAfterTenSeconds.x, data.coastInput.x, "松键十秒后横向惯性丢失")
        near(data.coastAfterTenSeconds.y, data.coastInput.y, "松键十秒后纵向惯性丢失")
        near(data.sideways.x, data.coastInput.x, "横向推力错误清除了已有侧向漂移")
        near(data.sideways.y, data.coastInput.y + 60 * 0.02, "横向输入没有施加对应速度变化")
        near(data.reverse.x, data.reverseInput.x - 100 * 0.02, "反向输入必须逐步制动")
        near(data.unpoweredOverspeed.x, 180, "无动力不能擅自限制碰撞后的超速惯性")
        near(data.unpoweredOverspeed.y, 0, "无动力不应新增纵向漂移")
        assert(#data.arrivals == 4 and type(data.arrivalDistance) == "number" and type(data.arrivalSpeed) == "number",
            "缺少多步长导航结果或正式到达阈值")
        for _, arrival in ipairs(data.arrivals) do
            assert(arrival.seconds > 0 and arrival.steps < 10000, "领域导航未在预算步数内到达")
            assert(arrival.distance >= 0 and arrival.distance <= data.arrivalDistance,
                "多步长导航未进入到达距离")
            assert(arrival.speed >= 0 and arrival.speed <= data.arrivalSpeed,
                "多步长导航进入范围时尚未停稳")
        end
        return "覆盖四向喷嘴、缺油比例、纯转向、松键惯性、侧向漂移、反向制动和多步长到点停稳。"
    end, "喷嘴推进惯性与自动导航")
end

do
    -- 使用正式乘员与星体扫掠领域入口，船对船、散落物接船和联机仍需实际会话覆盖。
    local function near(actual, expected, label)
        assert(type(actual) == "number" and actual == actual and math.abs(actual - expected) <= 0.0000001, label)
    end
    test.space("ship_contacts", function(data)
        assert(data.operation == "ship_contacts" and data.scope == "production-domain", "缺少正式接触领域结果")
        assert(data.surfaceContact == true and data.contactBody == "gameplay-probe-body", "扫掠未命中实际星体")
        near(data.contactFraction, (150 - 100 - 1) / 100, "高速星体扫掠接触时间错误")
        near(data.contactPosition.x, 100 + 1, "独立乘员未停在星体接触位置")
        near(data.contactPosition.y, 0, "直线星体扫掠产生错误侧移")
        near(data.freeShipPosition.x, 150 - 100, "自由飞行错误地被星体画面截停")
        near(data.freeShipPosition.y, 0, "自由飞行产生错误纵向位移")
        assert(data.passengerPairContact == true, "相向高速乘员应发生真实成对接触")
        near(data.momentumAfter, data.momentumBefore, "乘员质量不同的碰撞没有守恒总动量")
        near(data.leftVelocity.x, data.rightVelocity.x, "非弹性接触后两侧法向速度不一致")
        assert(type(data.separation) == "number" and data.separation >= 1, "乘员实际碰撞后仍穿透彼此")
        assert(data.distantPairContact == false, "遥远星区相同局部坐标不能发生接触")
        assert(data.crossing.sectorX == "1" and data.crossing.sectorY == "0", "正向跨区归属错误或身份未按文字传输")
        near(data.crossing.x, -data.sectorSize / 2 + 0.25, "正向跨区没有保留亚米局部位置")
        assert(data.negativeCrossing.sectorX == "-1" and data.negativeCrossing.sectorY == "0", "负向跨区归属错误")
        near(data.negativeCrossing.x, data.sectorSize / 2 - 0.25, "负向跨区没有保留亚米局部位置")
        near(data.highSectorRelative.x, data.sectorSize + 10, "巨大整数邻区失去局部米级精度")
        near(data.highSectorRelative.y, -10, "巨大整数邻区产生错误纵向偏移")
        assert(type(data.limitations) == "string" and #data.limitations > 0, "接触领域结果必须保留覆盖边界")
        return "覆盖星体高速扫掠、自由飞行、乘员碰撞动量、远区排除、正负跨区和巨大编号局部精度。"
    end, "领域接触与跨星区精度")
end

do
    -- 独立生产会话模型往返验证身份和数值，不把内存 JSON 等同于磁盘存档验收。
    local function near(actual, expected, label)
        assert(type(actual) == "number" and actual == actual and math.abs(actual - expected) <= 0.0000001, label)
    end
    test.space("ship_snapshots", function(data)
        assert(data.operation == "ship_snapshots" and data.scope == "production-domain", "缺少正式快照领域结果")
        assert(data.version == 2 and data.stableRepeatSnapshot == true, "会话快照版本或重复往返不稳定")
        near(data.simulationSeconds, 123.5, "会话时钟往返丢失")
        assert(data.shipCount == 1 and data.flightCount == 1 and data.navigationCount == 1 and data.looseCount == 1,
            "快照往返丢失船体、飞行、导航或散落物")
        assert(data.ship.id == "gameplay-probe-snapshot" and data.ship.pieces == 3, "船体身份或成员数量丢失")
        assert(data.ship.sectorX == "-3" and data.ship.sectorY == "4", "船体星区身份丢失或未按文字传输")
        near(data.ship.velocityX, 17, "船体横向速度丢失")
        near(data.ship.velocityY, -8, "船体纵向速度丢失")
        assert(data.flightPhase == "Cruising" and data.flightIgnited == true, "真实飞行阶段和点火状态丢失")
        assert(data.navigationSectorX == "-2" and data.navigationSectorY == "4", "导航目标星区丢失")
        near(data.passengerReleasedVelocity.x, 20, "读回后松开步行误删横向继承速度")
        near(data.passengerReleasedVelocity.y, -2, "读回后松开步行误删纵向继承速度")
        assert(#data.scaleKeys == 3, "缩放快照应只保存三个真实分量")
        local keys = {}
        for _, key in ipairs(data.scaleKeys) do
            assert(key == "x" or key == "y" or key == "z", "缩放包含可能递归的计算属性")
            assert(not keys[key], "缩放分量重复")
            keys[key] = true
        end
        assert(keys.x and keys.y and keys.z, "缩放缺少真实分量")
        near(data.scaleX, -2, "负缩放没有完整保存")
        near(data.scaleY, 0, "零缩放没有完整保存")
        near(data.scaleZ, 3.5, "非等比缩放没有完整保存")
        assert(data.loosePayload == "opaque-fixture-payload", "散落物原始载荷被快照修改")
        near(data.loosePosition.x, 8, "散落物横向位置丢失")
        near(data.loosePosition.y, -9, "散落物纵向位置丢失")
        near(data.looseVelocity.x, 10, "散落物横向速度丢失")
        near(data.looseVelocity.y, -11, "散落物纵向速度丢失")
        assert(type(data.limitations) == "string" and #data.limitations > 0, "快照结果必须保留覆盖边界")
        return "覆盖内存快照身份、时钟、船速、航点、步行分量、散落物载荷和三分量缩放往返。"
    end, "太空会话快照往返")
end

do
    -- 真实安装预检验证四向外缘、喷口通道和地板支撑，实际建造由场景流程覆盖。
    test.space("ship_engine_placement", function(data)
        assert(data.operation == "ship_engine_placement" and data.scope == "production-domain", "缺少正式引擎安装领域结果")
        assert(#data.cardinal == 4, "四个外缘安装方向结果不完整")
        local directions = {}
        for _, sample in ipairs(data.cardinal) do
            assert(type(sample.quarterTurns) == "number" and sample.quarterTurns >= 0 and sample.quarterTurns <= 3,
                "外缘引擎安装朝向错误")
            assert(not directions[sample.quarterTurns], "安装朝向重复")
            directions[sample.quarterTurns] = true
            assert(sample.allowed == true, "朝外的四向边缘引擎应允许安装")
        end
        assert(data.centerAllowed == false and type(data.centerReason) == "string" and #data.centerReason > 0,
            "内部引擎必须被拒绝并返回原因")
        assert(data.exhaustObstacleAllowed == false and type(data.exhaustObstacleReason) == "string"
            and #data.exhaustObstacleReason > 0, "新增地板不能堵住已有引擎喷口")
        assert(data.pipeAllowed == true, "管道不应被当作阻挡喷口的结构层")
        assert(data.unsupportedAllowed == false and type(data.unsupportedReason) == "string" and #data.unsupportedReason > 0,
            "缺少地板支撑的引擎必须被拒绝")
        return "覆盖四向边缘安装、内部喷口拒绝、后续阻挡拒绝、管道例外和缺少支撑拒绝。"
    end, "引擎安装与喷口净空")
end

do
    -- 氢氧反应直接使用生产燃料和库存入口，管路供给另由真实机器流程覆盖。
    local function near(actual, expected, label)
        assert(type(actual) == "number" and actual == actual and math.abs(actual - expected) <= 0.0000001, label)
    end
    test.space("ship_fuel", function(data)
        assert(data.operation == "ship_fuel" and data.scope == "production-domain", "缺少正式燃料领域结果")
        assert(data.hydrogenAdded == true and data.oxygenAdded == true and data.eligible == true,
            "真实二比一氢氧库存应可作航空燃料")
        near(data.availableReactionMoles, 10, "真实反应量应由氢氧限制组分共同决定")
        assert(data.burnAccepted == true, "合格实际燃料没有成功燃烧")
        near(data.hydrogenAfter, (data.availableReactionMoles - data.burnRequestedOxygen) * 2,
            "燃烧氢气扣量未满足二比一比例")
        near(data.oxygenAfter, data.availableReactionMoles - data.burnRequestedOxygen,
            "燃烧氧气扣量错误")
        near(data.energyJoules, data.burnRequestedOxygen * 483600, "生产燃烧能量不匹配实际反应量")
        assert(data.overdrawAccepted == false, "燃料不足时不能部分执行超额反应")
        near(data.overdrawEnergy, 0, "失败的反应不应产生能量")
        near(data.hydrogenAfterFailure, data.hydrogenAfter, "失败反应错误消耗了氢气")
        near(data.oxygenAfterFailure, data.oxygenAfter, "失败反应错误消耗了氧气")
        assert(data.impurityAdded == true and data.impureEligible == false, "过量杂气燃料必须被拒绝")
        assert(data.singleGasPrepared == true and data.singleGasEligible == false, "只有氢气的库存不能作为合格燃料")
        return "覆盖真实二比一燃料、限制组分、原子扣量、能量、超额请求回滚、杂气和缺氧拒绝。"
    end, "飞船真实氢氧燃料反应")
end

do
    -- 光纤独立连接规则验证控制台与导航之间断开、修复以及工作区复用。
    test.space("ship_optical", function(data)
        assert(data.operation == "ship_optical" and data.scope == "production-domain", "缺少正式光纤领域结果")
        assert(data.connected == true, "相邻光纤必须连接独立控制台和导航端口")
        assert(data.afterRemoval == false, "光纤移除后不能继续沿用旧连接")
        assert(data.afterRepair == true, "同一工作区修复光纤后必须恢复实际连接")
        return "覆盖独立控制台与导航光纤连接、断开、修复和集合工作区复用。"
    end, "飞船独立光纤连接")
end
