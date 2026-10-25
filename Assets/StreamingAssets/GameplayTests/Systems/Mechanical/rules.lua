test.system("mechanical", "process_lookup_index", {}, function(data)
    assert(data.rules > 0 and data.exact and data.matched == 8192,
        "机械目录按站点及材料的值类型索引遗漏正式配方或返回不同对象")
    assert(data.missing and data.caseSensitive and data.delimiterRejected,
        "无匹配/空参数/大小写或历史复合键边界改变了原始语义")
    assert(data.allocatedBytes == 0, "机械配方查询预热后仍逐次拼接字符串产生 GC")
end, "机械热配方查询：真实目录、一致性、严格身份与零分配")

test.system("mechanical", "machine_pulse_order", {}, function(data)
    local function sequence_equals(actual, expected)
        if #actual ~= #expected then return false end
        for i = 1, #expected do
            if actual[i] ~= expected[i] then return false end
        end
        return true
    end
    assert(sequence_equals(data.first, {3, 1, 2, 0}),
        "机械命中顺序没有优先扫掠时间，再比较接触距离")
    assert(sequence_equals(data.second, {1, 3, 0, 2}),
        "相同命中集合按另一攻击中心排序时错误复用了上次中心")
    assert(data.nestedIndependent and data.firstUnchanged and data.emptyCleared and data.sourceUntouched
        and data.exceptionLeaseCleared,
        "嵌套攻击复用了相同缓冲、排序污染原数据或异常回收后遗留旧候选")
end, "机械武器查询：无闭包排序、嵌套缓存独立与清空")

local function near(a, b, message)
    assert(type(a) == "number" and a == a and math.abs(a - b) <= 0.00001, message)
end

test.system("mechanical", "runtime_snapshot_clone", {}, function(data)
    assert(data.identity and data.valuesPreserved and data.machineStatePreserved,
        "机器运行态复制丢失身份、品质、特殊状态或炉体/核心载荷")
    assert(data.configurationShared and data.definitionUnchanged,
        "机器复制没有复用只读定义，或副本修改反写了定义配置")
    assert(data.rootIndependent and data.modulesIndependent and data.inventoryIndependent and data.sourceUnchanged,
        "机器副本共享了可变堆叠、位姿、模块字节或嵌套库存，修改污染源对象")
    assert(data.preparationEmpty and data.preparationIndependent and data.noWorldOrRandomChanges,
        "炉体缓存预备使用了燃烧/非空库存、共享临时状态，或改变世界与游戏随机数")
end, "机器独立快照：共享定义、可变状态隔离、机器载荷与空炉体预备")

test.system("mechanical", "workbench_cabinet", {}, function(data)
    local linked = 0
    for _, position in ipairs(data.positions) do
        local expected = math.abs(position.x) <= 1 and math.abs(position.y) <= 1
        assert(position.linked == expected, "工具柜连接范围没有严格限制在相邻八格")
        if position.linked then linked = linked + 1 end
    end
    assert(linked == 8 and data.selected == 2 and data.preferred == 3,
        "多个工具柜重复加格，或重绑时无故换柜")
    assert(data.fallback == 3 and data.removed, "拆除当前工具柜没有切换或断开共享槽")
end, "工具柜邻格：八格生效、两格拒绝、多个不叠加与拆除切换")

test.system("mechanical", "ship_scope_identity", {}, function(data)
    assert(data.identity and data.strictPrefix,
        "船体作用域 ID 解析污染了地表身份，或改变了前缀大小写和空船 ID 规则")
    assert(data.stableReference,
        "重复读取船体作用域 ID 没有复用同一字符串引用，仍可能逐次分配")
    assert(data.switchedShip and data.switchedSurface and data.restoredShip,
        "跨船、地表切换或恢复作用域时仍错误复用了旧船 ID")
end, "机器船体身份按作用域冻结：重读零字符串创建与跨世界刷新")

test.system("mechanical", "load_allocation", {}, function(data)
    assert(data.first.nearSatisfied and not data.first.farSatisfied, "不足扭矩没有优先满足近处完整负载")
    near(data.first.nearRpm, 60, "近处负载没有获得真实转速")
    near(data.first.farRpm, 0, "不足完整需求的远处设备仍在运行")
    assert(data.increased.nearSatisfied and data.increased.farSatisfied, "增加源扭矩后远处负载没有恢复")
    near(data.increased.nearRpm, 60, "增力改变近处额定转速")
    near(data.increased.farRpm, 60, "远处负载恢复转速错误")
    assert(not data.stoppedNearSatisfied and not data.stoppedFarSatisfied, "断动力后负载仍报告满足")
    near(data.stoppedNearRpm, 0, "断动力后近处继续转动")
    near(data.stoppedFarRpm, 0, "断动力后远处继续转动")
end, "扭矩负载优先、供给恢复与断动力")

