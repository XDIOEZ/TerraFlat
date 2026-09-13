using UnityEngine;

public class Mod_Droping : Module
{
	private const string WaterEffectMaterialAddress = "Assets/9_Shaders/Material/Sprite-Lit-Master.mat";

	#region 运行时归属

    public override ModuleData _Data
    {
        get => modData;
        set => modData = (Ex_ModData)value;
    }

    public Mod_BaseDroper.Drop drop;
    public Ex_ModData modData;
    /// <summary>资源目录注册 Prefab 时使用的稳定掉落模块 ID，不依赖实例化后的 Awake。</summary>
    public override string CanonicalModuleId => ModText.Drop;
    public Chunk LastChunk; // 上一帧 item 所处的 chunk
    private bool usesLegacyChunkOwnership;

    private bool isWaterSinking;
    private float waterSinkElapsed;
    private float resolvedWaterSinkDuration;
    private bool isWaterFloating;
    private float waterFloatElapsed;
    private float waterFloatTargetDepth;
    private bool waterFloatSettled;
    private bool suppressFloatingPersistenceOnce;
    private Item waterItem;
    private ActorRenderEffectController waterRenderEffects;
    private WaterImmersionRenderEffect waterImmersionEffect;

    /// <summary>只要掉落模块仍持有轨迹数据，物品就处于掉落阶段。</summary>
    public static bool IsDropInProgress(Item targetItem)
    {
        Mod_Droping dropping = targetItem?.itemMods?.GetMod_ByID<Mod_Droping>(ModText.Drop);
        return dropping?.drop != null &&
               !dropping.drop.waterFloating &&
               !dropping.drop.waterSinking;
    }

    /// <summary>拾取前只从本次快照移除水中浮沉用的临时掉落模块，避免动态世界状态进入库存 ItemData。</summary>
    public static void PrepareFloatingPickupSnapshot(Item targetItem)
    {
        Mod_Droping dropping = targetItem?.itemMods?.GetMod_ByID<Mod_Droping>(ModText.Drop);
        if (dropping?.drop == null ||
            !dropping.drop.waterFloating && !dropping.drop.waterSinking)
            return;

        dropping.suppressFloatingPersistenceOnce = true;
        dropping.RemoveOwnPersistedModuleData();
    }

    /// <summary>水中物品已被完整拾取时先摘除临时模块，恢复原材质并保持物品对象池层级完整。</summary>
    public static void PrepareFloatingItemForDespawn(Item targetItem)
    {
        Mod_Droping dropping = targetItem?.itemMods?.GetMod_ByID<Mod_Droping>(ModText.Drop);
        if (dropping?.drop == null ||
            !dropping.drop.waterFloating && !dropping.drop.waterSinking)
            return;

        dropping.transform.SetParent(null, true);
        Module.REMOVEModFROMItem(targetItem, dropping._Data);
    }

	#endregion

    [Header("丢弃动画参数")]
    [Tooltip("垂直方向最大高度（与之前一致）")]
    public float arcHeight = 1f;

    [Header("水体浮沉")]
    [Tooltip("游戏内重量/体积比达到该值时下沉。该参数按现有物品数据标定，不按真实水密度解释。")]
    [SerializeField, Min(0.01f)] private float sinkRatioThreshold = 0.64f;

    [Tooltip("刚超过下沉阈值时，从水面到完全没入的最长时间。")]
    [SerializeField, Min(0.1f)] private float slowSinkDuration = 4.5f;

    [Tooltip("高密度物品从水面到完全没入的最短时间。")]
    [SerializeField, Min(0.1f)] private float fastSinkDuration = 1f;

    [Tooltip("重量/体积比达到“下沉阈值 × 此倍率”时使用最快下沉时间。")]
    [SerializeField, Min(1.01f)] private float fastSinkRatioMultiplier = 1.3f;

    [Tooltip("极轻漂浮物最终水线深度；数值越小，露出水面的部分越多。")]
    [SerializeField, Range(0f, 1f)] private float floatingMinDepth = 0.08f;

    [Tooltip("接近下沉阈值但仍能漂浮的物品最终水线深度。")]
    [SerializeField, Range(0f, 1f)] private float floatingMaxDepth = 0.42f;

