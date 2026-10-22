-- 渲染参数由正式 IsValid 规则判定，图像效果需场景验收。
do
    local clear = { 0, 0, 0, 0 }
    local metal = { 90, 110, 140, 255 }
    local hairColor = { 70, 35, 15, 255 }
    local hide = { 0, 0, 0, 255 }
    local helmet = { metal, clear, clear, metal, clear, clear }
    local allHair = { clear, clear, clear, hide, hide, hide }
    local partialHair = { clear, clear, clear, hide, clear, clear }

    local function checkPixels(actual, expected, label, tolerance)
        assert(#actual == #expected, label .. "：输出画布尺寸错误")
        for index, pixel in ipairs(expected) do
            for channel = 1, 4 do
                if channel == 4 or pixel[4] ~= 0 then
                    assert(math.abs(actual[index][channel] - pixel[channel]) <= (tolerance or 0),
                        label .. "：第 " .. index .. " 像素第 " .. channel .. " 通道错误")
                end
            end
        end
    end

    -- 两套底图使用同一份头盔与遮发素材，半包围头盔的开口始终露出自己的脸。
    for _, skin in ipairs({ { 220, 165, 125, 255 }, { 135, 90, 60, 255 } }) do
        local body = { clear, skin, skin, skin, skin, clear }
        local hair = { clear, clear, clear, hairColor, hairColor, hairColor }
        local original = { clear, skin, skin, hairColor, hairColor, hairColor }
        local expected = {
            original,
            { metal, skin, skin, metal, skin, clear },
            { metal, skin, skin, metal, hairColor, hairColor },
            original,
            original,
            { metal, skin, clear, metal, hairColor, hairColor }
        }
        test.system("rendering", "character_layers", { body = body, hair = hair, steps = {
            {},
            { layers = { { wearable = helmet, hairMask = allHair } } },
            { layers = { { wearable = helmet, hairMask = partialHair } } },
            { layers = { { hairMask = allHair, bodyMask = allHair } } },
            {},
            { layers = { { wearable = helmet, bodyMask = { clear, clear, hide, clear, clear, clear } } } }
        } }, function(data)
            assert(#data.samples == 6, "穿脱装备没有返回全部步骤")
            for index, sample in ipairs(data.samples) do
                assert(not sample.rejection, "合法人物图层被拒绝")
                assert(sample.sourcesUnchanged, "穿戴合成修改了人物或装备原图，摘下后无法恢复")
                checkPixels(sample.pixels, expected[index], "共享头盔穿脱步骤 " .. index)
            end
            assert(data.samples[2].pixels[1][4] == 255, "装备不能扩展到原身体轮廓以外")
        end, "共享半包围头盔：保留自己的脸、整体/局部遮发、缺穿戴图不遮发、摘掉恢复")
    end

    for _, row in ipairs({
        { body = { { 200, 100, 50, 255 } }, hair = { { 0, 200, 0, 128 } },
            layers = { { wearable = { { 0, 0, 255, 128 } } } }, expected = { { 50, 75, 140, 255 } },
            label = "半透明头发和装备按先后叠加" },
        { body = { { 255, 0, 255, 0 } },
            layers = { { wearable = { { 80, 120, 200, 128 } } } }, expected = { { 80, 120, 200, 128 } },
            label = "透明底图不污染装备颜色和透明度" },
        { body = { clear }, layers = {
                { wearable = { { 255, 0, 0, 128 } } }, { wearable = { { 0, 0, 255, 128 } } }
            }, expected = { { 85, 0, 170, 192 } }, label = "两层半透明装备按正确透明度混合" },
        { body = { { 200, 100, 50, 255 } }, layers = {
                { wearable = { clear }, bodyMask = { { 0, 0, 0, 128 } } }
            }, expected = { { 200, 100, 50, 127 } }, label = "身体遮罩只减少覆盖部位的透明度" },
        { body = { clear }, hair = { { 70, 35, 15, 255 } }, layers = {
                { wearable = { clear }, hairMask = { { 0, 0, 0, 128 } } },
                { wearable = { clear }, hairMask = { { 0, 0, 0, 128 } } }
            }, expected = { { 70, 35, 15, 63 } }, label = "多件装备的半透明遮发遮罩叠乘" },
        { body = { { 200, 100, 50, 255 } }, layers = {
                { wearable = { { 255, 0, 0, 255 } } }, { wearable = { { 0, 0, 255, 255 } } }
            }, expected = { { 0, 0, 255, 255 } }, label = "外层装备盖住内层且不受身体分区裁切" }
    }) do
        test.system("rendering", "character_layers", {
            width = 1, height = 1, body = row.body, hair = row.hair, steps = { { layers = row.layers } }
        }, function(data)
            local sample = data.samples[1]
            assert(not sample.rejection and sample.sourcesUnchanged, "合法 Alpha 合成失败或修改原图")
            checkPixels(sample.pixels, row.expected, row.label, 1)
        end, "人物分层 Alpha：" .. row.label)
    end

    test.system("rendering", "character_layers", { width = 3, height = 1, sourceFacesLeft = true,
        body = { { 255, 0, 0, 255 }, { 0, 255, 0, 255 }, clear },
        steps = { { layers = { { wearable = { clear, clear, { 0, 0, 255, 255 } } } } } }
    }, function(data)
        assert(not data.samples[1].rejection and data.samples[1].sourcesUnchanged, "镜像合成失败或改写原图")
        checkPixels(data.samples[1].pixels,
            { { 0, 0, 255, 255 }, { 0, 255, 0, 255 }, { 255, 0, 0, 255 } }, "身体和装备一同镜像")
    end, "人物分层只在最终输出统一镜像，不错开装备与身体")

    for _, row in ipairs({
        { body = { clear }, label = "身体图尺寸不匹配" },
        { hair = { clear }, label = "头发图尺寸不匹配" },
        { layers = { { wearable = { clear } } }, label = "穿戴图尺寸不匹配" },
        { layers = { { wearable = helmet, hairMask = { clear } } }, label = "遮发图尺寸不匹配" },
        { layers = { { wearable = helmet, bodyMask = { clear } } }, label = "身体遮罩尺寸不匹配" }
    }) do
        test.system("rendering", "character_layers", {
            body = row.body or { clear, clear, clear, clear, clear, clear }, hair = row.hair,
            steps = { { layers = row.layers } }
        }, function(data)
            assert(data.samples[1].rejection == "ArgumentException", "未拒绝" .. row.label)
            assert(data.samples[1].sourcesUnchanged, "拒绝错误尺寸时修改了原图")
        end, "人物分层拒绝无效素材：" .. row.label)
    end
end

test.system("rendering", "terrain_shadow_dirty_queue", {}, function(data)
    assert(data.fullCount == 4096 and data.sparseVisits == 384 and data.fullScanVisits == 524288,
        "地形阴影小范围改动仍按全区块扫描，或重建时漏掉完整格子")
    assert(data.deduplicated and data.drained and data.retried and data.retryContents,
        "重复脏格未合并，或中断/失败后丢失待更新格")
    assert(data.fullReentrant and data.rebuilding and data.empty and data.cleared and data.reset,
        "提交中重建、清理或重新绑定遗留旧阴影脏格")
end, "地形阴影增量脏格队列、提交重入与异常重试")

test.system("rendering", "dropped_combustion_material", {}, function(data)
    assert(data.assigned, "掉落物燃烧粒子没有绑定材质，会显示紫块")
    assert(data.shared, "重复创建燃烧粒子没有复用共享材质")
    assert(data.shader == "Universal Render Pipeline/2D/Sprite-Unlit-Default" and data.supported,
        "掉落物燃烧材质没有使用可用的 URP 2D 着色器")
    assert(data.texture == "Circle", "掉落物燃烧材质丢失透明粒子贴图")
end, "掉落物燃烧粒子绑定可用材质并共享透明贴图")

test.system("rendering", "cave_shadow_lazy_creation", {}, function(data)
    assert(data.candidates == 1 and data.createdAtBind == 0 and data.createdWithoutLight == 0,
        "没有局部光的墙仍在绑定时创建 URP 阴影对象")
    assert(data.createdWhenLit == 1 and data.enabledWhenLit == 1 and data.hasMesh and data.selfShadows,
        "墙首次受光时未补齐原有遮挡网格和墙面阴影")
    assert(data.initialX == 1 and data.initialWidth == 2, "首次受光的合并墙轮廓错误")
    assert(data.disabledWithoutLight == 0 and data.retainedWithoutLight == 1 and data.sameAfterRelight,
        "光移走后没有关闭墙阴影，或再次受光时重复创建对象")
    assert(data.sameAfterRebind and data.sameMeshAfterRebind and data.retainedAtEnd == 1,
        "区块重绑重新创建了已有单位阴影网格")
    assert(data.reboundX == 2.5 and data.reboundY == 1.5 and data.reboundWidth == 3 and data.reboundHeight == 1,
        "区块复用没有刷新合并墙的位置和尺寸")
    assert(data.worldX == 17.5 and data.worldY == 13 and data.enabledAfterRebind == 1,
        "阴影筛选没有跟随区块位置和镜像缩放")
    assert(data.fallbackEnabled == 1, "无法判断局部光范围时丢失保守阴影回退")
end, "洞穴墙阴影首次受光按需创建并复用原网格")

for _, row in ipairs({
    { args = {}, level = 11, neighbour = 11, inner = 0, outer = 0, label = "同高异材质不画边" },
    { args = { support = true }, level = 11, neighbour = 11, inner = 0, outer = 0, label = "木地板与普通地面齐平" },
    { args = { depth = 1 }, level = 10, neighbour = 11, inner = 1, outer = 0, label = "挖掉第一层后向内凹" },
    { args = { depth = 2 }, level = 9, neighbour = 11, inner = 2, outer = 0, label = "每挖一层继续降低" },
    { args = { depth = 6 }, level = 5, neighbour = 11, inner = 6, outer = 0, label = "五层石头及土层采尽后基岩最低" },
    { args = { minimumDepth = 5 }, level = 6, neighbour = 11, inner = 5, outer = 0, label = "基岩底材至少向内凹五层" },
    { args = { depth = 6, neighbourDepth = 6 }, level = 5, neighbour = 5, inner = 0, outer = 0, label = "成片基岩内部没有接缝阴影" },
    { args = { neighbourDepth = 1 }, level = 11, neighbour = 10, inner = 0, outer = 1, label = "较高地块只向外侧投影" },
    { args = { depth = 6, support = true }, level = 11, neighbour = 11, inner = 0, outer = 0, label = "地板跨过挖坑回到地面齐平" },
    { args = { depth = 2, covers = 1 }, level = 10, neighbour = 11, inner = 1, outer = 0, label = "填土只回填一层" },
    { args = { covers = 1 }, level = 12, neighbour = 11, inner = 0, outer = 1, label = "额外填高后向外投影" },
    { args = { liquid = true }, level = 12, neighbour = 11, inner = 0, outer = 1, label = "平地倒水高一层并向外投影" },
    { args = { liquid = true, naturalDepth = 2 }, level = 10, neighbour = 11, inner = 1, outer = 0, label = "浅水底床低两层且水面低于岸一层" },
    { args = { liquid = true, naturalDepth = 3 }, level = 10, neighbour = 11, inner = 1, outer = 0, label = "深水底床低三层但水面仍只低于岸一层" },
    { args = { liquid = true, naturalDepth = 2, neighbourLiquid = true, neighbourNaturalDepth = 3, neighbourHeight = 0.5 }, level = 10, neighbour = 9, inner = 0, outer = 1, label = "河床深浅不产生台阶但真实地势高差保留" },
    { args = { naturalDepth = 2 }, level = 9, neighbour = 11, inner = 2, outer = 0, label = "浅水抽干仍保留天然底床" },
    { args = { naturalDepth = 3, depth = 1 }, level = 7, neighbour = 11, inner = 4, outer = 0, label = "天然底床继续采挖时叠加深度" },
    { args = { naturalDepth = 2, covers = 1 }, level = 10, neighbour = 11, inner = 1, outer = 0, label = "天然底床填土逐层回升" },
    { args = { liquid = true, naturalDepth = 3, support = true }, level = 11, neighbour = 11, inner = 0, outer = 0, label = "天然深水上平台仍与岸齐平" },
    { args = { liquid = true, depth = 1 }, level = 11, neighbour = 11, inner = 0, outer = 0, label = "一层坑中倒水与岸齐平" },
    { args = { liquid = true, depth = 2 }, level = 10, neighbour = 11, inner = 1, outer = 0, label = "两层坑中倒水仍向坑内投影" },
    { args = { liquid = true, naturalDepth = 2, surface = false }, level = -1, neighbour = 0, inner = 1, outer = 0, label = "洞穴水床同样使用真实高差" },
    { args = { liquid = true, naturalDepth = 3, surface = false }, level = -1, neighbour = 0, inner = 1, outer = 0, label = "洞穴深浅水面保持同一高度" },
    { args = { liquid = true, support = true }, level = 11, neighbour = 11, inner = 0, outer = 0, label = "水上平台表面仍与岸边齐平" },
    { args = { depth = 1, surface = false }, level = -1, neighbour = 0, inner = 1, outer = 0, label = "洞穴忽略生成海拔但保留采挖高差" },
    { args = { missingHeight = true }, level = 0, neighbour = 0, inner = 0, outer = 0, label = "缺失高度回退平地" },
    { args = { height = 1 }, level = 19, neighbour = 19, inner = 0, outer = 0, label = "最高生成高度不会溢出档位" }
}) do
    test.system("rendering", "terrain_height", row.args, function(data)
        assert(data.level == row.level and data.neighbour == row.neighbour, "统一表面高度错误")
        assert(data.inner == row.inner and data.outer == row.outer, "阴影没有只落在较低一侧")
        assert(not data.outside, "未知邻格被伪造成有效高度")
        if row.args.liquid then assert(data.waterLevel == 5, "高度查询改变了权威水量") end
    end, "地表高低阴影：" .. row.label)
end

-- 深浅底床与剩余水量可以不同，同一地势的天然水面仍须齐平。
for _, surface in ipairs({ true, false }) do
    for _, naturalDepth in ipairs({ 2, 3 }) do
        for _, liquidLevel in ipairs({ 1, 5, 6, 10 }) do
            test.system("rendering", "terrain_height", {
                liquid = true, naturalDepth = naturalDepth, liquidLevel = liquidLevel,
                neighbourLiquid = true, neighbourNaturalDepth = 5 - naturalDepth,
                neighbourLiquidLevel = 11 - liquidLevel, surface = surface
            }, function(data)
                assert(data.level == data.base - 1 and data.neighbour == data.level,
                    "天然浅水与深水的水面没有齐平")
                assert(data.inner == 0 and data.outer == 0, "水内深浅交界仍出现高低台阶阴影")
                assert(data.waterLevel == liquidLevel and data.neighbourWaterLevel == 11 - liquidLevel,
                    "统一水面高度改写了真实水量")
            end, "天然深浅水面齐平：" .. tostring(surface) .. "/" .. naturalDepth .. "/" .. liquidLevel)
        end
    end
end

test.system("rendering", "terrain_height", { wall = true }, function(data)
    assert(data.top == 12 and data.inner == 1, "墙脚没有通过统一顶部高度向周围投影")
    assert(not data.neighbourReceiver and data.outer == 0, "墙体错误接收贴地阴影")
end, "墙脚接入同一套高低阴影规则")

test.system("rendering", "terrain_height_save", {}, function(data)
    assert(data.resourceDepth == 5 and data.surfaceDepth == 6 and data.savedDepth == 6,
        "地表深度与五层石头产量混用，或保存恢复丢失新高度层")
    assert(data.level == 5 and math.abs(data.rawHeight - 0.55) < 0.0001, "采挖高差污染生成高度")
    assert(data.negativeRejected, "非法负采挖深度没有被拒绝")
end, "地表高低层保存恢复且不改生成高度和石头产量")

test.system("rendering", "terrain_height_save", { naturalDepth = 2 }, function(data)
    assert(data.naturalDepth == 2 and data.surfaceDepth == 6 and data.level == 3,
        "采挖快照恢复覆盖了天然底床，或把天然凹陷混进玩家采挖层数")
end, "天然底床独立于玩家采挖快照恢复")

test.system("rendering", "terrain_height_neighbours", {}, function(data)
    assert(data.dimensionServicePreserved, "只读高度查询创建或替换了全局维度管理器")
    assert(not data.missing and data.available and data.initial == 0, "缺失或刚加载的邻区高度查询错误")
    assert(data.changed and data.lowered == -3, "跨区块查询没有立即读取新的采挖深度")
    assert(data.evicted and not data.afterEviction, "邻区卸载后仍返回旧高度")
    assert(data.reloaded and data.restored == 0, "邻区重载后复用了旧高度缓存")
end, "跨区块高低查询跟随邻区加载、采挖、卸载与重载")

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

test.system("rendering", "shadow_footprint_matrix", {}, function(data)
    assert(data.cases == 192 and data.maximumError < 0.0002,
        "角色阴影仿射包围盒与原四角投影不一致，误差：" .. tostring(data.maximumError))
end, "接触与太阳阴影的四角投影数学等价：镜像、旋转、偏心与缩放")

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
    { vacuum = true, expectedStarfield = true },
    { expectedStarfield = false, initialStarfield = true },
    { remainingFraction = 0, expectedStarfield = true },
    { remainingFraction = 0.000000001, expectedStarfield = false },
    { missingAtmosphere = true, expectedStarfield = false, initialStarfield = true }
}) do
    test.system("rendering", "zoom_cloud_mask", args, function(data)
        assert(data.starfield == args.expectedStarfield, "加载边界没有按真实大气切换云层和星空")
        assert(data.clearWidth == 80 and data.clearHeight == 80, "背景切换改变了清晰加载范围")
        assert(data.opaqueWidth == 112 and data.opaqueHeight == 112, "星空没有沿用原来的完全遮挡边界")
        assert(data.featherX == 16 and data.featherY == 16, "背景切换改变了原来的柔边宽度")
    end, "无大气加载边界用星空且返回有气星球时恢复云层")
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