test.system("mechanical", "gear_conflict", {}, function(data)
    assert(data.gearSatisfied, "齿轮传动没有满足负载")
    near(data.gearRpm, -60, "齿轮啮合没有反转")
    assert(data.conflict, "异速动力源没有触发整网冲突")
    near(data.conflictRelayRpm, 0, "动力冲突后中间轴仍转动")
end, "齿轮反转与异速源停网")

test.system("mechanical", "bellows_effects", {}, function(data)
    assert(data.bellowsIndexed and data.emptyFastPath and
        data.nonBellowsFastPath and data.restoredAfterRebuild,
        "机械拓扑变化后风箱快速索引未正确更新或空拓扑仍执行邻格查询")
    near(data.none.strength, 0, "没有风箱时仍有鼓风")
    near(data.none.rate, 50, "没有风箱时改变了基础效率")
    near(data.single.strength, 1, "额定转速没有达到满风量")
    near(data.single.rate, 60, "单台风箱没有给50%效率加10个百分点")
    near(data.multiple.strength, 1, "多台风箱叠加了温度风量")
    near(data.multiple.rate, 70, "多台风箱没有按加法叠加效率")
    near(data.partial.strength, 1, "半速风箱改变了已有满速风箱的温度效果")
    near(data.partial.rate, 65, "半速风箱没有按比例增加5个百分点")
    near(data.reversed.rate, 65, "反转时没有按实际转速绝对值计算风量")
    near(data.overspeed.rate, 70, "超过额定转速后单台加成没有封顶")
    near(data.stopped.rate, 60, "停转风箱仍提供效率加成")
    near(data.misdirected.rate, 60, "出风口未对准炉体仍提供加成")
    near(data.inactive.rate, 60, "休眠网络仍提供效率加成")
    near(data.rejectedRate, 50, "不接受鼓风的炉体仍获得加成")
    near(data.otherBaseRate, 120, "100%基础效率没有按加法增加20个百分点")
    near(data.combined.strength, 1, "扩展来源与风箱叠加了温度风量")
    near(data.combined.rate, 75, "扩展鼓风来源没有累加自己的效率加成")
    near(data.cleared.rate, 50, "移除全部来源后残留效率加成")
    assert(math.abs(data.single.materialTemperature - 500.3) < .0001, "60%实际材料受热倍率没有生效")
    assert(math.abs(data.multiple.materialTemperature - 500.35) < .0001, "70%实际材料受热倍率没有生效")
    near(data.configuredBonus, 10, "正式机械风箱没有配置10个百分点加成")
end, "风箱效率加法叠加、温度取最大、风量比例、停转朝向与来源撤除")

test.system("mechanical", "conveyor_side_ports", {}, function(data)
    local function port(sample, mask, changed, label)
        assert(sample.mask == mask, label .. "：侧轴连接方向错误")
        assert(sample.changed == changed, label .. "：侧轴变化没有正确通知表现层")
    end
    port(data.empty, 0, true, "未接轴")
    port(data.unchanged, 0, false, "无变化重建")
    port(data.rightAdded, 1, true, "后放右轴且没有动力")
    port(data.bothAdded, 5, true, "两侧独立接轴")
    port(data.rightRemoved, 4, true, "拆右轴只收回右侧")
    port(data.misaligned, 0, true, "邻轴旋转错向")
    port(data.aligned, 4, true, "邻轴恢复朝向")
    port(data.disengaged, 0, true, "离合器断开")
    port(data.engaged, 4, true, "离合器重新接合")
    port(data.rpmChanged, 4, false, "转速和反转不改变连接")
    port(data.noPort, 0, true, "邻物没有机械端口")
    port(data.shaftFirst, 1, true, "先放轴再放传送带")
    port(data.bridge, 1, false, "有效的一层跨轴器")
    port(data.wrongLayer, 0, true, "两边同为一层时不能连接")
    port(data.parallel, 0, false, "侧边平行传送带不能当作连接轴")
    assert(data.pathNeighbour.mask == 0 and data.pathLinked, "带端连接被错误画成侧轴")
    assert(data.rightAdded.route == data.empty.route and data.rightAdded.changes == 1,
        "只有侧轴变化时漏刷新，或重复登记同一传送带")
    assert(data.rotations[1] == 10 and data.rotations[2] == 5 and
        data.rotations[3] == 10 and data.rotations[4] == 5, "旋转后的侧轴方向不正确")
    assert(data.wrappedMask == 1 and data.wrappedChanged, "跨区块的环绕邻格没有通知连接变化")
    assert(data.curveHidden, "改变了拐角隐藏侧轴图片的既有规则")
    near(data.portOuterEdge, .5, "铁环没有伸到格边")
    near(data.retractedOffset, .1, "断开后没有恢复短接口")
    near(data.shaftStart, 0, "木芯没有藏入带框")
    near(data.shaftEnd, .5, "木芯与相邻格之间仍有空隙")
    near(data.shaftHeight, 28 / 128, "木芯厚度不匹配标准传动轴")
    near(data.shaftCenterY, 0, "非中心Pivot让木芯偏离连接轴线")
end, "传送带侧轴按真实图边伸缩：装拆、旋转、断开、环绕与接缝几何")

