using System;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>通用液体容器面板：显示液体身份和容量，提供饮用、容器拖入转液与拖拽倾倒，离开交互距离自动关闭。</summary>
public sealed class WaterVesselPanel : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler, IEndDragHandler, IInventoryDragDropTarget
{
    private const float FullPourDragWidthRatio = 0.72f; // 中心附近起手时的二维线性回退距离。
    private const float MaxPourTiltDegrees = 180f; // 倒扣时达到 180°，此时容器允许保留的液体量为 0。
    private const float FullEmptyTiltToleranceDegrees = 10f; // 接近倒扣时直接视为完全倒空，避免最后 0.1 份卡住。
    private const float PourFollowDegreesPerSecond = 540f; // 手势目标再快也只能以该角速度追随，强制保留可见倾倒过程。
    private const float PourReturnDegreesPerSecond = 180f; // 松手后恢复直立的速度。
    private const float PourSettleAngleEpsilon = 0.25f; // 抬手后先追到最后手势姿态，再开始回正。
    private const float MinimumPourCommitAmount = Mod_WaterVessel.AmountStep; // 连续倾倒按 0.1 份落格，避免保存无意义的高精度浮点余量。
    private const float OrbitGestureMinimumRadiusRatio = 0.11f; // 离中心足够远时按绕罐体中心的圆弧手势解释。
    private const int InvalidPointerId = int.MinValue;

    public WaterVesselLiquidGraphic Liquid; // 正式 Prefab 中的罐内水层。
    public RectTransform LeftPourOutlet; // 正倾角时使用的左侧罐口嘴沿锚点。
    public RectTransform RightPourOutlet; // 负倾角时使用的右侧罐口嘴沿锚点。
    public const string PrefabKey = "UI_WaterVessel";

    #region 容器外观配置

    /// <summary>单种容器的 UI 外观：剖面、内腔、水位区间和出口一起切换；位置按剖面 Rect 的 0..1 坐标配置。</summary>
    [Serializable]
    public struct VesselAppearance
    {
        public string ItemId; // 匹配当前容器物品定义。
        public Sprite Cutaway, Interior; // 剖面和同画布内腔遮罩。
        public Vector2 FillRange; // 水位下限、上限，按底部为零归一化。
        public Vector2 LeftOutlet, RightOutlet; // 左右嘴沿的归一化位置。
    }

    public VesselAppearance[] Appearances = Array.Empty<VesselAppearance>(); // 特定容器的外观覆盖，未匹配时恢复 Prefab 默认外观。
    private VesselAppearance defaultAppearance; // 首次绑定时保存的默认外观。
    private Image vesselImage, interiorImage; // 已绑定的剖面和内腔图像。

    /// <summary>切换容器时一次性应用完整外观，避免复用面板残留上一个容器的遮罩或出水位置。</summary>
    private void ApplyAppearance(string itemId)
    {
        VesselAppearance appearance = defaultAppearance;
        foreach (VesselAppearance candidate in Appearances)
        {
            if (!string.Equals(candidate.ItemId, itemId, StringComparison.Ordinal)) continue;
            appearance = candidate;
            break;
        }
        vesselImage.sprite = appearance.Cutaway;
        interiorImage.sprite = appearance.Interior;
        Liquid.FillRange = appearance.FillRange;
        Rect rect = vesselArt.rect;
        LeftPourOutlet.localPosition = rect.min + Vector2.Scale(rect.size, appearance.LeftOutlet);
        RightPourOutlet.localPosition = rect.min + Vector2.Scale(rect.size, appearance.RightOutlet);
        Liquid.SetVerticesDirty();
    }

    #endregion

