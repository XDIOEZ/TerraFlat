using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>一件共用装备及其对身体、头发的独立遮挡要求。</summary>
public readonly struct CharacterAppearanceLayer
{
    #region 图层数据

    public readonly Color32[] Wearable;
    public readonly Color32[] HairMask;
    public readonly Color32[] BodyMask;

    public CharacterAppearanceLayer(Color32[] wearable, Color32[] hairMask = null, Color32[] bodyMask = null)
    {
        Wearable = wearable;
        HairMask = hairMask;
        BodyMask = bodyMask;
    }

    #endregion
}

/// <summary>纯像素外观组合规则，不依赖角色性别、库存或渲染器。</summary>
public static class CharacterAppearanceCompositor
{
    #region 全幅图层合成

    public static void Compose(Color32[] body, Color32[] hair, IReadOnlyList<CharacterAppearanceLayer> layers,
        Color32[] output, int width, int height, bool sourceFacesLeft)
    {
        if (width <= 0 || height <= 0 || width > int.MaxValue / height)
            throw new ArgumentOutOfRangeException(nameof(width), "角色画布尺寸无效。");
        int pixelCount = width * height;
        ValidatePixels(body, pixelCount, nameof(body));
        ValidatePixels(output, pixelCount, nameof(output));
        ValidateOptionalSource(hair, output, pixelCount, nameof(hair));
        if (ReferenceEquals(body, output))
            throw new ArgumentException("合成输出不能覆盖身体源像素。", nameof(output));

        int layerCount = layers?.Count ?? 0;
        for (int i = 0; i < layerCount; i++)
        {
            CharacterAppearanceLayer layer = layers[i];
            if (layer.Wearable == null) continue;
            ValidateOptionalSource(layer.Wearable, output, pixelCount, nameof(layer.Wearable));
            ValidateOptionalSource(layer.HairMask, output, pixelCount, nameof(layer.HairMask));
            ValidateOptionalSource(layer.BodyMask, output, pixelCount, nameof(layer.BodyMask));
        }

        for (int y = 0, index = 0; y < height; y++)
            for (int x = 0; x < width; x++, index++)
            {
                Color32 bodyPixel = body[index];
                Color32 hairPixel = hair != null ? hair[index] : default;
                for (int i = 0; i < layerCount; i++)
                {
                    CharacterAppearanceLayer layer = layers[i];
                    if (layer.Wearable == null) continue;
                    // 遮挡只裁剪声明的基础层，半包围头盔的遮发不会擦掉原人物的脸。
                    if (layer.BodyMask != null)
                        bodyPixel.a = MultiplyAlpha(bodyPixel.a, 255 - layer.BodyMask[index].a);
                    if (layer.HairMask != null)
                        hairPixel.a = MultiplyAlpha(hairPixel.a, 255 - layer.HairMask[index].a);
                }

                Color32 combined = AlphaOver(bodyPixel, hairPixel);
                for (int i = 0; i < layerCount; i++)
                {
                    Color32[] wearable = layers[i].Wearable;
                    if (wearable != null) combined = AlphaOver(combined, wearable[index]);
                }

                int outputX = sourceFacesLeft ? width - 1 - x : x;
                output[y * width + outputX] = combined;
            }
    }

    #endregion

    #region 像素运算与契约

    private static Color32 AlphaOver(Color32 background, Color32 foreground)
    {
        if (foreground.a == 0) return background;
        if (foreground.a == 255 || background.a == 0) return foreground;
        int foregroundAlpha = foreground.a * 255;
        int backgroundAlpha = background.a * (255 - foreground.a);
        int alpha = foregroundAlpha + backgroundAlpha;
        return new Color32(
            (byte)((foreground.r * foregroundAlpha + background.r * backgroundAlpha + alpha / 2) / alpha),
            (byte)((foreground.g * foregroundAlpha + background.g * backgroundAlpha + alpha / 2) / alpha),
            (byte)((foreground.b * foregroundAlpha + background.b * backgroundAlpha + alpha / 2) / alpha),
            (byte)((alpha + 127) / 255));
    }

    private static byte MultiplyAlpha(byte alpha, int multiplier) => (byte)((alpha * multiplier + 127) / 255);

    private static void ValidateOptionalSource(Color32[] pixels, Color32[] output, int count, string name)
    {
        if (pixels == null) return;
        ValidatePixels(pixels, count, name);
        if (ReferenceEquals(pixels, output))
            throw new ArgumentException("合成输出不能覆盖源图层或遮挡图。", nameof(output));
    }

    private static void ValidatePixels(Color32[] pixels, int count, string name)
    {
        if (pixels == null) throw new ArgumentNullException(name);
        if (pixels.Length != count)
            throw new ArgumentException("图层像素数必须与角色画布一致。", name);
    }

    #endregion
}