-- 两种载体使用同一运动内核，来源不再各自写位置。
for _, belt in ipairs({false, true}) do
    local label = belt and "传送带" or "水流"
    for _, case in ipairs({{force = 0, x = .2}, {force = 1, x = .4},
        {force = -1, x = 0}, {force = -2, x = -.2}}) do
        test.system("mechanical", "loose_item_motion", {belt = belt, water = not belt, forceX = case.force}, function(data)
            near(data.x, case.x, "相同外力没有叠加、抵消或反向推动")
            near(data.splitX, case.x, "同样的总时间分步后速度改变")
            near(data.y, 0, "平行外力产生侧向位移")
        end, label .. "共用推动：" .. case.force)
    end
    test.system("mechanical", "loose_item_motion", {belt = belt, water = not belt, forceY = 3, seconds = .1}, function(data)
        assert(data.y > .1, "侧向外力被载体自己的位置提交覆盖")
    end, label .. "可受侧向外力改变方向")
    for _, args in ipairs({{seconds = 0}, {seconds = -.2}, {invalidTime = true}}) do
        args.belt = belt
        test.system("mechanical", "loose_item_motion", args, function(data)
            near(data.x, 0, "无效时间仍移动")
            near(data.moves, 0, "无效时间仍查询阻挡")
        end, label .. "拒绝无效时间")
    end
    test.system("mechanical", "loose_item_motion", {belt = belt, invalidForce = true}, function(data)
        near(data.x, .2, "非法外力污染有效的载体运动")
    end, label .. "非法外力隔离")
    test.system("mechanical", "loose_item_motion", {belt = belt, missing = true}, function(data)
        near(data.x, 0, "缺失地形仍向前移动")
    end, label .. "未加载区域停步")
    test.system("mechanical", "loose_item_motion", {belt = belt, speed = 5, wall = .35}, function(data)
        assert(data.x <= .35 and data.x >= .2, "高速小步阻挡穿墙或提前停步")
    end, label .. "共享高速阻挡处理")
end

test.system("mechanical", "loose_item_motion", {water = true, shore = .15}, function(data)
    assert(data.x < .15, "只有水流时把物品推进了陆地")
end, "水流自身受岸线约束")
test.system("mechanical", "loose_item_motion", {water = true, shore = .15, forceX = 2}, function(data)
    assert(data.x >= .15, "水流岸线错误限制了独立外力")
end, "额外推动力可以把漂浮物送上岸")
test.system("mechanical", "loose_item_motion", {water = true, submerged = true, forceX = 10}, function(data)
    near(data.x, .2, "完全沉没后仍受到表面外力")
end, "完全沉没只保留水流推动")
test.system("mechanical", "loose_item_motion", {wrap = 1, x = .95}, function(data)
    near(data.x, .15, "环绕跨缝没有使用最短位移")
end, "统一运动保留环绕世界")
test.system("mechanical", "loose_item_motion", {belt = true, curve = true, x = -.5}, function(data)
    near(data.x, -.5 + math.sin(.4) * .5, "弯带路径横向位置错误")
    near(data.y, .5 - math.cos(.4) * .5, "弯带路径纵向位置错误")
    near(data.splitX, data.x, "弯带分步结果不一致")
end, "统一运动保留真实圆弧转弯")
test.system("mechanical", "loose_item_motion", {belt = true, curve = true, y = .5, speed = -1}, function(data)
    near(data.x, -.5 + math.sin(math.pi / 2 - .4) * .5, "弯带反转横向位置错误")
    near(data.y, .5 - math.cos(math.pi / 2 - .4) * .5, "弯带反转纵向位置错误")
end, "统一运动保留弯带反转")
test.system("mechanical", "loose_item_terrain_batch", {}, function(data)
    local function near(actual, expected, label)
        assert(math.abs(actual - expected) < .0001, label .. "：" .. tostring(actual))
    end
    assert(data.initialDry and data.wet and data.covered and data.uncovered and data.drained,
        "散物同轮采样没有实时响应入水、遮水、揭开与抽干")
    assert(data.nextDry and data.nextWet and data.nextDrained, "跨区块后复用了旧水量")
    near(data.movedX, .35, "可走地块移动")
    near(data.blockedX, .25, "新增墙体即时阻挡")
    near(data.wetBlockedX, .35, "有水格保留原来的散物通行规则")
    near(data.seamX, -3.95, "散物跨环绕接缝")
    near(data.seamDeltaX, .1, "接缝最短位移")
    near(data.independentMotionX, 4.05, "运动拓扑不能被地形查询的环绕域覆盖")
    near(data.missingY, 1.95, "未加载地形拒绝推进")
    assert(not data.evictedWater, "已卸载地形仍被识别为水域")
    near(data.evictedX, .25, "区块卸载后禁止移动")
    near(data.reloadedX, .35, "同坐标重新加载后恢复移动")
    near(data.oldEpochX, .25, "旧世界代际不能继续推进")
end, "散物推进复用地形寻址并实时读取水态、阻挡、环绕与世界代际")

