-- 完整测试才生成全部物品，避免快速回归承担大量目录初始化成本。
test.check("player_ready")
test.gm("initialize_creative_inventory")
test.check("gm_admin_enabled", "创造背包启用管理员身份")
test.check("gm_creative_inventory_enabled", "主背包获得创造模式容量豁免")
