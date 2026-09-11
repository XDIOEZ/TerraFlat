using System.IO;
using UnityEngine;

/// <summary>
/// 编辑器阶段从原始山林图烘焙主菜单蓝绿柔焦背景，保留原图，不引入运行时模糊或全局后处理。
/// 输出宽度 836，三次半径 6 的可分离盒式滤波近似柔焦；蓝色天空与深绿前景限制夕阳的暖色。
/// </summary>
internal static class MainMenuBackdropBaker
{
    #region 背景烘焙

    /// <summary>重新生成独立 PNG；不修改源图或源图的导入设置。</summary>
    public static void Bake(string sourcePath, string targetPath)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("[MainMenu] 缺少原始山林背景。", sourcePath);
        if (Path.GetFullPath(sourcePath) == Path.GetFullPath(targetPath))
            throw new System.ArgumentException("背景烘焙不能覆盖源图。", nameof(targetPath));

        Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        Texture2D output = null;
        try
        {
            if (!ImageConversion.LoadImage(source, File.ReadAllBytes(sourcePath)))
                throw new System.InvalidOperationException("[MainMenu] 无法解码原始背景。");
            source.wrapMode = TextureWrapMode.Clamp;
            int width = Mathf.Min(836, source.width);
            int height = Mathf.Max(1, Mathf.RoundToInt(width * (float)source.height / source.width));
            Color[] pixels = new Color[width * height];
            Color[] scratch = new Color[pixels.Length];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    pixels[y * width + x] = source.GetPixelBilinear((x + 0.5f) / width, (y + 0.5f) / height);

            for (int pass = 0; pass < 3; pass++)
            {
                Blur(pixels, scratch, width, height, true, 6);
                Blur(scratch, pixels, width, height, false, 6);
            }

            Color forest = new Color32(10, 30, 18, 255);
            Color sky = new Color32(75, 102, 139, 255);
            for (int y = 0; y < height; y++)
            {
                float v = (y + 0.5f) / height;
                for (int x = 0; x < width; x++)
                {
                    int index = y * width + x;
                    Color sourceColor = pixels[index];
                    float luminance = sourceColor.r * 0.2126f + sourceColor.g * 0.7152f + sourceColor.b * 0.0722f;
                    // 随源图明暗保留山脊和林地轮廓，不能把整幅风景压成水平渐变。
                    float horizon = v + (luminance - 0.30f) * 0.65f;
                    float skyWeight = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.34f, 0.70f, horizon));
                    Color tint = Color.Lerp(forest, sky, skyWeight);
                    float shade = Mathf.Lerp(Mathf.Lerp(0.55f, 1.50f, luminance), Mathf.Lerp(0.83f, 1.12f, luminance), skyWeight);
                    float u = (x + 0.5f) / width - 0.5f;
                    float vignette = 1f - 0.12f * Mathf.Clamp01(u * u * 2f + (v - 0.5f) * (v - 0.5f));
                    Color graded = tint * (shade * vignette);
                    graded.a = 1f;
                    pixels[index] = graded;
                }
            }

            output = new Texture2D(width, height, TextureFormat.RGB24, false);
            output.SetPixels(pixels);
            output.Apply(false, false);
            File.WriteAllBytes(targetPath, ImageConversion.EncodeToPNG(output));
        }
        finally
        {
            if (output != null)
                Object.DestroyImmediate(output);
            Object.DestroyImmediate(source);
        }
    }

    #endregion

    #region 离线柔焦

    /// <summary>滑动窗口分离滤波，边缘夹取；复杂度不随模糊半径增长。</summary>
    private static void Blur(Color[] source, Color[] target, int width, int height, bool horizontal, int radius)
    {
        int lines = horizontal ? height : width;
        int length = horizontal ? width : height;
        int stride = horizontal ? 1 : width;
        float divisor = 1f / (radius * 2 + 1);
        for (int line = 0; line < lines; line++)
        {
            int origin = horizontal ? line * width : line;
            Color sum = Color.clear;
            for (int sample = -radius; sample <= radius; sample++)
                sum += source[origin + Mathf.Clamp(sample, 0, length - 1) * stride];
            for (int position = 0; position < length; position++)
            {
                target[origin + position * stride] = sum * divisor;
                int remove = Mathf.Clamp(position - radius, 0, length - 1);
                int add = Mathf.Clamp(position + radius + 1, 0, length - 1);
                sum += source[origin + add * stride] - source[origin + remove * stride];
            }
        }
    }

    #endregion
}
