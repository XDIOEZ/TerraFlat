using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace PuertsUnityMcp.Editor
{
    public static class UnityMcpExtensionDemoInstaller
    {
        private const string SamplesRelativePath = "ExtensionSamples~/puerts-unity-mcp-extension";

        [MenuItem("PuerTS Unity MCP/Create Extension Demos", priority = 43)]
        public static void CreateDemosFromMenu()
        {
            var result = CreateDemos();
            Debug.Log("[UnityMCP] Extension demo install finished. Created "
                + result.created.Length + ", skipped " + result.skipped.Length + ".");
        }

        public static UnityMcpExtensionDemoInstallResult CreateDemos()
        {
            var created = new System.Collections.Generic.List<string>();
            var skipped = new System.Collections.Generic.List<string>();
            UnityMcpProjectLayoutMigrator.MigrateLegacyLayout();
            var sourceRoot = ResolveSamplesRoot();
            var targetRoot = UnityMcpPaths.ProjectExtensionRoot;
            if (string.IsNullOrEmpty(sourceRoot) || !Directory.Exists(sourceRoot))
            {
                skipped.Add("Sample source was not found: " + (sourceRoot ?? string.Empty));
                return BuildResult(created, skipped, targetRoot);
            }

            CopyMissing(sourceRoot, targetRoot, created, skipped);
            AssetDatabase.Refresh();
            return BuildResult(created, skipped, targetRoot);
        }

        public static string ResolveSamplesRoot()
        {
            var packageRoot = ResolvePackageRoot();
            return string.IsNullOrEmpty(packageRoot)
                ? string.Empty
                : Path.Combine(packageRoot, SamplesRelativePath.Replace('/', Path.DirectorySeparatorChar));
        }

        private static string ResolvePackageRoot()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UnityMcpConstants).Assembly);
            if (packageInfo != null && !string.IsNullOrEmpty(packageInfo.resolvedPath))
            {
                return packageInfo.resolvedPath;
            }

            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "puerts-unity-mcp", "Packages", UnityMcpConstants.PackageName));
        }

        private static UnityMcpExtensionDemoInstallResult BuildResult(System.Collections.Generic.List<string> created, System.Collections.Generic.List<string> skipped, string targetRoot)
        {
            return new UnityMcpExtensionDemoInstallResult
            {
                targetRoot = targetRoot,
                created = created.ToArray(),
                skipped = skipped.ToArray()
            };
        }

        private static void CopyMissing(string sourceRoot, string targetRoot, System.Collections.Generic.List<string> created, System.Collections.Generic.List<string> skipped)
        {
            Directory.CreateDirectory(targetRoot);
            foreach (var sourcePath in Directory.GetFileSystemEntries(sourceRoot))
            {
                var targetPath = Path.Combine(targetRoot, Path.GetFileName(sourcePath));
                if (Directory.Exists(sourcePath))
                {
                    CopyMissing(sourcePath, targetPath, created, skipped);
                    continue;
                }

                if (File.Exists(targetPath))
                {
                    skipped.Add(targetPath);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
                File.Copy(sourcePath, targetPath);
                created.Add(targetPath);
            }
        }
    }

    [Serializable]
    public sealed class UnityMcpExtensionDemoInstallResult
    {
        public string targetRoot;
        public string[] created = new string[0];
        public string[] skipped = new string[0];
    }
}
