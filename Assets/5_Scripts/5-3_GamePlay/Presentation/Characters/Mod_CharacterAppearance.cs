using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>穿脱时按部位拼合角色外观，复用原身体渲染器与摇摆动作。</summary>
public sealed class Mod_CharacterAppearance : Module, IItemModuleDependencyBinder
{
    #region 模块与配置

    public Ex_ModData AppearanceData = new();
    public override ModuleData _Data
    {
        get => AppearanceData;
        set => AppearanceData = value as Ex_ModData ??
            throw new ArgumentException("[角色外观] 模块数据类型错误。", nameof(value));
    }

    public override string CanonicalModuleId => "CharacterAppearance";
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public CharacterAppearanceProfile profile;
    public CharacterBodyStyle bodyStyle = CharacterBodyStyle.Female;

    private static readonly string[] PartSlots =
    {
        EquipmentSlotCatalog.Head, EquipmentSlotCatalog.TorsoOuter, EquipmentSlotCatalog.Hands,
        EquipmentSlotCatalog.Legs, EquipmentSlotCatalog.FeetOuter
    };

    #endregion

    #region 运行时缓存

    private Mod_Equipment equipment;
    private SpriteRenderer targetRenderer;
    private Sprite originalSprite;
    private Texture2D outputTexture;
    private Sprite outputSprite;
    private Color32[] outputPixels;
    private readonly Color32[][] partPixels = new Color32[5][];
    private readonly Dictionary<Sprite, Color32[]> sourcePixels = new();
    private readonly HashSet<Sprite> invalidSources = new();
    private bool bound;

    #endregion

    #region 生命周期

    public void BindModuleDependencies(ItemMods modules)
    {
        equipment = modules?.GetMod_ByID<Mod_Equipment>(ModText.Equipment_Module);
    }

    protected override void OnLoad()
    {
        if (!string.IsNullOrEmpty(AppearanceData.BitData))
            bodyStyle = AppearanceData.GetData<CharacterBodyStyle>();
        if (bodyStyle != CharacterBodyStyle.Male && bodyStyle != CharacterBodyStyle.Female)
            bodyStyle = profile != null ? profile.DefaultBodyStyle : CharacterBodyStyle.Female;
        if (isActiveAndEnabled) BindAppearance();
    }

    protected override void OnSave()
    {
        // 只保存身体样式，装备外观仍由真实槽位恢复。
        AppearanceData.WriteData(bodyStyle);
    }

    protected override void OnUnload() => ReleaseAppearance();

    private void OnEnable()
    {
        if (IsRuntimeLoaded && !bound) BindAppearance();
    }

    private void OnDisable() => ReleaseAppearance();

    private void OnDestroy()
    {
        Unload();
        ReleaseAppearance();
    }

    private void OnValidate()
    {
        AppearanceData ??= new Ex_ModData();
    }

    public override void OnResourcesReloaded()
    {
        base.OnResourcesReloaded();
        if (!bound) return;
        sourcePixels.Clear();
        invalidSources.Clear();
        RefreshAppearance();
    }

    private void BindAppearance()
    {
        if (bound) return;
        if (profile == null || profile.Width <= 0 || profile.Height <= 0 ||
            !float.IsFinite(profile.PixelsPerUnit) || profile.PixelsPerUnit <= 0f)
            throw new InvalidOperationException("[角色外观] 缺少有效的角色母版配置。");
        targetRenderer = item != null ? item.Sprite : null;
        if (targetRenderer == null)
            throw new MissingComponentException("[角色外观] 所属角色缺少身体渲染器。");
        originalSprite = targetRenderer.sprite;
        equipment = item.itemMods?.GetMod_ByID<Mod_Equipment>(ModText.Equipment_Module);
        if (equipment != null) equipment.EquipmentChanged += HandleEquipmentChanged;
        Item.RuntimeStructureChanged += HandleOwnerStructureChanged;
        bound = true;
        RefreshAppearance();
    }

    private void ReleaseAppearance()
    {
        if (equipment != null) equipment.EquipmentChanged -= HandleEquipmentChanged;
        Item.RuntimeStructureChanged -= HandleOwnerStructureChanged;
        equipment = null;
        if (targetRenderer != null && targetRenderer.sprite == outputSprite)
            targetRenderer.sprite = originalSprite;
        targetRenderer = null;
        originalSprite = null;
        bound = false;
        sourcePixels.Clear();
        invalidSources.Clear();
        Array.Clear(partPixels, 0, partPixels.Length);
        outputPixels = null;
        DestroyGeneratedObject(outputSprite);
        DestroyGeneratedObject(outputTexture);
        outputSprite = null;
        outputTexture = null;
    }

    #endregion

    #region 装备外观组合

