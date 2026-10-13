-- 光纤独立连接规则验证控制台与导航之间断开、修复以及工作区复用。
test.space("ship_optical", function(data)
    assert(data.operation == "ship_optical" and data.scope == "production-domain", "缺少正式光纤领域结果")
    assert(data.connected == true, "相邻光纤必须连接独立控制台和导航端口")
    assert(data.afterRemoval == false, "光纤移除后不能继续沿用旧连接")
    assert(data.afterRepair == true, "同一工作区修复光纤后必须恢复实际连接")
    return "覆盖独立控制台与导航光纤连接、断开、修复和集合工作区复用。"
end, "飞船独立光纤连接")
