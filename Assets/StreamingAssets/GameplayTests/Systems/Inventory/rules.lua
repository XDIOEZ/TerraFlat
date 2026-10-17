local pickup_cases = {
    {name = "开关关闭保持原数量", args = {enabled = false}, first = 3, second = 0, remaining = 0, accepted = true},
    {name = "材料入包十倍", args = {}, first = 30, second = 0, remaining = 0, accepted = true},
    {name = "不可堆叠工具不复制", args = {amount = 1, stackable = false}, first = 1, second = 0, remaining = 0, accepted = true},
    {name = "一倍保持原数量", args = {multiplier = 1}, first = 3, second = 0, remaining = 0, accepted = true},
    {name = "倍率下限", args = {multiplier = 0}, first = 3, second = 0, remaining = 0, accepted = true, applied = 1},
    {name = "倍率上限", args = {multiplier = 1000}, first = 300, second = 0, remaining = 0, accepted = true, applied = 100},
    {name = "两个库存分摊同一次倍率", args = {firstCapacity = 6, secondCapacity = 24, allowPartial = false}, first = 6, second = 24, remaining = 0, accepted = true},
    {name = "不切开原物品且重试不重复放大", args = {firstCapacity = 15, retry = true}, first = 10, second = 0, remaining = 2, accepted = true},
    {name = "一份倍率也放不下则不拾取", args = {firstCapacity = 9}, first = 0, second = 0, remaining = 3, accepted = false},
    {name = "满包拒绝不改变地面数量", args = {firstCapacity = 0}, first = 0, second = 0, remaining = 3, accepted = false},
    {name = "倍率保留重量上限", args = {maxWeight = 15}, first = 10, second = 0, remaining = 2, accepted = true},
    {name = "快捷栏不能绕过主背包体积", args = {firstCapacity = 6, secondCapacity = 1000, maxVolume = 15}, first = 6, second = 4, remaining = 2, accepted = true},
    {name = "整组授权失败无部分入包", args = {firstCapacity = 15, allowPartial = false}, first = 0, second = 0, remaining = 3, accepted = false},
    {name = "重复目标不虚增可用容量", args = {firstCapacity = 6, duplicateTarget = true}, first = 0, second = 0, remaining = 3, accepted = false}
}
for _, case in ipairs(pickup_cases) do
    test.system("inventory", "pickup_multiplier", case.args, function(data)
        assert(data.accepted == case.accepted and data.firstAmount == case.first and data.secondAmount == case.second,
            "拾取倍率、容量预检或跨库存分配错误")
        assert(data.remaining == case.remaining and data.pickable == (case.remaining > 0),
            "失败或部分拾取改变了地面原数量/可拾取状态")
        if case.applied then assert(data.appliedMultiplier == case.applied, "倍率未限制在1至100") end
        if case.args.retry then
            assert(not data.blockedRetry and data.afterBlockedRetry == 10, "无容量重试再次发放了物品")
            assert(data.retryAccepted and data.afterRetry == 30 and data.remainingAfterRetry == 0,
                "剩余两份没有只按原数量再次结算，或拾取倍率被重复应用")
        end
    end, "拾取倍率：" .. case.name)
end

