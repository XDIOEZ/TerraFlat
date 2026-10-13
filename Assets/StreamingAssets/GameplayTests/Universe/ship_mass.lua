-- 载荷改变只移动物理重心，船体上的世界点和点速度必须保持连续。
local function near(actual, expected, label)
    assert(type(actual) == "number" and actual == actual and math.abs(actual - expected) <= 0.0000001, label)
end
test.space("ship_mass", function(data)
    assert(data.operation == "ship_mass" and data.scope == "production-domain", "缺少正式质量领域结果")
    near(data.initial.mass, data.floorInputMass, "初始结构质量错误")
    near(data.initial.centerX, 1, "两格矩形地板的横向重心错误")
    near(data.initial.centerY, 0.5, "一格高地板的纵向重心错误")
    near(data.initial.inertia, data.floorInputMass * (2 * 2 + 1) / 12, "矩形地板实际惯量错误")
    near(data.loaded.mass, data.floorInputMass + data.cargoInputMass, "相同货物来源被重复计重")
    local center = (data.initial.centerX * data.floorInputMass + 3 * data.cargoInputMass) / data.loaded.mass
    near(data.loaded.centerX, center, "真实货物没有按位置改变横向重心")
    near(data.loaded.centerY, data.initial.centerY, "同高度货物不应改变纵向重心")
    local inertia = data.initial.inertia + data.floorInputMass * data.cargoInputMass / data.loaded.mass
        * (3 - data.initial.centerX) ^ 2
    near(data.loaded.inertia, inertia, "新增货物的平行轴惯量错误")
    for _, field in ipairs({"x", "y", "velocityX", "velocityY"}) do
        near(data.pointAfter[field], data.pointBefore[field], "重心改变时世界点或点速度不连续：" .. field)
    end
    for _, field in ipairs({"mass", "centerX", "centerY", "inertia"}) do
        near(data.unloaded[field], data.initial[field], "卸载货物后物理属性未恢复：" .. field)
    end
    assert(data.invalidMassRejected == true, "负质量贡献必须被生产入口拒绝")
    return "覆盖部件质量、矩形惯量、来源去重、货物重心、点速度守恒、卸载恢复和负质量拒绝。"
end, "船体质量重心与惯量")
