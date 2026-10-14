-- 只执行独立资源计划，阶段依赖和失败清理由生产管线处理。
test.system("core_lifecycle", "resource_plan", { mode = "ordered" }, function(data)
    assert(data.failure == nil, "合法资源依赖被拒绝")
    assert(#data.order == 2 and data.order[1] == "startup" and data.order[2] == "content", "资源没有按依赖执行")
    assert(data.finished == 2, "正常阶段没有释放迭代器")
    local previous = -1
    for _, value in ipairs(data.progress) do
        assert(value >= previous and value >= 0 and value <= 1, "加载进度倒退或越界")
        previous = value
    end
    assert(previous == 1, "全部阶段完成后进度没有达到一")
end, "资源依赖顺序与单调进度")

for _, mode in ipairs({ "missing", "cycle" }) do
    test.system("core_lifecycle", "resource_plan", { mode = mode }, function(data)
        assert(data.failure == "InvalidOperationException" and #data.order == 0, "非法依赖没有在执行前拒绝")
        assert(data.finished == 0, "错误依赖仍执行了资源阶段")
    end, "资源拒绝非法依赖：" .. mode)
end

test.system("core_lifecycle", "resource_plan", { mode = "throw" }, function(data)
    assert(data.failure == "InvalidOperationException" and data.stage == "content", "没有保留失败阶段和异常")
    assert(#data.order == 2 and data.finished == 2, "异常阶段没有执行 finally 清理")
end, "资源阶段异常与清理")

for _, row in ipairs({ { fail = false, commit = true }, { fail = true, commit = true }, { fail = false, commit = false } }) do
    test.system("core_lifecycle", "resource_candidate", row, function(data)
        assert(#data.betweenFrames == 1 and data.betweenFrames[1], "候选加载让出帧时没有恢复正式目录")
        assert(data.disposed == 1 and data.preservedBeforeCommit and data.officialPreserved, "候选释放或正式目录隔离错误")
        if row.fail then
            assert(data.error == "InvalidOperationException" and not data.committed and not data.hasCandidate, "失败候选仍被发布")
        elseif row.commit then
            assert(data.error == nil and data.committed and data.hasCandidate and data.hasSecond and data.hasCleanup, "完成候选没有完整发布或 comparer 丢失")
        else
            assert(data.error == nil and not data.committed and not data.hasCandidate, "尚未提交的候选污染正式目录")
        end
    end, "资源候选跨帧恢复、释放与原子发布")
end
