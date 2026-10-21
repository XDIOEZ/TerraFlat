-- 过滤电解与发动机：组内检查各用自己的夹具，失败保留报告并继续其余检查。
test.check("world_ready")
test.check("player_ready")

local capacity_cases = {
    {name = "无限残渣槽整型上限", args = {requested = 2147483647}, capacity = 2147483647, process = true},
    {name = "容量不超过一份请求", args = {requested = 1}, capacity = 1, process = true},
    {name = "有限槽取整后保留小数进度", args = {requested = 2147483647, slotCapacity = 2.5}, capacity = 2, process = true},
    {name = "零容量拒绝且原料滤材不变", args = {slotCapacity = 0}, capacity = 0, process = false},
    {name = "满残渣槽原子拒绝", args = {slotCapacity = 2, full = true}, capacity = 0, process = false},
    {name = "异物占槽原子拒绝", args = {occupied = true}, capacity = 0, process = false},
    {name = "库存禁止转入原子拒绝", args = {blocked = true}, capacity = 0, process = false},
    {name = "液体产物满腔原子拒绝", args = {outputFull = true}, capacity = 2147483647, process = false},
    {name = "整份残渣超出端口整数范围原子拒绝", args = {residuePerReaction = 2147483648}, capacity = 2147483647, process = false, rejectAtCommit = true}
}
for _, case in ipairs(capacity_cases) do
    test.runtime("industrial_fluids", "reaction_capacity", case.args, function(data)
        assert(data.capacity >= 0 and data.capacity <= data.requested, "端口容量越过请求或溢出为负数")
        assert(data.capacity == case.capacity, "真实残渣端口容量错误，actual=" .. data.capacity)
        local extent = (data.inputBefore - data.inputAfter) / data.inputPerReaction
        if case.process then
            assert(data.outputExtent > 0 and extent > 0, "有滤材和真实容量仍无法加工")
            assert(data.filterBefore - data.filterAfter == 1, "首次加工没有扣且只扣一份滤材")
            assert(math.abs(data.filterWork + extent - data.filterWorkPerItem) < .000001, "滤材工作余量不守恒")
            assert(math.abs(data.residueAfter - data.residueBefore + data.residuePending - extent * data.residuePerReaction) < .000001,
                "残渣整份和小数尾量不守恒")
            assert(data.residuePending >= 0 and data.residuePending < 1, "残渣进度不是合法小数尾量")
            for _, product in ipairs(data.products) do
                assert(math.abs(product.after - product.before - extent * product.perReaction) < .000001,
                    "正式配方产物比例错误：" .. product.fluidId)
            end
        else
            assert((case.rejectAtCommit and data.outputExtent > 0 or not case.rejectAtCommit and data.outputExtent == 0)
                and data.inputUnchanged and extent == 0, "拒绝加工仍扣原料或伪造预算")
            assert(data.filterBefore == data.filterAfter and data.filterWork == 0, "拒绝加工仍扣滤材或推进工作量")
            assert(data.residueBefore == data.residueAfter and data.residuePending == 0, "拒绝加工仍生成残渣")
            for _, product in ipairs(data.products) do
                assert(product.after == product.before, "拒绝加工仍生成产物：" .. product.fluidId)
            end
        end
    end, "真实过滤事务：" .. case.name)
end

local transfer_cases = {
    {name = "小于一份的液碱尾量真实转移", args = {}, success = true},
    {name = "部分份数转移保持两端守恒", args = {requested = .00001}, success = true},
    {name = "满接收腔拒绝且两端回滚", args = {full = true}, success = false},
    {name = "异种液体拒收保留真实原因", args = {mismatched = true}, success = false}
}
for _, case in ipairs(transfer_cases) do
    test.runtime("industrial_fluids", "residue_transfer", case.args, function(data)
        assert(data.sourceBefore > 0 and data.visibleServings > 0, "真实电解未产生可见液碱")
        assert(data.machineDenied and not data.machineAccess, "机器访问绕过副产物取液权限")
        assert(data.machineFailureStatus == "容器端口不允许机器抽取", "权限拒绝被误报成液体入口堵塞")
        assert(data.success == case.success, "正式液碱事务结果错误：" .. data.failure)
        assert(math.abs(data.sourceBefore + data.targetBefore - data.sourceAfter - data.targetAfter) < .00000001,
            "源和目标的真实液碱摩尔量不守恒")
        assert(data.gasBefore == data.gasAfter, "液碱取液错误扣了混气出口")
        if case.success then
            assert(data.previewSuccess and data.transferredServings > 0 and data.targetAfter > data.targetBefore,
                "成功事务没有真实转移液碱")
            assert(data.sourceAfter >= 0 and data.sourceAfter < data.sourceBefore, "源腔没有真实扣量")
            assert(data.visibleRoundtripMoles <= data.sourceBefore, "端口可见份数回转后超过真实预留量")
        else
            assert(data.failure == "Full" and not data.previewSuccess, "满腔没有在预检拒绝")
            local expected = case.args.mismatched and "液相槽里还有其他物质" or "液体接收空间不足"
            assert(data.transferFailureStatus == expected, "容器拒收没有保留真实原因：" .. data.transferFailureStatus)
            assert(data.sourceUnchanged and data.targetUnchanged, "失败事务修改了真实库存或预留")
        end
    end, "真实液碱端口：" .. case.name)
end

local scenarios = {
    {"device_filter", "真实滤材消耗与残渣记账"},
    {"device_residue_capacity", "残渣大整数容量与异类物品拒收"},
    {"device_filter_blocked", "过滤产物和残渣满槽保护"},
    {"device_electrolysis_recipes", "全部实际电解配方产物比例"},
    {"device_electrolysis_blocked", "电解断电及混气液碱满腔保护"},
    {"device_electrolysis_residue_port", "独立液碱取液与访问权限"},
    {"device_engine_blocked", "氢能发动机缺气无负载保护"},
    {"device_engine_work", "氢能发动机实际负载与能量结算"},
}

for _, scenario in ipairs(scenarios) do
    test.fluid(scenario[1], scenario[2])
end