local partial_pickups = {
    {name = "十根原木按体积捡三留七", args = {maxVolume = 36, unitWeight = 3, unitVolume = 12}, first = 3, second = 0, remaining = 7},
    {name = "十根原木按重量捡三留七", args = {maxWeight = 9, unitWeight = 3, unitVolume = 12}, first = 3, second = 0, remaining = 7},
    {name = "快捷栏和背包共同容纳三根", args = {firstCapacity = 2, secondCapacity = 1}, first = 2, second = 1, remaining = 7},
    {name = "只有槽位能容纳三根", args = {firstCapacity = 3}, first = 3, second = 0, remaining = 7},
    {name = "不足一根不切碎原木", args = {maxVolume = 11, unitVolume = 12}, first = 0, second = 0, remaining = 10},
    {name = "小数槽容量只接收整根", args = {firstCapacity = 2.5}, first = 2, second = 0, remaining = 8},
    {name = "零容量保持原堆", args = {firstCapacity = 0}, first = 0, second = 0, remaining = 10},
    {name = "入库门禁保持原堆", args = {blocked = true}, first = 0, second = 0, remaining = 10},
    {name = "整组授权可跨快捷栏和背包", args = {firstCapacity = 2, secondCapacity = 8, allowPartial = false}, first = 2, second = 8, remaining = 0},
    {name = "整组授权容量不足零提交", args = {firstCapacity = 2, secondCapacity = 1, allowPartial = false}, first = 0, second = 0, remaining = 10}
}
for _, case in ipairs(partial_pickups) do
    case.args.enabled = false
    case.args.amount = 10
    test.system("inventory", "pickup_multiplier", case.args, function(data)
        assert(data.accepted == (case.remaining < 10) and data.firstAmount == case.first and data.secondAmount == case.second,
            "普通拾取没有按真实重量、体积和槽位接收最大整数数量")
        assert(data.remaining == case.remaining and data.pickable == (case.remaining > 0), "地面余量或可拾取状态错误")
        assert(data.consumed == 10 - case.remaining and data.added == case.first + case.second
            and data.firstAmount + data.secondAmount + data.remaining == 10, "拾取回执或数量守恒错误")
        assert(data.preflightUnchanged, "容量预检改动了世界源物品或触发库存事件")
        for _, amount in ipairs(data.sourceDuringCommit) do
            assert(amount == 10, "库存接收期间临时篡改了地面原物品数量")
        end
        assert(#data.sourceMassChanges == (case.remaining > 0 and case.remaining < 10 and 1 or 0),
            "源物品数量在提交前被反复改写或拒绝拾取仍产生质量变化")
    end, "普通部分拾取：" .. case.name)
end

test.system("inventory", "discard_hover_query", {}, function(data)
    assert(data.idleQueries == 0, "空闲60帧仍然为丢弃扫描UI")
    assert(data.firstQueries == 1 and data.switchedQueries == 2, "实际丢弃没有及时查询当前UI落点")
    assert(data.blockedReads == 2, "单次丢弃绕过了首个UI遮挡命中")
    assert(data.repeatedQueries == 4 and data.finalQueries == 5, "重复丢弃没有在实际动作时重新查询")
    assert(#data.reads == 4 and data.reads[1] == 1 and data.reads[2] == 2 and
        data.reads[3] == 1 and data.reads[4] == 2, "单次或重复丢弃使用了过期悬停槽位")
end, "丢弃按需悬停：空闲零扫描、遮挡与重复动作实时目标")

local cases = {
    {name = "扣除后整堆移交", args = {}, source = 0, target = 3, transferred = true},
    {name = "部分堆叠转移", args = {transfer = 1}, source = 2, target = 1, transferred = true},
    {name = "关闭入库门禁", args = {blocked = true}, source = 3, target = 0, transferred = false},
    {name = "目标槽容量限量", args = {slotCapacity = 1}, source = 2, target = 1, transferred = true},
    {name = "目标无容量", args = {slotCapacity = 0}, source = 3, target = 0, transferred = false}
}
for _, case in ipairs(cases) do
    test.system("inventory", "transactions", case.args, function(data)
        assert(data.consumed and data.afterConsume == 3, "没有通过真实库存扣数量")
        assert(not data.excessiveAccepted and not data.foreignAccepted, "超额或外来槽扣料被接受")
        assert(data.transferred == case.transferred and data.sourceAmount == case.source and data.targetAmount == case.target, "转移结果错误")
        assert(data.sourceAmount + data.targetAmount == 3 and data.totalWeight == 3, "跨库存数量或质量不守恒")
        assert(data.preflightUnchanged and data.preflight == case.target and data.foreignPreflight == 0,
            "只读预检改变数量/发布事件，或容量结果与正式转移不一致/接受外来槽")
    end, "库存事务：" .. case.name)
