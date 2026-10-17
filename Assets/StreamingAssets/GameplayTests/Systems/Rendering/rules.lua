-- 渲染参数由正式 IsValid 规则判定，图像效果需场景验收。
test.system("rendering", "fog_config", {}, function(data)
    assert(data.valid, "合法雾配置被拒绝")
end, "大雾合法配置")
for _, args in ipairs({ { clearRadius = 0 }, { opaqueRadius = 10 }, { transitionSeconds = -1 }, { noiseScale = 0 }, { driftSpeed = -1 }, { noiseStrength = 0.21 }, { red = 1.1 } }) do
    test.system("rendering", "fog_config", args, function(data)
        assert(not data.valid, "大雾接受非法半径、时间、噪声或颜色")
    end, "大雾配置拒绝非法边界")
end

test.system("rendering", "building_capability_cache", {}, function(data)
    assert(data.empty and data.emptyAgain, "建筑能力空结果缓存失效错误")
    assert(data.addInvalidated and data.removeInvalidated and data.readdInvalidated, "模块增删后仍然使用旧建筑能力")
    assert(data.firstPreserved, "能力缓存改变了多个匹配模块的原先首个选择顺序")
    assert(data.floorLayer == 6 and not data.floorBlocks, "船地板能力规则错误")
    assert(data.changedLayer == 3 and not data.changedBlocks and data.liveBlocks, "能力缓存冻结了模块的实时占地或通行属性")
    assert(data.allocated == 0, "预热后的建筑能力查询仍有线程托管分配")
end, "建筑能力缓存保留结构失效与动态属性")

test.system("rendering", "shadow_proxy_changes", {}, function(data)
    assert(data.initialSource and data.initialProperties, "太阳阴影首帧没有提交")
    assert(not data.repeatedSource and not data.repeatedProperties, "相同阴影仍重复提交源帧或属性")
    assert(data.frameChanged and data.frameProperties and data.frameSprite and data.frameMesh, "太阳阴影动画换帧没有同步Sprite、共享网格和属性")
    assert(data.alphaUpdated and math.abs(data.alpha - 0.25) < 0.0001, "阴影透明度变化未提交")
    assert(data.mirroredScale == -2 and data.repeatedScale == -2, "阴影重复同步造成翻转来回切换")
    assert(data.movedFootUpdated and data.foot == 7, "阴影脚点变化未提交")
    assert(data.slicedVisible and data.meshVisible, "阴影绘制模式切换后显示节点错误")
    assert(data.recycledSource and data.recycledProperties, "阴影归池后没有重新提交源与属性")
end, "太阳阴影静态复用与动态变化")

for _, args in ipairs({
    { strength = 1, clear = 10, opaque = 20 },
    { strength = 0.5, clear = 20, opaque = 30 },
    { strength = 0.25, clear = 40, opaque = 50 },
    { strength = 0.0005, clear = 20000, opaque = 20010 },
    { strength = 2, clear = 10, opaque = 20 },
    { strength = 0.5, clearRadius = 5, opaqueRadius = 10, clear = 10, opaque = 15 }
}) do
    test.system("rendering", "fog_config", args, function(data)
        assert(data.visible, "非零雾强度没有生成视野圆")
        assert(math.abs(data.clearRadius - args.clear) < 0.01, "雾强度没有按反比改变清晰圆半径")
        assert(math.abs(data.opaqueRadius - args.opaque) < 0.01, "雾圈柔边宽度随强度改变")
    end, "天气雾强度只收缩视野圆并保留固定柔边")
end
for _, strength in ipairs({ 0, -1 }) do
    test.system("rendering", "fog_config", { strength = strength }, function(data)
        assert(not data.visible, "无雾时仍生成遮挡圈")
    end, "无雾和负强度关闭天气雾圈")
end