-- 采样正式世界半径轨迹，起雾与散雾都按完整真实秒计时。
for _, args in ipairs({ { strength = 1, radius = 5 }, { strength = 0.1, radius = 50 } }) do
    test.system("rendering", "fog_transition", { steps = {
        { target = args.strength }, { target = args.strength, seconds = 15 },
        { target = args.strength, seconds = 14.9 }, { target = args.strength, seconds = 0.1 },
        { target = 0 }, { target = 0, seconds = 15 },
        { target = 0, seconds = 14.9 }, { target = 0, seconds = 0.1 }
    } }, function(data)
        local s = data.samples
        assert(data.validConfig and data.defaultDuration == 30, "天气雾没有使用 30 秒正式配置")
        assert(data.defaultClearRadius == 5 and data.defaultOpaqueRadius == 25,
            "天气雾柔边没有从 10 格精确加倍到 20 格，或改变了满强度清晰范围")
        assert(s[1].hasValue and not s[1].visible and s[1].clearRadius == 200,
            "首次起雾直接跳到目标，未从画面外围开始")
        assert(s[2].visible and s[2].clearRadius > args.radius and s[2].clearRadius < s[1].clearRadius,
            "起雾没有从外围向内聚拢")
        assert(s[3].transitioning and s[3].clearRadius > args.radius,
            "起雾在完整 30 秒之前到达目标，小雾不能缩短时间")
        assert(not s[4].transitioning and math.abs(s[4].clearRadius - args.radius) < 0.0001,
            "起雾满 30 秒后没有到达所在地浓度对应半径")
        assert(s[5].clearRadius == s[4].clearRadius and s[5].visible,
            "开始散雾时半径发生跳变")
        assert(s[6].visible and s[6].clearRadius > s[5].clearRadius and s[6].clearRadius < 200,
            "散雾没有从当前位置缓慢退向外围")
        assert(s[7].transitioning and s[7].visible, "散雾在完整 30 秒前提前关闭")
        assert(not s[8].transitioning and not s[8].visible and s[8].clearRadius == 200,
            "散雾满 30 秒后仍保留遮挡")
        for _, sample in ipairs(s) do
            assert(math.abs(sample.opaqueRadius - sample.clearRadius - 20) < 0.0001,
                "起雾或散雾途中改变了加倍后的柔边宽度")
        end
    end, "天气雾大小强度均用完整 30 秒聚拢与散开：" .. args.strength)
