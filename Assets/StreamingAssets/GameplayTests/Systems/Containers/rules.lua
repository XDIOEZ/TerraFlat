local cases = {
    {name = "半份端口消费", args = {}, consumed = true, after = 4.5, pending = .5, quantized = 2},
    {name = "整份端口消费", args = {consume = 1}, consumed = true, after = 4, pending = 0, quantized = 2},
    {name = "消费不能超过预约", args = {consume = 2}, consumed = false, after = 5, pending = 1, quantized = 2},
    {name = "零消费无变化", args = {consume = 0, quantize = -.5}, consumed = false, after = 5, pending = 1, quantized = 0},
    {name = "负消费无变化", args = {consume = -1, quantize = .5}, consumed = false, after = 5, pending = 1, quantized = 0}
}
for _, case in ipairs(cases) do
    test.system("containers", "composition", case.args, function(data)
        assert(data.initial == 5 and data.reserved and data.repeatReserved, "混液总量或端口预约失败")
        assert(data.sameBatch and data.reserveRandomStable, "反复预约改变批次或消耗随机状态")
        assert(not data.wrongPortAccepted and not data.exceededAccepted, "错误端口或超量提交被接受")
        assert(data.consumed == case.consumed and math.abs(data.afterConsume - case.after) < .00001, "真实组分消费错误")
        assert(math.abs(data.pendingAfter - case.pending) < .00001 and data.afterRejected == data.afterConsume, "预约剩余或拒绝事务改变总量")
        assert(data.restored == 5 and data.restoredPending == 1, "快照恢复遗漏组分或预约")
        assert(data.cloneIndependent and data.restoredWater == 3 and data.restoredOil == 2 and data.restoredBatchServings == 1, "消费修改了快照中的真实组分或批次")
        assert(data.quantized == case.quantized, "整份量化错误")
    end, "容器组分事务：" .. case.name)
end

