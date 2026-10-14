-- 同一主题集中编排，子检查保留独立标签和原始采样。

do
    -- 只采样正式表现插值，不创建世界或操作当前船体与镜头。
    test.system("space", "presentation_interpolation", {}, function(data)
        local function near(actual, expected, label)
            assert(type(actual) == "number" and actual == actual and math.abs(actual - expected) <= 0.0000001, label)
        end
        local function position(actual, sectorX, sectorY, x, y, label)
            assert(type(actual) == "table" and actual.sector.x == sectorX and actual.sector.y == sectorY,
                label .. "星区归属错误")
            near(actual["local"].x, x, label .. "局部 X 错误")
            near(actual["local"].y, y, label .. "局部 Y 错误")
        end
        local function pose(actual, x, y, angle, label)
            position(actual.position, "0", "0", x, y, label)
            near(actual.angle, angle, label .. "角度错误")
        end
        assert(data.operation == "presentation_interpolation" and data.scope == "production-domain",
            "缺少正式飞船表现插值结果")
        local expected = {
            previous = {10, 20, 0}, current = {14, 28, math.pi / 2}, middle = {12, 24, math.pi / 4},
            ["clamp-low"] = {10, 20, 0}, ["clamp-high"] = {14, 28, math.pi / 2}
        }
        assert(type(data.interpolation) == "table" and #data.interpolation == 5, "插值端点、中点和越界样本不完整")
        local seen = {}
        for _, sample in ipairs(data.interpolation) do
            local value = expected[sample.name]
            assert(value and not seen[sample.name], "未知或重复插值样本：" .. sample.name)
            seen[sample.name] = true
            pose(sample, value[1], value[2], value[3], sample.name)
        end
        local wrapped = data.wrappedRotation
        near(math.abs(wrapped.forward.angle), math.pi, "跨正负 π 时应沿最短弧旋转")
        near(math.abs(wrapped.reverse.angle), math.pi, "反向跨正负 π 时应沿最短弧旋转")
        position(wrapped.forwardPoint, "0", "0", -1, 0, "正向旋转中点")
        position(wrapped.reversePoint, "0", "0", -1, 0, "反向旋转中点")
        local crossingExpected = {
            quarter = {"9223372036854775805", 127999.875, -9},
            middle = {"9223372036854775806", -128000, -8},
            ["three-quarters"] = {"9223372036854775806", -127999.875, -7}
        }
        assert(type(data.sectorCrossing) == "table" and #data.sectorCrossing == 3, "极大星区跨界样本不完整")
        seen = {}
        for _, sample in ipairs(data.sectorCrossing) do
            local value = crossingExpected[sample.name]
            assert(value and not seen[sample.name], "未知或重复跨界样本：" .. sample.name)
            seen[sample.name] = true
            position(sample.position, value[1], "-9223372036854775806", value[2], value[3], sample.name)
        end
        local mass = data.massShift
        pose(mass.before, 13, 18, math.pi / 2, "质量中心变化前的船格原点")
        pose(mass.after, 13, 18, math.pi / 2, "质量中心变化后的船格原点")
        position(mass.pieceBefore, "0", "0", 6, 24, "质量中心变化前的部件")
        position(mass.pieceAfter, "0", "0", 6, 24, "质量中心变化后的部件")
        local shared = data.sharedTransform
        pose(shared.pose, 12, 24, math.pi / 2, "共同船体姿态")
        position(shared.piece, "0", "0", 5, 30, "船件共同变换")
        position(shared.passenger, "0", "0", 8, 26, "乘员共同变换")
        position(shared.cameraCenter, "0", "0", 7, 27, "驾驶镜头中心共同变换")
        local track = data.track
        pose(track.initial, 10, 20, 0, "初始化不能从默认原点拖动")
        pose(track.advancedPrevious, 10, 20, 0, "推进保留前一个固定步")
        pose(track.advancedCurrent, 14, 28, math.pi / 2, "推进记录当前固定步")
        pose(track.advancedMiddle, 12, 24, math.pi / 4, "首个固定步之间插值")
        pose(track.secondMiddle, 16, 32, math.pi * 3 / 4, "第二次推进不能继续追首个位置")
        for _, key in ipairs({"resetPrevious", "resetCurrent", "resetLow", "resetMiddle", "resetHigh"}) do
            pose(track[key], 1000, -500, .3, "重置后不拖尾：" .. key)
        end
        assert(type(data.limitations) == "string" and #data.limitations > 0, "插值采样必须说明场景覆盖边界")
        return "覆盖端点、中点、越界限制、最短旋转、极大星区跨界、质量原点稳定、共同变换和重置不拖尾。"
    end, "飞船前后物理姿态插值与共同坐标变换")
end

do
    -- 点火、起降和太空阶段调用正式空地规则；真实贴图、碰撞和联机由现场验收。
    test.system("space", "ship_surface_state", {}, function(data)
        local function near(actual, expected, label)
            assert(type(actual) == "number" and actual == actual and math.abs(actual - expected) <= 0.0000001, label)
        end
        assert(data.operation == "ship_surface_state" and data.scope == "production-domain", "缺少正式飞船空地结果")
        near(data.airborneVisualRise, 1, "空中阈值必须是贴图升高 1 Unity 单位")
        local expected = {
            ["no-flight"] = {0, false, false}, landed = {0, false, false},
            ["landed-stale-height"] = {0, false, false}, floating = {0, false, false},
            ["ignited-ground"] = {0, false, true}, ["lifting-negative"] = {0, false, true},
            ["lifting-zero"] = {0, false, true}, ["lifting-below"] = {.99996, false, true},
            ["lifting-threshold"] = {1, true, true}, ["lifting-above"] = {1.00004, true, true},
            ["lifting-unpowered"] = {1, true, true}, ["descending-above"] = {1.00004, true, true},
            ["descending-threshold"] = {1, true, true}, ["descending-below"] = {.99996, false, true},
            ["descending-zero"] = {0, false, true}, cruising = {0, true, true},
            ["cruising-unpowered"] = {0, true, true}
        }
        assert(type(data.samples) == "table" and #data.samples == 17, "空地边界与阶段样本不完整")
        local seen = {}
        for _, sample in ipairs(data.samples) do
            local value = expected[sample.name]
            assert(value and not seen[sample.name], "未知或重复空地样本：" .. sample.name)
            seen[sample.name] = true
            near(sample.visualRise, value[1], "贴图升高错误：" .. sample.name)
            assert(sample.airborne == value[2], "空地模式错误：" .. sample.name)
            assert(sample.elevated == value[3], "点火或起降排序资格错误：" .. sample.name)
            local damage, airborne = sample.damage, value[2]
            assert(type(damage) == "table", "缺少投送资格矩阵：" .. sample.name)
            assert(damage.melee == not airborne, "普通近战空地资格错误：" .. sample.name)
            assert(damage["projectile-without-airborne"] == not airborne,
                "只有投射物标记不能绕过空中命中能力：" .. sample.name)
            assert(damage.ranged == true, "可命中空中的远程投射物必须兼容地面与空中：" .. sample.name)
            assert(damage["airborne-capability"] == not airborne,
                "只有空中能力的近身攻击不能伤害空中船：" .. sample.name)
            assert(damage["airborne-only"] == false and damage["airborne-only-projectile"] == airborne,
                "空中限定攻击必须同时具备投射物和空中命中能力：" .. sample.name)
            assert(damage["airborne-only-without-capability"] == false and
                damage["airborne-only-projectile-without-capability"] == false,
                "缺少空中命中能力的空中限定攻击必须拒绝：" .. sample.name)
        end
        assert(type(data.limitations) == "string" and #data.limitations > 0, "空地结果必须保留覆盖边界")
        return "覆盖点火排序资格、1 单位阈值两侧、降落恢复、熄火和太空阶段，以及全部八种投送能力组合。"
    end, "飞船点火排序与空地伤害模式")
end

do
    -- 独立结构样本验证四邻支撑、真实伤害后的分裂和旋转占地，不代表已执行场景放置。
    test.system("space", "ship_structure", {}, function(data)
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
            assert(ship.wreck == false and ship.pieces == 2, "两侧控制台与地板未保持各自归属")
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
    -- 只展开正式矩形读数核对预期格子，不在夹具中复制合批算法或创建物理场景。
    local function cell_key(x, y) return string.format("%d:%d", x, y) end
    local function expected_rectangle(x, y, width, height, blocks)
        local cells = {}
        for cy = y, y + height - 1 do
            for cx = x, x + width - 1 do cells[cell_key(cx, cy)] = blocks end
        end
        return cells
    end
    local function check(sample, expected, rectangle_count, label)
        assert(type(sample) == "table" and type(sample.rectangles) == "table", label .. "缺少正式合批读数")
        if rectangle_count then assert(#sample.rectangles == rectangle_count, label .. "矩形数量错误") end
        local actual, count, blocked = {}, 0, 0
        for _, rect in ipairs(sample.rectangles) do
            assert(type(rect.x) == "number" and rect.x % 1 == 0 and type(rect.y) == "number" and rect.y % 1 == 0,
                label .. "矩形起点必须是整数格")
            assert(type(rect.width) == "number" and rect.width > 0 and rect.width % 1 == 0 and
                type(rect.height) == "number" and rect.height > 0 and rect.height % 1 == 0,
                label .. "矩形尺寸必须是正整数")
            assert(type(rect.blocksPassage) == "boolean", label .. "缺少通行类别")
            for y = rect.y, rect.y + rect.height - 1 do
                for x = rect.x, rect.x + rect.width - 1 do
                    local key = cell_key(x, y)
                    assert(actual[key] == nil, label .. "同一格被多个碰撞矩形重复覆盖:" .. key)
                    assert(expected[key] ~= nil, label .. "合批填上了本来没有碰撞的格子:" .. key)
                    assert(expected[key] == rect.blocksPassage, label .. "阻挡与非阻挡类别错误:" .. key)
                    actual[key], count = rect.blocksPassage, count + 1
                    if rect.blocksPassage then blocked = blocked + 1 end
                end
            end
        end
        local expected_count = 0
        for key, blocks in pairs(expected) do
            assert(actual[key] ~= nil and actual[key] == blocks, label .. "漏掉真实碰撞格:" .. key)
            expected_count = expected_count + 1
        end
        assert(count == expected_count, label .. "占地总数不守恒")
        return count, blocked
    end
    local function check_rebuild(sample, changed, count, label)
        assert(sample.rebuilt == changed and sample.rebuilds == count, label .. "缓存重建次数错误")
    end
    test.system("space", "ship_collider_batch", {}, function(data)
        assert(data.operation == "ship_collider_batch" and data.scope == "production-domain", "缺少正式碰撞合批结果")
        assert(data.full.inputPieces == 96 and data.overlapping.inputPieces == 97 and data.blockingOverlap.inputPieces == 98,
            "完整地板与同格设备输入样本不完整")
        local full = expected_rectangle(0, 0, 12, 8, false)
        check(data.full, full, 1, "96格完整地板")
        check(data.overlapping, full, 1, "同格控制台去重")
        full["5:3"] = true
        local count, blocked = check(data.blockingOverlap, full, nil, "地板与墙重叠时阻挡优先")
        assert(count == 96 and blocked == 1 and count - blocked == 95, "同格墙不应重复生成地板接触")
        check(data.blockingKinds, expected_rectangle(0, 0, 2, 1, true), 1, "相邻阻挡墙与设备")
        return "覆盖96格地板合为1矩形、设备同格去重、阻挡优先和不同部件按通行类别合并。"
    end, "飞船碰撞体合批与重叠去重")
    test.system("space", "ship_collider_batch", {}, function(data)
        local hole = expected_rectangle(0, 0, 3, 3, false)
        hole["1:1"] = nil
        check(data.hole, hole, nil, "中央空洞")
        assert(#data.hole.rectangles > 1, "不能用整块矩形填上中央空洞")
        local corner = expected_rectangle(0, 0, 3, 3, false)
        corner["2:2"] = nil
        check(data.corner, corner, nil, "缺角地板")
        assert(#data.corner.rectangles > 1, "不能用整块矩形填上缺角")
        check(data.islands, { ["0:0"] = false, ["1:1"] = false, ["5:0"] = false, ["6:0"] = false }, 3,
            "斜角与分离岛")
        local rotated = expected_rectangle(-4, -7, 3, 2, false)
        for key, blocks in pairs(expected_rectangle(-9, -3, 2, 3, false)) do rotated[key] = blocks end
        check(data.rotated, rotated, 2, "负坐标与奇偶旋转宽高")
        check(data.exclusions, expected_rectangle(0, 0, 2, 1, false), 1, "管道保留与无碰撞部件排除")
        return "覆盖洞和缺角保留、斜角/分离岛、负坐标旋转、管道保留以及线缆/光纤/开门/死亡部件排除。"
    end, "飞船碰撞合批占地边界")
    test.system("space", "ship_collider_batch", {}, function(data)
        local closed = { ["0:0"] = false, ["1:0"] = true }
        check(data.closed, closed, 2, "初次关闭舱门")
        check_rebuild(data.closed, true, 1, "初次几何")
        assert(#data.stableFrames == 5, "稳定帧读数不完整")
        for _, frame in ipairs(data.stableFrames) do
            check(frame, closed, 2, "稳定帧")
            check_rebuild(frame, false, 1, "稳定帧复用")
        end
        check(data.moved, closed, 2, "船体位姿变化")
        check_rebuild(data.moved, false, 1, "位移旋转速度变化")
        check(data.massChanged, closed, 2, "货物改变质量")
        check_rebuild(data.massChanged, false, 1, "普通质量变化")
        assert(data.doorStructureRevisionUnchanged == true, "门开关样本不得依靠结构版本变化")
        check(data.opened, { ["0:0"] = false }, 1, "打开门")
        check_rebuild(data.opened, true, 2, "打开门失效")
        check(data.reclosed, closed, 2, "重新关门")
        check_rebuild(data.reclosed, true, 3, "重新关门失效")
        check(data.removed, { ["1:0"] = true }, 1, "拆除地板")
        check_rebuild(data.removed, true, 4, "拆除结构通知")
        assert(data.destroyedByDamage == true, "真实伤害没有摧毁最后舱门")
        check(data.emptied, {}, 0, "全部摧毁")
        check_rebuild(data.emptied, true, 5, "真实摧毁通知")
        assert(type(data.limitations) == "string" and #data.limitations > 0, "合批结果必须保留领域覆盖边界")
        return "覆盖稳定帧、运动和质量复用，门开关独立失效，以及正式拆除/伤害通知后的重建。"
    end, "飞船碰撞几何缓存与门开关")
end

do
    -- 独立相向接口样本走双方确认和断开事务，场景按钮与多人请求由额外流程验收。
    test.system("space", "ship_docking", {}, function(data)
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
    -- 独立舱室没有气体物性目录依赖，真实充气链保留在集成专项。
    test.system("space", "cabin_topology", {}, function(data)
        assert(data.enclosedRooms == 1 and data.enclosedCells == 9, "完整边界没有形成九格密闭内舱")
        assert(data.closedBlocks and not data.openBlocks, "舱门通行开关没有改变阻挡")
        assert(data.openRooms == 1, "开门通行擅自破坏独立气密能力")
        assert(data.broken and data.breachedRooms == 0, "摧毁真实边界后仍保留密闭舱")
        assert(data.repairedRooms == 1, "修补边界后没有重新形成密闭拓扑")
    end, "密闭舱拓扑、舱门通行、破口与修补")
end

do
    -- 载荷改变只移动物理重心，船体上的世界点和点速度必须保持连续。
    local function near(actual, expected, label)
        assert(type(actual) == "number" and actual == actual and math.abs(actual - expected) <= 0.0000001, label)
    end
    test.system("space", "ship_mass", {}, function(data)
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
    test.system("space", "ship_propulsion", {}, function(data)
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
    test.system("space", "ship_contacts", {}, function(data)
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
    test.system("space", "ship_snapshots", {}, function(data)
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
    test.system("space", "ship_engine_placement", {}, function(data)
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
    test.system("space", "ship_fuel", {}, function(data)
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
    test.system("space", "ship_optical", {}, function(data)
        assert(data.operation == "ship_optical" and data.scope == "production-domain", "缺少正式光纤领域结果")
        assert(data.connected == true, "相邻光纤必须连接独立控制台和导航端口")
        assert(data.afterRemoval == false, "光纤移除后不能继续沿用旧连接")
        assert(data.afterRepair == true, "同一工作区修复光纤后必须恢复实际连接")
        return "覆盖独立控制台与导航光纤连接、断开、修复和集合工作区复用。"
    end, "飞船独立光纤连接")
end

-- 同一主题集中编排，子检查保留独立标签和原始采样。

do
    -- 检查星区边界、极大编号精度和真实空域接触。
    test.system("space", "coordinates", {}, function(data)
        local function near(value, expected, label)
            assert(type(value) == "number" and value == value and math.abs(value - expected) <= 0.000001, label .. "不一致")
        end
        near(data.size, 256000, "星区边长")
        near(data.halfSize, 128000, "星区半边长")
        local expected = {
            inside = {"0", "0", .125, -.25}, left = {"-1", "0", 127999.75, 0},
            right = {"1", "0", -128000, 0}, down = {"0", "-1", 0, 127999.75},
            up = {"0", "1", 0, -128000}, ["negative-edge"] = {"0", "0", -128000, -128000},
            ["multi-corner"] = {"8", "-10", -127999.875, 127999.75},
            ["huge-neighbor"] = {"9223372036854775807", "-9223372036854775808", -127999.875, 127999.75}
        }
        assert(type(data.normalizations) == "table" and #data.normalizations == 8, "归一化样本不完整")
        local seen = {}
        for _, sample in ipairs(data.normalizations) do
            local value = expected[sample.name]
            assert(value and not seen[sample.name], "未知或重复边界样本:" .. sample.name)
            seen[sample.name] = true
            assert(sample.output.sector.x == value[1] and sample.output.sector.y == value[2], sample.name .. "星区归属错误")
            near(sample.output["local"].x, value[3], sample.name .. "/局部X")
            near(sample.output["local"].y, value[4], sample.name .. "/局部Y")
            assert(sample.output["local"].x >= -data.halfSize and sample.output["local"].x < data.halfSize and
                sample.output["local"].y >= -data.halfSize and sample.output["local"].y < data.halfSize, sample.name .. "局部位置未归一化")
        end
        near(data.relative.x, 256000.125, "long 极值附近相对X")
        near(data.relative.y, -256000.25, "long 极值附近相对Y")
        assert(type(data.sceneKeys) == "table" and #data.sceneKeys >= 4, "场景键样本不足")
        for _, sample in ipairs(data.sceneKeys) do
            assert(sample.parsed == true and sample.restored.x == sample.sector.x and sample.restored.y == sample.sector.y,
                "场景键往返丢失 long 精度:" .. sample.key)
        end
        assert(type(data.identities) == "table" and #data.identities >= 8, "星体身份样本不足")
        for _, sample in ipairs(data.identities) do
            assert(sample.parsed == true and sample.restored.x == sample.sector.x and sample.restored.y == sample.sector.y and
                type(sample["local"]) == "string" and #sample["local"] > 0, "全局身份往返失败:" .. sample.identity)
        end
        assert(type(data.invalid) == "table" and #data.invalid >= 5, "非法身份样本不足")
        for _, sample in ipairs(data.invalid) do
            assert(sample.sceneParsed == false and sample.identityParsed == false, "非法身份被接受:" .. sample.input)
        end
        local before, after = data.roundTrip.before, data.roundTrip.after
        assert(before.sector.x == after.sector.x and before.sector.y == after.sector.y, "坐标快照星区改变")
        near(after["local"].x, before["local"].x, "坐标快照X")
        near(after["local"].y, before["local"].y, "坐标快照Y")
        assert(type(data.guards) == "table" and #data.guards >= 4, "坐标保护样本不足")
        for _, guard in ipairs(data.guards) do
            assert(type(guard.exception) == "string" and #guard.exception > 0, "无效坐标未拒绝:" .. guard.name)
        end
        local contacts = {}
        assert(type(data.contacts) == "table", "缺少接触采样")
        for _, sample in ipairs(data.contacts) do contacts[sample.name] = sample end
        for _, name in ipairs({"neighbor", "far", "empty", "overlap", "tangent", "miss"}) do
            assert(type(contacts[name]) == "table", "接触样本缺失:" .. name)
        end
        assert(contacts.neighbor.hit == true, "相邻星区实际星体没有被扫掠命中")
        near(contacts.neighbor.fraction, .1, "跨星区接触时刻")
        near(contacts.neighbor.contactPosition.x, 128000, "接触点的星体参考区")
        near(contacts.neighbor.motionPosition.x, -128000, "运动体的原参考区")
        assert(contacts.far.hit == false, "遥远星区相同局部坐标出现假接触")
        assert(contacts.empty.hit == false, "空域出现假星体")
        near(contacts.empty.position.x, 24.125, "空域加速位置X")
        near(contacts.empty.position.y, -4.25, "空域加速位置Y")
        near(contacts.empty.velocity.x, 14, "空域速度X")
        near(contacts.empty.velocity.y, -1, "空域速度Y")
        assert(contacts.overlap.hit == true and contacts.tangent.hit == true and contacts.miss.hit == false, "圆形扫掠边界错误")
        near(contacts.overlap.fraction, 0, "初始重叠接触")
        near(contacts.tangent.fraction, .5, "切线接触")
        return "覆盖 8 种归一化、long 极值往返、6 种接触与空域惯性"
    end, "极大星区、跨界局部精度、空域与星体接触")
end

do
    -- 读取真实租约规则的逐帧输出，检查预加载、折返和清退。
    test.system("space", "streaming", {}, function(data)
        assert(data.transitionMeters == 4096 and data.prefetchMeters == 8192, "星区过渡范围与现有规则不一致")
        local origin, left = "0:0", "-1:0"
        local expectations = {
            left = {{origin}, {origin, left}}, right = {{origin}, {origin, "1:0"}},
            down = {{origin}, {origin, "0:-1"}}, up = {{origin}, {origin, "0:1"}},
            corner = {{origin, "1:0", "0:-1", "1:-1"}},
            ["return-release"] = {{origin, left}, {origin, left}, {origin, left}, {left}, {origin, left}, {}},
            observers = {{origin}, {origin, left}, {left}, {}},
            ["fast-jump"] = {{origin}, {"1000000000000000:0"}},
            ["max-corner"] = {{"9223372036854775807:9223372036854775807"}},
            ["min-corner"] = {{"-9223372036854775808:-9223372036854775808"}}
        }
        assert(type(data.scenarios) == "table" and #data.scenarios == 10, "租约场景采样不完整")
        local seen, frameCount = {}, 0
        for _, scenario in ipairs(data.scenarios) do
            local expected = expectations[scenario.name]
            assert(expected and not seen[scenario.name], "未知或重复租约场景:" .. scenario.name)
            seen[scenario.name] = true
            assert(type(scenario.frames) == "table" and #scenario.frames == #expected, scenario.name .. "帧采样缺失")
            for index, frame in ipairs(scenario.frames) do
                local label = scenario.name .. "/第" .. index .. "帧"
                assert(type(frame.required) == "table" and #frame.required == #expected[index], label .. "租约数量错误")
                local required = {}
                for _, sector in ipairs(frame.required) do
                    local key = sector.x .. ":" .. sector.y
                    assert(not required[key], label .. "星区重复:" .. key)
                    required[key] = true
                end
                for _, key in ipairs(expected[index]) do assert(required[key], label .. "缺少星区:" .. key) end
                if #frame.observations == 0 then assert(#frame.required == 0, label .. "无观察者时未清退") end
                frameCount = frameCount + 1
            end
        end
        return "覆盖 " .. #data.scenarios .. " 个租约场景、" .. frameCount .. " 帧及多人保留/折返清退"
    end, "四向与角落预加载、多人租约、折返和极值清退")
end