test.system("mechanical", "loose_item_sources", {}, function(data)
    near(data.combinedX, 1, "第一个来源丢失")
    near(data.combinedY, 2, "第二个来源覆盖了第一个来源")
    near(data.removedX, 1, "释放来源时误删其他来源")
    near(data.removedY, 0, "已释放的来源仍在生效")
    near(data.cleared, 0, "全部释放后残留推动")
    near(data.selfOnceY, 1, "回调内撤销破坏本次采样")
    near(data.selfTwice, 0, "回调撤销后再次参与采样")
    assert(data.duplicate == "InvalidOperationException", "重复来源没有被拒绝")
end, "推动来源组合、撤销、重复标识与自撤销")

for rotation = 0, 3 do
    for _, mirror in ipairs({ false, true }) do
        for _, upper in ipairs({ false, true }) do
            test.system("mechanical", "conveyor_loads", {
                rotation = rotation, mirror = mirror, upper = upper, manual = upper
            }, function(data)
                assert(data.networks == 1 and data.curved, "截图布局未构成含拐角的完整机械网")
                for _, state in ipairs({ data.forward, data.reverse }) do
                    near(state.supply, 20, "不得通过抬高手摇供给掩盖负载问题")
                    near(state.demand, 10, "五段带面的两点负载被错误放大")
                    near(math.abs(state.lowerRpm), 20, "下方手摇曲柄转速错误")
                    near(math.abs(state.upperRpm), 20, "上方手摇曲柄没有跟转")
                    for _, belt in ipairs(state.belts) do
                        assert(belt.flow and belt.satisfied, "拐角或下游没有分配到完整负载")
                        near(belt.demand, 2, "面板与实际每段两点负载不一致")
                        near(math.abs(belt.rpm), 20, "带面没有得到运行转速")
                        assert(belt.moved > .02, "真实直线或圆弧路径没有前进")
                    end
                end
                for i, belt in ipairs(data.forward.belts) do
                    near(data.reverse.belts[i].rpm, -belt.rpm, "反转没有沿同一条带路传播")
                    near(data.stopped.belts[i].rpm, 0, "断供后仍强行运行")
                    near(data.stopped.belts[i].moved, 0, "断供后运输仍在推进")
                end
                near(data.coarseLoad.demand, 50, "旧十点档故障复现失效")
                assert(data.coarseLoad.belts[3].flow and not data.coarseLoad.belts[3].satisfied,
                    "故障样本应为机械已连通而拐角负载不足")
                near(data.coarseLoad.belts[3].rpm, 0, "旧故障拐角应停转")
                near(math.abs(data.coarseLoad.upperRpm), 20, "旧故障中上方曲柄仍应跟转")
                near(data.budgetDemand, 22, "十一段传送带总负载错误")
                assert(data.satisfiedCount == 10, "真实过载时应只带动最近十段")
                near(data.lastRpm, 0, "动力不足的第十一段仍运行")
                near(data.ordinaryDemand, 10, "普通机器原十点档被意外修改")
                near(data.fractionalDemand, .25, "自定义小数档位无效")
                for _, failure in ipairs(data.invalidSteps) do
                    assert(failure == "ArgumentException", "非法负载精度未拒绝")
                end
            end, "传送带小负载贯通拐角：朝向" .. rotation .. " 镜像" .. tostring(mirror) .. " 上方供能" .. tostring(upper))
        end
    end
end