end
local directions = {
    {name = "向右选右侧输入", origin = {0, 0}, direction = {1, 0}, targets = {{100, 0}, {0, -100}}, selected = 0},
    {name = "向下选快捷栏", origin = {0, 0}, direction = {0, -1}, targets = {{100, 0}, {0, -100}}, selected = 1},
    {name = "斜下更近下方夹角", origin = {0, 0}, direction = {0.3, -1}, targets = {{100, 0}, {0, -100}}, selected = 1},
    {name = "角度优先于距离", origin = {0, 0}, direction = {1, 0}, targets = {{1000, 0}, {1, -1}}, selected = 0},
    {name = "使用按下位置而非屏幕原点", origin = {800, 600}, direction = {0, -1}, targets = {{1200, 600}, {800, 100}}, selected = 1},
    {name = "同角度保留稳定顺序", origin = {0, 0}, direction = {1, 0}, targets = {{1000, 0}, {10, 0}}, selected = 0},
    {name = "中心排序保留背向候选，窗口资格另测", origin = {0, 0}, direction = {1, 0}, targets = {{-100, 0}}, selected = 0},
    {name = "零方向拒绝", origin = {0, 0}, direction = {0, 0}, targets = {{100, 0}}, selected = -1},
    {name = "目标与起点重合拒绝", origin = {0, 0}, direction = {1, 0}, targets = {{0, 0}}, selected = -1},
    {name = "非有限方向拒绝", invalid = "nan", origin = {0, 0}, direction = {1, 0}, targets = {{100, 0}}, selected = -1},
    {name = "非有限起点拒绝", invalid = "infinity", origin = {0, 0}, direction = {1, 0}, targets = {{100, 0}}, selected = -1}
}
for _, case in ipairs(directions) do
    test.system("inventory", "flick_direction", case, function(data)
        assert(data.selected == case.selected and not data.nullSourceAccepted, "方向夹角排序或无效输入拒绝错误")
    end, "手机甩动方向：" .. case.name)
end
local function angle_direction(degrees)
    local radians = degrees * math.pi / 180
    return {math.cos(radians), math.sin(radians)}
