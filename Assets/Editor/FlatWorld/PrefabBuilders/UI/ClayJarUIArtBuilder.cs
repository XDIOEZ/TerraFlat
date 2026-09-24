using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>把生成的陶罐概念稿机械转换成 128 像素透明精灵，并提取腹部内壁供水层裁剪。</summary>
public static class ClayJarUIArtBuilder
{
    public const string Root = "Assets/6_Art/Generated/WaterVessel/ClayJarUI/";
    public static void Build()
    {
        var source = new Texture2D(2, 2);
        source.LoadImage(File.ReadAllBytes(Root + "ClayJar_Cutaway_Concept.png"));
        var target = new Texture2D(128, 128, TextureFormat.RGBA32, false);
        var mask = new Texture2D(128, 128, TextureFormat.RGBA32, false);
        int left = source.width, right = 0, bottom = source.height, top = 0;
        for (int y = 0; y < source.height; y++) for (int x = 0; x < source.width; x++)
            if (IsClay(source.GetPixel(x, y))) { left = Mathf.Min(left, x); right = Mathf.Max(right, x); bottom = Mathf.Min(bottom, y); top = Mathf.Max(top, y); }
        float scale = 120f / Mathf.Max(right - left + 1, top - bottom + 1);
        string[] colors = { "392313", "512C16", "65341B", "7C3D1D", "934422", "AA5024", "C36228", "D77530", "E8893F", "F69B50", "FFB469", "FFC879", "9B6238", "BD804D", "DF9E63", "F9CF95" };
        var palette = new Color[colors.Length];
        for (int i = 0; i < colors.Length; i++) ColorUtility.TryParseHtmlString("#" + colors[i], out palette[i]);
        for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++)
        {
            int sx = Mathf.RoundToInt((x - 64) / scale + (left + right) * .5f);
            int sy = Mathf.RoundToInt((y - 64) / scale + (bottom + top) * .5f);
            Color c = sx >= 0 && sy >= 0 && sx < source.width && sy < source.height ? source.GetPixel(sx, sy) : Color.clear;
            Color result = Color.clear;
            if (IsClay(c))
            {
                float distance = float.MaxValue;
                foreach (Color candidate in palette)
                {
                    float d = (new Vector3(c.r, c.g, c.b) - new Vector3(candidate.r, candidate.g, candidate.b)).sqrMagnitude;
                    if (d < distance) { distance = d; result = candidate; }
                }
            }
            target.SetPixel(x, y, result);
            mask.SetPixel(x, y, Color.clear);
        }
        // 仅提取罐腹中心连通的暗色内腔，排除罐口与外部背景。
        for (int y = 16; y <= 93; y++)
        {
            if (!IsInterior(target.GetPixel(64, y))) continue;
            int a = 64, b = 64;
            while (a > 0 && IsInterior(target.GetPixel(a - 1, y))) a--;
            while (b < 127 && IsInterior(target.GetPixel(b + 1, y))) b++;
            for (int x = a + 1; x < b; x++) mask.SetPixel(x, y, Color.white);
        }
        Save(target, "ClayJar_Cutaway.png");
        Save(mask, "ClayJar_Interior.png");
        Object.DestroyImmediate(source); Object.DestroyImmediate(target); Object.DestroyImmediate(mask);
    }
    private static bool IsClay(Color c) => c.a > .5f && c.r > c.g * 1.18f && c.g > c.b * 1.12f;
    private static bool IsInterior(Color c) => c.a > .5f && c.r < .54f && c.g < .34f;
    private static void Save(Texture2D texture, string name)
    {
        string path = Root + name;
        texture.Apply(); File.WriteAllBytes(path, texture.EncodeToPNG());
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
        importer.filterMode = FilterMode.Point; importer.mipmapEnabled = false; importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed; importer.spritePixelsPerUnit = 128;
        importer.SaveAndReimport();
    }
}
