-- 第一份Lua留下自有清理租约，收尾后由下一份Lua核对。
local token
test.check("world_ready", "测试宿主的真实世界就绪")
test.runtime("testing", "begin_lease", {}, function(d)
    token = d.token
    assert(type(token) == "string" and #token > 0, "没有创建测试租约")
    assert(not d.cleaned and not d.asyncCleaned, "租约提前释放")
    assert(type(d.controlOwner) == "string" and #d.controlOwner > 0, "真实玩家没有控制租约")
end, "登记本Lua的同步和跨帧收尾")
test.wait(.1)
for expected = 1, 2 do
    local index = expected
    test.runtime("testing", "sample_lease", {}, function(d)
        assert(d.token == token and d.sample == index, "命令顺序或回调状态没有保留")
        assert(d.elapsed >= .1 and d.frames >= 1, "等待没有实际跨帧")
        assert(d.profilePreserved and not d.cleaned and not d.asyncCleaned, "租约在Lua结束前丢失")
    end, "验证实际命令队列采样" .. index)
end
