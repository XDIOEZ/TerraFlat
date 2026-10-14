-- Lua 提供补丁组合，C# 只调用 MOD 正式补丁实现。
test.system("modding", "patch", { target = { count = 1, other = 9 }, path = "count", operation = "replace", value = 3, expect = 1 }, function(data)
    assert(data.accepted and data.target.count == 3 and data.target.other == 9, "带前置条件替换失败或污染旁支")
end, "MOD 补丁替换与 expect 条件")

test.system("modding", "patch", { target = { count = 1 }, path = "count", operation = "replace", value = 3, expect = 2 }, function(data)
    assert(not data.accepted and data.target.count == 1, "错误前置条件仍修改内容")
end, "MOD 拒绝不匹配补丁前置")

test.system("modding", "patch", { target = { count = 1 }, path = "missing", operation = "replace", value = 3 }, function(data)
    assert(not data.accepted and data.target.count == 1, "replace 误创建不存在字段")
end, "MOD replace 拒绝缺失目标")

test.system("modding", "patch", { target = { tags = { "wood", "fuel" }, keep = 1 }, path = "tags", operation = "remove", value = "fuel" }, function(data)
    assert(data.accepted and #data.target.tags == 1 and data.target.tags[1] == "wood" and data.target.keep == 1, "MOD 数组删除范围错误")
end, "MOD 数组定值删除")

test.system("modding", "patch", { target = { nested = { a = 1, values = { 1, 2 } } }, path = "nested", operation = "merge", value = { b = 3, values = { 2, 4 } } }, function(data)
    assert(data.accepted and data.target.nested.a == 1 and data.target.nested.b == 3, "MOD 合并对象丢失字段")
    assert(#data.target.nested.values == 3, "MOD 数组并集合并未去重")
end, "MOD 对象合并与数组并集")

for _, row in ipairs({ { path = "Scripts/main.lua", expected = true }, { path = "../outside.lua", expected = false }, { path = "", expected = false } }) do
    test.system("modding", "package_path", row, function(data)
        assert(data.accepted == row.expected, "包路径边界判断错误")
        if row.expected then assert(data.relative == row.path, "包内路径解析错误") end
    end, "MOD 包内路径：" .. row.path)
end

for _, fail in ipairs({ false, true }) do
    test.system("modding", "candidate_patch", { fail = fail }, function(data)
        assert(data.officialCount == 1 and data.other == 9, "候选补丁修改了旧目录或旁支")
        if fail then
            assert(data.error == "InvalidDataException" and not data.published and data.actualCount == 1, "部分成功补丁在后续失败后被发布")
        else
            assert(data.error == nil and data.published and data.actualCount == 5, "全部补丁完成后没有发布候选")
        end
    end, "MOD 候选补丁全部成功才发布")
end