for _, args in ipairs({
    { size = 5, aspect = 1 }, { size = 20, aspect = 2.2 },
    { size = 40, aspect = 0.6, flipped = true }, { size = 55, aspect = 1.8 },
    { size = 60, aspect = 2.2 }, { size = 90, aspect = 1 }
}) do
    test.system("rendering", "zoom_cloud_mask", args, function(data)
        assert(data.clearWidth == 80 and data.clearHeight == 80, "镜头缩放或宽高比改变了三圈活动区块的方形清晰窗")
        assert(data.opaqueWidth == 112 and data.opaqueHeight == 112, "不透明边界没有落在额外表现预加载圈外沿")
        assert(data.clearMinX == -32 and data.clearMaxX == 48 and data.clearMinY == -48 and data.clearMaxY == 32,
            "清晰窗没有对齐玩家所在区块的实际格边")
        assert(data.windowCenterOffsetX == -5 and data.windowCenterOffsetY == -1, "方窗中心错误地锁在玩家脚下而没有对齐区块")
        assert(data.noiseScale == data.expectedNoiseScale, "缩放改变了云纹的世界尺寸")
        assert(math.abs(data.viewportHeight - data.expectedHeight) < 0.001, "云圈没有使用实际镜头世界投影")
        assert(math.abs(data.viewportWidth - data.expectedWidth) < 0.001, "宽高比改变了雾圈世界距离")
        assert(data.playerProjectionCentered, "雾层世界投影中点没有对齐玩家位置")
    end, "方形雾边界随加载窗口变化并独立于镜头缩放")
end

for _, args in ipairs({
    { playerX = 15.75, playerY = -0.25, minX = -32, maxX = 48, minY = -48, maxY = 32 },
    { playerX = -0.25, playerY = -16.25, minX = -48, maxX = 32, minY = -64, maxY = 16 },
    { playerX = 16, playerY = 0, minX = -16, maxX = 64, minY = -32, maxY = 48 }
}) do
    test.system("rendering", "zoom_cloud_mask", args, function(data)
        assert(data.clearMinX == args.minX and data.clearMaxX == args.maxX and
            data.clearMinY == args.minY and data.clearMaxY == args.maxY, "区块内移动、负坐标或跨区块后雾窗边界错位")
        assert(data.clearWidth == 80 and data.clearHeight == 80, "移动改变了玩家设置的窗口大小")
    end, "方形雾窗对齐正负坐标区块边界")
end

for _, args in ipairs({
    { distance = 3, nextDistance = 5, initial = 80, clear = 144, opaque = 176, expectedDistance = 5 },
    { distance = 5, nextDistance = 2, initial = 144, clear = 48, opaque = 80, expectedDistance = 2 },
    { distance = 2, nextDistance = 1, initial = 48, clear = 16, opaque = 48, expectedDistance = 1 },
    { distance = 0, initial = 16, clear = 16, opaque = 48, expectedDistance = 1 }
}) do
    test.system("rendering", "zoom_cloud_mask", args, function(data)
        assert(data.initialWidth == args.initial and data.loadDistance == args.expectedDistance, "正式加载器没有应用范围设置或最小一圈约束")
        assert(data.clearWidth == args.clear and data.clearHeight == args.clear, "修改加载范围后清晰方窗没有同步扩大或缩小")
        assert(data.opaqueWidth == args.opaque and data.opaqueHeight == args.opaque, "修改加载范围后柔雾外边界没有同步")
        assert(data.featherX == 16 and data.featherY == 16, "柔雾带没有复用额外预绑定的一圈")
    end, "玩家加载范围动态扩大缩小与最小值")
end

test.system("rendering", "zoom_cloud_mask", { distance = 2, chunkWidth = 32, chunkHeight = 8 }, function(data)
    assert(data.clearWidth == 96 and data.clearHeight == 24 and data.opaqueWidth == 160 and data.opaqueHeight == 40,
        "非正方形区块没有按实际宽高生成加载窗和柔雾带")
    assert(data.clearMinX == -32 and data.clearMaxX == 64 and data.clearMinY == -16 and data.clearMaxY == 8,
        "非正方形区块边界错位")
end, "雾窗兼容非正方形区块")

