using MemoryPack;
using UnityEngine;

/// <summary>
/// Weed 的可序列化外观模块。
/// 首次生成时由世界种子、确定性物品 GUID 与地块坐标决定外观，之后从模块数据恢复。
/// </summary>
[DisallowMultipleComponent]
public sealed partial class Mod_WeedAppearance : Module
{
    private const string ModuleId = "WeedAppearance";

    [System.Serializable]
    [MemoryPackable]
    public partial class AppearanceData
    {
        public int VariantIndex = -1;
        public bool FlipX;
        public float UniformScale = 1f;
    }

    [Header("模块数据")]
    public Ex_ModData_MemoryPackable ModSaveData = new Ex_ModData_MemoryPackable();

    public override ModuleData _Data
    {
        get => ModSaveData;
        set => ModSaveData = value as Ex_ModData_MemoryPackable
            ?? new Ex_ModData_MemoryPackable();
    }

    [Header("外观资源")]
    [SerializeField] private SpriteRenderer targetRenderer;
    [SerializeField] private Sprite[] variants = new Sprite[0];

    [Header("轻微随机变化")]
    [SerializeField] private bool allowHorizontalFlip = true;
    [SerializeField] private Vector2 scaleRange = new Vector2(0.9f, 1.12f);

    private AppearanceData runtimeData;

    public override void Awake()
    {
        ModSaveData ??= new Ex_ModData_MemoryPackable();
        ModSaveData.ID = ModuleId;
    }

    public override void Load()
    {
        ModSaveData ??= new Ex_ModData_MemoryPackable();
        ModSaveData.ID = ModuleId;

        runtimeData = null;
        ModSaveData.ReadData(ref runtimeData);

        if (!IsSavedAppearanceValid(runtimeData))
        {
            runtimeData = CreateDeterministicAppearance();
            ModSaveData.WriteData(runtimeData);
        }

        ApplyAppearance(runtimeData);
    }

    public override void Save()
    {
        runtimeData ??= CreateDeterministicAppearance();
        ModSaveData.WriteData(runtimeData);
    }

    private bool IsSavedAppearanceValid(AppearanceData data)
    {
        return data != null &&
               variants != null &&
               data.VariantIndex >= 0 &&
               data.VariantIndex < variants.Length;
    }

    private AppearanceData CreateDeterministicAppearance()
    {
        AppearanceData result = new AppearanceData();
        if (variants == null || variants.Length == 0)
            return result;

        uint state = BuildStableSeed();
        result.VariantIndex = (int)(Next(ref state) % (uint)variants.Length);
        result.FlipX = allowHorizontalFlip && (Next(ref state) & 1u) == 0u;

        float minScale = Mathf.Min(scaleRange.x, scaleRange.y);
        float maxScale = Mathf.Max(scaleRange.x, scaleRange.y);
        result.UniformScale = Mathf.Lerp(minScale, maxScale, Next01(ref state));
        return result;
    }

    private void ApplyAppearance(AppearanceData data)
    {
        if (!IsSavedAppearanceValid(data))
            return;

        if (targetRenderer == null)
            targetRenderer = GetComponentInChildren<SpriteRenderer>(true);

        if (targetRenderer == null)
            return;

        targetRenderer.sprite = variants[data.VariantIndex];
        targetRenderer.flipX = data.FlipX;

        Vector3 currentScale = targetRenderer.transform.localScale;
        targetRenderer.transform.localScale = new Vector3(
            data.UniformScale,
            data.UniformScale,
            Mathf.Approximately(currentScale.z, 0f) ? 1f : currentScale.z);

        if (item != null)
            item.Sprite = targetRenderer;
    }

    private uint BuildStableSeed()
    {
        int worldSeed = SaveDataMgr.Instance?.SaveData?.Seed ?? 1;
        int itemGuid = item != null && item.itemData != null
            ? item.itemData.Guid
            : 0;
        Vector3 position = item != null ? item.transform.position : transform.position;

        unchecked
        {
            uint state = 2166136261u;
            state = (state ^ (uint)worldSeed) * 16777619u;
            state = (state ^ (uint)itemGuid) * 16777619u;
            state = (state ^ (uint)Mathf.FloorToInt(position.x)) * 16777619u;
            state = (state ^ (uint)Mathf.FloorToInt(position.y)) * 16777619u;
            return state == 0u ? 0x9E3779B9u : state;
        }
    }

    private static float Next01(ref uint state)
        => (Next(ref state) & 0xFFFFFFu) / (float)0x1000000;

    private static uint Next(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }
}
