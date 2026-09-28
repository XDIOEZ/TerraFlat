using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace PuertsUnityMcp.ExtensionDemo.Editor
{
    public sealed class ExtensionDemoEditorTools : IUnityMcpToolProvider
    {
        public string EndpointKind => "editor";

        public void RegisterTools(UnityMcpToolProviderContext context)
        {
            context.TryRegister(new DelegateUnityMcpTool(
                "demo.editor.selection.cs",
                "Demo Editor C# MCP tool provider. Returns current Unity selection data.",
                JsonSchemas.Object(),
                (ctx, args) => Task.FromResult(UnityJson.ToJson(BuildResult(ctx)))));
        }

        private static EditorDemoResult BuildResult(UnityMcpToolContext context)
        {
            return new EditorDemoResult
            {
                ok = true,
                source = "demo-editor-cs",
                endpointKind = context.EndpointKind,
                selectedObjectCount = Selection.objects == null ? 0 : Selection.objects.Length,
                activeObjectName = Selection.activeObject == null ? string.Empty : Selection.activeObject.name,
                unityVersion = Application.unityVersion
            };
        }

        [Serializable]
        private sealed class EditorDemoResult
        {
            public bool ok;
            public string source;
            public string endpointKind;
            public int selectedObjectCount;
            public string activeObjectName;
            public string unityVersion;
        }
    }
}
