using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>器物界面的外观装配独立于设备规则和实例存档。</summary>
public sealed class PhysicalDeviceArtworkCatalog : ScriptableObject
{
    #region 外观与部件声明
    public enum Motion
    {
        Still, Rotate, Reciprocate, Compress, Stroke, Drill, Flame, Smoke, Ember,
        Liquid, Charge, Valve, Powered, Heat, Progress, CordLeft, CordRight, Wheel, Gas, Fuel,
        SliderCrank, ConnectingRod, WorkingNeedle, VerticalStroke, PumpCordLeft, PumpCordRight, OpenLift, FlowPulse,
        InputAttachment, ToolStroke, Contents0, Contents1, Contents2, Contents3
    }

    [Serializable]
    public sealed class Layer
    {
        public Sprite Sprite;
        public Vector4 Rect;
        public Vector2 Pivot = new(.5f, .5f);
        public Color Tint = Color.white;
        public Motion Movement;
        public float Travel = 24f;
        public float Compression = .15f;
        public float WheelRadius = .18f;
        public Vector2 CordAnchor = new(0f, 24f);
        public float Rotation;
        public Vector2 CrankCenter;
        public float CrankRadius, RodLength, SliderAxis;
        public int SliderBranch = 1;
    }

    [Serializable]
    public sealed class Appearance
    {
        public string[] ItemIds;
        public Layer[] Layers;
    }

    public Appearance[] Appearances;
    [Serializable]
    public sealed class PanelLayout
    {
        public string PanelId, DefaultAppearance;
        public float ArtworkWidth = 420f;
        public bool Dedicated;
    }
    public PanelLayout[] Panels;
    public const int LayerCapacity = 16;
    private Dictionary<string, Appearance> lookup;

    private void OnEnable() => lookup = null;
#if UNITY_EDITOR
    private void OnValidate() => lookup = null;
#endif

    public Appearance Find(string id)
    {
        if (lookup == null)
        {
            lookup = new Dictionary<string, Appearance>(StringComparer.Ordinal);
            foreach (Appearance appearance in Appearances)
                foreach (string key in appearance.ItemIds) lookup.Add(key, appearance);
        }
        return id != null && lookup.TryGetValue(id, out Appearance result) ? result : null;
    }
    #endregion
}
