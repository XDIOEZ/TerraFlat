test.check("world_ready")
test.runtime("industrial_fluids", "pump", {}, function(d)
    assert(d.unpowered == 0 and d.unpoweredSourceStable, "机械气泵无动力仍抽气")
    assert(math.abs(d.full-d.capacity)<.000001 and d.fullSourceStable, "机械气泵未充满固定容量或超量扣源")
    assert(math.abs(d.sourceTaken-d.full)<.000001, "机械气泵没有扣真实气源")
end, "机械动力气泵实际抽气与固定容量")

test.runtime("mechanical", "articulated_pump_pose", {}, function(d)
    assert(math.abs(d.canvasPixelsPerUnit-d.partPixelsPerUnit)<.001, "拆件导入PPU与源画布PPU不一致")
    assert(math.abs(d.itemPixelsPerUnit-d.partPixelsPerUnit)>1, "Idle与拆件PPU未形成有效回归区分")
    assert(math.abs(d.actualScale-d.partScale*d.partPixelsPerUnit/d.canvasPixelsPerUnit)<.00001,
        "活塞缩放没有按源画布PPU归一")
    assert(math.abs(d.actualOffsetX-(d.anchorX-d.canvasPivotX)/d.canvasPixelsPerUnit)<.00001 and
        math.abs(d.actualOffsetY-(d.canvasPivotY-d.anchorY)/d.canvasPixelsPerUnit)<.00001,
        "活塞偏移没有按源画布PPU换算")
end, "机械气泵拆件PPU与源画布坐标装配")
