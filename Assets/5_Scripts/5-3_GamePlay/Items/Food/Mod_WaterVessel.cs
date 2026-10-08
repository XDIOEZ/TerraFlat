using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using MemoryPack;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// 通用液体容器状态保存真实混合组分；LiquidId 和 Amount 只用于汇总显示。
/// </summary>
[Serializable, MemoryPackable]
public partial class LiquidContainerState
{
    public string LiquidId; // 主组分的稳定 ID；空容器必须为空。
    public float Amount; // 全部组分的真实份数，保留工业输送产生的小数尾量。
    public float ProcessingSeconds; // 当前液体加热处理的累计秒数。
    public float Temperature; // 混合液体的共同温度，由加工模块按秒冷却。
}

/// <summary>
/// 可装任意已注册液体的复用容器模块。历史类名仍由现有模块 Prefab 使用，但运行时语义已经是通用液体容器；
/// 液体属性全部来自 LiquidDefinition，新增 MOD 液体无需新增容器 Item；声明 liquidSurface 的容器 Sprite 由本模块按主色重绘。
/// </summary>
public sealed partial class Mod_WaterVessel : Module, IInteractable, ILiquidVessel, IContainerPortProvider
{
    #region 数据与生命周期

    public const string ModuleId = "Mod_WaterVessel";
    public const int DefaultCapacity = 8;
    public const float AmountStep = 1f; // 每批最多一份，最后不足一份的真实余量继续保留并转移。
    public const float AmountEpsilon = 0.0001f;
    public Ex_ModData_MemoryPackable ModData = new(); // 容器独立持久化载体。
    public LiquidContainerState Data = new(); // 液体 ID、数量、温度与加工进度。
    public int capacity = DefaultCapacity; // 当前容器最大份数，由物品定义配置。
    public float reach = 2f; // 装液与转移距离。
    public static event Action<Mod_WaterVessel, Item> OpenRequested; // 表现层打开容器。
    public event Action Changed; // 当前容器状态变化。
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public int Capacity => capacity;
    public LiquidDefinition CurrentLiquid => Data != null ? MixedLiquidContents.DisplayLiquid(Data) : null;
    LiquidContainerState ILiquidVessel.Data => Data;
    public ItemData ItemData => item.itemData;
    public Item Item => item;
    public MachineEntity Machine => null;
    public IVesselContents ContentsSource
    {
        get
        {
            var contents = item.itemMods?.GetMod_ByID<Mod_VesselContents>(Mod_VesselContents.ModuleId);
            return contents != null && contents.Enabled && contents.IsRuntimeLoaded ? contents : null;
        }
    }
    public void CommitVessel() => Commit();
    private WorldTileTargetOutline targetOutline; // 当前准心命中的单格液体来源轮廓。
    private bool actionBound;
    private const float WorldHeatTickInterval = 1f;
    private float worldHeatClock;

    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData_MemoryPackable ?? throw new ArgumentException("液体容器数据类型错误。");
    }

    /// <summary>恢复容器状态并绑定统一使用入口。</summary>
    protected override void OnLoad()
    {
        ModData.ReadData(ref Data);
        Data ??= new LiquidContainerState();
        MixedLiquidContents.Ensure(Data);
        if (Data.Revision == 0 && item?.itemData?.Guid != 0) Data.RandomState = unchecked((uint)item.itemData.Guid);
        Validate(Data, capacity);
        bool normalized = NormalizeStoredAmount(Data);
        Validate(Data, capacity);
        if (normalized)
            ModData.WriteData(Data);
        RefreshVisual();
        worldHeatClock = 0f;
        actionBound = item?.itemMods?.GetMod_ByID<Mod_Mortar>(Mod_Mortar.ModuleId)?.IsCrucible != true;
        if (actionBound)
            item.OnAct += Act;
    }

    /// <summary>写入液体身份、数量与加工进度。</summary>
    protected override void OnSave()
    {
        NormalizeStoredAmount(Data);
        Validate(Data, capacity);
        ModData.WriteData(Data);
    }

    /// <summary>解除池化前的动作和视图订阅。</summary>
    protected override void OnUnload()
    {
        if (actionBound && item != null) item.OnAct -= Act;
        actionBound = false;
        worldHeatClock = 0f;
        ReleaseTargetOutline();
        Changed = null;
    }

    /// <summary>准心目标属于连续变化的表现状态，直接按帧刷新而不启用 Module Tick。</summary>
    private void LateUpdate()
    {
        TickWorldHeat();
        if (item?.Owner is not Player ownerPlayer || !ownerPlayer.IsLocalProfile)
        {
            targetOutline?.Hide();
            return;
        }

        if (!TryResolveCurrentWorldLiquidTarget(item?.Owner, out WorldLiquidSourceTarget target))
        {
            targetOutline?.Hide();
            return;
        }

        targetOutline ??= WorldTileTargetOutline.Create("Liquid Tile Target Outline");
        targetOutline.Show(target.WorldCell);
    }

    /// <summary>落地完整容器每秒读取所在格温度，让液体按统一速率升温、降温并触发无产物热转换。</summary>
    private void TickWorldHeat()
    {
        if (!GameNetwork.HasStateAuthority || item == null || item.DestructionHandled ||
            item.InHand || item.Owner != null || IsEmptyAmount(Data?.Amount ?? 0f))
        {
            worldHeatClock = 0f;
            return;
        }

        worldHeatClock += Time.deltaTime;
        if (worldHeatClock < WorldHeatTickInterval)
            return;

        float seconds = worldHeatClock;
        worldHeatClock = 0f;
        TemperatureMgr temperatureManager = TemperatureMgr.Instance;
        if (temperatureManager == null ||
            !temperatureManager.TryGetAmbientTemperature(item.transform.position, out float ambientTemperature))
        {
            return;
        }

        if (InventoryVesselHeating.ProcessWorldHeat(Data, ambientTemperature, seconds))
            Commit();
    }

    private void OnDisable() => ReleaseTargetOutline();

    private void OnDestroy() => ReleaseTargetOutline();

    /// <summary>拒绝非法容器状态；非空容器引用的液体必须已经存在于最终液体目录。</summary>
    public static void Validate(LiquidContainerState state, int containerCapacity)
    {
        if (state == null)
            throw new InvalidOperationException("液体容器状态为空。");
        MixedLiquidContents.Ensure(state);
        if (containerCapacity < 1 || float.IsNaN(state.Amount) || float.IsInfinity(state.Amount) ||
            state.Amount < -AmountEpsilon || state.Amount > containerCapacity + AmountEpsilon ||
            IsEmptyAmount(state.Amount) != string.IsNullOrWhiteSpace(state.LiquidId) ||
            float.IsNaN(state.Temperature) || float.IsInfinity(state.Temperature) || state.Temperature < -273.15f ||
            float.IsNaN(state.ProcessingSeconds) || float.IsInfinity(state.ProcessingSeconds) || state.ProcessingSeconds < 0f)
        {
            throw new InvalidOperationException("液体容器容量、液体 ID、数量、温度或加工状态无效。");
        }

        float total = 0f;
        foreach (var component in state.Composition)
        {
            if (!IsFinitePositive(component.Value) || ResolveLiquidDefinition(component.Key, false) == null)
                throw new InvalidOperationException($"液体容器组分无效：{component.Key}");
            total += component.Value;
        }
        if (Mathf.Abs(total - state.Amount) > AmountEpsilon)
            throw new InvalidOperationException("液体容器组分与总份数不一致。");
        foreach (var pending in state.PendingOutputs)
            if (string.IsNullOrWhiteSpace(pending.Key) || pending.Value == null || !IsFinitePositive(pending.Value.Servings) ||
                MixedLiquidContents.Reserved(state, pending.Value.LiquidId) > MixedLiquidContents.GetAmount(state, pending.Value.LiquidId) + AmountEpsilon)
                throw new InvalidOperationException("液体容器输出口预留超出真实库存。");
    }

    #endregion

    #region 玩家操作

    /// <summary>对准可提取液体地块时装液，否则打开通用液体容器面板。</summary>
    public override void Act()
    {
        if (!item.InHand || item.Owner == null)
            return;
        if (item.itemMods.GetMod_ByID<Mod_Building>(ModText.Building)?.TryHandlePlacementAction() == true)
            return;

        Item actor = item.Owner;
        if (!CanOperate(actor)) return;
        if (TryResolveCurrentWorldLiquidTarget(actor, out WorldLiquidSourceTarget target))
        {
            if (Data.Amount >= Capacity - AmountEpsilon)
            {
                ItemActionFeedback.Show(actor, "容器已经装满了。");
            }
            else
            {
                float moved = LiquidVesselOperations.FillFromWorld(this, actor, target);
                if (moved > AmountEpsilon)
                    ItemActionFeedback.Show(actor, $"已装入{target.Liquid.DisplayName}。");
            }
            return;
        }

        OpenPanel(actor);
    }

    /// <summary>世界中的液体容器通过交互打开同一面板。</summary>
    public void OnInteractStart(Item actor) { OpenPanel(actor); }

    /// <summary>交互取消不改变容器内容。</summary>
    public void OnInteractCancel(Item actor) { }

    /// <summary>面板与意图入口验证归属和距离，实际修改仍由权威端口提交。</summary>
    public bool CanOperate(Item actor)
    {
        if (!IsRuntimeLoaded || actor == null || actor.DestructionHandled || item == null || item.DestructionHandled || item.gameObject.scene != actor.gameObject.scene ||
            !(actor.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp)?.Hp > 0f))
            return false;
        return item.InHand ? item.Owner == actor :
            item.Owner == null && WorldTopologyRuntime.ShortestDelta(actor.transform.position, item.transform.position).sqrMagnitude <= reach * reach;
    }

    /// <summary>统一的液体面板打开入口，坩埚模式与普通容器共用同一 UI 事件。</summary>
    public void OpenPanel(Item actor)
    {
        if (CanOperate(actor))
            OpenRequested?.Invoke(this, actor);
    }

    /// <summary>公开当前世界液体目标检测，坩埚只负责把使用动作路由给液体模块。</summary>
    public bool HasCurrentWorldLiquidTarget(Item actor) =>
        TryResolveCurrentWorldLiquidTarget(actor, out _);

    /// <summary>液体定义声明可饮用即可消耗；恢复量与饮用后的状态后果都由同一液体定义决定。</summary>
    public bool Drink(Item actor)
        => LiquidVesselOperations.Drink(this, actor);

    /// <summary>玩家明确倒空当前容器。</summary>
    public void Empty(Item actor)
    {
        if (!CanOperate(actor)) return;
        PourToGround(actor, Data.Amount);
    }

    /// <summary>主动倾倒才向操作者脚下提交液体；配方、饮用、转移等普通扣液入口不会重复浇地。</summary>
    public float PourToGround(Item actor, float amount)
        => LiquidVesselOperations.PourToGround(this, actor, amount);

    /// <summary>向另一只通用液体容器送出当前口的一批，真实组分在目标累加。</summary>
    public bool TransferTo(Mod_WaterVessel target, Item actor)
        => LiquidVesselOperations.Transfer(this, target, actor);

    /// <summary>
    /// 把库存容器中的液体或目录声明的原料装入当前容器。来源容器保持原槽位，原料按整份扣除；
    /// 当前手持来源优先走实例 API，原料数量不超过本次拖拽量，避免半组拖拽误消费整组。
    /// </summary>
    public bool TransferFromInventoryItem(ItemData sourceItemData, Item actor, float maximumItemAmount = float.PositiveInfinity)
        => LiquidVesselOperations.TransferFromInventory(this, sourceItemData, actor, maximumItemAmount);

    #endregion

    #region 通用液体 API

    /// <summary>玩法和 MOD 可向容器加入任意已注册液体；返回实际加入份数。</summary>
    public int AddLiquid(string liquidId, int amount)
    {
        if (!GameNetwork.HasStateAuthority || amount <= 0)
            return 0;
        ResolveLiquidDefinition(liquidId, true);
        int wholeSpace = Mathf.FloorToInt(Mathf.Max(0f, Capacity - Data.Amount) + AmountEpsilon);
        int moved = Math.Min(amount, wholeSpace);
        if (moved <= 0)
            return 0;
        AddLiquidInternal(liquidId, moved);
        Commit();
        return moved;
    }

    /// <summary>玩法和 MOD 从容器移除液体；返回实际移除份数。</summary>
    public int RemoveLiquid(int amount)
    {
        int whole = Mathf.Min(amount, Mathf.FloorToInt(Data.Amount));
        return whole > 0 ? Mathf.FloorToInt(LiquidVesselOperations.Remove(this, whole) + AmountEpsilon) : 0;
    }

    /// <summary>向容器加入真实液体份数，允许小数尾量。</summary>
    public float AddLiquidAmount(string liquidId, float amount)
        => LiquidVesselOperations.Add(this, liquidId, amount);

    /// <summary>按稳定批次移除真实液体份数，允许小数尾量。</summary>
    public float RemoveLiquidAmount(float amount)
        => LiquidVesselOperations.Remove(this, amount);

    /// <summary>玩法和 MOD 清空容器，不生成额外物品。</summary>
    public bool ClearContents()
    {
        if (!GameNetwork.HasStateAuthority || IsEmptyAmount(Data.Amount))
            return false;
        ClearContentsInternal();
        Commit();
        return true;
    }

    /// <summary>投料前检查液体身份与装量；此入口不修改容器，供库存事务预检。</summary>
    public bool CanApplyIngredientReaction(LiquidIngredientReaction reaction)
    {
        return GameNetwork.HasStateAuthority && reaction != null &&
               !IsEmptyAmount(Data.Amount) && CurrentLiquid != null && Data.Composition.Count == 1 && Data.PendingOutputs.Count == 0 &&
               !string.Equals(Data.LiquidId, reaction.ResultLiquidId, StringComparison.Ordinal) &&
               (!reaction.RequireFullContainer || Data.Amount >= Capacity - AmountEpsilon) &&
               ResolveLiquidDefinition(reaction.ResultLiquidId, false) != null;
    }

    /// <summary>原料库存事务成功后替换液体身份，保留原有液量并刷新存档、图标和面板。</summary>
    public bool TryApplyIngredientReaction(LiquidIngredientReaction reaction)
    {
        if (!CanApplyIngredientReaction(reaction)) return false;
        if (!MixedLiquidContents.TransformSpecies(Data, Data.LiquidId, reaction.ResultLiquidId)) return false;
        Data.ProcessingSeconds = 0f;
        Commit();
        return true;
    }

    /// <summary>库存中的模块没有运行时组件时读取容器状态，供炉体等系统处理。</summary>
    public static bool TryRead(ItemData itemData, out Ex_ModData_MemoryPackable storage, out LiquidContainerState state)
    {
        storage = null;
        state = null;
        if (itemData?.ModuleDataDic == null) return false;
        foreach (var pair in itemData.ModuleDataDic)
        {
            if (pair.Value?.ID != ModuleId || !pair.Value.Enabled || pair.Value is not Ex_ModData_MemoryPackable data)
                continue;

            storage = data;
            state = new LiquidContainerState();
            data.ReadData(ref state);
            state ??= new LiquidContainerState();
            int configuredCapacity = ResolveConfiguredCapacity(itemData, pair.Key);
            Validate(state, configuredCapacity);
            NormalizeStoredAmount(state);
            Validate(state, configuredCapacity);
            return true;
        }
        return false;
    }

    /// <summary>检查库存中的容器能否加入指定液体，不修改任何 ItemData。</summary>
    public static bool CanAddLiquidToItemData(ItemData itemData, string liquidId, float amount)
    {
        if (!IsFinitePositive(amount) || string.IsNullOrWhiteSpace(liquidId) ||
            !TryRead(itemData, out _, out LiquidContainerState state) ||
            ResolveLiquidDefinition(liquidId, false) == null)
            return false;

        int capacity = ResolveContainerCapacity(itemData);
        return amount <= capacity - state.Amount + AmountEpsilon;
    }

    /// <summary>把常温液体直接写回库存容器 ItemData；已有液体的温度保持不变。</summary>
    public static bool TryAddLiquidToItemData(ItemData itemData, string liquidId, float amount)
    {
        return TryAddLiquidToItemData(itemData, liquidId, amount, float.NaN);
    }

    /// <summary>把带温度的液体直接写回库存容器 ItemData，供通用热源产出熔融液体。</summary>
    public static bool TryAddLiquidToItemData(ItemData itemData, string liquidId, float amount, float temperature)
    {
        if (!CanAddLiquidToItemData(itemData, liquidId, amount) ||
            !TryRead(itemData, out Ex_ModData_MemoryPackable storage, out LiquidContainerState state))
            return false;

        float moved = amount;
        if (moved <= 0f || moved > ResolveContainerCapacity(itemData) - state.Amount)
            return false;
        MixedLiquidContents.Add(state, liquidId.Trim(), moved, IsFinite(temperature) ? temperature : state.Temperature);
        NormalizeStoredAmount(state);
        storage.WriteData(state);
        return true;
    }

    /// <summary>按 ItemData 状态在容器模块内重绘液面 Sprite；其他展示端只读取解析后的通用 Sprite。</summary>
    public static bool TryResolvePresentationSprite(ItemData itemData, out Sprite sprite)
    {
        sprite = null;
        if (itemData == null || GameRes.Instance == null ||
            !GameRes.Instance.TryGetItemDefinition(itemData.IDName, out RuntimeItemDefinition definition) ||
            !TryRead(itemData, out _, out LiquidContainerState state))
        {
            return false;
        }

        if (IsEmptyAmount(state.Amount))
        {
            if (definition.TryGetVisualStateSprite("empty", out sprite))
                return true;

            sprite = definition.Sprite;
            return sprite != null;
        }

        LiquidDefinition liquid = MixedLiquidContents.DisplayLiquid(state);
        if (liquid == null)
            return false;

        if (definition.Visual?.LiquidSurface != null)
        {
            sprite = GenerateLiquidSurfaceSprite(
                itemData,
                definition.Sprite,
                definition.Visual.LiquidSurface,
                liquid.PrimaryColor,
                Mathf.Clamp01(state.Amount / ResolveContainerCapacity(itemData)));
            return true;
        }

        if (definition.TryGetVisualStateSprite(liquid.VisualState, out sprite))
            return true;

        if (definition.TryGetVisualStateSprite("filled", out sprite))
            return true;

        sprite = definition.Sprite;
        return sprite != null;
    }

    #region 容器液面 Sprite 重绘

    private const float LiquidSurfaceShadowBlend = 0.22f; // 水面下层混入阴影色的比例。
    private const float LiquidSurfaceHighlightBlend = 0.24f; // 水面顶边混入高光色的比例。

    private readonly struct VesselSpriteCacheKey : IEquatable<VesselSpriteCacheKey>
    {
        public readonly int SourceSpriteId; // 原始物品 Sprite 实例。
        public readonly int X; // 内腔像素区左下角 X。
        public readonly int Y; // 内腔像素区左下角 Y。
        public readonly int Width; // 内腔像素区像素宽度。
        public readonly int Height; // 内腔像素区像素高度。
        public readonly int FillRows; // 按容器余量量化后的液面高度。
        public readonly int MaxSourceChannel; // 当前贴图内腔源像素的亮度上限。
        public readonly Color32 LiquidColor; // 液体定义提供的主色。

        public VesselSpriteCacheKey(Sprite source, RectInt bounds, int fillRows, int maxSourceChannel, Color32 liquidColor)
        {
            SourceSpriteId = source.GetInstanceID();
            X = bounds.x;
            Y = bounds.y;
            Width = bounds.width;
            Height = bounds.height;
            FillRows = fillRows;
            MaxSourceChannel = maxSourceChannel;
            LiquidColor = liquidColor;
        }

        public bool Equals(VesselSpriteCacheKey other) =>
            SourceSpriteId == other.SourceSpriteId && X == other.X && Y == other.Y &&
            Width == other.Width && Height == other.Height && FillRows == other.FillRows &&
            MaxSourceChannel == other.MaxSourceChannel &&
            LiquidColor.Equals(other.LiquidColor);

        public override bool Equals(object obj) => obj is VesselSpriteCacheKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            SourceSpriteId, X, Y, Width, Height, FillRows, MaxSourceChannel, LiquidColor);
    }

    private static readonly Dictionary<VesselSpriteCacheKey, Sprite> GeneratedVesselSprites = new(); // 按源 Sprite、颜色和像素液面复用运行时贴图。

    /// <summary>每次进入运行时清理上个会话创建的纹理对象，避免反复进出 Play Mode 累积。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearGeneratedVesselSprites()
    {
        foreach (Sprite generatedSprite in GeneratedVesselSprites.Values)
        {
            if (generatedSprite == null)
                continue;

            Texture2D generatedTexture = generatedSprite.texture;
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(generatedSprite);
                if (generatedTexture != null)
                    UnityEngine.Object.Destroy(generatedTexture);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(generatedSprite);
                if (generatedTexture != null)
                    UnityEngine.Object.DestroyImmediate(generatedTexture);
            }
        }

        GeneratedVesselSprites.Clear();
    }

    /// <summary>按开口配置、主色和可见液面行数复用程序生成的容器 Sprite。</summary>
    private static Sprite GenerateLiquidSurfaceSprite(
        ItemData itemData,
        Sprite sourceSprite,
        LiquidSurfaceDefinition surface,
        Color primaryColor,
        float fillRatio)
    {
        if (sourceSprite == null || sourceSprite.texture == null)
            throw new InvalidOperationException($"液体容器 {itemData.IDName} 缺少可重绘的基础 Sprite。");

        Texture2D sourceTexture = sourceSprite.texture;
        if (!sourceTexture.isReadable)
            throw new InvalidOperationException(
                $"液体容器 {itemData.IDName} 的贴图 {sourceTexture.name} 必须启用 Read/Write，才能程序化重绘液面。");

        int width = Mathf.RoundToInt(sourceSprite.rect.width);
        int height = Mathf.RoundToInt(sourceSprite.rect.height);
        int sourceX = Mathf.RoundToInt(sourceSprite.rect.x);
        int sourceY = Mathf.RoundToInt(sourceSprite.rect.y);
        Rect bounds01 = surface.Bounds;
        int xMin = Mathf.Clamp(Mathf.FloorToInt(bounds01.xMin * width), 0, width);
        int yMin = Mathf.Clamp(Mathf.FloorToInt(bounds01.yMin * height), 0, height);
        int xMax = Mathf.Clamp(Mathf.CeilToInt(bounds01.xMax * width), 0, width);
        int yMax = Mathf.Clamp(Mathf.CeilToInt(bounds01.yMax * height), 0, height);
        RectInt bounds = new RectInt(xMin, yMin, xMax - xMin, yMax - yMin);
        if (width <= 0 || height <= 0 || bounds.width <= 0 || bounds.height <= 0 ||
            sourceX < 0 || sourceY < 0 || sourceX + width > sourceTexture.width || sourceY + height > sourceTexture.height)
            throw new InvalidOperationException($"液体容器 {itemData.IDName} 的液面开口配置超出了基础 Sprite。");

        Color32 liquidColor = primaryColor;
        int fillRows = Mathf.Clamp(Mathf.CeilToInt(Mathf.Clamp01(fillRatio) * bounds.height), 1, bounds.height);
        var key = new VesselSpriteCacheKey(sourceSprite, bounds, fillRows, surface.MaxSourceChannel, liquidColor);
        if (GeneratedVesselSprites.TryGetValue(key, out Sprite cachedSprite) && cachedSprite != null)
            return cachedSprite;

        Color32[] texturePixels = sourceTexture.GetPixels32();
        Color32[] spritePixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
            Array.Copy(texturePixels, (sourceY + y) * sourceTexture.width + sourceX, spritePixels, y * width, width);

        Color32 shadowColor = (Color32)Color.Lerp((Color)liquidColor, new Color(0.03f, 0.06f, 0.08f, 1f), LiquidSurfaceShadowBlend);
        Color32 highlightColor = (Color32)Color.Lerp((Color)liquidColor, Color.white, LiquidSurfaceHighlightBlend);
        int liquidTop = bounds.yMin + fillRows - 1;
        float centerX = bounds.xMin + bounds.width * 0.5f;
        float centerY = bounds.yMin + bounds.height * 0.5f;
        float radiusX = bounds.width * 0.5f;
        float radiusY = bounds.height * 0.5f;

        for (int y = bounds.yMin; y <= liquidTop; y++)
        {
            for (int x = bounds.xMin; x < bounds.xMax; x++)
            {
                float normalizedX = (x + 0.5f - centerX) / radiusX;
                float normalizedY = (y + 0.5f - centerY) / radiusY;
                if (normalizedX * normalizedX + normalizedY * normalizedY > 1f)
                    continue;

                int pixelIndex = y * width + x;
                Color32 sourcePixel = spritePixels[pixelIndex];
                if (sourcePixel.a == 0 ||
                    Mathf.Max(Mathf.Max(sourcePixel.r, sourcePixel.g), sourcePixel.b) > surface.MaxSourceChannel)
                    continue;

                spritePixels[pixelIndex] = y == liquidTop ? highlightColor : shadowColor;
            }
        }

        var generatedTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = $"{sourceSprite.name}_Liquid_{ColorUtility.ToHtmlStringRGBA(liquidColor)}_{fillRows}",
            filterMode = sourceTexture.filterMode,
            wrapMode = TextureWrapMode.Clamp
        };
        generatedTexture.SetPixels32(spritePixels);
        generatedTexture.Apply(false, false);

        Vector2 pivot = new Vector2(sourceSprite.pivot.x / width, sourceSprite.pivot.y / height);
        // 落地建筑的光照遮挡读取当前液面 Sprite 的物理轮廓。
        Sprite generatedSprite = Sprite.Create(
            generatedTexture,
            new Rect(0f, 0f, width, height),
            pivot,
            sourceSprite.pixelsPerUnit,
            0,
            SpriteMeshType.Tight,
            sourceSprite.border,
            true);
        generatedTexture.Apply(false, true);
        generatedSprite.name = generatedTexture.name;
        GeneratedVesselSprites.Add(key, generatedSprite);
        return generatedSprite;
    }

    /// <summary>装水容器加入通用 ItemData 图标解析注册表，不让任意 UI 知道容器模块类型。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterPresentationResolver() =>
        ItemDataPresentationResolverRegistry.Register(ModuleId, TryResolvePresentationSprite);

    #endregion

    /// <summary>库存中的模块没有运行时组件，容量从正式物品定义的模块参数读取。</summary>
    private static int ResolveConfiguredCapacity(ItemData itemData, string stableModuleName)
    {
        if (GameRes.Instance == null ||
            !GameRes.Instance.TryGetItemDefinition(itemData.IDName, out RuntimeItemDefinition definition) ||
            !definition.TryGetModuleParameters(stableModuleName, out string json) ||
            string.IsNullOrWhiteSpace(json))
            return DefaultCapacity;

        JObject parameters = JObject.Parse(json);
        return parameters.Value<int?>(nameof(capacity)) ?? DefaultCapacity;
    }

    /// <summary>按容器模块在 ItemData 中的稳定名读取容量，避免把运行时模块 ID 当成 JSON 模块键。</summary>
    public static int ResolveContainerCapacity(ItemData itemData)
    {
        if (itemData?.ModuleDataDic != null)
        {
            foreach (var pair in itemData.ModuleDataDic)
            {
                if (pair.Value?.ID == ModuleId)
                    return ResolveConfiguredCapacity(itemData, pair.Key);
            }
        }

        return DefaultCapacity;
    }

    private static LiquidDefinition ResolveLiquidDefinition(string liquidId, bool throwOnMissing)
    {
        LiquidDefinition definition = GameRes.ExistingInstance?.GetLiquidDefinition(liquidId);
        if (definition == null && throwOnMissing)
            throw new InvalidOperationException($"液体定义不存在：{liquidId}");
        return definition;
    }

    public static bool IsEmptyAmount(float amount) => amount <= 0f;

    private static bool IsFinite(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value);

    private static bool IsFinitePositive(float value) =>
        !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;

    /// <summary>总量由真实组分汇总，保留工业输送产生的小数余量。</summary>
    public static bool NormalizeStoredAmount(LiquidContainerState state)
    {
        if (state == null || float.IsNaN(state.Amount) || float.IsInfinity(state.Amount))
            return false;

        float previousAmount = state.Amount;
        string previousLiquidId = state.LiquidId;
        float previousTemperature = state.Temperature;
        MixedLiquidContents.SynchronizeAmounts(state);

        return previousAmount != state.Amount || previousLiquidId != state.LiquidId ||
            previousTemperature != state.Temperature;
    }

    private void AddLiquidInternal(string liquidId, float amount)
    {
        MixedLiquidContents.Add(Data, liquidId.Trim(), Mathf.Min(amount, Capacity - Data.Amount), Data.Temperature);
    }

    private void ClearContentsInternal()
    {
        MixedLiquidContents.Clear(Data);
    }

    /// <summary>让外部加工模块提交容器温度或凝固结果，并刷新图标、库存表现和联机状态。</summary>
    public void CommitExternalState() => Commit();

    /// <summary>状态提交后刷新持久化数据、网络状态和表现订阅。</summary>
    private void Commit()
    {
        Validate(Data, capacity);
        Save();
        RefreshVisual();
        RefreshContainingInventoryPresentation();
        // 快捷栏当前手持实例会把 OnUIRefresh 绑定到 Mod_HotBar.RefreshUI；
        // 模块内部状态变化不会替换 ItemData 引用，因此必须显式发布这一运行时表现事件。
        item.OnUIRefresh?.Invoke();
        ItemNetworkStateSerialization.NotifyRuntimeStateChanged(item);
        Changed?.Invoke();
    }

    /// <summary>模块数据原地变化时通知真实所属库存，否则快捷栏仍会保留变更前的图标。</summary>
    private void RefreshContainingInventoryPresentation()
    {
        if (item?.Owner == null || item.itemData == null ||
            !InventoryContextResolver.TryResolveContainingInventory(item.Owner, item.itemData, out Inventory inventory))
        {
            return;
        }

        inventory.Data?.NotifyItemStateChanged(item.itemData);
    }

    /// <summary>同一容器物品按液体定义选择状态 Sprite；未知专用状态时回退到 filled，再回退外壳默认图。</summary>
    private void RefreshVisual()
    {
        if (item?.Sprite == null || item.itemData == null)
            return;

        if (TryResolvePresentationSprite(item.itemData, out Sprite sprite))
            item.Sprite.sprite = sprite;
    }

    /// <summary>高亮与实际装液共用这一目标解析入口，保证显示格与操作格严格一致。</summary>
    private bool TryResolveCurrentWorldLiquidTarget(Item actor, out WorldLiquidSourceTarget target)
    {
        target = default;
        if (actor == null || item == null || !item.InHand || item.Owner != actor || !CanOperate(actor))
            return false;

        Mod_Building building = item.itemMods.GetMod_ByID<Mod_Building>(ModText.Building);
        if (building != null && building.IsPlacementModeActive)
            return false;

        Mod_GameController controller = actor.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
        if (controller == null || controller.IsGameplayInputLocked ||
            (!controller.IsUsingMobile && controller.IsPointerOverUI()))
        {
            return false;
        }

        if (!WorldLiquidSourceResolver.TryResolve(controller.GetMouseWorldPosition(), out target))
            return false;

        return FarmlandSystem.IsWithinReach(actor.transform.position, target.WorldCell, reach);
    }

    private void ReleaseTargetOutline()
    {
        if (targetOutline == null)
            return;

        Destroy(targetOutline.gameObject);
        targetOutline = null;
    }

    #endregion
}
