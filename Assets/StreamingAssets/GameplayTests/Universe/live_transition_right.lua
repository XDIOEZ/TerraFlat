-- 以正式太空入场准备速度，随后由真实固定步越界、释放租约并折返。
test.space("sector_transition", function(data)
    local function same(a, b) return a.x == b.x and a.y == b.y end
    local function contains(values, expected)
        for _, value in ipairs(values) do if same(value, expected) then return true end end
        return false
    end
    assert(data.scope == "live-sector-transition" and data.direction == "right", "没有采样指定方向的真实过渡")
    assert(same(data.before.sector, data.source), "初始玩家不在源星区")
    assert(contains(data.before.required, data.source) and contains(data.before.required, data.destination), "行进方向星区没有提前加载")
    assert(same(data.crossed.sector, data.destination), "真实移动未越过目标边界")
    assert(contains(data.crossed.required, data.source) and contains(data.crossed.required, data.destination), "过渡带没有保留两侧租约")
    assert(contains(data.crossed.loadedScenes, data.source) and contains(data.crossed.loadedScenes, data.destination), "过渡两侧实际场景缺失")
    assert(same(data.settled.sector, data.destination) and not contains(data.settled.required, data.source), "离开过渡带后源星区没有释放")
    assert(same(data.returned.sector, data.source), "玩家无法自然折返")
    assert(contains(data.returned.required, data.destination), "折返时没有保留目标侧过渡场景")
    assert(data.before.playerInstance == data.crossed.playerInstance and data.crossed.playerInstance == data.returned.playerInstance, "普通星区越界重建了玩家")
    assert(data.before.playerProfile == data.returned.playerProfile, "过渡后玩家身份丢失")
    assert(data.before.realmRevision == data.crossed.realmRevision and data.crossed.realmRevision == data.settled.realmRevision, "普通星区过渡错误地改变维度版本")
    assert(not data.crossed.dimensionTransitioning and not data.returned.dimensionTransitioning, "普通星区跨界触发了维度加载")
    assert(data.returned.seconds > data.before.seconds, "过渡期间宇宙时钟没有继续推进")
    return "right：真实越界、双侧场景、租约清退、玩家连续性与折返"
end, "真实星区 right 过渡", "right")
