-- 正式端点解析和网络物品字节协议无需创建 Host/Client。
for _, row in ipairs({
    { address = "localhost", fallbackPort = 7777, host = "localhost", port = 7777 },
    { address = " kcp://example.com:9000 ", fallbackPort = 7777, host = "example.com", port = 9000 },
    { address = "udp://127.0.0.1:65535", fallbackPort = 7777, host = "127.0.0.1", port = 65535 },
    { address = "[::1]:8000", fallbackPort = 0, port = 8000 },
    { address = "localhost", fallbackPort = 0, host = "localhost", port = 7777 },
    { address = "kcp://localhost", fallbackPort = 9001, host = "localhost", port = 9001 },
    { address = "udp://localhost:7777", fallbackPort = 9001, host = "localhost", port = 7777 },
    { address = "localhost:1", fallbackPort = 9001, host = "localhost", port = 1 },
    { address = "kcp://localhost:00001", fallbackPort = 9001, host = "localhost", port = 1 },
    { address = "[::1]", fallbackPort = 9001, port = 9001 },
    { address = "udp://[::1]:65535", fallbackPort = 7777, port = 65535 }
}) do
    test.system("networking", "endpoint", row, function(data)
        assert(data.accepted and data.port == row.port and data.error == "", "合法联机端点解析失败")
        if row.host then assert(data.host == row.host, "主机地址解析错误") end
        assert(data.display ~= nil and #data.display > 0, "端点无法显示")
    end, "联机端点：" .. row.address)
end

for _, address in ipairs({ "", "http://localhost:7777", "kcp://user@localhost:7777", "kcp://localhost:7777/path", "kcp://localhost:7777?x=1",
    "kcp://localhost:0", "localhost:0", "udp://localhost:00000", "kcp://[::1]:0", "[::1]:0",
    "kcp://localhost:65536", "localhost:-1", "kcp://localhost:abc", "kcp://localhost:", "udp://[::1]:65536" }) do
    test.system("networking", "endpoint", { address = address }, function(data)
        assert(not data.accepted and data.error ~= nil and #data.error > 0, "非法联机端点被接受")
    end, "拒绝非法联机端点：" .. address)
end

test.system("networking", "item_payload", { guid = 143, amount = 3 }, function(data)
    assert(data.serialized and data.valid and data.read and data.bytes > 4, "正式网络实例负载无法读取")
    assert(data.guid == 143 and data.itemId == "probe:item", "网络快照身份错误")
    assert(not data.corruptedAccepted and not data.emptyAccepted, "网络协议接受损坏或空负载")
    assert(data.sourceAmount == 3, "网络捕获修改了源物品")
end, "网络实例身份与协议头拒绝")
