-- 正式生命结算发布真实短音，不把纯选音断言当作接线验证。
test.runtime("audio", "combat_settlement", {}, function(data)
    assert(#data.samples == 8, "GO 与原生资源声音现场采样不完整")
    local expected = {
        go_blocked = "combat.blocked.high.low", go_damaged = "combat.impact.high.low",
        go_rejected = "", go_environment = "", go_fatal = "combat.impact.high.low",
        native_soft_blocked = "combat.blocked.low.high",
        native_blocked = "combat.blocked.high.high", native_fatal = "combat.impact.high.high"
    }
    for _, row in ipairs(data.samples) do
        local cue = expected[row.label]
        assert(cue ~= nil and row.cue == cue, "正式提交后选错命中结果声：" .. row.label)
        assert(row.eventCount == (cue == "" and 0 or 1), "单次提交重复或遗漏声音：" .. row.label)
        if cue ~= "" then
            assert(row.playing, "权威事件已发出但真实 AudioSource 未播放：" .. row.label)
        end
        if row.label == "go_blocked" or row.label == "native_blocked" or row.label == "native_soft_blocked" then
            assert(row.loss == 0, "未破防现场实际产生伤害：" .. row.label)
        elseif row.label == "go_rejected" then
            assert(row.loss < 0, "临时无敌未拒绝命中")
        else
            assert(row.loss > 0, "有效伤害现场没有扣血：" .. row.label)
        end
    end
end, "正式生命提交、两种命中声音与真实声源")
