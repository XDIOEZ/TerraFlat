-- 管网拓扑与路由：组内检查各用自己的夹具，失败保留报告并继续其余检查。
test.check("world_ready")
test.check("player_ready")

local scenarios = {
    {"network_catalog", "本体工业气液目录完整性"},
    {"network_masks", "管道 16 种连接掩码"},
    {"network_rotation", "四向旋转与输入输出相态"},
    {"network_pressure_direction", "压差与有向输送规则"},
    {"network_valve", "阀门断边与恢复输送"},
    {"network_selector", "气体选择器双路分流"},
    {"network_phase_preference", "四方向两相优先与回退"},
    {"network_port_rejection", "端口物质筛选与出口相态"},
    {"network_receiver_routes", "按需接收路径与混气预算"},
    {"network_probe_hysteresis", "机械电子探针测压回差"},
}

for _, scenario in ipairs(scenarios) do
    test.fluid(scenario[1], scenario[2])
end