end
local window_angles = {
    {name = "正对上方容器", origin = {0, 0}, direction = {0, 1}, bounds = {-100, 100, 200, 100}, accepted = true},
    {name = "射线穿过容器边角", origin = {0, 0}, direction = {1, 1}, bounds = {-100, 100, 200, 100}, accepted = true},
    {name = "右边缘额外十度", origin = {0, 0}, direction = angle_direction(35), bounds = {-100, 100, 200, 100}, accepted = true},
    {name = "右边缘超过十度", origin = {0, 0}, direction = angle_direction(34.9), bounds = {-100, 100, 200, 100}, accepted = false},
    {name = "左边缘额外十度", origin = {0, 0}, direction = angle_direction(145), bounds = {-100, 100, 200, 100}, accepted = true},
    {name = "左边缘超过十度", origin = {0, 0}, direction = angle_direction(145.1), bounds = {-100, 100, 200, 100}, accepted = false},
    {name = "宽容器允许偏斜", origin = {0, 0}, direction = angle_direction(70), bounds = {-100, 100, 200, 100}, accepted = true},
    {name = "窄容器同方向范围外", origin = {0, 0}, direction = angle_direction(70), bounds = {-10, 100, 20, 100}, accepted = false},
    {name = "高容器允许偏斜", origin = {0, 0}, direction = angle_direction(20), bounds = {100, -100, 100, 200}, accepted = true},
    {name = "矮容器同方向范围外", origin = {0, 0}, direction = angle_direction(20), bounds = {100, -10, 100, 20}, accepted = false},
    {name = "完全背向拒绝", origin = {0, 0}, direction = {0, -1}, bounds = {-100, 100, 200, 100}, accepted = false},
    {name = "正侧向拒绝", origin = {0, 0}, direction = {1, 0}, bounds = {-100, 100, 200, 100}, accepted = false},
    {name = "负轴上侧跨一百八十度", origin = {0, 0}, direction = angle_direction(179), bounds = {-200, -20, 100, 40}, accepted = true},
    {name = "负轴下侧跨负一百八十度", origin = {0, 0}, direction = angle_direction(-179), bounds = {-200, -20, 100, 40}, accepted = true},
    {name = "起点在窗口内允许任意方向", origin = {50, 50}, direction = {-1, 0}, bounds = {0, 0, 100, 100}, accepted = true},
    {name = "起点在窗口边界允许向外", origin = {0, 0}, direction = {-1, -1}, bounds = {0, 0, 100, 100}, accepted = true},
    {name = "使用真实按下点", origin = {800, 600}, direction = {0, -1}, bounds = {700, 200, 200, 100}, accepted = true},
    {name = "零方向拒绝", origin = {0, 0}, direction = {0, 0}, bounds = {-100, 100, 200, 100}, accepted = false},
    {name = "无宽度窗口拒绝", origin = {0, 0}, direction = {0, 1}, bounds = {0, 100, 0, 100}, accepted = false},
    {name = "负尺寸窗口拒绝", origin = {0, 0}, direction = {0, 1}, bounds = {0, 100, -10, 100}, accepted = false},
    {name = "非有限方向拒绝", invalid = "nan", origin = {0, 0}, direction = {0, 1}, bounds = {-100, 100, 200, 100}, accepted = false},
    {name = "非有限起点拒绝", invalid = "infinity", origin = {0, 0}, direction = {0, 1}, bounds = {-100, 100, 200, 100}, accepted = false},
    {name = "非有限窗口坐标拒绝", invalid = "bounds_nan", origin = {0, 0}, direction = {0, 1}, bounds = {-100, 100, 200, 100}, accepted = false},
    {name = "非有限窗口尺寸拒绝", invalid = "bounds_infinity", origin = {0, 0}, direction = {0, 1}, bounds = {-100, 100, 200, 100}, accepted = false}
}
for _, case in ipairs(window_angles) do
    test.system("inventory", "flick_direction", case, function(data)
        assert(data.angleAccepted == case.accepted and data.edgeTolerance == 10, "实际窗口范围或边缘十度限制错误")
        assert(not data.nullSourceAccepted, "无来源库存通过了方向转移")
    end, "甩动窗口角度：" .. case.name)
