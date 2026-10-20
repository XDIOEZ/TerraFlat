test.system("inventory", "dynamic_slot_maintenance", {}, function(data)
    assert(data.initialScan and data.stableSkipped and data.initialCount == 195,
        "创造背包初次整理错误，或256次稳定Tick仍扫描全部槽位")
    assert(data.removedScan and data.afterRemoveCount == 194 and data.removedStableSkipped,
        "取走占用槽位后没有同步回收多余空格，或稳定帧仍重扫")
    assert(data.refilledScan and data.afterRefillCount == 195,
        "空槽重新填充后没有立即补足三个备用空格")
    assert(data.replacedSlotScan and data.replacedStableSkipped and data.notifiedScan,
        "同数量替换槽位或显式结构通知没有使缓存失效")
    assert(not data.disabledScan and data.reenabledScan and data.finalCount == 195
        and data.occupied == 192 and data.indexed,
        "容量模式切换后没有重检，或丢失物品/槽位索引")
end, "创造背包动态槽位稳定帧免全表扫描与结构变化复核")

test.system("inventory", "incremental_mass_binding", {}, function(data)
    assert(data.replaced and data.oldDetached and data.newAttached and data.otherAttached,
        "单格替换没有只更新原物品与新物品的质量监听，或影响了其它槽位")
    assert(data.cleared and data.clearedDetached and data.restored,
        "槽位置空与恢复后的质量监听或结构版本错误")
    assert(data.layoutRebound and data.oldNeighborDetached and data.newNeighborAttached,
        "同数量换槽后没有全量同步新的质量监听，或旧槽仍保留订阅")
    assert(data.totalSlots == 128 and data.structureEvents == 4 and data.massEvents == 7,
        "批量槽位中的结构与质量事件次数不符合真实变更")
end, "大库存单格增量质量绑定与换槽重建事件一致性")

test.system("inventory", "carry_usage_single_pass", {}, function(data)
    assert(data.slotCount == 128 and data.initiallyEqual and
        data.initialWeight == 192 and data.initialVolume == 256,
        "单轮汇总没有与逐项重量、体积保持一致")
    assert(data.amountUpdated and data.volumeUpdated and data.replaced,
        "物品数量、直接体积变更或换槽后使用了过期的携带量")
    assert(data.capacityLimited and data.replacementAccepted and data.replacementRejected,
        "合并扫描改变了容量限额或换物门禁")
    assert(data.unlimited and data.empty,
        "无限容量绕过或空库存读取没有保留原有边界")
end, "大库存重量体积同轮汇总与动态变更实时读取")

test.system("inventory", "readable_book_snapshot", {}, function(data)
    assert(data.written and data.failure == "", "初始书页没有写入正式模块载荷")
    assert(data.addedPages == 2 and #data.pages == 3 and data.materialSlots == 2,
        "超过静态页数的书页或空材料槽没有保存")
    assert(data.guid == 101 and not data.noPlayerAccepted, "书籍库存快照丢失身份或接受未就绪玩家")
    assert(not data.emptyAccepted and not data.nullPageAccepted and data.rejectedUnchanged,
        "无效内容被接受或覆盖了已写入的书页")
    local expected = {
        {title = "聊天命令", body = "/carryweight 5000\n/carryvolume 5000"},
        {title = "管理员操作", body = "F4：打开 GM 面板。"},
        {title = "说明", body = "中文与换行\n完整保留。"}
    }
    for index, page in ipairs(data.pages) do
        assert(page.index == index - 1 and page.title == expected[index].title and page.body == expected[index].body
            and page.customTitle and page.customBody, "书籍快照往返丢失顺序、标题、正文或覆盖标记")
    end
    assert(data.independentBody == expected[1].body, "调用方或另一本书改动污染了本书内容")
end, "命令手册：独立书页、扩页状态和正式库存快照往返")

test.system("inventory", "readable_book_snapshot", {withMaterial = true}, function(data)
    assert(data.written and data.failure == "" and data.materialSlots == 2,
        "带书写材料的书页快照没有成功写入")
    assert(data.materialId == "Charcoal" and data.materialAmount == 1,
        "书写槽未保存独立的一份木炭")
    assert(data.paperEmpty and #data.pages == 3 and data.rejectedUnchanged,
        "随书材料污染了纸张槽、书页或无效请求的拒绝结果")
end, "命令手册：书写槽自带一份木炭并随书籍快照往返")