test.system("rendering", "zoom_cloud_mask", { distance = 3, preload = false }, function(data)
    assert(data.opaqueWidth == 80 and data.opaqueHeight == 80, "没有表现预加载圈时暴露了窗口外未加载地形")
    assert(data.clearWidth == 78 and data.clearHeight == 78 and data.featherX == 1 and data.featherY == 1,
        "没有表现预加载圈时未在已加载窗口内保留一格柔边")
end, "雾窗缺少额外表现圈时安全内收柔边")

test.system("rendering", "zoom_cloud_mask", { distance = 1, chunkWidth = 1, chunkHeight = 1, preload = false }, function(data)
    assert(data.clearWidth == 0 and data.clearHeight == 0 and data.opaqueWidth == 1 and data.opaqueHeight == 1,
        "极小窗口产生负数清晰区或越出已加载范围")
    assert(data.featherX == 0.5 and data.featherY == 0.5, "极小窗口的柔边退化为零宽度")
end, "极小雾窗保持有限柔边")

-- 直接采样正式窗口交接，覆盖雾边连续移动和旧画面释放的几何条件。
test.system("rendering", "zoom_cloud_transition", { duration = 0.8, steps = {
    { x = 16 }, { x = 16, seconds = 0.2 }, { x = 16, seconds = 0.2 },
    { x = 16, seconds = 0.4 }, { x = 16, seconds = 1 }
} }, function(data)
    local s = data.samples
    assert(data.validDuration and s[1].centerX == 0, "跨块当帧雾窗瞬移")
    assert(math.abs(s[2].centerX - 2.5) < 0.001 and math.abs(s[3].centerX - 8) < 0.001,
        "雾窗没有按时间平滑退去并在另一侧出现")
    assert(s[4].centerX == 16 and s[5].centerX == 16, "雾窗未按配置时长收敛或完成后继续移动")
    assert(s[1].leftChunkRetained and s[3].leftChunkRetained and not s[4].leftChunkRetained,
        "旧区块在雾盖住之前被释放，或完全遮住后仍被保留")
    assert(s[3].opaqueMinX == -48 and s[3].opaqueMaxX == 64 and s[3].clearWidth == 80,
        "渐变改变了窗口宽度或只移动了一侧边缘")
end, "区块雾窗渐退渐现和旧画面遮挡后释放")

test.system("rendering", "zoom_cloud_transition", { duration = 0.8, steps = {
    { x = 16 }, { x = 16, seconds = 0.4 }, { x = -16 },
    { x = -16, seconds = 0.4 }, { x = -16, seconds = 0.4 }
} }, function(data)
    local s = data.samples
    assert(s[2].centerX == 8 and s[3].centerX == 8, "折返时雾窗跳回原区块")
    assert(s[4].centerX == -4 and s[5].centerX == -16, "折返没有从当前进度继续缓动")
    assert(s[4].rightChunkRetained and not s[5].rightChunkRetained, "折返后右侧旧画面释放条件错误")
end, "雾窗跨块中途折返不断层")

test.system("rendering", "zoom_cloud_transition", { duration = 0.8, steps = {
    { x = 16 }, { x = 16, seconds = 0.4 }, { x = 32, y = 16 },
    { x = 32, y = 16, seconds = 0.4 }, { x = 32, y = 16, seconds = 0.4 }
} }, function(data)
    local s = data.samples
    assert(s[3].centerX == 8 and s[3].centerY == 0, "连续跨块重置了可见雾窗")
    assert(s[4].centerX == 20 and s[4].centerY == 8 and s[5].centerX == 32 and s[5].centerY == 16,
        "连续跨块或斜向移动没有平滑接续")
end, "雾窗连续跨块与斜向交接")

test.system("rendering", "zoom_cloud_transition", { duration = 0.8, steps = {
    { width = 144, height = 48 }, { width = 144, height = 48, seconds = 0.4 },
    { width = 144, height = 48, seconds = 0.4 }, { width = 16, height = 16 },
    { width = 16, height = 16, seconds = 0.8 }
} }, function(data)
    local s = data.samples
    assert(s[1].clearWidth == 80 and s[2].clearWidth == 112 and s[2].clearHeight == 64,
        "加载距离或长方形窗口变化直接跳变")
    assert(s[3].clearWidth == 144 and s[4].clearWidth == 144 and s[5].clearWidth == 16,
        "扩大或缩小雾窗没有按配置时长完成")
    assert(s[2].opaqueWidth - s[2].clearWidth == 32 and s[5].opaqueWidth == 48,
        "窗口大小渐变破坏了柔雾带")
end, "加载距离和非正方形雾窗平滑变化")