end
local flicks = {
    {name = "快速甩动", points = {{0, 0, 0}, {0.04, 48, 0}, {0.08, 96, 0}}, accepted = true},
    {name = "距离不足", points = {{0, 0, 0}, {0.02, 20, 0}, {0.04, 40, 0}}, accepted = false},
    {name = "距离刚低于新阈值", points = {{0, 0, 0}, {0.04, 42, 0}}, accepted = false},
    {name = "距离刚过新阈值", points = {{0, 0, 0}, {0.04, 43, 0}}, accepted = true},
    {name = "原短距离现可甩动", points = {{0, 0, 0}, {0.02, 24, 0}, {0.04, 48, 0}}, accepted = true},
    {name = "原中速动作现可甩动", points = {{0, 0, 0}, {0.08, 48, 0}, {0.16, 96, 0}}, accepted = true},
    {name = "速度不足", points = {{0, 0, 0}, {0.08, 40, 0}, {0.16, 80, 0}}, accepted = false},
    {name = "速度刚低于新阈值", points = {{0, 0, 0}, {0.1, 53, 0}}, accepted = false},
    {name = "速度刚过新阈值", points = {{0, 0, 0}, {0.1, 54, 0}}, accepted = true},
    {name = "末段速度不足仍拒绝", points = {{0, 0, 0}, {0.04, 50, 0}, {0.08, 60, 0}}, accepted = false},
    {name = "末段速度同步降低", points = {{0, 0, 0}, {0.04, 50, 0}, {0.08, 61, 0}}, accepted = true},
    {name = "长距离慢拖", points = {{0, 0, 0}, {0.5, 100, 0}, {1, 200, 0}}, accepted = false},
    {name = "快速移动后停住", points = {{0, 0, 0}, {0.04, 96, 0}, {0.12, 96, 0}}, accepted = false},
    {name = "手指轻微移动不冒充继续甩动", points = {{0, 0, 0}, {0.04, 96, 0}, {0.13, 97, 0}}, accepted = false},
    {name = "原地抖动", points = {{0, 0, 0}, {0.02, 50, 0}, {0.04, -50, 0}, {0.06, 10, 0}}, accepted = false},
    {name = "来回划动", points = {{0, 0, 0}, {0.02, 150, 0}, {0.04, 10, 0}, {0.06, 100, 0}}, accepted = false},
    {name = "负向甩动", points = {{0, 0, 0}, {0.04, -48, 0}, {0.08, -96, 0}}, accepted = true},
    {name = "斜向甩动", points = {{0, 0, 0}, {0.04, 36, 36}, {0.08, 72, 72}}, accepted = true},
    {name = "按住后再甩", points = {{0, 0, 0}, {0.5, 0, 0}, {0.54, 48, 0}, {0.58, 96, 0}}, accepted = true},
    {name = "缩放两倍仍识别相同动作", scale = 2, points = {{0, 0, 0}, {0.04, 96, 0}, {0.08, 192, 0}}, accepted = true},
    {name = "放大界面不放宽距离", scale = 2, points = {{0, 0, 0}, {0.02, 40, 0}, {0.04, 80, 0}}, accepted = false},
    {name = "同帧跳变", points = {{0, 0, 0}, {0, 96, 0}}, accepted = false},
    {name = "取消后不残留", cancel = true, points = {{0, 0, 0}, {0.04, 48, 0}, {0.08, 96, 0}}, accepted = false}
}
for _, case in ipairs(flicks) do
    test.system("inventory", "flick_gesture", case, function(data)
        assert(data.accepted == case.accepted, "甩动距离、末段速度或取消规则错误")
        assert(math.abs(data.minimumDistance - 64 / 1.5) < 0.001 and math.abs(data.minimumSpeed - 800 / 1.5) < 0.001,
            "甩动灵敏度没有按原阈值除以1.5")
    end, "触屏甩动：" .. case.name)
end
local capacities = {
    {name = "容器由体积限制", args = {}, allowed = 3, weight = 10, same = true, stack = true},
    {name = "整数物品不能塞半件", args = {currentVolume = 9}, allowed = 0, weight = 10, same = true, stack = true},
    {name = "重量先达到上限", args = {currentWeight = 8}, allowed = 1, weight = 10, same = true, stack = true},
    {name = "玩家承载宽限", args = {playerBag = true, currentWeight = 10}, allowed = 2, weight = 15, same = true, stack = true},
    {name = "创造容量绕过", args = {unlimited = true}, allowed = 10, weight = 10, same = true, stack = true},
    {name = "制作品质不同不合堆", args = {otherQuality = 2}, allowed = 3, weight = 10, same = false, stack = false},
    {name = "特殊载荷不同不合堆", args = {otherSpecial = "payload"}, allowed = 3, weight = 10, same = false, stack = false},
    {name = "不可堆叠属性独立", args = {otherStackable = false}, allowed = 3, weight = 10, same = true, stack = false}
}
for _, case in ipairs(capacities) do
    test.system("inventory", "capacity_identity", case.args, function(data)
        assert(data.allowed == case.allowed and data.weightLimit == case.weight, "重量体积容量策略错误")
        assert(data.sameIdentity == case.same and data.canStack == case.stack, "堆叠身份错误")
        assert(data.wholeLarge == 2147483647 and data.wholeNegative == 0, "数量转整数边界错误")
        if case.args.playerBag then assert(data.slots == 20, "生存背包基础格数错误") end
    end, "库存容量与身份：" .. case.name)
end