end

test.system("rendering", "fog_transition", { steps = {
    { target = 1 }, { target = 1, seconds = 29 }, { target = 0.5 },
    { target = 0.5, seconds = 1 }, { target = 0.5, seconds = 28.9 },
    { target = 0.5, seconds = 0.1 }, { target = 1, seconds = 10 },
    { target = 0 }, { target = 0, seconds = 29.9 }, { target = 0, seconds = 0.1 }
} }, function(data)
    local s = data.samples
    assert(s[2].clearRadius == s[3].clearRadius and s[3].progress == 0,
        "跨到另一浓度的雾区时没有从当前半径重新接续")
    assert(s[4].transitioning and s[4].clearRadius < 10 and s[5].transitioning,
        "临近完成时换雾区沿用旧时钟，把新过渡压缩到最后一秒")
    assert(not s[6].transitioning and math.abs(s[6].clearRadius - 10) < 0.0001,
        "新雾区目标没有在自己的完整 30 秒后收敛")
    assert(s[7].visible and s[7].clearRadius > 5 and s[7].clearRadius < 10,
        "重新起浓雾没有从现有雾半径接续")
    assert(s[7].clearRadius == s[8].clearRadius and s[8].progress == 0,
        "起雾中途转为散雾时画面跳变")
    assert(s[9].transitioning and s[9].visible and not s[10].visible,
        "中途反向散雾没有重新使用完整 30 秒")
end, "天气雾跨浓度区与起散反向从当前状态接续")