    [Tooltip("物品刚落水时的瞬时浸没深度；随后会向自身浮力平衡深度上浮。")]
    [SerializeField, Range(0f, 1f)] private float floatingEntryDepth = 0.48f;

    [Tooltip("漂浮物从入水深度上浮到稳定水线所需时间。")]
    [SerializeField, Min(0.05f)] private float floatingRiseDuration = 0.8f;

    [Tooltip("漂浮物沿河流真实下游方向移动的世界单位速度。")]
    [SerializeField, Min(0f)] private float riverDriftSpeed = 0.45f;

    [Tooltip("海面没有独立洋流数据时，沿现有风场方向移动的较弱世界单位速度。")]
    [SerializeField, Min(0f)] private float oceanDriftSpeed = 0.15f;

    public override ModuleTickMode TickMode =>
        isWaterFloating && waterFloatSettled
            ? ModuleTickMode.FixedInterval
            : ModuleTickMode.EveryFrame;

    public override float FixedTickInterval => 0.5f;

    public override void Awake()
    {
         base.Awake();
    }

    public override void Load()
    {
        modData.ReadData(ref drop);
        BindDropItemReference();

        // 掉落物先尝试绑定新版 ChunkView。新区块窗口已启用时，即使当前画面尚未
        // 完成绑定也不能回退到旧 Chunk 查询，否则灌木死亡掉落会触发同步加载卡顿。
        bool attachedToWorldModel = item != null &&
            ItemWorldPlacement.TryAttachWorldModelDrop(item, item.transform.position);
        bool worldModelActive = ChunkMgr.ExistingInstance != null &&
            ChunkMgr.ExistingInstance.IsWorldModelRuntimeActive;
        usesLegacyChunkOwnership = !attachedToWorldModel && !worldModelActive;
        LastChunk = usesLegacyChunkOwnership && item != null
            ? item.GetComponentInParent<Chunk>()
            : null;
        if (drop != null && item?.itemData?.Stack != null)
            item.itemData.Stack.CanBePickedUp = false;

        if (drop?.waterSinking == true && item != null)
        {
            BeginWaterSink(item, drop.waterSinkElapsed);
        }
        else if (drop?.waterFloating == true && item != null)
        {
            float floatDepth = drop.waterFloatDepth > 0f
                ? drop.waterFloatDepth
                : ResolveFloatingDepth(ResolveWeightVolumeRatio(item));
            BeginWaterFloat(item, floatDepth, drop.waterFloatElapsed);
        }
    }

