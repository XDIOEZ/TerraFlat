using MemoryPack;
using UnityEngine;

public partial class Mod_Seed : Module
{
    public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
    public override float FixedTickInterval => 0.25f;

#region 数据定义

    [System.Serializable]
    [MemoryPackable]
    public partial class Mod_Seed_Data
    {
        [Tooltip("是否已种植")]
        public bool isPlanted = false;

        [Tooltip("种植的地块位置")]
        public Vector2Int plantedTilePos = Vector2Int.zero;

        [Tooltip("当前生长进度")]
        public float growProgress = 0f;

        [Tooltip("生长完成所需进度")]
        public float growCompletionThreshold = 100f;

        public Mod_Seed_Data() { }
    }

#endregion

#region 字段和属性

    public Mod_Seed_Data Data = new Mod_Seed_Data();
    public Ex_ModData_MemoryPackable ModData;

    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = (Ex_ModData_MemoryPackable)value;
    }

    [Header("播种配置")]
    [Tooltip("播种后生成的权威作物 Item 名称")]
    public string harvestedCropName = "Crop_Wheat";

    private Map _cachedMap;
    private bool _isActBound;

#endregion

#region 生命周期

    public override void Awake()
    {
        if (_Data.ID == "")
            _Data.ID = ModText.PlantSeed;
    }

    public override void Load()
    {
        ModData.ReadData(ref Data);
        BindItemActEvent();
    }

    public override void Save()
    {
        ModData.WriteData(Data);
    }

    public override void Unload()
    {
        UnbindItemActEvent();
    }

    private void OnDestroy()
    {
        Unload();
    }

#endregion

#region 种植逻辑

    public override void Act()
    {
        base.Act();
        PlantAuthoritativeCrop();
    }

#endregion

#region 事件绑定

    private void BindItemActEvent()
    {
        if (_isActBound || item == null)
            return;

        item.OnAct += Act;
        _isActBound = true;
    }

    private void UnbindItemActEvent()
    {
        if (!_isActBound || item == null)
            return;

        item.OnAct -= Act;
        _isActBound = false;
    }

#endregion

#region 辅助方法



    private bool TryResolveMapByWorldPos(Vector3 worldPos, out Map map)
    {
        map = null;

        if (ChunkMgr.Instance == null)
            return false;

        ChunkMgr.Instance.GetChunkBy_ItemPosition(worldPos, out Chunk chunk);
        if (chunk == null || chunk.Map == null)
            return false;

        map = chunk.Map;
        return true;
    }

    private void ConsumeSeedInHand()
    {
        item.itemData.Stack.Amount--;
        item.OnUIRefresh?.Invoke();
    }

#endregion

#region 生长更新

    public override void ModUpdate(float deltaTime)
    {
        MigrateLegacyPlantedSeed();
    }

#endregion
}