test.system("rendering", "fog_transition", { steps = {
    { target = 1 }, { target = 1, seconds = 10 }, { target = 1 },
    { target = 1, seconds = -1 }, { target = 1, seconds = 20 }, { reset = true },
    { target = 1 }, { target = 1, seconds = 30 }
} }, function(data)
    local s = data.samples
    assert(s[2].clearRadius == s[3].clearRadius and s[3].clearRadius == s[4].clearRadius,
        "零或负真实时间推进了天气雾")
    assert(not s[5].transitioning and s[5].clearRadius == 5,
        "重复采样同一目标重启了过渡计时")
    assert(not s[6].hasValue and not s[6].visible, "重置后遗留了旧玩家或旧场景的雾状态")
    assert(s[7].clearRadius == 200 and not s[7].visible and s[8].clearRadius == 5,
        "重置后首次采样未重新从画面外聚拢")
end, "天气雾零负时间、重复采样和生命周期重置")

test.system("rendering", "fog_transition", { duration = 0, steps = {
    { target = 0.1 }, { target = 0 }
} }, function(data)
    local s = data.samples
    assert(data.validConfig and s[1].visible and not s[1].transitioning and s[1].clearRadius == 50,
        "零时长天气雾没有立即到达目标")
    assert(not s[2].visible and not s[2].transitioning, "零时长散雾没有立即结束")
end, "天气雾零时长配置立即收敛")

