using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

/// <summary>以 Unity 实际打包和逐图绑定结果作为图集验收依据。</summary>
internal static class FlatWorldSpriteAtlasPackVerification
{
    #region 真实打包报告

    private const string ReportPath = "Library/FlatWorldSpriteAtlasPlan/packing-verification.json";

    internal sealed class PageInput
    {
        public string Name;
        public string Path;
        public Sprite[] Sprites;
    }

    [Serializable]
    internal sealed class PageResult
    {
        public string atlasName;
        public string path;
        public int expectedSpriteCount;
        public int actualSpriteCount;
        public int actualTexturePageCount;
        public string[] actualTextureDimensions;
        public string[] missingSpriteIdentities;
    }

    [Serializable]
    internal sealed class VerificationResult
    {
        public int schemaVersion = 1;
        public string generatedUtc;
        public string buildTarget;
        public int plannedAtlasCount;
        public int actualTexturePageCount;
        public int expectedSpriteCount;
        public int boundSpriteCount;
        public int missingSpriteCount;
        public string error;
        public PageResult[] pages = Array.Empty<PageResult>();
    }

    internal static VerificationResult PackAndVerify(IReadOnlyList<PageInput> pages)
    {
        var result = new VerificationResult
        {
            generatedUtc = DateTime.UtcNow.ToString("O"),
            buildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
            plannedAtlasCount = pages.Count,
            expectedSpriteCount = pages.Sum(page => page.Sprites.Length)
        };

        try
        {
            SpriteAtlas[] atlases = pages.Select(page => AssetDatabase.LoadAssetAtPath<SpriteAtlas>(page.Path)).ToArray();
            for (int index = 0; index < atlases.Length; index++)
                if (atlases[index] == null)
                    throw new BuildFailedException("Unity 未导入运行时图集：" + pages[index].Path);

            MethodInfo preview = typeof(UnityEditor.U2D.SpriteAtlasExtensions).GetMethod("GetPreviewTextures",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                null, new[] { typeof(SpriteAtlas) }, null);

            // Unity V2 真打包；不能把 shelf 估算页数当作真实 GPU 纹理数。
            SpriteAtlasUtility.PackAtlases(atlases, EditorUserBuildSettings.activeBuildTarget, false);
            var reports = new List<PageResult>(pages.Count);
            for (int index = 0; index < pages.Count; index++)
            {
                PageInput page = pages[index];
                SpriteAtlas atlas = atlases[index];
                var missing = new List<string>();
                foreach (Sprite sprite in page.Sprites)
                {
                    if (sprite != null && atlas.CanBindTo(sprite))
                    {
                        result.boundSpriteCount++;
                        continue;
                    }
                    string identity = sprite == null ? "<null>" : sprite.name;
                    if (sprite != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string guid, out long localId))
                        identity = guid + ":" + localId + "/" + sprite.name;
                    missing.Add(identity);
                }

                Texture2D[] textures = null;
                if (preview != null)
                {
                    try { textures = preview.Invoke(null, new object[] { atlas }) as Texture2D[]; }
                    catch (TargetInvocationException) { /* 当前 Unity 版本不支持预览时改用真实运行图集。 */ }
                }
                if (textures == null || textures.Length == 0)
                {
                    // GetSprites 是 Unity 公开 API，返回绑定到打包纹理的 Sprite 副本。
                    Sprite[] actualSprites = new Sprite[atlas.spriteCount];
                    try
                    {
                        int actualSpritesCount = atlas.GetSprites(actualSprites);
                        textures = actualSprites.Take(actualSpritesCount)
                            .Select(sprite => sprite?.texture).Where(texture => texture != null)
                            .GroupBy(texture => texture.GetInstanceID()).Select(group => group.First()).ToArray();
                    }
                    finally
                    {
                        foreach (Sprite clone in actualSprites)
                            if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
                    }
                }
                if (textures == null || textures.Length == 0 || textures.Any(texture => texture == null))
                    throw new BuildFailedException("Unity 图集没有实际打包的预览纹理：" + page.Path);
                if (atlas.spriteCount != page.Sprites.Length ||
                    textures.Any(texture => texture.width > 2048 || texture.height > 2048))
                    throw new BuildFailedException("实际图集 Sprite 数或纹理尺寸超出构建计划：" + page.Path);
                string[] dimensions = textures.Select(texture => texture.width + "x" + texture.height).ToArray();
                result.actualTexturePageCount += textures.Length;
                result.missingSpriteCount += missing.Count;
                reports.Add(new PageResult
                {
                    atlasName = page.Name,
                    path = page.Path,
                    expectedSpriteCount = page.Sprites.Length,
                    actualSpriteCount = atlas.spriteCount,
                    actualTexturePageCount = textures.Length,
                    actualTextureDimensions = dimensions,
                    missingSpriteIdentities = missing.ToArray()
                });
            }
            result.pages = reports.ToArray();
            if (result.missingSpriteCount != 0)
                throw new BuildFailedException($"实际 SpriteAtlas 漏绑 {result.missingSpriteCount} 个 Sprite，请查看 {ReportPath}。");
            return result;
        }
        catch (Exception error)
        {
            result.error = error.ToString();
            throw;
        }
        finally
        {
            WriteReport(result);
        }
    }

    private static void WriteReport(VerificationResult result)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, JsonUtility.ToJson(result, true), new UTF8Encoding(false));
    }

    #endregion
}