test.system("rendering", "zoom_cloud_transition", { duration = 0.8, steps = {
    { x = 16 }, { x = 16, seconds = -1 }, { x = 16, seconds = 0 }, { x = 16, seconds = 5 },
    { x = 1000 }, { x = -16, reset = true }
} }, function(data)
    local s = data.samples
    assert(s[2].centerX == 0 and s[3].centerX == 0, "暂停或负时间推动了雾边")
    assert(s[4].centerX == 16, "长帧没有完成交接")
    assert(s[5].centerX == 1000 and not s[5].leftChunkRetained and not s[5].rightChunkRetained,
        "不重叠的传送仍让雾扫过整张地图或保留远处旧画面")
    assert(s[6].centerX == -16, "换世界后仍沿用上个窗口的交接状态")
end, "雾窗暂停、长帧、传送与重置边界")

for _, duration in ipairs({ 0, -1 }) do
    test.system("rendering", "zoom_cloud_transition", { duration = duration, steps = { { x = 16 } } }, function(data)
        assert(data.validDuration == (duration == 0), "雾窗时间没有接受零值或拒绝负数")
        if duration == 0 then
            assert(data.samples[1].centerX == 16 and not data.samples[1].leftChunkRetained,
                "关闭渐变后仍延迟移动或保留旧画面")
        end
    end, "雾窗渐变时长边界：" .. duration)
end

for _, args in ipairs({
    { size = 20, expected = 0 }, { size = 55, expected = 0 }, { size = 60, expected = 0 },
    { size = 75, expected = 0.5 }, { size = 90, expected = 1 }, { size = 200, expected = 1 }
}) do
    test.system("rendering", "universe_view_transition", args, function(data)
        assert(math.abs(data.opacity - args.expected) < 0.0001, "宇宙画面没有在原视野区间直接淡入")
    end, "动态加载雾窗下的宇宙视图直接淡入")
end

for _, args in ipairs({
    { current = 20, requested = 40, presentedOpacity = 0, expected = 40, label = "普通地表缩放" },
    { current = 60, requested = 75, presentedOpacity = 0, expected = 75, label = "过渡区内拉远" },
    { current = 60, requested = 5000000, presentedOpacity = 0, expected = 90, label = "滑条或双指一步越过过渡" },
    { current = 75, requested = 93.75, presentedOpacity = 0.5, expected = 90, label = "滚轮跨过终点" },
    { current = 90, requested = 112.5, presentedOpacity = 0.999, expected = 90, label = "淡入未完成仍拦截" },
    { current = 90, requested = 112.5, presentedOpacity = 1, expected = 112.5, label = "淡入完成恢复拉远" },
    { current = 90, requested = 60, presentedOpacity = 0.5, expected = 60, label = "过渡中允许拉近取消" },
    { current = 60, requested = 5000000, presentedOpacity = 1, expected = 90, label = "拉近后同帧重新拉远不能跳过过渡" },
    { current = 200, requested = 250, presentedOpacity = 0.5, expected = 200, label = "已有远景不强行拉近" },
    { current = 200, requested = 100, presentedOpacity = 0.5, expected = 100, label = "已有远景允许拉近" },
    { current = 60, requested = 5000000, presentedOpacity = 0, transitionSeconds = 0, expected = 5000000, label = "无过渡时立即放行" },
    { current = 90, requested = 5000000, presentedOpacity = 0.5, entrySize = 120, expected = 120, label = "终点跟随正式配置" }
}) do
    test.system("rendering", "universe_view_transition", args, function(data)
        assert(math.abs(data.clampedSize - args.expected) < 0.0001, "宇宙过渡缩放约束错误：" .. args.label)
        if args.expected == 90 and args.presentedOpacity < 1 then
            assert(data.repeatedSize == 90, "过渡期间连续拉远突破终点")
            assert(data.releasedSize == 90, "过渡结束后补算了先前被丢弃的输入")
        end
    end, "宇宙过渡期间的手动缩放：" .. args.label)