for _, args in ipairs({
    { expectedRadius = 5.01 },
    { originX = -1, expectedRadius = math.sqrt(41) + 0.01 },
    { originX = -12, rightX = 24, expectedRadius = math.sqrt(160) + 0.01 }
}) do
    test.system("rendering", "fog_transition", args, function(data)
        assert(math.abs(data.screenClearRadius - args.expectedRadius) < 0.0001,
            "天气雾屏外起点没有覆盖实际横竖比例或偏心投影的最远角")
    end, "天气雾屏外起点覆盖实际投影四角")
end

test.system("rendering", "fog_transition", { steps = {
    { target = 1, seconds = 12 }, { target = 1, seconds = 29.9 },
    { target = 1, seconds = 0.1 }, { reset = true },
    { target = 1 }, { target = 1, seconds = 15 }, { target = 0 },
    { target = 0, seconds = 10 }, { target = 0, outside = 300 },
    { target = 0, outside = 300, seconds = 20 },
    { target = 0.01, outside = 50, seconds = 30 }, { target = 1, outside = 50 },
    { target = 1, outside = 50, seconds = 15 }, { target = 1, outside = 50, seconds = 15 }
} }, function(data)
    local s = data.samples
    assert(s[1].clearRadius == 200 and s[1].progress == 0 and not s[1].visible,
        "加载首帧的大真实时间吞掉了起雾过渡")
    assert(s[2].transitioning and s[2].clearRadius > 5 and
        not s[3].transitioning and s[3].clearRadius == 5,
        "加载完成后没有重新使用完整 30 秒聚拢")
    assert(s[6].clearRadius == s[7].clearRadius and s[7].progress == 0,
        "起雾半途转为散雾时当前半径跳变")
    assert(s[8].clearRadius == s[9].clearRadius and s[8].progress == s[9].progress,
        "零时间改变视口端点时雾边跳变或重启了散雾计时")
    assert(not s[10].transitioning and not s[10].visible and s[10].clearRadius == 300,
        "视口改变后没有按原本完整 30 秒在新画面外围结束散雾")
    assert(not s[11].visible and s[11].clearRadius == 50,
        "完全位于画面外的弱雾仍保留了多余的物理半径")
    assert(s[12].clearRadius == 50 and s[12].progress == 0 and
        s[13].visible and s[13].clearRadius < 50 and s[13].clearRadius > 5,
        "屏外弱雾转强雾没有从当前视口外围接续，浪费了可见过渡时间")
    assert(not s[14].transitioning and s[14].clearRadius == 5,
        "屏外弱雾转强雾满 30 秒后没有达到目标")
end, "天气雾加载首帧、视口端点改变与屏外弱雾接续")
test.system("rendering", "snow_sparse_refresh", {}, function(data)
    assert(data.emptySkipped and data.zeroLayerSkipped and data.clearedSkipped,
        "没有积雪的区块仍进行逐格渲染扫描")
    assert(data.naturalDetected and data.weatherDetected and data.editedDetected,
        "自然雪、天气雪或玩家编辑雪被错误当作空区块")
    assert(data.compared == 5 and data.maximumError == 0,
        "复用有效地表改变了覆盖面、编辑雪或无地面时的雪厚")
end, "暖区块跳过全格覆雪扫描与已有雪厚结果一致")