    public override void ModUpdate(float deltaTime)
    {
        if (isWaterSinking)
        {
            UpdateWaterSink(deltaTime);
            return;
        }

        if (isWaterFloating)
        {
            UpdateWaterFloat(deltaTime);
            return;
        }

        // 检测droping是否为空，如果为空自动销毁模块本身
        if (drop == null)
        {
            Module.REMOVEModFROMItem(item, _Data);
            return;
        }

        // 掉落模块挂在掉落物自身，优先绑定宿主，避免注册顺序导致 GUID 反查失败。
        if (drop.item == null)
        {
            BindDropItemReference();
            if (drop.item == null)
                return;
        }

        // 更新进度时间并计算插值参数
        drop.progressTime += deltaTime;
        float duration = Mathf.Max(0.0001f, drop.time);
        float t = Mathf.Clamp01(drop.progressTime / duration);

        // Chunk 归属按地面轨迹计算。贝塞尔高度和 arcHeight 只是表现层高度，
        // 不能让物品在抛起时误切换到上方相邻 Chunk。
        Vector2 ownershipPos = WorldTopologyRuntime.NormalizePosition(Vector2.Lerp(drop.startPos, drop.endPos, t));

        // 区块画面可能正在分帧绑定；新版掉落在动画期间重试归属，
        // 仍只访问 WorldModel，不触发旧 Chunk 加载。
        if (!usesLegacyChunkOwnership &&
            drop.item.GetComponentInParent<ChunkNaturalItemRenderer>(true) == null)
        {
            ItemWorldPlacement.TryAttachWorldModelDrop(drop.item, ownershipPos);
        }

        // 使用存储在drop中的控制点进行贝塞尔插值计算位置
        Vector2 pos = Bezier2(drop.startPos, drop.controlPos, drop.endPos, t);

        // 垂直方向叠加正弦高度，形成抛物线效果
        pos.y += Mathf.Sin(t * Mathf.PI) * arcHeight;

        // 更新物品位置和旋转
        pos = WorldTopologyRuntime.NormalizePosition(pos);
        drop.item.transform.position = new Vector3(pos.x, pos.y, 0);
        drop.item.transform.Rotate(Vector3.forward * drop.rotationSpeed * deltaTime);

        // 旧 Chunk 物品仍同步归属；新版生态物品保持在 NaturalItems 下，不触发旧区块加载。
        bool hasTargetChunk = !usesLegacyChunkOwnership ||
            UpdateChunkOwner(drop.item, ownershipPos);

        // 检查动画是否完成
        if (t >= 1f)
        {
            if (usesLegacyChunkOwnership && !hasTargetChunk)
            {
                RequestTargetChunk(Chunk.GetChunkPosition(WorldTopologyRuntime.NormalizePosition(drop.endPos)));
                return;
            }

            if (usesLegacyChunkOwnership)
            {
                // 确保 Chunk 内的位置索引记录的是最终落点，而不是动画起点。
                LastChunk.AddItem(drop.item);
            }
            Item landedItem = drop.item;

            if (TryResolveWaterAt(landedItem.transform.position, out bool isWater) && isWater)
            {
                if (TryTransformOnWaterEntry(landedItem))
                    return;

                float ratio = ResolveWeightVolumeRatio(landedItem);
                if (ratio >= sinkRatioThreshold)
                {
                    drop.waterSinking = true;
                    drop.waterFloating = false;
                    drop.waterSinkElapsed = 0f;
                    BeginWaterSink(landedItem, 0f);
                }
                else
                {
                    float floatDepth = ResolveFloatingDepth(ratio);
                    drop.waterSinking = false;
                    drop.waterFloating = true;
                    drop.waterFloatDepth = floatDepth;
                    drop.waterFloatElapsed = 0f;
                    BeginWaterFloat(landedItem, floatDepth, 0f);
                }
                return;
            }

            drop = null;
            // 先结束掉落状态并移除驱动模块，再开放拾取，避免拾取回调与轨迹更新竞争同一物品。
            Module.REMOVEModFROMItem(item, _Data);
            landedItem.itemData.Stack.CanBePickedUp = true;
        }
    }

    #region 水体浮沉

    /// <summary>按权威地表查询是否为水体；查询失败与明确非水分开，避免区块切换时误结束漂浮。</summary>
    private static bool TryResolveWaterAt(Vector2 worldPosition, out bool isWater)
    {
        isWater = false;
        ChunkMgr chunkMgr = ChunkMgr.ExistingInstance;
        if (chunkMgr == null ||
            !chunkMgr.TryGetRuntimeTerrainTile(worldPosition, out RuntimeTerrainTileSample sample))
        {
            return false;
        }

        isWater = (sample.Cell.Flags & FlatWorld.WorldModel.TerrainCellFlags.Water) != 0;
        return true;
    }

    /// <summary>按物品 JSON 契约执行入水转换；替换物重新进入同一套掉落水体逻辑。</summary>
    private static bool TryTransformOnWaterEntry(Item landedItem)
    {
        if (landedItem?.itemData == null)
            return false;

        GameRes gameRes = GameRes.Instance;
        if (!gameRes.TryGetItemDefinition(landedItem.itemData.IDName, out RuntimeItemDefinition definition) ||
            string.IsNullOrWhiteSpace(definition.WaterEntryTransformItemId))
        {
            return false;
        }

        Vector3 position = landedItem.transform.position;
        float amount = landedItem.itemData.Stack.Amount;
        ItemData replacementData = gameRes.CreateItemData(definition.WaterEntryTransformItemId);
        replacementData.Stack.Amount = amount;

        PlayWaterEntryTransformEffects(landedItem);

        ItemMgr itemMgr = ItemMgr.Instance;
        itemMgr.DespawnItem(landedItem, saveData: false);

        Item replacement = itemMgr.InstantiateItem(
            replacementData,
            position,
            Quaternion.identity,
            Vector3.one);
        StaticDropItem_Pos(
            replacement,
            position,
            position,
            0f,
            isLinear: true,
            bezierOffset: 0f,
            arcHeight: 0f,
            minRotationSpeed: 0f,
            maxRotationSpeed: 0f);
        return true;
    }

