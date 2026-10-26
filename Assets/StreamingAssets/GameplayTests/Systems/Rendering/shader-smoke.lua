test.system("rendering", "combustion_shader_smoke", {}, function(data)
    assert(#data.enabledItems == 1 and data.enabledItems[1] == "BlastFurnace" and data.optIn,
        "烟雾没有限定为高炉主动启用，影响了其它燃烧物")
    assert(data.mouthLocalY > 0.36 and data.mouthLocalY < 0.4
        and math.abs(data.configuredWidth - 0.65) < 0.0001
        and math.abs(data.configuredHeight - 1.25) < 0.0001,
        "高炉烟雾没有从上方炉口按正式尺寸冒出")
    assert(data.shader == "Game/Effects/ShaderSmoke" and data.supported and data.passes,
        "烟雾缺少可用的 URP 2D / Forward Shader Pass")
    assert(data.sharedMaterial and data.sharedMesh and data.vertices == 4 and data.particles == 0,
        "常驻烟雾没有共享四边形与材质，或仍在 CPU 模拟粒子")
    assert(data.independentPhase, "多个炉子的烟团完全同步")
    assert(data.boundsWidth == 2 and data.boundsHeight == 3 and data.boundsCenterY == 1.5
        and data.finiteBounds, "烟雾裁剪范围或非法尺寸回退错误")
    assert(data.startsStopped and data.emits and data.disabled and data.reenabled
        and data.staysStopped and data.zeroDensityHidden, "烟雾启停或回池状态错误")
    assert(data.adapterEmits and data.anchorError < 0.0001 and data.order == 83
        and math.abs(data.uprightX) < 0.0001 and math.abs(data.uprightY - 1) < 0.0001,
        "烟雾没有跟随缩放后的炉口、宿主排序或保持向上")
    assert(data.adapterStops and data.adapterDisabled and data.adapterResumes and data.configurationStops,
        "熄火、停用、恢复或关闭配置后残留烟雾")
end, "高炉 Shader 烟雾的炉口配置、共享资源、燃烧开关与回收")
