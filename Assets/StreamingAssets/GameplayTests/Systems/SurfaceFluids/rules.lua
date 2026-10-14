local function near(a, b, message)
    assert(type(a) == "number" and a == a and math.abs(a - b) <= 0.00001, message)
end

test.system("surface_fluids", "liquid_layer", {}, function(data)
    assert(data.added and not data.repeated and data.repeatedRevisionStable, "液深重复写入错误地产生变更")
    assert(data.wetChanged and data.wetId == "core:dirty_water", "液体独立身份或差量未保存")
    near(data.wetDepth, 0.6, "液深写入错误")
    assert(data.ground == 17 and data.cost == 5, "写液体篡改底层地形或导航成本")
    assert(data.cleared and data.dryType == 0 and not data.dryChanged, "极小液深没有归零或恢复基线后仍有差量")
    near(data.dryDepth, 0, "抽干后残留液深")
    near(data.negativeDepth, 0, "负液深没有归零")
    near(data.excessDepth, 1, "超额液深没有封顶")
    assert(data.missingId == "ArgumentException" and data.invalidDepth == "ArgumentOutOfRangeException",
        "无身份液体或非有限液深没有拒绝")
end, "独立液体层、变更版本、抽干与基线恢复")