    private BasePanel panel; // 通用面板生命周期。
    private Mod_WaterVessel vessel; // 当前目标液体容器。
    private Item actor; // 操作者。
    private TextMeshProUGUI title, hint; // 当前容器名称与通用操作提示。
    private TextMeshProUGUI status; // 水质、份数与提示。
    private Button drink; // 根据液体定义与余量启用。
    private static WaterVesselPanel current; // 世界 UI 下的一份面板实例。
    private float nextRefresh; // 可见时五次每秒刷新，避免逐帧生成文本。
    private RectTransform vesselArt; // 可摇摆的陶罐切面根节点。
    private RectTransform vesselGestureFrame; // 不随罐体旋转的手势坐标系。
    private WaterVesselPourGraphic pourGraphic; // 罐口外可见的分段液流。
    private int pourPointerId = InvalidPointerId; // 当前占用摇摆手势的触点。
    private Vector2 pourStartLocalPoint; // 按下位置；中心附近起手时用于二维线性回退。
    private Vector2 pourStartRadial; // 按下位置相对罐体中心的向量，用于半圆/圆弧手势。
    private float pourStartPolarAngle; // 圆弧手势起始极角。
    private float pourStartTiltDegrees; // 本次手势开始时的罐体角度，支持回正途中再次抓住。
    private bool useOrbitGesture; // 是否按围绕罐体中心的角度变化解释本次手势。
    private float previousGestureTiltDegrees; // 上一采样倾角，用于推导摇晃速度。
    private float previousGestureAngularVelocity; // 上一角速度，用于识别左右反复摇晃。
    private float previousGestureTime; // 上一手势采样时间。
    private float vesselTiltDegrees; // 当前罐体视觉倾角；允许跨过 ±180° 后连续展开，避免分支点跳变。
    private float targetVesselTiltDegrees; // 手势请求的目标倾角，实际显示角只按固定角速度追随。
    private bool pourGestureActive; // 正在按住陶罐左右摇摆。
    private bool pourReleaseSettling; // 抬手后先平滑到达最后目标，再进入自动回正。