local rain_cases = {
    {name = "普通雨不足一分钟", steps = {{seconds = 59}}, collected = {0}, progress = {59 / 60}},
    {name = "普通雨一分钟一份", steps = {{seconds = 60}}, collected = {1}, progress = {0}},
    {name = "小雨不足两分钟", steps = {{seconds = 119, intensity = .1}}, collected = {0}, progress = {119 / 120}},
    {name = "小雨两分钟一份", steps = {{seconds = 120, intensity = .1}}, collected = {1}, progress = {0}},
    {name = "大雨不足三十秒", steps = {{seconds = 29, intensity = 1}}, collected = {0}, progress = {29 / 30}},
    {name = "大雨三十秒一份", steps = {{seconds = 30, intensity = 1}}, collected = {1}, progress = {0}},
    {name = "雨量过大仍最快三十秒", steps = {{seconds = 30, intensity = 10}}, collected = {1}, progress = {0}},
    {name = "连续中雨保留不足一份进度", steps = {{seconds = 20}, {seconds = 40}}, collected = {0, 1}, progress = {1 / 3, 0}},
    {name = "雨势改变按真实进度续接", steps = {{seconds = 30}, {seconds = 15, intensity = 1}}, collected = {0, 1}, progress = {.5, 0}},
    {name = "停雨不凭空加水且保留进度", initial = .5, steps = {{seconds = 600, intensity = 0}, {seconds = 30}}, collected = {0, 1}, progress = {.5, 0}},
    {name = "暂停不推进", initial = .5, steps = {{seconds = 0}}, collected = {0}, progress = {.5}},
    {name = "负时间不推进", steps = {{seconds = -60}}, collected = {0}, progress = {0}},
    {name = "负雨量不接水", steps = {{intensity = -1}}, collected = {0}, progress = {0}},
    {name = "非法时间不推进", steps = {{invalid = "nan_seconds"}}, collected = {0}, progress = {0}},
    {name = "非法雨量不接水", steps = {{invalid = "infinite_intensity"}}, collected = {0}, progress = {0}},
    {name = "仅余半份空间不接水", initial = .75, steps = {{space = .5}}, collected = {0}, progress = {0}},
    {name = "满桶不积攒补水债务", initial = .75, steps = {{seconds = 6000, space = 0}, {seconds = 1, space = 8}}, collected = {0, 0}, progress = {0, 1 / 60}},
    {name = "长帧最多装满且丢弃溢出", steps = {{seconds = 6000, space = 2.5}}, collected = {2}, progress = {0}},
    {name = "连续雨量保留半份便于保存", steps = {{seconds = 90}}, collected = {1}, progress = {.5}}
}
for _, case in ipairs(rain_cases) do
    local expected = case.progress
    test.system("containers", "rainwater", {progress = case.initial or 0, steps = case.steps}, function(data)
        assert(data.defaultInterval == 60 and #data.samples == #case.steps, "默认接雨周期或采样数错误")
        for index, sample in ipairs(data.samples) do
            assert(sample.collected == case.collected[index], "接雨整份数量错误")
            assert(math.abs(sample.progress - expected[index]) < .000001, "接雨进度、停雨或溢出处理错误")
        end
        assert(math.abs(data.restoredProgress - expected[#expected]) < .000001, "保存克隆或回滚丢失接雨进度")
    end, "容器自动接雨：" .. case.name)
end

local quality_cases = {
    {name = "无污染层为纯水", args = {}, dirty = false},
    {name = "零脏污为纯水", args = {values = {["core:filth"] = 0}}, dirty = false},
    {name = "微量脏污也为脏水", args = {values = {["core:filth"] = .000001}}, dirty = true},
    {name = "满脏污为脏水", args = {values = {["core:filth"] = 1}}, dirty = true},
    {name = "植物病原污染为脏水", args = {values = {["core:plant_blight"] = .2}}, dirty = true},
    {name = "动物病原污染为脏水", args = {values = {["core:animal_plague"] = .1}}, dirty = true},
    {name = "蚊媒污染为脏水", args = {values = {["core:mosquito_pressure"] = .5}}, dirty = true},
    {name = "扩展污染也参与水质", args = {includeMod = true, values = {["test:mod"] = .01}}, dirty = true}
}
for _, case in ipairs(quality_cases) do
    test.system("containers", "rainwater_quality", case.args, function(data)
        assert(data.liquidId == (case.dirty and "core:dirty_water" or "core:pure_water"), "雨水没有按所在地块污染选水质")
        assert((data.load > 0) == case.dirty and data.neighborLiquidId == "core:pure_water", "污染查询串到了相邻格")
        assert(data.invalidLiquidId == nil, "无法读取的污染状态被当成干净地块")
    end, "容器接雨水质：" .. case.name)
end

for _, id in ipairs({"WoodenBarrel", "IronBucket", "Crucible"}) do
    test.system("containers", "portable_vessels", {id = id}, function(data)
        assert(#data.definitions == 2, "容器缺少手持或落地本体定义")
        for index, definition in ipairs(data.definitions) do
            local building = definition.building
            assert(definition.buildingEnabled and definition.rainEnabled, "容器建筑或接雨能力未启用")
            assert(building.BuildingPrefabId == id .. "_Building" and building.SummonerPrefabId == id,
                "容器的放置和拆回配对错误")
            assert(building.Role == index - 1, "手持或建筑角色错误")
            local shared = false
            for _, module_id in ipairs(building.SharedModuleIds) do
                if module_id == "Mod_WaterVessel" then shared = true end
            end
            assert(shared, "装拆没有保留真实液体模块")
            if index == 1 then assert(definition.requiresPlacement, "手持容器没有显式放置入口") end
            if id == "Crucible" then
                local solid_shared = false
                for _, module_id in ipairs(building.SharedModuleIds) do
                    if module_id == "Mod_Mortar" then solid_shared = true end
                end
                assert(solid_shared and definition.mortarEnabled, "坩埚装拆没有保留固体材料库存")
                if index == 2 then assert(definition.gameObjectBackend, "落地坩埚错误进入不支持组合能力的机器领域") end
            end
        end
        assert(data.amount == 3.5 and data.water == 3 and data.oil == .5 and data.temperature == 18,
            "放置和拆回丢失液体组分或温度")
        assert(data.progress == .75 and data.distinctPayload, "放置和拆回丢失接雨进度或错误共用载荷")
        if id == "Crucible" then
            assert(data.solidAmount == 3 and data.solidTemperature == 700 and data.solidIndependent,
                "坩埚放置或拆回丢失固体数量、材料温度，或共享了同一份载荷")
        end
    end, "便携容器放置配置与状态往返：" .. id)
end

for _, case in ipairs({
    {tilt = 0, center = 100, canSpill = false},
    {tilt = 90, center = 100, canSpill = false},
    {tilt = -90, center = 100, canSpill = false},
    {tilt = 135, center = 99, canSpill = false},
    {tilt = 135, center = 100, canSpill = true},
    {tilt = -135, center = 100, canSpill = true},
    {tilt = 180, center = 101, canSpill = true},
    {tilt = -180, center = 100, canSpill = true},
    {tilt = 360, center = 100, canSpill = false}
}) do
    test.system("containers", "contents_open_mouth", case, function(data)
        assert(data.canSpill == case.canSpill, "真实物品未到开口、直立或横放时也出料，或倒置后仍封口")
    end, string.format("固体容器开口边界：倾角%g，物品高度%g", case.tilt, case.center))
end
test.system("containers", "composition", {water = 0, oil = 0}, function(data)
    assert(data.initial == 0 and not data.reserved and not data.repeatReserved, "空容器产生虚假预约")
    assert(not data.consumed and data.afterConsume == 0 and data.restoredPending == 0, "空容器产生虚假消费或恢复预约")
end, "空容器无组分不生成输出批次")
test.system("containers", "composition", {water = .5, oil = 0}, function(data)
    assert(data.reserved and data.consumed and data.initial == .5 and data.afterConsume == 0, "小数尾量无法真实消费")
    assert(data.pendingAfter == 0 and data.restored == .5 and data.restoredBatchServings == .5, "小数尾量或预约快照丢失")
end, "小数尾量能够预约消费并完整恢复")

local whole_cases = {
    {name = "每次只倒完整一份", args = {amount = 8, requested = 1, retainedFraction = .75}, transfer = 1, spill = 2, fill = 0},
    {name = "不足一份的倾斜不扣液", args = {amount = 8, retainedFraction = .95}, transfer = 1, spill = 0, fill = 0},
    {name = "分数请求不落地", args = {amount = 8, requested = .5}, transfer = 0, spill = 8, fill = 0},
    {name = "分数尾量留桶", args = {amount = .5}, transfer = 0, spill = 0, fill = 7},
    {name = "混液空位只装整份", args = {amount = 6.5, requested = 20}, transfer = 6, spill = 6, fill = 1},
    {name = "桶内半份空位不抽地表", args = {amount = 7.5}, transfer = 1, spill = 7, fill = 0},
    {name = "倒置可以倒尽完整份", args = {amount = 8, retainedFraction = -1}, transfer = 1, spill = 8, fill = 0}
}
for _, case in ipairs(whole_cases) do
    test.system("containers", "whole_world_transfer", case.args, function(data)
        assert(data.transfer == case.transfer and data.spill == case.spill and data.fill == case.fill,
            "地表倒液、倾角或装液产生了分数份，或丢失可移动的完整份")
    end, "世界容器整份边界：" .. case.name)
end
for _, reserved in ipairs({false, true}) do
    test.system("containers", "whole_batch", {reservedElsewhere = reserved}, function(data)
        local count = reserved and 1 or 2
        for index, sample in ipairs(data.samples) do
            assert(sample.reserved == (index <= count) and sample.consumed == (index <= count),
                "整份出口被其他组分尾量卡住，或超用了别的出口预约")
            if index <= count then
                assert(sample.liquidId == "test:water" and sample.servings == 1,
                    "整份出口把不足一份的组分倒进了世界")
            end
        end
        assert(data.water == (reserved and 1.5 or .5) and data.oil == .5
            and data.remaining == (reserved and 2 or 1) and data.otherReserved == (reserved and 1 or 0),
            "整份预约或消费丢弃了小数尾量，或修改了其他出口的真实预约")
    end, reserved and "整份倾倒尊重其它端口预约" or "半份尾量不阻塞完整组分并保留所有尾量")
end