    [Sirenix.OdinInspector.Button("应用人物外观")]
    public void SetBodyStyle(CharacterBodyStyle style)
    {
        if (style != CharacterBodyStyle.Male && style != CharacterBodyStyle.Female)
            throw new ArgumentOutOfRangeException(nameof(style));
        bodyStyle = style;
        RefreshAppearance();
    }

    private void HandleEquipmentChanged() => RefreshAppearance();

    private void HandleOwnerStructureChanged(Item source)
    {
        if (source != item || !bound) return;
        Mod_Equipment current = item.itemMods?.GetMod_ByID<Mod_Equipment>(ModText.Equipment_Module);
        if (current != equipment)
        {
            if (equipment != null) equipment.EquipmentChanged -= HandleEquipmentChanged;
            equipment = current;
            if (equipment != null) equipment.EquipmentChanged += HandleEquipmentChanged;
        }
        // 全部模块加载后的结构通知负责补上装备晚于外观恢复的初次显示。
        RefreshAppearance();
    }

    public void RefreshAppearance()
    {
        if (!bound || !isActiveAndEnabled || targetRenderer == null || profile == null) return;
        Color32[] baseline = ReadSourcePixels(profile.GetBaseSprite(bodyStyle));
        if (baseline == null)
            throw new InvalidOperationException("[角色外观] 身体母版必须是与画布尺寸一致、启用读写的 Sprite。");

        for (int i = 0; i < PartSlots.Length; i++)
            partPixels[i] = ReadSourcePixels(ResolveEquippedSprite(PartSlots[i])) ?? baseline;

        EnsureOutput();
        for (int y = 0, index = 0; y < profile.Height; y++)
            for (int x = 0; x < profile.Width; x++, index++)
                outputPixels[index] = partPixels[profile.GetPartIndex(x, y)][index];
        outputTexture.SetPixels32(outputPixels);
        outputTexture.Apply(false, false);
        targetRenderer.sprite = outputSprite;
    }

    private Sprite ResolveEquippedSprite(string slotId)
    {
        ItemData equippedItem = equipment?.GetEquippedItem(slotId);
        GameRes resources = GameRes.ExistingInstance;
        if (equippedItem == null || resources == null ||
            !resources.TryGetItemDefinition(equippedItem.IDName, out RuntimeItemDefinition definition))
            return null;
        string preferred = bodyStyle == CharacterBodyStyle.Female ? "wearableFemale" : "wearableMale";
        string alternate = bodyStyle == CharacterBodyStyle.Female ? "wearableMale" : "wearableFemale";
        if (definition.TryGetVisualStateSprite(preferred, out Sprite sprite)) return sprite;
        return definition.TryGetVisualStateSprite(alternate, out sprite) ? sprite : null;
    }

    private void EnsureOutput()
    {
        if (outputTexture != null && outputTexture.width == profile.Width && outputTexture.height == profile.Height &&
            outputSprite != null && Mathf.Approximately(outputSprite.pixelsPerUnit, profile.PixelsPerUnit)) return;
        DestroyGeneratedObject(outputSprite);
        DestroyGeneratedObject(outputTexture);
        outputTexture = new Texture2D(profile.Width, profile.Height, TextureFormat.RGBA32, false)
        {
            name = $"CharacterAppearance_{item.GetInstanceID()}",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave
        };
        outputSprite = Sprite.Create(outputTexture, new Rect(0, 0, profile.Width, profile.Height),
            new Vector2(0.5f, 0f), profile.PixelsPerUnit, 0, SpriteMeshType.FullRect);
        outputSprite.name = outputTexture.name;
        outputSprite.hideFlags = HideFlags.DontSave;
        outputPixels = new Color32[profile.Width * profile.Height];
    }

    private Color32[] ReadSourcePixels(Sprite source)
    {
        if (source == null || invalidSources.Contains(source)) return null;
        if (sourcePixels.TryGetValue(source, out Color32[] cached)) return cached;
        Rect rect = source.rect;
        Texture2D texture = source.texture;
        if (texture == null || !texture.isReadable || rect.width != profile.Width || rect.height != profile.Height || source.packed)
        {
            invalidSources.Add(source);
            Debug.LogWarning($"[角色外观] 贴图 {source.name} 必须保持 {profile.Width}×{profile.Height}、可读写且不打入图集，已使用基础外观。", this);
            return null;
        }

        Color32[] texturePixels = texture.GetPixels32();
        var pixels = new Color32[profile.Width * profile.Height];
        int startX = Mathf.RoundToInt(rect.x), startY = Mathf.RoundToInt(rect.y);
        for (int y = 0; y < profile.Height; y++)
            Array.Copy(texturePixels, (startY + y) * texture.width + startX, pixels, y * profile.Width, profile.Width);
        sourcePixels.Add(source, pixels);
        return pixels;
    }

    private static void DestroyGeneratedObject(UnityEngine.Object value)
    {
        if (value == null) return;
        if (Application.isPlaying) Destroy(value);
        else DestroyImmediate(value);
    }

    #endregion
}
