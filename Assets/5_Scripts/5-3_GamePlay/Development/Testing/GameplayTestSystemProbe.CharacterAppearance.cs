using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 人物分层穿戴采样

    private static void RegisterCharacterAppearanceOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("rendering/character_layers", ServiceCharacterLayers);
    }

    // 夹具只给正式合成器传入独立像素，各像素颜色与遮挡预期由 Lua 断言。
    private static JObject ServiceCharacterLayers(JObject args)
    {
        int width = (int)Number(args, "width", 3), height = (int)Number(args, "height", 2);
        if (width < 1 || height < 1 || width > 16 || height > 16)
            throw new ArgumentException("人物分层采样只允许 1 至 16 像素宽高。");
        Color32[] body = CharacterLayerPixels(args["body"]);
        Color32[] hair = CharacterLayerPixels(args["hair"]);
        if (args["steps"] is not JArray steps || steps.Count == 0 || steps.Count > 16)
            throw new ArgumentException("人物分层采样需要 1 至 16 个步骤。");

        var output = new Color32[width * height];
        for (int i = 0; i < output.Length; i++) output[i] = new Color32(9, 19, 29, 39);
        var samples = new JArray();
        foreach (JToken value in steps)
        {
            if (value is not JObject step) throw new ArgumentException("人物分层步骤必须是对象。");
            var layers = new List<CharacterAppearanceLayer>();
            if (step["layers"] is JArray entries)
            {
                if (entries.Count > 8) throw new ArgumentException("人物采样不能超过八层装备。");
                foreach (JToken entry in entries)
                {
                    if (entry is not JObject layer) throw new ArgumentException("穿戴图层必须是对象。");
                    layers.Add(new CharacterAppearanceLayer(CharacterLayerPixels(layer["wearable"]),
                        CharacterLayerPixels(layer["hairMask"]), CharacterLayerPixels(layer["bodyMask"])));
                }
            }
            JArray before = CharacterLayerSources(body, hair, layers);
            string rejection = null;
            try
            {
                CharacterAppearanceCompositor.Compose(body, hair, layers, output, width, height,
                    Flag(args, "sourceFacesLeft", false));
            }
            catch (ArgumentException exception)
            {
                rejection = exception.GetType().Name;
            }
            samples.Add(new JObject
            {
                ["pixels"] = CharacterLayerPixelSample(output),
                ["rejection"] = rejection,
                ["sourcesUnchanged"] = JToken.DeepEquals(before, CharacterLayerSources(body, hair, layers))
            });
        }
        return new JObject { ["samples"] = samples };
    }

    private static Color32[] CharacterLayerPixels(JToken value)
    {
        if (value == null || value.Type == JTokenType.Null) return null;
        if (value is not JArray pixels || pixels.Count > 256)
            throw new ArgumentException("像素采样必须是最多 256 个 RGBA 的数组。");
        var result = new Color32[pixels.Count];
        for (int i = 0; i < pixels.Count; i++)
        {
            if (pixels[i] is not JArray channels || channels.Count != 4)
                throw new ArgumentException("像素必须明确给出四个 RGBA 字节。");
            var bytes = new byte[4];
            for (int channel = 0; channel < 4; channel++)
            {
                if (channels[channel].Type is not (JTokenType.Integer or JTokenType.Float))
                    throw new ArgumentException("RGBA 通道必须是 0 至 255 的整数。");
                double number = channels[channel].Value<double>();
                if (!double.IsFinite(number) || number < 0 || number > 255 || number != Math.Floor(number))
                    throw new ArgumentException("RGBA 通道必须是 0 至 255 的整数。");
                bytes[channel] = (byte)number;
            }
            result[i] = new Color32(bytes[0], bytes[1], bytes[2], bytes[3]);
        }
        return result;
    }

    private static JArray CharacterLayerPixelSample(Color32[] pixels)
    {
        if (pixels == null) return null;
        var sample = new JArray();
        foreach (Color32 pixel in pixels)
            sample.Add(new JArray(pixel.r, pixel.g, pixel.b, pixel.a));
        return sample;
    }

    private static JArray CharacterLayerSources(Color32[] body, Color32[] hair,
        IReadOnlyList<CharacterAppearanceLayer> layers)
    {
        var sources = new JArray { CharacterLayerPixelSample(body), CharacterLayerPixelSample(hair) };
        foreach (CharacterAppearanceLayer layer in layers)
        {
            sources.Add(CharacterLayerPixelSample(layer.Wearable));
            sources.Add(CharacterLayerPixelSample(layer.HairMask));
            sources.Add(CharacterLayerPixelSample(layer.BodyMask));
        }
        return sources;
    }

    #endregion
}
