using MCPForUnity.Editor.Services;
using UnityEditor;

namespace FlatWorld.GameplayMCP
{
    /// <summary>
    /// 在编辑器域重载完成后重建 MCP 工具发现缓存，确保项目级 GamePlayMCP 工具不会被早期缓存遗漏。
    /// </summary>
    [InitializeOnLoad]
    public static class GameplayMcpToolDiscoveryBootstrap
    {
        static GameplayMcpToolDiscoveryBootstrap()
        {
            EditorApplication.delayCall += RefreshToolDiscovery;
        }

        /// <summary>延迟到程序集全部加载后重新发现项目自定义 MCP 工具。</summary>
        private static void RefreshToolDiscovery()
        {
            var discovery = MCPServiceLocator.ToolDiscovery;
            discovery.InvalidateCache();
            discovery.DiscoverAllTools();
        }
    }
}
