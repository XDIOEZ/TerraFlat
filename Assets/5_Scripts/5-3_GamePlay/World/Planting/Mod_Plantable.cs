using System;
using FlatWorld.Networking;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 种植能力模块：物品只要挂载本模块，就会进入统一种植预览和放置链路。
/// 模块本身只保存作物 Item ID 与放置配置；耕地校验、作物生成和种子消耗集中在这里，
/// 具体成长实现只需实现 IPlantableCrop，种子模块不依赖某个成长组件。
/// </summary>
public sealed class Mod_Plantable : Module
{
    #region 模块数据

    public Ex_ModData_MemoryPackable ModData = new();

    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = (Ex_ModData_MemoryPackable)value;
    }

    public override string CanonicalModuleId => ModText.Plantable;
    public override ModuleTickMode TickMode => ModuleTickMode.EveryFrame;

    #endregion

    #region 种植配置

    [Header("种植产物")]
    [Tooltip("放置成功后生成的权威作物 Item ID。")]
    public string cropItemId = "Crop_Wheat";

    [Header("放置范围")]
    [Min(0.1f)]
    [Tooltip("从玩家位置到目标耕地中心的最大距离。")]
    public float maxPlantingDistance = Mod_InteractSender.DefaultMaxInteractDistance;

    [Header("预览表现")]
    [Range(0.1f, 1f)]
    public float previewAlpha = 0.7f;

    #endregion

    #region 运行时

    private bool actBound;
    private bool previewCreationFailed;
    private PlantingSummoner plantingSummoner;
    private GameController ownerController;

    /// <summary>当前指向有效耕地时，本模块保留这次使用动作，供食物等并存模块做优先级仲裁。</summary>
    public bool IsPlantingActionAvailable
    {
        get
        {
            if (!GameNetwork.HasStateAuthority || item == null || !item.InHand || item.Owner == null)
                return false;
            if (!TryResolveOwnerController(out GameController controller))
                return false;
            return TryResolvePlantingTarget(controller.GetMouseWorldPosition(), out _, out _);
        }
    }

    #endregion

    #region 生命周期

    public override void Awake()
    {
        ModData ??= new Ex_ModData_MemoryPackable();
        ModData.ID = ModText.Plantable;
    }

    public override void Load()
    {
        ModData ??= new Ex_ModData_MemoryPackable();
        item ??= GetComponentInParent<Item>();
        if (item == null)
            throw new MissingComponentException("[Mod_Plantable] 未找到所属 Item。");

        UnbindItemActEvent();
        BindItemActEvent();
    }

    public override void Save()
    {
        // 本模块没有额外运行时状态，配置字段随 ModuleData 直接持久化。
    }

    public override void Unload()
    {
        UnbindItemActEvent();
        DisposePlantingSummoner();
        ownerController = null;
    }

    private void OnDisable()
    {
        DisposePlantingSummoner();
    }

    private void OnDestroy()
    {
        Unload();
    }

    public override void ModUpdate(float deltaTime)
    {
        if (item == null || !item.InHand || item.Owner == null)
        {
            DisposePlantingSummoner();
            return;
        }

        if (!TryResolveOwnerController(out GameController controller))
        {
            DisposePlantingSummoner();
            return;
        }

        if (!EnsurePlantingSummoner())
            return;

        Vector3 pointerWorldPosition = controller.GetMouseWorldPosition();
        UpdatePreview(pointerWorldPosition);
    }

    #endregion

    #region 输入与统一预览

    public override void Act()
    {
        base.Act();

        if (!GameNetwork.HasStateAuthority || item == null || !item.InHand || item.Owner == null)
            return;

        if (!TryResolveOwnerController(out GameController controller))
            return;

        Vector3 pointerWorldPosition = controller.GetMouseWorldPosition();
        if (!TryResolvePlantingTarget(pointerWorldPosition, out PlantingTarget target, out string reason))
        {
            // 同一物品既是食物又是种子时，无效耕地应让食用动作自然接管，避免每次进食都刷种植警告。
            if (item.itemMods?.GetMod_ByID(ModText.Food) is not Mod_Food)
                Debug.LogWarning($"[种植] {reason}", item);
            return;
        }

        Item crop = TryCreateCultivatedCrop(target);
        if (crop == null)
            return;

        if (!ConsumeOneSeed())
        {
            ItemMgr.Instance.DespawnItem(crop, saveData: false);
            Debug.LogError("[种植] 作物已生成但种子扣除失败，已回滚作物。", item);
            return;
        }

        target.agriculture.RegisterCrop(target.tilePosition, crop);
        target.agriculture.CaptureState();

        Debug.Log($"[种植] {cropItemId} 已种下，地块={target.tilePosition}，剩余种子={item.itemData.Stack.Amount}", item);
        if (item.itemData.Stack.Amount <= 0f)
            item.DestroySelf();
    }

    private void UpdatePreview(Vector3 pointerWorldPosition)
    {
        Vector3 previewPosition = WorldTopologyRuntime.NormalizePosition(pointerWorldPosition);
        if (TryResolvePreviewCell(previewPosition, out Vector3 cellCenter))
            previewPosition = cellCenter;

        bool valid = TryResolvePlantingTarget(
            pointerWorldPosition,
            out PlantingTarget target,
            out _);
        if (valid)
            previewPosition = target.worldCenter;

        plantingSummoner.SetPreview(previewPosition, valid, previewAlpha);
    }

    private bool EnsurePlantingSummoner()
    {
        if (plantingSummoner != null)
            return true;

        if (previewCreationFailed || GameRes.Instance == null || string.IsNullOrWhiteSpace(cropItemId))
            return false;

        if (!GameRes.Instance.TryGetItemPresentation(cropItemId, out _, out Sprite cropSprite) || cropSprite == null)
        {
            previewCreationFailed = true;
            Debug.LogError($"[Mod_Plantable] 找不到作物 {cropItemId} 的预览 Sprite。", item);
            return false;
        }

        plantingSummoner = new PlantingSummoner(cropSprite);
        return true;
    }

    private void DisposePlantingSummoner()
    {
        plantingSummoner?.Dispose();
        plantingSummoner = null;
    }

    private void BindItemActEvent()
    {
        if (actBound || item == null)
            return;

        item.OnAct += Act;
        actBound = true;
    }

    private void UnbindItemActEvent()
    {
        if (!actBound || item == null)
            return;

        item.OnAct -= Act;
        actBound = false;
    }

    #endregion

    #region 目标解析

    private bool TryResolvePlantingTarget(
        Vector3 pointerWorldPosition,
        out PlantingTarget target,
        out string reason)
    {
        target = default;
        reason = null;

        if (item?.itemData?.Stack == null || item.itemData.Stack.Amount < 1f)
        {
            reason = "种子数量不足";
            return false;
        }

        if (ChunkMgr.ExistingInstance == null ||
            !ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(pointerWorldPosition, out var sample) ||
            !ChunkMgr.ExistingInstance.TryGetRuntimeChunkView(sample.Address, out ChunkView view) ||
            !FarmlandSystem.IsOpen(sample))
        {
            reason = "目标地块未加载或被建筑占用";
            return false;
        }
        Vector2Int tilePosition = sample.WorldCell;
        Vector3 worldCenter = new(tilePosition.x + 0.5f, tilePosition.y + 0.5f, 0f);
        if (!FarmlandSystem.TryReadSoil(tilePosition, out TileData_Farmland farmland))
        {
            reason = "请先把地块锄成耕地";
            return false;
        }
        farmland.NormalizeValues();
        if (farmland.waterValue <= 0f)
        {
            reason = $"地块 {tilePosition} 缺水，无法种植";
            return false;
        }

        if (farmland.Fertility <= 0f)
        {
            reason = $"地块 {tilePosition} 缺肥，无法种植";
            return false;
        }

        if (!TryResolveOwnerController(out GameController controller) || controller == null || item.Owner == null)
        {
            reason = "种植者控制器尚未就绪";
            return false;
        }

        if (!FarmlandSystem.IsWithinReach(item.Owner.transform.position, tilePosition, maxPlantingDistance))
        {
            reason = "目标地块超出种植范围";
            return false;
        }

        if (FarmlandSystem.HasWorldPlant(tilePosition))
        {
            reason = $"地块 {tilePosition} 已有作物，不能重复种植";
            return false;
        }

        target = new PlantingTarget(tilePosition, worldCenter, view.GetComponent<ChunkAgricultureRenderer>());
        return true;
    }

    private bool TryResolvePreviewCell(Vector3 pointerWorldPosition, out Vector3 cellCenter)
    {
        cellCenter = WorldTopologyRuntime.NormalizePosition(pointerWorldPosition);
        cellCenter = new Vector3(Mathf.Floor(cellCenter.x) + 0.5f, Mathf.Floor(cellCenter.y) + 0.5f, 0f);
        return ChunkMgr.ExistingInstance != null &&
            ChunkMgr.ExistingInstance.TryGetRuntimeTerrainTile(cellCenter, out _);
    }

    #endregion

    #region 作物生成与种子消耗

    private Item TryCreateCultivatedCrop(PlantingTarget target)
    {
        if (ItemMgr.Instance == null || string.IsNullOrWhiteSpace(cropItemId))
        {
            Debug.LogError("[Mod_Plantable] ItemMgr 未初始化或作物 Item ID 为空。", item);
            return null;
        }

        Item crop = null;
        try
        {
            crop = ItemMgr.Instance.InstantiateItem(
                cropItemId,
                target.worldCenter,
                Quaternion.identity,
                Vector3.one,
                target.agriculture.gameObject);
            crop.Load();
            crop.SetInHand(false);
            if (crop.itemData?.Stack == null)
                throw new MissingComponentException($"作物 {cropItemId} 缺少堆叠数据。");

            crop.itemData.Stack.CanBePickedUp = false;
            IPlantableCrop[] plantingModules = crop.GetComponentsInChildren<IPlantableCrop>(true);
            if (plantingModules.Length != 1)
                throw new MissingComponentException(
                    $"作物 {cropItemId} 必须且只能包含一个 IPlantableCrop，当前数量={plantingModules.Length}。");

            plantingModules[0].InitializePlantedCrop(target.tilePosition);
            return crop;
        }
        catch (Exception exception)
        {
            if (crop != null && !crop.DestructionHandled)
                ItemMgr.Instance.DespawnItem(crop, saveData: false);

            Debug.LogError($"[Mod_Plantable] 创建作物 {cropItemId} 失败，未消耗种子。\n{exception}", item);
            return null;
        }
    }

    private bool ConsumeOneSeed()
    {
        if (item?.itemData?.Stack == null || item.itemData.Stack.Amount < 1f)
            return false;

        item.itemData.Stack.Amount -= 1f;
        item.OnUIRefresh?.Invoke();
        return true;
    }

    #endregion

    #region 玩家控制器

    private bool TryResolveOwnerController(out GameController controller)
    {
        if (ownerController != null)
        {
            controller = ownerController;
            return true;
        }

        Item owner = item?.Owner;
        ownerController = owner?.itemMods?.GetMod_ByID<GameController>(ModText.Controller);
        ownerController ??= owner?.GetComponent<GameController>();
        controller = ownerController;
        return controller != null;
    }

    #endregion

    #region 目标数据

    private readonly struct PlantingTarget
    {
        public readonly Vector2Int tilePosition;
        public readonly Vector3 worldCenter;
        public readonly ChunkAgricultureRenderer agriculture;

        public PlantingTarget(Vector2Int tilePosition, Vector3 worldCenter, ChunkAgricultureRenderer agriculture)
        {
            this.tilePosition = tilePosition;
            this.worldCenter = worldCenter;
            this.agriculture = agriculture;
        }
    }

    #endregion
}
