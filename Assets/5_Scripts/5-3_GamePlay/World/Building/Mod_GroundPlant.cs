using System;
using UnityEngine;

/// <summary>
/// 地栽模块：只负责建筑实例的埋入式表现和拔取交互，不参与建筑占地、存档或放置校验。
/// 使用 Sprite-Lit-Master 的 BodyClip 保留地上半截；E 键交互复用掉落模块的贝塞尔抛物线，
/// 产出一个可再次种植的萝卜物品后再销毁地里的建筑实例。
/// </summary>
public sealed class Mod_GroundPlant : Module, IInteractable
{
    #region Shader 属性

    private static readonly int BodyClipProperty = Shader.PropertyToID("_BodyClip");
    private static readonly int BodyMinVProperty = Shader.PropertyToID("_BodyMinV");
    private static readonly int BodyMaxVProperty = Shader.PropertyToID("_BodyMaxV");

    #endregion

    #region 模块数据

    public Ex_ModData GroundPlantData = new();

    public override ModuleData _Data
    {
        get => GroundPlantData;
        set => GroundPlantData = value as Ex_ModData ??
            throw new ArgumentException("[Mod_GroundPlant] 模块数据类型错误。", nameof(value));
    }

    public override string CanonicalModuleId => ModText.GroundPlant;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;

    #endregion

    #region 配置

    [Header("拔取产出")]
    public string harvestItemId = "Radish";

    [Header("地表遮挡")]
    [Range(0f, 1f)]
    public float buriedClip = 0.5f;

    [Header("拔取掉落表现")]
    [Min(0.05f)]
    public float pullRadius = 1f;

    [Min(0.05f)]
    public float pullDuration = 0.5f;

    public float pullBezierOffset = 0.8f;
    public float pullArcHeight = 0.6f;

    #endregion

    #region 运行时

    private Mod_Building building;
    private SpriteRenderer spriteRenderer;
    private MaterialPropertyBlock propertyBlock;
    private bool pullInProgress;

    #endregion

    #region 生命周期

    public override void Awake()
    {
        base.Awake();
        EnsureDataContainer();
    }

    public override void Load()
    {
        EnsureDataContainer();
        item ??= GetComponentInParent<Item>();
        if (item == null)
            throw new MissingComponentException("[Mod_GroundPlant] 未找到所属 Item");

        building = item.itemMods?.GetMod_ByID<Mod_Building>(ModText.Building);
        if (building == null)
            throw new MissingComponentException("[Mod_GroundPlant] 地栽物品缺少建筑模块");

        spriteRenderer = item.Sprite ?? item.GetComponentInChildren<SpriteRenderer>(true);
        if (spriteRenderer == null || spriteRenderer.sprite == null)
            throw new MissingComponentException("[Mod_GroundPlant] 地栽物品缺少有效 SpriteRenderer");

        UnbindBuildingState();
        building.OnStateChanged += OnBuildingStateChanged;
        ApplyVisualState();
    }

    public override void Save()
    {
        if (GroundPlantData == null || item?.itemData?.ModuleDataDic == null ||
            string.IsNullOrWhiteSpace(GroundPlantData.Name))
            return;

        item.itemData.ModuleDataDic[GroundPlantData.Name] = GroundPlantData;
    }

    private void OnDisable()
    {
        UnbindBuildingState();
    }

    private void OnDestroy()
    {
        UnbindBuildingState();
    }

    #endregion

    #region 地栽表现

    /// <summary>建筑状态变化时切换完整萝卜与埋入式萝卜表现。</summary>
    private void OnBuildingStateChanged(BuildingState previous, BuildingState current)
    {
        ApplyVisualState();
    }

    /// <summary>沿用 Sprite-Lit-Master 的 BodyClip，裁掉萝卜下半截而不改动原始素材。</summary>
    private void ApplyVisualState()
    {
        if (spriteRenderer == null || spriteRenderer.sprite == null || building == null)
            return;

        propertyBlock ??= new MaterialPropertyBlock();
        Bounds spriteBounds = spriteRenderer.sprite.bounds;
        spriteRenderer.GetPropertyBlock(propertyBlock);
        propertyBlock.SetFloat(BodyMinVProperty, spriteBounds.min.y);
        propertyBlock.SetFloat(BodyMaxVProperty, spriteBounds.max.y);
        propertyBlock.SetFloat(
            BodyClipProperty,
            building.IsInstalled() ? Mathf.Clamp01(buriedClip) : 0f);
        spriteRenderer.SetPropertyBlock(propertyBlock);
    }

    private void EnsureDataContainer()
    {
        GroundPlantData ??= new Ex_ModData();
        GroundPlantData.ID = ModText.GroundPlant;
    }

    private void UnbindBuildingState()
    {
        if (building == null)
            return;

        building.OnStateChanged -= OnBuildingStateChanged;
    }

    #endregion

    #region 交互

    public bool CanInteract(Item playerItem)
    {
        return playerItem != null && !pullInProgress && item != null &&
            !item.DestructionHandled && building != null && building.IsInstalled();
    }

    /// <summary>按 E 生成萝卜掉落物，并复用浆果同款的抛物线飞出效果。</summary>
    public void OnInteractStart(Item playerItem)
    {
        if (playerItem == null)
            throw new ArgumentNullException(nameof(playerItem), "[Mod_GroundPlant] 拔取失败：playerItem 为空。");

        if (!CanInteract(playerItem))
            return;

        pullInProgress = true;
        Item harvestItem = null;
        try
        {
            if (ItemMgr.Instance == null)
                throw new InvalidOperationException("[Mod_GroundPlant] 拔取失败：ItemMgr 尚未初始化");

            Vector2 startPosition = item.transform.position;
            harvestItem = ItemMgr.Instance.InstantiateItem(
                harvestItemId,
                startPosition,
                Quaternion.identity,
                Vector3.one);
            if (harvestItem == null)
                throw new MissingReferenceException($"[Mod_GroundPlant] 找不到拔取产物：{harvestItemId}");

            harvestItem.Load();
            harvestItem.SetInHand(false);
            if (harvestItem.itemData?.Stack == null)
                throw new MissingComponentException($"[Mod_GroundPlant] 产物 {harvestItemId} 缺少堆叠数据");

            harvestItem.itemData.Stack.Amount = 1f;
            Vector2 endPosition = ResolvePullEndPosition(startPosition);
            Mod_BaseDroper.StaticDropItem_Pos(
                harvestItem,
                startPosition,
                endPosition,
                Mathf.Max(0.05f, pullDuration),
                Mod_BaseDroper.MoveMode.BezierCurve,
                pullBezierOffset,
                pullArcHeight);

            ItemMgr.Instance.DespawnItem(item, saveData: false);
        }
        catch
        {
            if (harvestItem != null && !harvestItem.DestructionHandled)
                ItemMgr.Instance.DespawnItem(harvestItem, saveData: false);
            throw;
        }
        finally
        {
            pullInProgress = false;
        }
    }

    public void OnInteractCancel(Item playerItem)
    {
    }

    private Vector2 ResolvePullEndPosition(Vector2 startPosition)
    {
        Vector2 randomDirection = UnityEngine.Random.insideUnitCircle.normalized;
        float radius = Mathf.Max(0.05f, pullRadius);
        float distance = UnityEngine.Random.Range(radius * 0.5f, radius);
        return startPosition + randomDirection * distance;
    }

    #endregion
}
