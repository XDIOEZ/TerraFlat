using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP 14 的 2D 光照接收层适配器：用排序层名称配置 Light2D 与 ShadowCaster2D，空列表表示所有层。
/// 排序层仅决定接收对象，不代表物理高度，也不是 Physics Layer；反射集中在此，避免升级管线时散落私有字段依赖。
/// </summary>
public static class Light2DSortingLayerUtility
{
    #region URP 14 字段适配

    private static readonly FieldInfo LightLayersField = ResolveLayersField(typeof(Light2D));
    private static readonly FieldInfo ShadowLayersField = ResolveLayersField(typeof(ShadowCaster2D));

    /// <summary>解析接收层；拼写错误直接报错，避免不存在的名称悄悄变成 Default。</summary>
    public static int[] ResolveLayerIds(string[] layerNames)
    {
        SortingLayer[] availableLayers = SortingLayer.layers;
        if (layerNames == null || layerNames.Length == 0)
        {
            var allLayers = new int[availableLayers.Length];
            for (int i = 0; i < availableLayers.Length; i++)
                allLayers[i] = availableLayers[i].id;
            return allLayers;
        }

        var resolved = new int[layerNames.Length];
        for (int i = 0; i < layerNames.Length; i++)
        {
            bool found = false;
            for (int j = 0; j < availableLayers.Length; j++)
            {
                if (!string.Equals(layerNames[i], availableLayers[j].name, StringComparison.Ordinal))
                    continue;
                resolved[i] = availableLayers[j].id;
                found = true;
                break;
            }

            if (!found)
                throw new ArgumentException($"[Light2D] 不存在的目标排序层：{layerNames[i]}");
        }

        // 统一保存顺序，让解析结果不依赖名称列表的填写顺序。
        Array.Sort(resolved);
        return resolved;
    }

    /// <summary>读取光源原有的接收层作为恢复基线；返回数组只允许读取，不允许原地修改。</summary>
    public static int[] GetLightLayers(Light2D light)
        => light != null ? (int[])LightLayersField.GetValue(light) : null;

    /// <summary>设置局部光源的接收层；Global Light 另有管线缓存，不能从此入口修改。</summary>
    public static void SetLightLayers(Light2D light, int[] layerIds)
    {
        if (light == null || light.lightType == Light2D.LightType.Global)
            return;
        LightLayersField.SetValue(light, layerIds);
    }

    /// <summary>设置建筑阴影影响的接收层，不改变建筑 Sprite 的绘制顺序。</summary>
    public static void SetShadowLayers(ShadowCaster2D caster, int[] layerIds)
    {
        if (caster != null)
            ShadowLayersField.SetValue(caster, layerIds);
    }

    /// <summary>只有光和阴影影响同一接收层时，才需要避让自身光源。</summary>
    public static bool SharesShadowLayers(Light2D light, ShadowCaster2D caster)
    {
        int[] lightLayers = GetLightLayers(light);
        int[] shadowLayers = caster != null ? (int[])ShadowLayersField.GetValue(caster) : null;
        if (lightLayers == null || shadowLayers == null)
            return false;
        for (int i = 0; i < lightLayers.Length; i++)
        {
            if (Array.IndexOf(shadowLayers, lightLayers[i]) >= 0)
                return true;
        }
        return false;
    }

    /// <summary>环绕世界镜像必须继承源光的接收层；数组只读共享，不逐帧复制。</summary>
    public static void CopyLightLayers(Light2D source, Light2D destination)
    {
        if (source == null || destination == null)
            return;
        object layers = LightLayersField.GetValue(source);
        if (!ReferenceEquals(layers, LightLayersField.GetValue(destination)))
            LightLayersField.SetValue(destination, layers);
    }

    /// <summary>只适配当前管线的序列化字段，缺失时明确暴露版本不兼容。</summary>
    private static FieldInfo ResolveLayersField(Type type)
        => type.GetField("m_ApplyToSortingLayers", BindingFlags.Instance | BindingFlags.NonPublic)
           ?? throw new MissingFieldException(type.FullName, "m_ApplyToSortingLayers");

    #endregion
}
