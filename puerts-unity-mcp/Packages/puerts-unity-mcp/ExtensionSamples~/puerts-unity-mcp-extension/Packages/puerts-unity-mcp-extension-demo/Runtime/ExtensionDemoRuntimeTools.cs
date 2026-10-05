using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PuertsUnityMcp.ExtensionDemo
{
    public sealed class ExtensionDemoRuntimeTools : IUnityMcpToolProvider
    {
        public string EndpointKind => "runtime";

        public void RegisterTools(UnityMcpToolProviderContext context)
        {
            context.TryRegister(new DelegateUnityMcpTool(
                "demo.runtime.status.cs",
                "Demo Runtime C# MCP tool provider. Returns player Application, Screen, and active scene data.",
                JsonSchemas.Object(),
                (ctx, args) => Task.FromResult(UnityJson.ToJson(BuildResult(ctx)))));
        }

        private static RuntimeDemoResult BuildResult(UnityMcpToolContext context)
        {
            var scene = SceneManager.GetActiveScene();
            return new RuntimeDemoResult
            {
                ok = true,
                source = "demo-runtime-cs",
                endpointKind = context.EndpointKind,
                productName = Application.productName,
                platform = Application.platform.ToString(),
                unityVersion = Application.unityVersion,
                activeSceneName = scene.name,
                activeScenePath = scene.path,
                screenWidth = Screen.width,
                screenHeight = Screen.height
            };
        }

        [Serializable]
        private sealed class RuntimeDemoResult
        {
            public bool ok;
            public string source;
            public string endpointKind;
            public string productName;
            public string platform;
            public string unityVersion;
            public string activeSceneName;
            public string activeScenePath;
            public int screenWidth;
            public int screenHeight;
        }
    }
}