for _, mode in ipairs({"success", "full", "weight", "volume", "deposit", "tags", "slot_limit", "pending"}) do
    test.system("inventory", "readable_book_grant", {mode = mode}, function(data)
        local accepted = mode == "success"
        assert(data.inserted == accepted and data.received == (accepted and 1 or 0),
            "发书没有严格按空槽、完整携带容量、入库门禁或联机等待状态决定是否提交")
        assert(data.existingUnchanged and data.slotsUnchanged and data.amount == 1,
            "发书修改了已有物品、扩容或改变了一本书的数量")
        if accepted then
            assert(data.sameBook and not data.pickable and data.changed == 1 and data.refreshed == 1,
                "发书没有通过真实库存事务移动该本物品并刷新状态")
        else
            assert(data.failure ~= "" and data.pickable and data.changed == 0 and data.refreshed == 0,
                "拒绝发书仍修改源物品、发布库存事件或没有失败原因")
        end
    end, "命令手册发放边界：" .. mode)
end

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
-- 玩家门槛以现有手感为 100%，纯净化不写偏好，轨迹通过正式偏好和识别器。
for _, row in ipairs({
    {value = -10, expected = 25}, {value = 25, expected = 25},
    {value = 100, expected = 100}, {value = 200, expected = 200},
    {value = 500, expected = 200}, {value = 73.4, expected = 73},
    {value = 73.6, expected = 74}, {invalid = "nan", expected = 100},
    {invalid = "positive_infinity", expected = 100}, {invalid = "negative_infinity", expected = 100}
}) do
    test.system("inventory", "flick_settings_normalize", row, function(data)
        assert(data.value == row.expected, "甩动门槛净化没有限制到整数百分比或非有限值未回默认")
        assert(data.minimum == 25 and data.maximum == 200 and data.default == 100 and data.step == 1
            and data.defaultAngleLimit, "甩动选项的范围、默认值或步长偏离约定")
    end, "甩动百分比净化：" .. (row.invalid or tostring(row.value)))
end

for _, row in ipairs({
    {speedPercent = 25, distancePercent = 25, distance = 20, time = 0.1, accepted = true},
    {speedPercent = 100, distancePercent = 25, distance = 20, time = 0.1, accepted = false},
    {speedPercent = 25, distancePercent = 100, distance = 20, time = 0.1, accepted = false},
    {speedPercent = 25, distancePercent = 100, distance = 50, time = 0.2, accepted = true},
    {speedPercent = 100, distancePercent = 100, distance = 60, time = 0.1, accepted = true},
    {speedPercent = 200, distancePercent = 100, distance = 60, time = 0.1, accepted = false},
    {speedPercent = 100, distancePercent = 200, distance = 60, time = 0.1, accepted = false},
    {speedPercent = 200, distancePercent = 200, distance = 120, time = 0.1, accepted = true},
    {speedPercent = 25, distancePercent = 200, distance = 90, time = 0.4, accepted = true}
}) do
    local args = {speedPercent = row.speedPercent, distancePercent = row.distancePercent,
        points = {{0, 0, 0}, {row.time / 2, row.distance / 2, 0}, {row.time, row.distance, 0}}}
    test.system("inventory", "flick_settings_gesture", args, function(data)
        assert(data.accepted == row.accepted, "速度与距离滑条未独立影响正式甩动判定")
        assert(math.abs(data.minimumSpeed - (800 / 1.5) * row.speedPercent / 100) < 0.001
            and math.abs(data.minimumDistance - (64 / 1.5) * row.distancePercent / 100) < 0.001,
            "玩家百分比没有基于现有默认手感缩放")
        assert(data.preferencesRestored, "甩动设置轨迹夹具未恢复玩家缓存或偏好键")
    end, "甩动滑条独立生效：速度" .. row.speedPercent .. "%/距离" .. row.distancePercent .. "%/位移" .. row.distance)
end

test.system("inventory", "flick_settings_gesture", {speedPercent = 25, distancePercent = 25,
    points = {{0, 0, 0}, {0.05, 20, 0}, {0.25, 20, 0}}}, function(data)
    assert(not data.accepted and data.preferencesRestored, "低门槛绕过了松手前停住规则或留下测试偏好")
end, "低门槛仍拒绝快速拖动后停住")

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
        if case.args.playerBag then assert(data.slots == 18, "生存背包基础格数错误") end
    end, "库存容量与身份：" .. case.name)
end
