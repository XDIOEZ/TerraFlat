-- 必须紧跟前置租约Lua；缺少前置时明确失败。
test.runtime("testing", "previous_cleanup", {}, function(d)
    assert(d.cleaned and d.asyncCleaned, "上一份Lua的同步或跨帧清理未在下一案例前完成")
    assert(d.samples == 2 and d.profilePreserved, "上一份队列执行不完整或角色档案串用")
    assert(type(d.controlOwner) == "string" and #d.controlOwner > 0, "下一份Lua没有重新获得玩家控制")
end, "跨Lua核对真实执行器的收尾顺序")
