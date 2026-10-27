# MCP 传输回归

在项目根目录运行：

    dotnet run --project Tools/Tests/McpTransport/McpTransport.Tests.csproj

直接编译生产 WebSocket 客户端，通过本机临时 WebSocket 服务检查长命令与工具注册期间的心跳、执行顺序、断连取消和命令超时；仅命令宿主与 Unity 入口使用替身。不连接或重启 Unity，不代表实际 Game View 截图已经验收。
