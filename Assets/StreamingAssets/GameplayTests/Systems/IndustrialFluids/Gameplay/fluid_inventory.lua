-- 库存与原子事务：组内检查各用自己的夹具，失败保留报告并继续其余检查。
test.check("world_ready")
test.check("player_ready")

local scenarios = {
    {"core_units", "气液单位与份数换算"},
    {"core_inventory_mixing", "两相库存与混料限制"},
    {"core_decimal_tail", "decimal 尾量与分辨率边界"},
    {"core_reservations", "出口预留与并发防超卖"},
    {"core_transfer_atomicity", "转移原子性与容量裁剪"},
    {"core_recipe_atomicity", "多组分配方原子扣料"},
}

for _, scenario in ipairs(scenarios) do
    test.fluid(scenario[1], scenario[2])
end