    /// <summary>让源物品自身决定入水转换表现；掉落系统不感知火把等具体玩法类型。</summary>
    private static void PlayWaterEntryTransformEffects(Item landedItem)
    {
        MonoBehaviour[] behaviours = landedItem.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IWaterEntryTransformEffect effect)
                effect.PlayWaterEntryTransformEffect();
        }
    }

    /// <summary>
    /// 使用物品定义里的“单位重量 / 单位体积”作为玩法浮沉比。
    /// 这些字段本来服务于携带容量，不能把 1.0 机械视作真实水密度；阈值必须按项目内容标定。
    /// </summary>
    private static float ResolveWeightVolumeRatio(Item targetItem)
    {
        ItemStack stack = targetItem?.itemData?.Stack;
        if (stack == null)
            return 0f;

        float volume = Mathf.Max(0.0001f, stack.Volume);
        return Mathf.Max(0f, stack.Weight) / volume;
    }

    /// <summary>重量/体积比越高，下沉越快；从阈值附近的慢沉平滑过渡到最快下沉。</summary>
    private float ResolveSinkDuration(float ratio)
    {
        float threshold = Mathf.Max(0.01f, sinkRatioThreshold);
        float fastRatio = threshold * Mathf.Max(1.01f, fastSinkRatioMultiplier);
        float ratioT = Mathf.InverseLerp(threshold, fastRatio, Mathf.Max(threshold, ratio));
        float slowDuration = Mathf.Max(0.1f, slowSinkDuration);
        float fastDuration = Mathf.Clamp(fastSinkDuration, 0.1f, slowDuration);
        return Mathf.Lerp(slowDuration, fastDuration, ratioT);
    }

    /// <summary>漂浮物的重量/体积比越接近阈值，稳定后浸入水中的比例越高，但始终保留可见部分。</summary>
    private float ResolveFloatingDepth(float ratio)
    {
        float threshold = Mathf.Max(0.01f, sinkRatioThreshold);
        float ratioT = Mathf.Clamp01(ratio / threshold);
        float minDepth = Mathf.Clamp01(Mathf.Min(floatingMinDepth, floatingMaxDepth));
        float maxDepth = Mathf.Clamp01(Mathf.Max(floatingMinDepth, floatingMaxDepth));
        return Mathf.Lerp(minDepth, maxDepth, ratioT);
    }

    /// <summary>建立掉落物专用的水下渲染绑定，复用角色使用的 WaterImmersionRenderEffect。</summary>
    private void EnsureWaterVisual(Item targetItem)
    {
        if (targetItem == null)
            return;

        GameRes gameRes = GameRes.ExistingInstance;
        if (gameRes == null)
            throw new System.InvalidOperationException("掉落物进入水体时 GameRes 不存在，无法加载水体表现材质。");

        var materialHandle = gameRes.ResourceAssets.Load<Material>(WaterEffectMaterialAddress);
        materialHandle.WaitForCompletion();
        Material waterEffectMaterial = ResourceAssetScope.Require(
            materialHandle,
            $"掉落物水体表现材质 {WaterEffectMaterialAddress}");

        waterRenderEffects = GetComponent<ActorRenderEffectController>() ??
                             gameObject.AddComponent<ActorRenderEffectController>();
        waterImmersionEffect = GetComponent<WaterImmersionRenderEffect>() ??
                               gameObject.AddComponent<WaterImmersionRenderEffect>();

        waterRenderEffects.enabled = true;
        waterImmersionEffect.enabled = true;
        waterRenderEffects.SetEffectSpriteMaterial(waterEffectMaterial);
        waterRenderEffects.RefreshBindings();
        waterRenderEffects.RegisterExternalRenderers(targetItem.transform);

        SpriteRenderer referenceRenderer = targetItem.Sprite != null && targetItem.Sprite.sprite != null
            ? targetItem.Sprite
            : FindReferenceRenderer(targetItem);
        waterImmersionEffect.SetReferenceRenderer(referenceRenderer);
        waterItem = targetItem;
    }

    /// <summary>复用角色水下 Shader 表现，让掉落物保持原位置并由水线逐步吞没。</summary>
    private void BeginWaterSink(Item landedItem, float elapsedSeconds)
    {
        if (landedItem == null || landedItem.itemData?.Stack == null)
            return;

        EnsureWaterVisual(landedItem);
        resolvedWaterSinkDuration = ResolveSinkDuration(ResolveWeightVolumeRatio(landedItem));
        float restoredElapsed = Mathf.Clamp(elapsedSeconds, 0f, resolvedWaterSinkDuration);
        float restoredProgress = Mathf.Clamp01(restoredElapsed / resolvedWaterSinkDuration);
        waterImmersionEffect.SetWaterState(restoredProgress, true);

        // 入水只改变世界表现与漂移/下沉过程，不改变物品的掉落物性质。
        // 落地后立即恢复可拾取，避免建筑召唤器等物品被当成不可拾取世界实体交互。
        landedItem.itemData.Stack.CanBePickedUp = true;
        waterSinkElapsed = restoredElapsed;
        isWaterSinking = true;
        isWaterFloating = false;
        waterFloatSettled = false;
        ItemNetworkStateSerialization.NotifyRuntimeStateChanged(landedItem);
    }

    /// <summary>漂浮物先表现一次入水，再按重量/体积比上浮到自己的稳定水线。</summary>
    private void BeginWaterFloat(Item landedItem, float targetDepth, float elapsedSeconds)
    {
        if (landedItem == null || landedItem.itemData?.Stack == null)
            return;

        EnsureWaterVisual(landedItem);
        waterFloatTargetDepth = Mathf.Clamp01(targetDepth);
        float riseDuration = Mathf.Max(0.05f, floatingRiseDuration);
        waterFloatElapsed = Mathf.Clamp(elapsedSeconds, 0f, riseDuration);
        float entryDepth = Mathf.Max(Mathf.Clamp01(floatingEntryDepth), waterFloatTargetDepth);
        float initialT = Mathf.Clamp01(waterFloatElapsed / riseDuration);
        float initialDepth = Mathf.Lerp(entryDepth, waterFloatTargetDepth,
            initialT * initialT * (3f - 2f * initialT));
        waterImmersionEffect.SetWaterState(initialDepth, true);

        // 漂浮属于视觉/世界运行态；物品仍然保持普通掉落物的拾取语义。
        landedItem.itemData.Stack.CanBePickedUp = true;
        isWaterFloating = true;
        isWaterSinking = false;
        waterFloatSettled = false;
        ItemNetworkStateSerialization.NotifyRuntimeStateChanged(landedItem);
        InvalidateTickSchedule();
    }

    /// <summary>优先选择有实际 Sprite 的表现节点作为水平水线参考。</summary>
    private static SpriteRenderer FindReferenceRenderer(Item targetItem)
    {
        SpriteRenderer[] renderers = targetItem.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null && renderers[i].sprite != null)
                return renderers[i];
        }

        return null;
    }

    /// <summary>水线从物品底部持续上升；视觉完全浸没后再真正移除世界物品。</summary>
    private void UpdateWaterSink(float deltaTime)
    {
        if (waterItem == null || waterImmersionEffect == null)
        {
            isWaterSinking = false;
            return;
        }

        UpdateWaterDrift(deltaTime);

        waterSinkElapsed += Mathf.Max(0f, deltaTime);
        if (drop != null)
            drop.waterSinkElapsed = waterSinkElapsed;
        float progress = Mathf.Clamp01(waterSinkElapsed / Mathf.Max(0.1f, resolvedWaterSinkDuration));
        waterImmersionEffect.SetWaterState(progress, true);

        if (progress < 1f ||
            waterImmersionEffect.CurrentDepth < 0.995f ||
            waterImmersionEffect.CurrentBlend < 0.995f)
        {
            return;
        }

        Item completedItem = waterItem;
        isWaterSinking = false;
        if (ItemMgr.Instance != null && completedItem != null && !completedItem.DestructionHandled)
            ItemMgr.Instance.DespawnItem(completedItem, saveData: false);
    }

    /// <summary>推进漂浮物的入水、上浮与随水流漂移；稳定后降频继续移动。</summary>
    private void UpdateWaterFloat(float deltaTime)
    {
        if (waterItem == null || waterImmersionEffect == null || waterRenderEffects == null)
        {
            isWaterFloating = false;
            return;
        }

        if (TryResolveWaterAt(waterItem.transform.position, out bool isWater) && !isWater)
        {
            EndWaterFloat();
            return;
        }

        UpdateWaterDrift(deltaTime);

        if (waterFloatSettled)
            return;

        float riseDuration = Mathf.Max(0.05f, floatingRiseDuration);
        waterFloatElapsed = Mathf.Min(riseDuration, waterFloatElapsed + Mathf.Max(0f, deltaTime));
        if (drop != null)
            drop.waterFloatElapsed = waterFloatElapsed;

        float t = Mathf.Clamp01(waterFloatElapsed / riseDuration);
        float smoothT = t * t * (3f - 2f * t);
        float entryDepth = Mathf.Max(Mathf.Clamp01(floatingEntryDepth), waterFloatTargetDepth);
        float targetDepth = Mathf.Lerp(entryDepth, waterFloatTargetDepth, smoothT);
        waterImmersionEffect.SetWaterState(targetDepth, true);

        if (t < 1f ||
            Mathf.Abs(waterImmersionEffect.CurrentDepth - waterFloatTargetDepth) > 0.01f ||
            waterImmersionEffect.CurrentBlend < 0.995f)
        {
            return;
        }

        waterFloatSettled = true;
        if (drop != null)
        {
            drop.waterFloatDepth = waterFloatTargetDepth;
            drop.waterFloatElapsed = riseDuration;
        }

        // 最终 MPB 已写入 Renderer；关闭逐帧控制器后 Shader 自身的水线波动仍会继续。
        waterRenderEffects.enabled = false;
        waterImmersionEffect.enabled = false;
        waterItem.itemData.Stack.CanBePickedUp = true;
        ItemNetworkStateSerialization.NotifyRuntimeStateChanged(waterItem);
        InvalidateTickSchedule();
    }

    /// <summary>按权威水文流向推进漂浮物；目标位置不是水时停在岸边，不把水流强行推上陆地。</summary>
    private void UpdateWaterDrift(float deltaTime)
    {
        float safeDeltaTime = Mathf.Max(0f, deltaTime);
        if (waterItem == null || safeDeltaTime <= 0f)
            return;

        ChunkMgr chunkMgr = ChunkMgr.ExistingInstance;
        if (chunkMgr == null ||
            !chunkMgr.TryGetRuntimeWaterCurrent(waterItem.transform.position,
                out RuntimeWaterCurrentSample current))
        {
            return;
        }

        float speed = current.Kind switch
        {
            RuntimeWaterCurrentKind.River => Mathf.Max(0f, riverDriftSpeed),
            RuntimeWaterCurrentKind.Ocean => Mathf.Max(0f, oceanDriftSpeed),
            _ => 0f
        };
        if (speed <= 0f || current.Direction.sqrMagnitude <= 0.000001f)
            return;

        Vector2 currentPosition = waterItem.transform.position;
        Vector2 targetPosition = WorldTopologyRuntime.NormalizePosition(
            currentPosition + current.Direction * (speed * safeDeltaTime));
        if (!TryResolveWaterAt(targetPosition, out bool targetIsWater) || !targetIsWater)
            return;

        Vector3 worldPosition = waterItem.transform.position;
        worldPosition.x = targetPosition.x;
        worldPosition.y = targetPosition.y;
        waterItem.transform.position = worldPosition;

        if (usesLegacyChunkOwnership)
            UpdateChunkOwner(waterItem, targetPosition);
        else
            ItemWorldPlacement.TryAttachWorldModelDrop(waterItem, targetPosition);

        ItemMgr.Instance?.NotifyRuntimeItemMoved(waterItem);
    }

    /// <summary>地表已不再是水体时恢复原材质并结束漂浮模块。</summary>
    private void EndWaterFloat()
    {
        Item landedItem = waterItem;
        drop = null;
        if (landedItem?.itemData?.Stack != null)
            landedItem.itemData.Stack.CanBePickedUp = true;

        // 动态掉落模块先脱离 Item 层级，避免同帧发生拾取/回池时被误判为原始层级发生变化。
        transform.SetParent(null, true);
        Module.REMOVEModFROMItem(item, _Data);
        if (landedItem != null)
            ItemNetworkStateSerialization.NotifyRuntimeStateChanged(landedItem);
    }

    /// <summary>外部回收或世界切换时恢复临时替换的材质，避免对象复用残留水下表现。</summary>
    public override void Unload()
    {
        if (waterRenderEffects != null && waterItem != null)
            waterRenderEffects.UnregisterExternalRenderers(waterItem.transform);

        isWaterSinking = false;
        waterSinkElapsed = 0f;
        resolvedWaterSinkDuration = 0f;
        isWaterFloating = false;
        waterFloatElapsed = 0f;
        waterFloatTargetDepth = 0f;
        waterFloatSettled = false;
        suppressFloatingPersistenceOnce = false;
        waterItem = null;
        waterRenderEffects = null;
        waterImmersionEffect = null;
    }

    #endregion

    /// <summary>绑定掉落模块所属的物品，并同步修正旧存档中的物品 GUID。</summary>
    private void BindDropItemReference()
    {
        if (drop == null || item == null)
            return;

        drop.item = item;
        if (item.itemData != null)
            drop.itemGuid = item.itemData.Guid;
    }

    /// <summary>
    /// 更新物品所属的 Chunk。只有确认目标 Chunk 可用后才解除旧归属。
    /// </summary>
    private bool UpdateChunkOwner(Item targetItem, Vector2 ownershipPos)
    {
        if (targetItem == null)
            return false;

        Vector2Int currentChunkPos = Chunk.GetChunkPosition(ownershipPos);

        // 新掉落物可能已被 ItemMgr 挂到 Chunk 下，但 LastChunk 尚未初始化。
        if (LastChunk == null)
            LastChunk = targetItem.GetComponentInParent<Chunk>();

        if (IsChunkAtPosition(LastChunk, currentChunkPos))
        {
            // 显式 parent 实例化不会自动写入 Chunk 的运行时字典，这里补齐一次。
            if (targetItem.itemData != null && !LastChunk.RunTimeItems.ContainsKey(targetItem.itemData.Guid))
                LastChunk.AddItem(targetItem);

            return true;
        }

        ChunkMgr chunkMgr = ChunkMgr.Instance;
        if (chunkMgr == null || !chunkMgr.TryGetActiveChunkByPos(currentChunkPos, out Chunk newChunk))
            return false;

        // 先确认新 Chunk，再从旧 Chunk 移除，避免加载边缘出现无归属物品。
        LastChunk?.RemoveItem(targetItem);
        newChunk.AddItem(targetItem);
        LastChunk = newChunk;
        return true;
    }

    private static bool IsChunkAtPosition(Chunk chunk, Vector2Int chunkPos)
    {
        if (chunk == null)
            return false;

        Vector2Int ownerPos = chunk.MapSave?.MapPosition
            ?? Chunk.GetChunkPosition(chunk.transform.position);
        return ownerPos == chunkPos;
    }

    private void RequestTargetChunk(Vector2Int chunkPos)
    {
        ChunkMgr chunkMgr = ChunkMgr.Instance;
        if (chunkMgr == null)
            return;

        // ChunkMgr 内部会对相同坐标的请求去重；每帧重试可避免加载队列
        // 因场景切换或快速移动被清空后，掉落物永久停留在等待状态。
        chunkMgr.RequestLoadChunk_By_Position(chunkPos);
    }

    public override void Save()
    {
        if (suppressFloatingPersistenceOnce &&
            drop != null &&
            (drop.waterFloating || drop.waterSinking))
        {
            suppressFloatingPersistenceOnce = false;
            RemoveOwnPersistedModuleData();
            return;
        }

        suppressFloatingPersistenceOnce = false;
        if (isWaterSinking && drop != null)
            drop.waterSinkElapsed = waterSinkElapsed;
        if (isWaterFloating && drop != null)
        {
            drop.waterFloating = true;
            drop.waterFloatDepth = waterFloatTargetDepth;
            drop.waterFloatElapsed = waterFloatElapsed;
        }
        modData.WriteData(drop);
        item.itemData.ModuleDataDic[modData.Name] = modData;
    }

    /// <summary>移除本模块上一次写入的临时运行态；不会影响当前仍在运行的 Module 实例。</summary>
    private void RemoveOwnPersistedModuleData()
    {
        if (item?.itemData?.ModuleDataDic == null || string.IsNullOrWhiteSpace(modData?.Name))
            return;

        item.itemData.ModuleDataDic.Remove(modData.Name);
    }

    /// <summary>
    /// 二阶贝塞尔曲线计算
    /// </summary>
    /// <param name="p0">起点</param>
    /// <param name="p1">控制点</param>
    /// <param name="p2">终点</param>
    /// <param name="t">插值参数(0-1)</param>
    /// <returns>插值位置</returns>
    public static Vector2 Bezier2(Vector2 p0, Vector2 p1, Vector2 p2, float t)
    {
        float mt = 1f - t;
        return mt * mt * p0 + 2f * mt * t * p1 + t * t * p2;
    }
    
    /// <summary>
    /// 创建直线运动的控制点（三点共线实现直线移动）
    /// </summary>
    /// <param name="startPos">起点</param>
    /// <param name="endPos">终点</param>
    /// <returns>控制点位置</returns>
    public static Vector2 CreateLinearControlPoint(Vector2 startPos, Vector2 endPos)
    {
        // 控制点设为起点和终点的中点，实现直线移动
        return (startPos + endPos) * 0.5f;
    }
    
    /// <summary>
    /// 创建抛物线运动的控制点
    /// </summary>
    /// <param name="startPos">起点</param>
    /// <param name="endPos">终点</param>
    /// <param name="bezierOffset">控制点垂直偏移量</param>
    /// <returns>控制点位置</returns>
    public static Vector2 CreateParabolicControlPoint(Vector2 startPos, Vector2 endPos, float bezierOffset)
    {
        // 计算二阶贝塞尔控制点：中点向上偏移
        Vector2 mid = (startPos + endPos) * 0.5f;
        mid.y += bezierOffset;
        return mid;
    }
    
    /// <summary>
    /// 静态丢弃物品方法，供外部模块调用
    /// </summary>
    public static void StaticDropItem_Pos(Item item, Vector2 startPos, Vector2 endPos, float time, bool isLinear = false, float bezierOffset = 1f, float arcHeight = 1f, float minRotationSpeed = 360f, float maxRotationSpeed = 1080f)
    {
        startPos = WorldTopologyRuntime.NormalizePosition(startPos);
        endPos = WorldTopologyRuntime.NearestImagePosition(startPos, endPos);
        item.transform.position = startPos;

        // 根据是否直线运动计算控制点
        Vector2 controlPos;
        if (isLinear)
        {
            controlPos = CreateLinearControlPoint(startPos, endPos);
        }
        else
        {
            controlPos = CreateParabolicControlPoint(startPos, endPos, bezierOffset);
        }

        Mod_BaseDroper.Drop drop = new Mod_BaseDroper.Drop
        {
            itemGuid = item.itemData.Guid,
            startPos = startPos,
            endPos = endPos,
            controlPos = controlPos,
            time = time,
            progressTime = 0f,
            rotationSpeed = Random.Range(minRotationSpeed, maxRotationSpeed),
            item = item
        };
        
        item.itemData.Stack.CanBePickedUp = false;
        Mod_Droping itemDrop = Module.ADDModTOItem(item, ModText.Drop) as Mod_Droping;
        itemDrop.Load();
        itemDrop.drop = drop;
        itemDrop.arcHeight = arcHeight; // 传递弧高参数
    }
    
    /// <summary>
    /// 静态丢弃物品（在指定半径范围内随机位置）
    /// </summary>
    public static void StaticDropItemInARange(Item item, Vector2 startPos, float radius, float time, bool isLinear = false, float bezierOffset = 1f, float arcHeight = 1f, float minRotationSpeed = 360f, float maxRotationSpeed = 1080f)
    {
        Vector2 randomDir = Random.insideUnitCircle.normalized;
        float randomDist = Random.Range(0.5f * radius, radius);
        Vector2 endPos = startPos + randomDir * randomDist;
        StaticDropItem_Pos(item, startPos, endPos, time, isLinear, bezierOffset, arcHeight, minRotationSpeed, maxRotationSpeed);
    }
}
