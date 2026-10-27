for _, eligible in ipairs({ false, true }) do
    local expected = eligible
    test.system("guide", "eligibility", { eligible = expected }, function(data)
        assert(not data.before and data.eligible == expected, "教程资格没有按本地角色显式建立")
        assert(data.unchangedOnRepeat, "重复建立资格改变了保存数据")
        assert(data.other.keep == 42 and data.custom == 7, "教程资格覆盖其他系统或命名空间字段")
    end, "新手引导本地资格：" .. tostring(eligible))
end
