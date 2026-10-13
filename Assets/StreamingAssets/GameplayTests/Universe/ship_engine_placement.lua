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