    /// <summary>表现层监听玩法请求，不把 UI 依赖传回容器模块。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        Mod_WaterVessel.OpenRequested -= Show;
        Mod_WaterVessel.OpenRequested += Show;
    }
    /// <summary>通过资源注册表实例化正式面板，并切换当前目标。</summary>
    private static void Show(Mod_WaterVessel target, Item owner)
    {
        if (current == null)
            current = UIManager.Instance.CreatePanelFromGameObject(GameRes.Instance.GetPrefab(PrefabKey)).GetComponent<WaterVesselPanel>();
        current.ClearTarget();
        current.vessel = target;
        target.Changed += current.Refresh;
        current.actor = owner;
        current.ApplyAppearance(target.item.itemData.IDName);
        BuildingPanelActions buildingActions = current.GetComponent<BuildingPanelActions>();
        if (buildingActions == null)
            throw new InvalidOperationException("水容器面板缺少 BuildingPanelActions，正式 Prefab 未完成建筑操作绑定。");
        buildingActions.Bind(target.item);
        current.panel.Open();
        current.Refresh();
        current.Liquid.SetWater(target.Data.Amount, target.Capacity, target.CurrentLiquid?.VisualState, true);
        current.ResetPourGesture(true);
    }
    /// <summary>绑定现有节点；界面层级只由 Prefab 决定。</summary>
    private void Awake()
    {
        panel = GetComponent<BasePanel>();
        title = panel.GetText("陶罐标题");
        hint = panel.GetText("说明文本");
        status = panel.GetText("水量状态");
        drink = panel.GetButton("饮水按钮");
        vesselArt = transform.Find("设置对话框/陶罐剖面/陶罐切面") as RectTransform;
        if (vesselArt == null)
            throw new InvalidOperationException("水容器面板缺少陶罐剖面/陶罐切面，无法绑定摇摆倒液体手势。");
        if (LeftPourOutlet == null || RightPourOutlet == null ||
            LeftPourOutlet.parent != vesselArt || RightPourOutlet.parent != vesselArt)
        {
            throw new InvalidOperationException("水容器面板缺少左右罐口出口锚点，正式 Prefab 未完成倾倒出口绑定。");
        }
        vesselGestureFrame = vesselArt.parent as RectTransform;
        if (vesselGestureFrame == null)
            throw new InvalidOperationException("水容器面板缺少陶罐剖面手势坐标系。");
        pourGraphic = vesselGestureFrame.Find("倾倒液流")?.GetComponent<WaterVesselPourGraphic>();
        if (pourGraphic == null)
            throw new InvalidOperationException("水容器面板缺少陶罐剖面/倾倒液流，正式 Prefab 未完成倾倒表现绑定。");
        vesselImage = vesselArt.GetComponent<Image>();
        interiorImage = Liquid.transform.parent.GetComponent<Image>();
        Rect artRect = vesselArt.rect;
        defaultAppearance = new VesselAppearance
        {
            Cutaway = vesselImage.sprite,
            Interior = interiorImage.sprite,
            FillRange = Liquid.FillRange,
            LeftOutlet = Rect.PointToNormalized(artRect, LeftPourOutlet.localPosition),
            RightOutlet = Rect.PointToNormalized(artRect, RightPourOutlet.localPosition)
        };
        drink.onClick.AddListener(Drink);
        panel.GetButton("关闭按钮").onClick.AddListener(Close);
        panel.Closed += ClearTarget;
    }
    /// <summary>只在面板可见时刷新，离开目标或目标销毁时收起。</summary>
    private void Update()
    {
        UpdatePourMotion();
        if (!panel.IsOpen()) return;
        if (vessel == null || !vessel.CanOperate(actor)) { Close(); return; }
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + 0.2f;
        Refresh();
    }
    /// <summary>显示当前液体定义；饮用能力和恢复量都由液体定义决定。</summary>
    private void Refresh()
    {
        title.text = GameRes.Instance != null &&
                     GameRes.Instance.TryGetItemDefinition(vessel.item.itemData.IDName, out RuntimeItemDefinition definition)
            ? definition.DisplayName
            : FlatWorldLocalizationService.GetUiText("水容器");
        hint.text = FlatWorldLocalizationService.GetUiText("一次只装一种液体；拖入液体原料或其他容器可装液，拖动容器可倾倒。");

        Liquid.SetWater(vessel.Data.Amount, vessel.Capacity, vessel.CurrentLiquid?.VisualState);
        LiquidDefinition liquid = vessel.CurrentLiquid;
        string liquidName = Mod_WaterVessel.IsEmptyAmount(vessel.Data.Amount)
            ? FlatWorldLocalizationService.GetUiText("空容器")
            : FlatWorldLocalizationService.GetUiText(liquid?.DisplayName ?? vessel.Data.LiquidId);
        status.text = FlatWorldLocalizationService.GetUiFormat("{0}　{1} / {2} 份\n加热进度：{3:0} 秒",
            liquidName, vessel.Data.Amount.ToString("0.#"),
            vessel.Capacity, vessel.Data.ProcessingSeconds);
        drink.interactable = liquid?.Drinkable == true &&
            !Mod_WaterVessel.IsEmptyAmount(vessel.Data.Amount);
    }
    /// <summary>完成一次饮水并即时更新余量。</summary>
    private void Drink() { vessel.Drink(actor); Refresh(); }

    #region 库存容器拖入转液

    /// <summary>只有陶罐剖面本体区域接收库存拖放，按钮和面板其它空白区域不参与转液。</summary>
    public bool ContainsInventoryDropPoint(Vector2 screenPosition, Camera eventCamera)
    {
        return panel != null && panel.IsOpen() && vesselArt != null &&
               RectTransformUtility.RectangleContainsScreenPoint(vesselArt, screenPosition, eventCamera);
    }

    /// <summary>拖入容器时原地转液，拖入液体原料时按本次拖拽数量和剩余容量扣料。</summary>
    public bool TryAcceptInventoryDrag(InventoryDragTransaction transaction, Vector2 screenPosition, Camera eventCamera)
    {
        if (transaction == null || vessel == null || !vessel.CanOperate(actor) ||
            !ContainsInventoryDropPoint(screenPosition, eventCamera))
        {
            return false;
        }

        return transaction.TryConsumeSourceItem(sourceItem => vessel.TransferFromInventoryItem(sourceItem, actor, transaction.DraggedAmount));
    }

    #endregion

    #region 摇摆倒液体

    /// <summary>从陶罐区域开始二维手势；靠外圈时直接把绕中心的半圆/圆弧轨迹换算成罐体倾角。</summary>
    public void OnPointerDown(PointerEventData eventData)
    {
        if (!panel.IsOpen() || vessel == null || !vessel.CanOperate(actor) ||
            !RectTransformUtility.RectangleContainsScreenPoint(vesselArt, eventData.position, eventData.pressEventCamera))
        {
            return;
        }

        pourPointerId = eventData.pointerId;
        pourGestureActive = true;
        if (!TryGetPourLocalPoint(eventData, out Vector2 localPoint))
        {
            ResetPourGesture(true);
            return;
        }
        pourStartLocalPoint = localPoint;
        Vector2 center = GetVesselCenterLocal();
        pourStartRadial = localPoint - center;
        float minimumRadius = Mathf.Min(vesselArt.rect.width, vesselArt.rect.height) * OrbitGestureMinimumRadiusRatio;
        useOrbitGesture = pourStartRadial.sqrMagnitude >= minimumRadius * minimumRadius;
        pourStartPolarAngle = Mathf.Atan2(pourStartRadial.y, pourStartRadial.x) * Mathf.Rad2Deg;
        pourStartTiltDegrees = Mathf.DeltaAngle(0f, vesselTiltDegrees);
        targetVesselTiltDegrees = vesselTiltDegrees;
        pourReleaseSettling = false;
        previousGestureTiltDegrees = vesselTiltDegrees;
        previousGestureAngularVelocity = 0f;
        previousGestureTime = Time.unscaledTime;
        UpdatePourGestureTarget(eventData);
    }

    /// <summary>横向、纵向、斜向与绕罐体的半圆轨迹都会连续改变倾角。</summary>
    public void OnDrag(PointerEventData eventData)
    {
        if (!pourGestureActive || eventData.pointerId != pourPointerId)
            return;

        UpdatePourGestureTarget(eventData);
    }

    /// <summary>抬手即停止继续倒液体，保留当前剩余份数。</summary>
    public void OnPointerUp(PointerEventData eventData)
    {
        if (pourGestureActive && eventData.pointerId == pourPointerId)
            UpdatePourGestureTarget(eventData);
        StopPourGesture(eventData.pointerId);
    }

    /// <summary>EventSystem 结束拖拽时同样释放触点，兼容触屏取消与拖出区域。</summary>
    public void OnEndDrag(PointerEventData eventData)
    {
        if (pourGestureActive && eventData.pointerId == pourPointerId)
            UpdatePourGestureTarget(eventData);
        StopPourGesture(eventData.pointerId);
    }

    /// <summary>
    /// 直立 0° 可保留 100% 容量，水平 90° 可保留 50%，完全倒扣 180° 可保留 0%。
    /// 实际液体只允许减少，不会因为玩家把罐子扶正而重新出现。
    /// </summary>
    private void UpdatePourGestureTarget(PointerEventData eventData)
    {
        if (!TryGetPourLocalPoint(eventData, out Vector2 localPoint))
            return;

        Vector2 center = GetVesselCenterLocal();
        Vector2 currentRadial = localPoint - center;
        float minimumRadius = Mathf.Min(vesselArt.rect.width, vesselArt.rect.height) * OrbitGestureMinimumRadiusRatio;
        float requestedTiltDegrees;
        if (useOrbitGesture && currentRadial.sqrMagnitude >= minimumRadius * minimumRadius)
        {
            float currentAngle = Mathf.Atan2(currentRadial.y, currentRadial.x) * Mathf.Rad2Deg;
            // Unity UI 的 Z 正角为逆时针：向右绕行应得到负角（顺时针朝右倒），向左绕行得到正角。
            requestedTiltDegrees = Mathf.Clamp(
                pourStartTiltDegrees + Mathf.DeltaAngle(pourStartPolarAngle, currentAngle),
                -MaxPourTiltDegrees,
                MaxPourTiltDegrees);
        }
        else
        {
            // 中心附近起手没有稳定极角时，使用起点切线；再退化到二维主轴位移，保证上下滑也有反馈。
            Vector2 delta = localPoint - pourStartLocalPoint;
            float fullPourDistance = Mathf.Max(1f, Mathf.Max(vesselArt.rect.width, vesselArt.rect.height) * FullPourDragWidthRatio);
            float signedTravel;
            if (pourStartRadial.sqrMagnitude >= minimumRadius * minimumRadius)
            {
                Vector2 tangent = new Vector2(-pourStartRadial.y, pourStartRadial.x).normalized;
                signedTravel = -Vector2.Dot(delta, tangent);
            }
            else
            {
                signedTravel = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y) ? delta.x : delta.y;
            }

            // 中心回退同样遵守“右拖=负 Z / 左拖=正 Z”，避免与外圈圆弧手势方向相反。
            requestedTiltDegrees = Mathf.Clamp(
                pourStartTiltDegrees - signedTravel / fullPourDistance * MaxPourTiltDegrees,
                -MaxPourTiltDegrees,
                MaxPourTiltDegrees);
        }

        // 跨过 ±180° 时选择离当前显示角最近的等价目标，避免分支点从朝上瞬间翻到朝下。
        targetVesselTiltDegrees = vesselTiltDegrees + Mathf.DeltaAngle(vesselTiltDegrees, requestedTiltDegrees);
    }

    /// <summary>每帧按受限角速度追随手势；抬手后先完成最后倾倒姿态，再缓慢回正。</summary>
    private void UpdatePourMotion()
    {
        float deltaTime = Time.unscaledDeltaTime;
        if (deltaTime <= 0f)
            return;

        bool followingPourTarget = pourGestureActive || pourReleaseSettling;
        float desiredTilt = followingPourTarget ? targetVesselTiltDegrees : 0f;
        float speed = followingPourTarget ? PourFollowDegreesPerSecond : PourReturnDegreesPerSecond;
        float previousTilt = vesselTiltDegrees;
        vesselTiltDegrees = Mathf.MoveTowards(vesselTiltDegrees, desiredTilt, speed * deltaTime);

        if (!Mathf.Approximately(previousTilt, vesselTiltDegrees))
        {
            UpdateLiquidAgitation();
            ApplyPourTilt();
            if (followingPourTarget)
                SpillForCurrentTilt();
        }

        if (pourReleaseSettling && Mathf.Abs(targetVesselTiltDegrees - vesselTiltDegrees) <= PourSettleAngleEpsilon)
        {
            pourReleaseSettling = false;
            targetVesselTiltDegrees = 0f;
        }
    }

    /// <summary>按当前真实显示倾角结算液体，保证液体不会领先于罐体动画瞬间消失。</summary>
    private void SpillForCurrentTilt()
    {
        if (vessel == null || !vessel.CanOperate(actor) || Mod_WaterVessel.IsEmptyAmount(vessel.Data.Amount))
            return;

        float physicalTilt = Mathf.Abs(Mathf.DeltaAngle(0f, vesselTiltDegrees));
        bool shouldFullyEmpty = physicalTilt >= MaxPourTiltDegrees - FullEmptyTiltToleranceDegrees;
        float retainedFraction = 1f - physicalTilt / MaxPourTiltDegrees;
        float maxRetainedAmount = vessel.Capacity * retainedFraction;
        float spillAmount = shouldFullyEmpty ? vessel.Data.Amount : vessel.Data.Amount - maxRetainedAmount;
        if (spillAmount <= Mod_WaterVessel.AmountEpsilon ||
            (!shouldFullyEmpty && spillAmount + Mod_WaterVessel.AmountEpsilon < MinimumPourCommitAmount))
            return;

        float removed = vessel.PourToGround(actor, spillAmount);
        if (removed <= Mod_WaterVessel.AmountEpsilon)
            return;

        float normalizedFlow = Mathf.Clamp01(removed / Mathf.Max(0.1f, vessel.Capacity * 0.18f));
        pourGraphic.Emit(
            normalizedFlow,
            Liquid.CurrentBodyColor,
            Liquid.CurrentSurfaceColor,
            Liquid.CurrentDetailColor,
            Liquid.CurrentMurkiness,
            Liquid.CurrentViscosity);
        Liquid.AddAgitation(Mathf.Clamp01(0.3f + normalizedFlow * 0.7f));
        Liquid.SetWater(vessel.Data.Amount, vessel.Capacity, vessel.CurrentLiquid?.VisualState, Liquid.CurrentViscosity <= 0.01f);
    }

    /// <summary>来回快速改变倾角会显著放大水面波纹，慢速单向倾倒只产生轻微扰动。</summary>
    private void UpdateLiquidAgitation()
    {
        float now = Time.unscaledTime;
        float deltaTime = Mathf.Max(0.001f, now - previousGestureTime);
        float deltaDegrees = Mathf.DeltaAngle(previousGestureTiltDegrees, vesselTiltDegrees);
        float angularVelocity = deltaDegrees / deltaTime;
        float reversalBoost = previousGestureAngularVelocity != 0f &&
                              Mathf.Sign(previousGestureAngularVelocity) != Mathf.Sign(angularVelocity)
            ? 0.45f
            : 0f;
        float impulse = Mathf.Clamp01(Mathf.Abs(angularVelocity) / 420f + reversalBoost);
        Liquid.AddAgitation(impulse);
        previousGestureTiltDegrees = vesselTiltDegrees;
        previousGestureAngularVelocity = angularVelocity;
        previousGestureTime = now;
    }

    /// <summary>始终在不旋转的父节点中读取触点，避免罐体自身旋转反过来改变拖拽角度计算。</summary>
    private bool TryGetPourLocalPoint(PointerEventData eventData, out Vector2 localPoint)
    {
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            vesselGestureFrame,
            eventData.position,
            eventData.pressEventCamera,
            out localPoint);
    }

    /// <summary>把罐体 Rect 中心换算到稳定的手势父坐标系，供圆弧拖拽计算。</summary>
    private Vector2 GetVesselCenterLocal()
    {
        Vector3 centerWorld = vesselArt.TransformPoint(vesselArt.rect.center);
        return vesselGestureFrame.InverseTransformPoint(centerWorld);
    }

    /// <summary>结束指定触点后先完成最后一次目标倾角，避免抬手时把尚未完成的倾倒过程直接取消。</summary>
    private void StopPourGesture(int pointerId)
    {
        if (!pourGestureActive || pointerId != pourPointerId)
            return;

        pourGestureActive = false;
        pourPointerId = InvalidPointerId;
        pourReleaseSettling = Mathf.Abs(targetVesselTiltDegrees - vesselTiltDegrees) > PourSettleAngleEpsilon;
        if (!pourReleaseSettling)
            targetVesselTiltDegrees = 0f;
    }

    /// <summary>罐体转动时让液体层反向抵消父级旋转，保留真实水平水面。</summary>
    private void ApplyPourTilt()
    {
        if (vesselArt != null)
            vesselArt.localRotation = Quaternion.Euler(0f, 0f, vesselTiltDegrees);
        if (Liquid != null)
            Liquid.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -vesselTiltDegrees);
        UpdatePourOutletPose();
    }

    /// <summary>根据当前倾倒方向选择下侧嘴沿，并把 Prefab 锚点的真实位置/朝向换算到液流坐标系。</summary>
    private void UpdatePourOutletPose()
    {
        if (pourGraphic == null || LeftPourOutlet == null || RightPourOutlet == null)
            return;

        // 这里保留连续角的正负方向；不能用 DeltaAngle，否则 -180° 会等价折叠成 +180°，导致嘴沿瞬间换边。
        float currentTilt = vesselTiltDegrees;
        float requestedTilt = Mathf.Abs(currentTilt) > 0.001f ? currentTilt : targetVesselTiltDegrees;
        RectTransform outlet = requestedTilt >= 0f ? LeftPourOutlet : RightPourOutlet;

        Vector2 mouth = pourGraphic.rectTransform.InverseTransformPoint(outlet.position);
        Vector2 outwardPoint = pourGraphic.rectTransform.InverseTransformPoint(outlet.TransformPoint(Vector3.up * 24f));
        pourGraphic.SetOutletPose(mouth, outwardPoint - mouth);
    }

    /// <summary>切换目标或销毁面板时彻底释放手势并恢复剖面姿态。</summary>
    private void ResetPourGesture(bool immediate)
    {
        pourGestureActive = false;
        pourPointerId = InvalidPointerId;
        pourStartLocalPoint = Vector2.zero;
        pourStartRadial = Vector2.zero;
        pourStartPolarAngle = 0f;
        pourStartTiltDegrees = 0f;
        useOrbitGesture = false;
        previousGestureTiltDegrees = 0f;
        previousGestureAngularVelocity = 0f;
        previousGestureTime = 0f;
        targetVesselTiltDegrees = 0f;
        pourReleaseSettling = false;
        pourGraphic?.Clear(immediate);
        if (!immediate)
            return;

        vesselTiltDegrees = 0f;
        ApplyPourTilt();
    }

    #endregion

    /// <summary>关闭后清理玩法引用。</summary>
    private void Close() => panel.Close();
    private void ClearTarget()
    {
        if (vessel != null) vessel.Changed -= Refresh;
        ResetPourGesture(true);
        vessel = null; actor = null;
    }
    private void OnDisable() => ResetPourGesture(true);
    private void OnDestroy() { ClearTarget(); if (panel != null) panel.Closed -= ClearTarget; }
}