test.system("rendering", "effect_composition", {}, function(data)
    assert(data.snowIdleMin == 42, "仅覆雪且参数未变时重复写入了MPB")
    assert(math.abs(data.snowDisabledMin + 4) < 0.0001, "效果启停后没有重新提交材质参数")
    assert(math.abs(data.waterFirstMin) < 0.0001 and math.abs(data.waterChangedMin + 4) < 0.0001,
        "水体效果的身体高度未跟随Sprite换图")
    assert(data.diagnosticFrames == 6 and data.diagnosticIdleFrames == 1,
        "渲染诊断未记录真实帧数和整轮空闲跳过")
    assert(data.diagnosticSubmitted == 4 and data.diagnosticSpriteSkipped == 1,
        "渲染诊断未区分MPB提交与同图跳过")
end, "静态覆雪免重传、效果启停与水线换图合成")


test.system("rendering", "ship_block_visual", {}, function(data)
    local s = {}
    for _, value in ipairs(data.samples) do
        s[value.name] = value
        assert(not value.sourceVisible, "双扇门显示时主体没有隐藏")
        assert(value.sourceScaleX == 2 and math.abs(value.sourceRotation - 90) < 0.001,
            "门动画改写了安装缩放或旋转")
    end
    assert(s.closed.progress == 0 and math.abs(s.closed.leftX + 0.25) < 0.0001
        and math.abs(s.closed.rightX - 0.25) < 0.0001, "关闭门板没有完整拼合一格")
    assert(math.abs(s.opening.progress - 0.5) < 0.0001 and s.opening.leftX < s.closed.leftX,
        "开门没有连续向两边滑动")
    assert(math.abs(s.reversing.progress - 0.25) < 0.0001
        and s.reversing.leftX > s.opening.leftX, "中途关门没有从当前进度反转")
    assert(s.closed_again.progress == 0 and s.open.progress == 1
        and s.restored_open.progress == 1, "关门终点、开门终点或首次开门恢复错误")
    assert(s.appearance.order == 37 and math.abs(s.appearance.green - 0.4) < 0.0001,
        "门叶没有同步主体排序与颜色")
    assert(data.sourceRestored and data.activeLeavesAfterRelease == 0, "回收未恢复主体或残留门叶")
    assert(data.engineSize == 1 and data.gmEngineSize == 1, "普通或GM化学引擎仍被缩成非满格")
    local expected = {0.85, 0.55, 0.32}
    for i, value in ipairs(data.defaultBodySizes) do
        assert(math.abs(value - expected[i]) < 0.0001, "引擎满格改动改变了其它设备默认尺寸")
    end
    for _, rejected in ipairs(data.invalidRejected) do assert(rejected, "无效机身尺寸没有被拒绝") end
end, "化学引擎满格、双扇门双向动画、反转恢复与回收")