end

test.system("rendering", "group_projection", { outerOrder = 77, innerOrder = 50, spriteOrder = 31 }, function(data)
    assert(data.outerOrder == 77 and data.outerLayerMatches, "代理没有继承最外层启用分组的排序键")
    assert(data.innerOrder == 50 and data.innerLayerMatches, "外层分组停用后没有使用内层排序键")
    assert(data.spriteOrder == 31 and data.spriteLayerMatches, "所有分组停用后没有恢复精灵自身排序键")
end, "独立渲染层级与外部代理排序投影")

test.system("rendering", "universe_view_config", {}, function(data)
    assert(data.valid, "合法宇宙视野配置被拒绝")
end, "宇宙视野合法配置")
for _, args in ipairs({ { start = 0 }, { opaque = 10 }, { maximum = 19 }, { zoom = 1 }, { transitionSeconds = -1 }, { entry = 0 }, { pixels = -1 } }) do
    test.system("rendering", "universe_view_config", args, function(data)
        assert(not data.valid, "宇宙视野接受非法层次、缩放或过渡参数")
    end, "宇宙视野配置拒绝非法边界")
end

for _, args in ipairs({
    { bx = 3, expected = true, label = "相交近光" },
    { bx = 10, expected = false, label = "远光" },
    { bx = 4, expected = true, label = "相切边界" },
    { bx = 4.0005, expected = true, label = "边界保守余量" },
    { bx = 4.01, expected = false, label = "超出边界余量" },
    { bz = 3, expected = true, label = "Z轴仍相交" },
    { bz = 5, expected = false, label = "Z轴分离" },
    { ar = 0, br = 0, expected = true, label = "同点零半径" },
    { ar = 0, br = 0, bx = 1, expected = false, label = "分离零半径" }
}) do
    test.system("rendering", "cave_shadow_spheres", args, function(data)
        assert(data.intersects == args.expected, "洞穴墙阴影球形筛选错误：" .. args.label)
        assert(data.reverseIntersects == data.intersects, "洞穴墙阴影球形筛选不满足对称性：" .. args.label)
    end, "洞穴墙阴影保守球形筛选：" .. args.label)
end

-- 船件建筑状态热查询：正式能力读取活模块，未装配时才读取持久状态。
test.system("rendering", "ship_part_placement_state", {}, function(data)
    assert(data.liveOrdinary.layer == 9 and data.liveOrdinary.blocks and data.liveOrdinary.speed == 1,
        "活建筑状态没有覆盖尚未保存的旧机械身份")
    assert(data.liveMechanical.layer == 0 and not data.liveMechanical.blocks and math.abs(data.liveMechanical.speed - 0.7) < 0.0001,
        "活建筑切换机械身份后没有复用正式机械通行能力")
    assert(data.liveChanged.layer == 9 and data.liveChanged.blocks and data.liveChanged.speed == 1,
        "运行建筑身份变化被旧查询缓存阻断")
    assert(data.allocated == 0 and data.layerSum == 0, "船件稳定通行查询仍然反复解析或分配建筑状态")
    assert(data.beforeBuildingLoad.layer == 0 and not data.beforeBuildingLoad.blocks and math.abs(data.beforeBuildingLoad.speed - 0.7) < 0.0001,
        "建筑模块尚未加载身份时没有沿用持久状态")
    assert(data.storedFallback.layer == 0 and not data.storedFallback.blocks and math.abs(data.storedFallback.speed - 0.7) < 0.0001,
        "缺少活建筑模块时没有使用持久建筑状态")
    assert(data.mechanicalRemoved.layer == 9 and data.mechanicalRemoved.blocks and data.mechanicalRemoved.speed == 1,
        "机械模块移除后船件没有恢复自身配置")
    assert(data.floor.layer == 6 and not data.floor.blocks and data.floor.speed == 1,
        "船地板没有绕开无关建筑JSON并保持可通行规则")
end, "船件建筑状态实时来源与稳定查询零分配")
