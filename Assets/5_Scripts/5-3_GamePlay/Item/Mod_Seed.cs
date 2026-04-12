using MemoryPack;
using System.Collections.Generic;
using UnityEngine;

public partial class Mod_Seed : Module
{
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

    [Header("种子配置")]
    [Tooltip("生长速度（每秒增长进度点数）")]
    public float baseGrowthSpeed = 5f;

    [Tooltip("生长完成后生成的幼苗 Item 名称")]
    public string harvestedCropName = "Crop_Wheat"; // 默认小麦

    [Tooltip("生长过程中每秒消耗的肥力")]
    public float fertilityConsumePerSecond = 0.5f;

    private Map _cachedMap;
    private Tile_Farmland _cachedFarmlandBehaviour;
    private TileData_Farmland _currentFarmlandData;
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
        UnbindItemActEvent();
        ModData.WriteData(Data);
    }

    private void OnDestroy()
    {
        UnbindItemActEvent();
    }

#endregion

#region 种植逻辑

    public override void Act()
    {
        base.Act();

        if (item == null || item.Owner == null)
        {
            Debug.LogWarning("[Mod_Seed] 种种子失败：物品或玩家为空");
            return;
        }

        Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        Vector2Int cellPos = GetTilePosFromWorldPos(mouseWorldPos);

        if (!TryPlantSeed(cellPos))
        {
            Debug.LogWarning($"[Mod_Seed] 无法在 {cellPos} 种植种子，该位置不是耕地或环境不符");
            return;
        }

        Item plantedSeed = CreatePlantedSeedInstance(cellPos);
        ResetHandSeedPlantState();
        ConsumeSeedInHand();

        Debug.Log($"[Mod_Seed] 种子已种植在 {cellPos}，地块实例={plantedSeed.name}，手上剩余：{item.itemData.Stack.Amount}");

        if (item.itemData.Stack.Amount <= 0)
            Destroy(item.gameObject);
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

    private Vector2Int GetTilePosFromWorldPos(Vector3 worldPos)
    {
        if (!TryResolveMapByWorldPos(worldPos, out Map targetMap))
            throw new System.NullReferenceException($"[Mod_Seed] 无法根据位置 {worldPos} 获取有效 Chunk/Map");

        _cachedMap = targetMap;
        Vector3Int cellPos3D = targetMap.tileMap.WorldToCell(worldPos);
        return new Vector2Int(cellPos3D.x, cellPos3D.y);
    }

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

    private bool TryPlantSeed(Vector2Int tilePos)
    {
        if (_cachedMap == null)
            GetTilePosFromWorldPos(Vector3.zero); // 初始化 Map

        TileData topTile = _cachedMap.GetTileAt(tilePos, 0);
        if (topTile == null || topTile is not TileData_Farmland farmlandData)
        {
            Debug.LogWarning($"[Mod_Seed] 位置 {tilePos} 没有耕地（TileData_Farmland）");
            return false;
        }

        if (farmlandData.waterValue <= 0f)
        {
            Debug.LogWarning($"[Mod_Seed] 位置 {tilePos} 没有水分，无法种植");
            return false;
        }

        if (farmlandData.fertilityValue.Value <= 0f)
        {
            Debug.LogWarning($"[Mod_Seed] 位置 {tilePos} 没有肥力，无法种植");
            return false;
        }

        // 种子成功种植：记录信息
        Data.isPlanted = true;
        Data.plantedTilePos = tilePos;
        Data.growProgress = 0f;
        _currentFarmlandData = farmlandData;

        Debug.Log($"[Mod_Seed] ✓ 种子已成功种植在 {tilePos}，水分={farmlandData.waterValue:F1}，肥力={farmlandData.fertilityValue.Value:F1}");
        return true;
    }

    private Item CreatePlantedSeedInstance(Vector2Int tilePos)
    {
        if (_cachedMap == null)
            throw new System.NullReferenceException("[Mod_Seed] _cachedMap为空，无法创建已种植种子实例");

        item.Save();
      
        ItemData plantedSeedData = FastCloner.FastCloner.DeepClone(item.itemData);
        plantedSeedData.Stack.Amount = 1;
        plantedSeedData.Stack.CanBePickedUp = false;
        plantedSeedData.inHand = false;

        Vector3 plantedWorldPos = _cachedMap.tileMap.GetCellCenterWorld(new Vector3Int(tilePos.x, tilePos.y, 0));
        Item plantedSeed = ItemMgr.Instance.InstantiateItem(plantedSeedData, plantedWorldPos, Quaternion.identity);
        if (plantedSeed == null)
            throw new System.NullReferenceException($"[Mod_Seed] 创建地块种子失败，位置={tilePos}");

        plantedSeed.Load();
        plantedSeed.SetInHand(false);
        plantedSeed.itemData.Stack.Amount = 1;
        plantedSeed.itemData.Stack.CanBePickedUp = false;
        ApplyPlantedSeedSpriteScale(plantedSeed, 1);

        // 重新绑定手上物品的Act事件（item.Save()会解绑，需要恢复）
        BindItemActEvent();

        return plantedSeed;
    }

    private void ConsumeSeedInHand()
    {
        item.itemData.Stack.Amount--;
        item.OnUIRefresh?.Invoke();
    }

    private void ResetHandSeedPlantState()
    {
        Data.isPlanted = false;
        Data.plantedTilePos = Vector2Int.zero;
        Data.growProgress = 0f;
        _currentFarmlandData = null;
        _cachedFarmlandBehaviour = null;
    }

    private void ApplyPlantedSeedSpriteScale(Item plantedSeed, float scale)
    {
        if (plantedSeed == null)
            throw new System.ArgumentNullException(nameof(plantedSeed));

        Transform spriteTransform = plantedSeed.transform.Find("SpriteRenderer");
        if (spriteTransform == null)
        {
            SpriteRenderer spriteRenderer = plantedSeed.GetComponentInChildren<SpriteRenderer>(true);
            if (spriteRenderer != null)
                spriteTransform = spriteRenderer.transform;
        }

        if (spriteTransform == null)
        {
            Debug.LogWarning($"[Mod_Seed] 未找到落地种子的SpriteRenderer节点，无法设置缩放，实例={plantedSeed.name}");
            return;
        }

        spriteTransform.localScale = new Vector3(scale, scale, scale);
    }

#endregion

#region 生长更新

    public override void ModUpdate(float deltaTime)
    {
        if (!Data.isPlanted || item == null)
            return;

        // 获取当前地块数据
        if (!UpdateCurrentFarmlandData())
        {
            Debug.LogWarning("[Mod_Seed] 无法更新地块数据，停止生长");
            Data.isPlanted = false;
            return;
        }

        // 检查水分和肥力是否满足
        if (_currentFarmlandData.waterValue <= 0f)
        {
            Debug.Log("[Mod_Seed] 缺水，暂停生长");
            return;
        }

        if (_currentFarmlandData.fertilityValue.Value <= 0f)
        {
            Debug.Log("[Mod_Seed] 缺肥料，暂停生长");
            return;
        }

        // 计算生长速度倍率（基于肥力和水分）
        float growthMultiplier = CalculateGrowthMultiplier();
        if (growthMultiplier <= 0f)
        {
            Debug.Log("[Mod_Seed] 生长倍率为 0，停止生长");
            return;
        }

        // 增加生长进度
        float growthThisFrame = baseGrowthSpeed * growthMultiplier * deltaTime;
        Data.growProgress += growthThisFrame;

        // 消耗肥力和水分
        if (fertilityConsumePerSecond > 0f)
        {
            _currentFarmlandData.fertilityValue = new GameValue_float
            {
                BaseValue = Mathf.Max(0.01f, _currentFarmlandData.fertilityValue.BaseValue - fertilityConsumePerSecond * deltaTime),
                BaseAdditive = _currentFarmlandData.fertilityValue.BaseAdditive,
                AdditiveModifier = _currentFarmlandData.fertilityValue.AdditiveModifier,
                MultiplicativeModifier = _currentFarmlandData.fertilityValue.MultiplicativeModifier,
                FinalAdditive = _currentFarmlandData.fertilityValue.FinalAdditive
            };
        }

        Tile_Farmland farmlandBehaviour = GetFarmlandBehaviour();
        if (farmlandBehaviour != null)
        {
            // 消耗水分（委托给耕地行为）
            farmlandBehaviour.ConsumeWater(_currentFarmlandData, 0.1f * deltaTime);
        }

        // 检查生长完成
        if (Data.growProgress >= Data.growCompletionThreshold)
        {
            CompleteGrowth();
        }
    }

    private bool UpdateCurrentFarmlandData()
    {
        Vector3 plantedWorldCenter = new Vector3(Data.plantedTilePos.x + 0.5f, Data.plantedTilePos.y + 0.5f, 0f);
        if (!TryResolveMapByWorldPos(plantedWorldCenter, out Map targetMap))
            return false;

        _cachedMap = targetMap;

        TileData topTile = _cachedMap.GetTileAt(Data.plantedTilePos, 0);
        if (topTile == null || topTile is not TileData_Farmland farmlandData)
            return false;

        _currentFarmlandData = farmlandData;
        return true;
    }

    private float CalculateGrowthMultiplier()
    {
        if (_currentFarmlandData == null)
            return 0f;

        Tile_Farmland farmlandBehaviour = GetFarmlandBehaviour();
        if (farmlandBehaviour == null)
            return 1f; // 默认倍率

        // 获取耕地计算的倍率（肥力和水分综合）
        return farmlandBehaviour.GetGrowSpeedMultiplier(_currentFarmlandData);
    }

    private Tile_Farmland GetFarmlandBehaviour()
    {
        if (_cachedFarmlandBehaviour != null)
            return _cachedFarmlandBehaviour;

        if (_cachedMap == null)
            return null;

        TileData topTile = _cachedMap.GetTileAt(Data.plantedTilePos, 0);
        if (topTile == null || topTile is not TileData_Farmland)
            return null;

        // 从 Tile_Block 的行为列表中获取 Tile_Farmland
        if (!GameRes.Instance.TileBlockDict.TryGetValue(topTile.Name, out var tileBlock))
            return null;

        if (tileBlock.behaviours == null || tileBlock.behaviours.Count == 0)
            return null;

        foreach (var behaviour in tileBlock.behaviours)
        {
            if (behaviour is Tile_Farmland farmland)
            {
                _cachedFarmlandBehaviour = farmland;
                return farmland;
            }
        }

        return null;
    }

    private void CompleteGrowth()
    {
        Data.isPlanted = false;

        Vector3 plantedWorldCenter = new Vector3(Data.plantedTilePos.x + 0.5f, Data.plantedTilePos.y + 0.5f, 0f);
        if (!TryResolveMapByWorldPos(plantedWorldCenter, out Map targetMap))
        {
            Debug.LogError($"[Mod_Seed] 无法为幼苗定位 Map，种植位置={Data.plantedTilePos}");
            return;
        }

        _cachedMap = targetMap;

        Debug.Log($"[Mod_Seed] ✓ 种子发芽完成，准备交接给幼苗：{harvestedCropName}");

        // 在地块位置生成幼苗 Item，后续由幼苗模块继续发育
        if (string.IsNullOrEmpty(harvestedCropName))
        {
            Debug.LogError("[Mod_Seed] 幼苗Item名称为空，无法完成种子到幼苗交接");
            return;
        }

        Vector3 spawnWorldPos = _cachedMap.tileMap.GetCellCenterWorld(
            new Vector3Int(Data.plantedTilePos.x, Data.plantedTilePos.y, 0)
        );

        Item harvestedCrop = ItemMgr.Instance.InstantiateItem(
            harvestedCropName,
            spawnWorldPos,
            Quaternion.identity
        );

        if (harvestedCrop == null)
        {
            Debug.LogError($"[Mod_Seed] 无法生成幼苗：{harvestedCropName}");
            return;
        }

        harvestedCrop.Load();
        harvestedCrop.SetInHand(false);
        harvestedCrop.itemData.Stack.CanBePickedUp = false;
        Debug.Log($"[Mod_Seed] 幼苗已生成在 {Data.plantedTilePos}，Mod_Seed职责结束并销毁自身");

        Destroy(item.gameObject);
    }

#endregion
}
