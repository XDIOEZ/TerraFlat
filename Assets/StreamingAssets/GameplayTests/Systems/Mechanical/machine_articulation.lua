local function near(actual, expected, message)
    assert(type(actual) == "number" and actual == actual and math.abs(actual - expected) < .00001,
        message .. "：" .. tostring(actual))
end

local function cycle(data)
    local first = data.frames[1]
    local leftMin, leftMax, rightMin, rightMax = math.huge, -math.huge, math.huge, -math.huge
    for _, frame in ipairs(data.frames) do
        local left, right = frame.left, frame.right
        assert(left.solved and right.solved, "合法气泵几何在某个曲柄相位失去解")
        near(left.pinX, right.pinX, "左右连杆未连接同一曲柄销")
        near(left.pinY, right.pinY, "左右连杆曲柄销高度不一致")
        near((left.pinX - data.centerX)^2 + (left.pinY - data.centerY)^2,
            data.radius^2, "曲柄销未沿固定半径转动")
        for _, rod in ipairs({left, right}) do
            near((rod.sliderX - rod.pinX)^2 + (rod.sliderY - rod.pinY)^2,
                data.length^2, "刚性连杆拉长、缩短或销轴脱节")
            near(rod.pinX + math.cos(rod.rodAngle) * data.length, rod.sliderX,
                "连杆旋转角未对准活塞销横坐标")
            near(rod.pinY + math.sin(rod.rodAngle) * data.length, rod.sliderY,
                "连杆旋转角未对准活塞销高度")
        end
        near(left.sliderX, data.leftAxisX, "左活塞横向偏离缸轴")
        near(right.sliderX, data.rightAxisX, "右活塞横向偏离缸轴")
        leftMin, leftMax = math.min(leftMin, left.sliderY), math.max(leftMax, left.sliderY)
        rightMin, rightMax = math.min(rightMin, right.sliderY), math.max(rightMax, right.sliderY)
    end
    assert(leftMax - leftMin > data.radius * 2 and rightMax - rightMin > data.radius * 2,
        "偏置双缸行程没有真实往复")
    local middle = data.frames[math.floor((#data.frames - 1) / 2) + 1]
    assert((first.left.sliderY - first.right.sliderY) *
        (middle.left.sliderY - middle.right.sliderY) < 0, "左右偏置双缸未交替做功")
    local last = data.frames[#data.frames]
    near(last.left.sliderY, first.left.sliderY, "左活塞完成一圈后没有精确回位")
    near(last.right.sliderY, first.right.sliderY, "右活塞完成一圈后没有精确回位")
    for _, accepted in ipairs(data.invalidAccepted) do
        assert(not accepted, "无效相位、半径或无解杆长没有被拒绝")
    end
end

test.system("mechanical", "articulated_pump_cycle", {samples = 128}, cycle,
    "气泵逐零件：固定曲柄半径、双缸轴线、刚性连杆端点与整圈回位")
test.system("mechanical", "articulated_pump_cycle", {samples = 128, lowerBranch = true}, cycle,
    "气泵关节解算：另一滑块分支同样保持杆长与销轴约束")

test.system("mechanical", "articulated_pump_working", {}, function(data)
    assert(#data.states == 48, "工作门禁没有完整采样启停、动力、流量、损坏与正反转")
    for _, state in ipairs(data.states) do
        local expected = state.active and state.powered and state.transferring and not state.ruptured
            and state.inputRpm or 0
        near(state.rpm, expected, "待机、断动力、无实际流量或损坏时仍假装抽气")
    end
    near(data.nanRpm, 0, "非法转速污染运动")
    near(data.infiniteRpm, 0, "无限转速污染运动")
end, "气泵反馈只消费真实工作资格：断供、堵塞、待机、损坏、反转与非法输入")

test.system("mechanical", "articulated_motion_clock", {}, function(data)
    near(data.quarter, math.pi / 2, "60转每分钟的四分之一秒相位错误")
    near(data.afterSwitch, data.beforeSwitch, "变速反转时先丢弃旧相位导致机械跳动")
    near(data.reversed, 0, "反转没有沿原相位返回")
    near(data.stopped, data.held, "停机后机械仍在移动")
    near(data.restart, data.stopped, "长时间待机后重启跳相")
    near(data.resumed, math.pi / 2, "重启没有恢复实际转速")
    near(data.split, data.whole, "相同总时间细分后机械相位漂移")
    near(math.sin(data.negative), -1, "负转速未沿反方向推进相位")
    near(math.cos(data.negative), 0, "负转速的四分之一周期端点错误")
    for _, phase in ipairs(data.invalidTime) do near(phase, .3, "无效推进时间改变相位") end
    near(data.invalidRpm, .3, "非法转速推进破坏原相位")
end, "关节相位：变速、反转、长停机重启、分步一致与非法输入隔离")
