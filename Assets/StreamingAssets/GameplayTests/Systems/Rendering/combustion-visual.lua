-- 火焰独立规则，正式熔炉JSON定位与共享资源回归不受其他主题步骤数量影响。
test.system("rendering", "dropped_combustion_material", {}, function(data)
    assert(data.assigned and data.shared and data.sharedMesh, "火焰没有共享正式材质与网格")
    assert(data.shader == "Game/Effects/ShaderFlame" and data.supported and data.passes,
        "火焰缺少可用的 URP 2D / Forward Shader Pass")
    assert(data.pixelsPerUnit == 32 and data.animationFPS == 12,
        "共享火焰没有使用与人物匹配的像素密度和逐帧动画")
    assert(data.vertices == 4 and data.particles == 0, "常驻火焰仍依赖粒子模拟或重复生成复杂网格")
    assert(data.independentPhase, "不同火源使用相同相位，会同步跳动")
    assert(data.width == 2 and data.height == 3 and data.boundsWidth == 2 and data.boundsHeight == 3
        and data.boundsCenterY == 1.5, "Shader 尺寸没有同步 CPU 裁剪边界或火焰底部锚点")
    assert(data.sortingOrder == 37 and data.adapterOrder == 82, "掉落物或设施火焰丢失宿主排序")
    assert(data.startsStopped and data.lights and data.disabled and data.reenabled,
        "火焰初始开关或组件启停错误")
    assert(data.staysExtinguished and data.zeroIntensityHidden and data.finiteBounds,
        "回池后重新点燃、零火势残留，或无效配置污染网格边界")
    assert(data.adapterLights and data.adapterExtinguishes, "燃烧模块没有转发点燃和熄灭状态")
    assert(math.abs(data.attachedUpX + math.sin(math.rad(75))) < .0001
        and math.abs(data.attachedUpY - math.cos(math.rad(75))) < .0001,
        "显式跟随锚点的火焰丢失宿主倾斜")
    for _, prefix in ipairs({"uprightUp", "mirroredUp"}) do
        assert(math.abs(data[prefix .. "X"]) < .0001 and math.abs(data[prefix .. "Y"] - 1) < .0001
            and math.abs(data[prefix .. "Z"]) < .0001, "直立火焰继承了手持倾斜或左右翻面")
    end
    local smelter = data.smelter
    assert(smelter.enabled and smelter.followRenderer, "熔炉火焰没有跟随正式主体配置")
    assert(smelter.spriteAddress == "Assets/6_Art/Generated/Building/Smelter/Smelter_Unlit.png[Smelter_Unlit]",
        "熔炉炉口像素断言需要按新贴图重新核对")
    assert(smelter.anchor.x == 0 and smelter.anchor.z == 0, "熔炉火焰没有水平居中或偏离主体平面")
    -- 正式熔炉贴图为16×16、16PPU、中心原点，炉口底沿上表面位于底部第2像素。
    assert(math.abs(smelter.anchor.y * 16 + 8 - 2) < .0001,
        "熔炉火焰底部没有贴齐炉口内底面（底部像素Y=2）")
end, "Shader 火焰共享资源、生命周期与熔炉炉口定位")
