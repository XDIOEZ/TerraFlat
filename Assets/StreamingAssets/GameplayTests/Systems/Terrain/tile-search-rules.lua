-- GM 地块定位规则，不操作当前玩家世界。
for _, input in ipairs({
    {scenario = "found", wrapped = true},
    {scenario = "found", wrapped = false},
    {scenario = "found", wrapped = true, cave = true},
    {scenario = "missing", wrapped = true},
    {scenario = "excluded", wrapped = true},
    {scenario = "rejected", wrapped = true},
    {scenario = "cancel_before", wrapped = true},
    {scenario = "cancel_during", wrapped = true}
}) do
    local case = input
    test.system("terrain", "tile_search", case, function(data)
        assert(data.clay == 2 and data.id == 2 and data.alias == 2 and data.partial == 1 and
            data.empty == 0 and data.unknown == 0, "地块名称、别名或粘土/黏土写法匹配错误")
        if case.scenario == "cancel_before" or case.scenario == "cancel_during" then
            assert(data.exception == "OperationCanceledException" and not data.found, "取消搜索后仍返回传送落点")
        elseif case.scenario == "found" then
            assert(data.exception == "" and data.found and data.x == 7 and data.y == -5 and data.sampled > 0,
                "定位未命中正式目标，或误把水下、被覆盖、被阻挡的地块作为落点")
        else
            assert(data.exception == "" and not data.found and data.sampled > 0,
                "有限世界不存在目标或目标区块已排除时，搜索没有正确结束")
        end
    end, "GM 地块定位：" .. input.scenario)
end
