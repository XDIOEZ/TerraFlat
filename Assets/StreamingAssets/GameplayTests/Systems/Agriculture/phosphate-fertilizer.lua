-- 磷肥配置、土壤封顶和培育仓接收资格独立回归。
for _, sample in ipairs({
    {fertility = .2, expected = .55, changed = true},
    {fertility = .9, expected = 1, changed = true},
    {fertility = 1, expected = 1, changed = false}
}) do
    test.system("agriculture", "phosphate_fertilizer", {water = 4, fertility = sample.fertility}, function(data)
        assert(data.moduleId == "Mod_FarmlandSupply" and data.enabled and data.waterAmount == 0
            and math.abs(data.fertilityAmount - .35) < .00001, "磷肥没有组合通用施肥能力或错误补水")
        assert(data.water == 4 and math.abs(data.fertility - sample.expected) < .00001
            and data.changed == sample.changed, "磷肥未按土壤正式规则补肥、封顶或跳过满肥")
        assert(data.cultivatorAccepts, "培育仓的正式肥料接收配置未接受磷肥")
    end, "磷肥配置与正式土壤规则：初始肥力" .. sample.fertility)
end
