using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 石棒手势视图：鼠标与单根手指共用事件，支持提起后下压捣击，以及贴住碗底的左右研磨。
/// 只发布加工意图并模拟材料的重力滚动，不直接修改库存、配方或存档。
/// </summary>
public sealed class MortarInteractionView : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    #region 视图配置
    public ItemSlot_UI BowlSlot; // 正式透明槽位模板，运行时按库存数量复用。
    public RectTransform DragArea; // 使用面板局部坐标，不受 Canvas 缩放影响。
    public Button PreviousPage; // 碗内物品上一页。
    public Button NextPage; // 碗内物品下一页。
    public TMP_Text PageText; // 库存页码，物品少时隐藏。
    public RectTransform BowlShape; // 碗内碰撞轮廓按石碗尺寸换算。
    public float LiftDistance = 90f; // 两次有效捣击之间必须提起的距离。
    public float PestleBottomVisualPadding = 6.5f; // 石棒 Sprite 底部透明留白，接触时把 RectTransform 再下沉到可见像素贴住碗底。
    public float GrindTravelDistance = 64f; // 石棒贴底累计横移达到该距离时触发一次研磨。
    public float GrindContactTolerance = 24f; // 允许手机触控在碗底上方少量浮动，超出后不累计研磨距离。
    public event Action Struck; // 有效捣击或研磨步进都会发布一次加工意图。
    private RectTransform pestle;
    private Vector2 restPosition;
    private Vector2 pointerOffset;
    private readonly List<SlotBody> slots = new List<SlotBody>();
    private readonly List<SlotBody> deposits = new List<SlotBody>();
    private int page;
    private const int PageSize = 4; // 每页两列两行，保证每种材料和产物都可独立拿取。
    private Coroutine impact;
    private Coroutine dustAnimation;
    private RectTransform dustRoot;
    private DustParticle[] dustParticles;
    private const int DustParticleCount = 12; // UI 内固定复用的像素粉尘数量，避免每次捣击产生临时对象。
    private static readonly Color32[] DustPalette =
    {
        new Color32(205, 198, 181, 225),
        new Color32(184, 178, 165, 215),
        new Color32(222, 216, 198, 205)
    };

    private int? pointerId;
    private bool armed;
    private float grindTravel;
    #endregion

    #region 手势与反馈
    private void Awake()
    {
        pestle = (RectTransform)transform;
        restPosition = pestle.anchoredPosition;
        PreviousPage.onClick.AddListener(() => ChangePage(-1));
        NextPage.onClick.AddListener(() => ChangePage(1));
        InitializeDustParticles();
    }

    /// <summary>每次库存变化只补齐新增槽位；物品数量变化不重置正在进行的捣击手势。</summary>
    public void SyncSlots(Inventory inventory)
    {
        bool changed = false;
        while (slots.Count < inventory.Data.itemSlots.Count)
        {
            ItemSlot_UI ui = Instantiate(BowlSlot, DragArea);
            ui.name = $"碗内物品_{slots.Count}";
            inventory.BindSlotUI(ui, slots.Count);
            ui.ItemAddedAtPointer += OnItemAddedAtPointer;
            slots.Add(new SlotBody { UI = ui, Rect = (RectTransform)ui.transform });
            changed = true;
        }
        for (int i = 0; i < slots.Count; i++)
        {
            bool occupied = inventory.Data.itemSlots[i].itemData != null;
            changed |= occupied != slots[i].Occupied;
            if (!occupied) slots[i].HasPose = false;
            slots[i].Occupied = occupied;
            slots[i].UI.RefreshUI();
        }
        if (changed) { LayoutSlots(); StartMotion(); }
    }

    private void ChangePage(int direction)
    {
        page += direction;
        LayoutSlots();
        StartMotion();
    }

    /// <summary>分页只限制可见数量，不限制库存容量；最后一个空槽覆盖凹槽作为投料区域。</summary>
    private void LayoutSlots(bool reset = false)
    {
        int occupiedCount = slots.FindAll(slot => slot.Occupied).Count;
        int pages = Mathf.Max(1, Mathf.CeilToInt(occupiedCount / (float)PageSize));
        page = Mathf.Clamp(page, 0, pages - 1);
        int visibleCount = Mathf.Min(PageSize, occupiedCount - page * PageSize);
        int ordinal = 0;
        for (int i = 0; i < slots.Count; i++)
        {
            SlotBody slot = slots[i];
            int local = slot.Occupied ? ordinal++ - page * PageSize : -1;
            bool visible = slot.Occupied && local >= 0 && local < PageSize;
            bool dropTarget = !slot.Occupied && i == slots.Count - 1;
            slot.Rect.gameObject.SetActive(visible || dropTarget);

            slot.Rect.sizeDelta = dropTarget ? new Vector2(InteriorHalfWidth * 2f, InteriorTop - InteriorBottom) : new Vector2(72, 64);
            float x = visibleCount == 1 ? 0 : (local % 2 == 0 ? -42f : 42f);
            slot.Home = dropTarget ? new Vector2(BowlShape.anchoredPosition.x, (InteriorTop + InteriorBottom) * .5f) : new Vector2(x, FloorAt(x) + 26f + local / 2 * 65f);
            if (dropTarget || reset || !slot.HasPose)
            {
                slot.Rect.anchoredPosition = !dropTarget && !reset
                    ? new Vector2(pestle.anchoredPosition.x, Mathf.Max(pestle.anchoredPosition.y + 24f, FloorAt(pestle.anchoredPosition.x) + 26f))
                    : slot.Home;
                slot.Velocity = Vector2.zero;
                slot.Spin = 0;
                slot.Rect.localRotation = Quaternion.identity;
            }
            if (slot.Occupied) slot.HasPose = true;
            if (dropTarget) slot.Rect.SetSiblingIndex(BowlShape.GetSiblingIndex() + 1);
        }
        transform.SetAsLastSibling();
        PreviousPage.gameObject.SetActive(pages > 1);
        NextPage.gameObject.SetActive(pages > 1);
        PreviousPage.interactable = page > 0;
        NextPage.interactable = page + 1 < pages;
        PageText.text = pages > 1 ? $"{page + 1} / {pages}" : string.Empty;
    }

    /// <summary>单个真实槽位的临时运动状态，不写入存档。</summary>
    private sealed class SlotBody
    {
        public ItemSlot_UI UI;
        public RectTransform Rect;
        public Vector2 Home;
        public Vector2 Velocity;
        public float Spin;
        public bool Occupied;
        public bool HasPose;
    }

    /// <summary>单个 UI 粉尘粒子的瞬时运动状态。</summary>
    private sealed class DustParticle
    {
        public RectTransform Rect;
        public Image Image;
        public Vector2 Velocity;
        public float Lifetime;
        public float Age;
        public float Spin;
        public Color32 BaseColor;
    }

    public void OnBeginDrag(PointerEventData data)
    {
        if (pointerId.HasValue || data.button != PointerEventData.InputButton.Left) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(DragArea, data.position, data.pressEventCamera, out Vector2 local)) return;
        pointerId = data.pointerId;
        pointerOffset = pestle.anchoredPosition - local;
        armed = pestle.anchoredPosition.y >= PestleContactY(pestle.anchoredPosition.x) + LiftDistance;
        grindTravel = 0f;
    }

    public void OnDrag(PointerEventData data)
    {
        if (pointerId != data.pointerId) return;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(DragArea, data.position, data.pressEventCamera, out Vector2 local)) return;
        Vector2 previous = pestle.anchoredPosition;
        Vector2 target = local + pointerOffset;
        float horizontalLimit = InteriorHalfWidth - pestle.rect.width * .3f;
        target.x = Mathf.Clamp(target.x, BowlShape.anchoredPosition.x - horizontalLimit, BowlShape.anchoredPosition.x + horizontalLimit);
        float contactY = PestleContactY(target.x);
        // 棒槌以底端为轴心；最低点按可见像素而不是 RectTransform 边界贴住碗底。
        target.y = Mathf.Clamp(target.y, contactY, InteriorTop);
        pestle.anchoredPosition = target;
        if (target.y >= contactY + LiftDistance) armed = true;
        if (armed && target.y <= contactY + 1f)
        {
            armed = false;
            grindTravel = 0f;
            Struck?.Invoke();
            PlayImpact(); // 捣击反馈独立于配方，空碗或只有产物时也能自由操作。
            return;
        }

        TrackGrinding(previous, target);
    }

    public void OnEndDrag(PointerEventData data)
    {
        if (pointerId == data.pointerId) CancelGesture();
    }

    public void CancelGesture()
    {
        pointerId = null;
        armed = false;
        grindTravel = 0f;
        if (pestle != null) pestle.anchoredPosition = restPosition;
    }

    /// <summary>石棒前后两帧都贴近碗底时累计横向行程，每达到一段行程触发一次研磨。</summary>
    private void TrackGrinding(Vector2 previous, Vector2 current)
    {
        float tolerance = Mathf.Max(0f, GrindContactTolerance);
        bool previousContact = previous.y <= PestleContactY(previous.x) + tolerance;
        bool currentContact = current.y <= PestleContactY(current.x) + tolerance;
        if (!previousContact || !currentContact)
        {
            grindTravel = 0f;
            return;
        }

        float horizontalDelta = current.x - previous.x;
        if (Mathf.Abs(horizontalDelta) <= .01f) return;

        grindTravel += Mathf.Abs(horizontalDelta);
        if (grindTravel < Mathf.Max(1f, GrindTravelDistance)) return;

        // 每段有效行程只结算一次；快速滑动不会因为单帧跨度过大而一次消耗多份原料。
        grindTravel = 0f;
        Struck?.Invoke();
        PlayGrindingMotion(Mathf.Sign(horizontalDelta));
    }

    /// <summary>每次捣击给图标追加向上、横向和旋转冲量；仅在运动期间推进局部重力模拟。</summary>
    private void PlayImpact()
    {
        foreach (SlotBody slot in slots)
        {
            if (!slot.Occupied || !slot.Rect.gameObject.activeSelf) continue;
            slot.Velocity += new Vector2(UnityEngine.Random.Range(-170f, 170f), UnityEngine.Random.Range(160f, 220f));
            slot.Velocity = Vector2.ClampMagnitude(slot.Velocity, 340f);
            slot.Spin = UnityEngine.Random.Range(-260f, 260f);
        }
        StartMotion();
    }

    /// <summary>研磨时只给材料较小的横向推力与抬升，区别于垂直捣击的明显弹跳。</summary>
    private void PlayGrindingMotion(float direction)
    {
        float pushDirection = Mathf.Approximately(direction, 0f) ? 1f : Mathf.Sign(direction);
        foreach (SlotBody slot in slots)
        {
            if (!slot.Occupied || !slot.Rect.gameObject.activeSelf) continue;
            slot.Velocity += new Vector2(
                pushDirection * UnityEngine.Random.Range(65f, 105f),
                UnityEngine.Random.Range(35f, 70f));
            slot.Velocity = Vector2.ClampMagnitude(slot.Velocity, 220f);
            slot.Spin = -pushDirection * UnityEngine.Random.Range(110f, 220f);
        }
        StartMotion();
    }

    /// <summary>配方真实提交成功后，在棒槌落点播放一小簇向上飘散的像素粉尘。</summary>
    public void PlayProcessingDust()
    {
        if (!isActiveAndEnabled || dustRoot == null || dustParticles == null) return;
        if (dustAnimation != null) StopCoroutine(dustAnimation);

        // 粉尘覆盖碗内材料，但始终位于石棒下方，避免遮住玩家正在拖动的工具。
        dustRoot.SetAsLastSibling();
        pestle.SetAsLastSibling();

        float x = pestle.anchoredPosition.x;
        Vector2 origin = new Vector2(x, FloorAt(x) + 44f);
        foreach (DustParticle particle in dustParticles)
        {
            float width = Mathf.Round(UnityEngine.Random.Range(4f, 9f));
            float height = Mathf.Round(UnityEngine.Random.Range(4f, 8f));
            particle.Rect.sizeDelta = new Vector2(width, height);
            particle.Rect.anchoredPosition = origin + new Vector2(
                Mathf.Round(UnityEngine.Random.Range(-24f, 24f)),
                Mathf.Round(UnityEngine.Random.Range(0f, 14f)));
            particle.Rect.localScale = Vector3.one;
            particle.Rect.localRotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(-35f, 35f));
            particle.Velocity = new Vector2(
                UnityEngine.Random.Range(-105f, 105f),
                UnityEngine.Random.Range(105f, 190f));
            particle.Lifetime = UnityEngine.Random.Range(.38f, .68f);
            particle.Age = UnityEngine.Random.Range(-.05f, .04f);
            particle.Spin = UnityEngine.Random.Range(-150f, 150f);
            particle.BaseColor = DustPalette[UnityEngine.Random.Range(0, DustPalette.Length)];
            particle.Image.color = particle.BaseColor;
            particle.Image.enabled = true;
        }

        dustAnimation = StartCoroutine(AnimateProcessingDust());
    }

    /// <summary>建立固定 UI 粒子池；使用纯色 Image，确保 Screen Space Overlay 下也能稳定显示。</summary>
    private void InitializeDustParticles()
    {
        GameObject rootObject = new GameObject("捣碎粉尘", typeof(RectTransform));
        dustRoot = rootObject.GetComponent<RectTransform>();
        dustRoot.SetParent(DragArea, false);
        dustRoot.anchorMin = Vector2.zero;
        dustRoot.anchorMax = Vector2.one;
        dustRoot.offsetMin = Vector2.zero;
        dustRoot.offsetMax = Vector2.zero;
        dustRoot.pivot = new Vector2(.5f, .5f);

        dustParticles = new DustParticle[DustParticleCount];
        for (int i = 0; i < dustParticles.Length; i++)
        {
            GameObject particleObject = new GameObject($"粉尘_{i + 1:00}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rect = particleObject.GetComponent<RectTransform>();
            rect.SetParent(dustRoot, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            Image image = particleObject.GetComponent<Image>();
            image.raycastTarget = false;
            image.enabled = false;
            dustParticles[i] = new DustParticle { Rect = rect, Image = image };
        }
    }

    /// <summary>用未缩放时间推进短促粉尘，先上扬再缓慢下落并淡出。</summary>
    private IEnumerator AnimateProcessingDust()
    {
        bool alive = true;
        while (alive)
        {
            alive = false;
            float dt = Mathf.Min(Time.unscaledDeltaTime, .04f);
            foreach (DustParticle particle in dustParticles)
            {
                if (!particle.Image.enabled) continue;
                particle.Age += dt;
                if (particle.Age < 0f)
                {
                    alive = true;
                    continue;
                }
                if (particle.Age >= particle.Lifetime)
                {
                    particle.Image.enabled = false;
                    continue;
                }

                alive = true;
                particle.Velocity.y -= 165f * dt;
                particle.Velocity.x *= Mathf.Exp(-2.1f * dt);
                particle.Rect.anchoredPosition += particle.Velocity * dt;
                particle.Rect.Rotate(0f, 0f, particle.Spin * dt);
                float progress = particle.Age / particle.Lifetime;
                particle.Rect.localScale = Vector3.one * Mathf.Lerp(1f, .55f, progress);
                Color color = particle.BaseColor;
                color.a = (particle.BaseColor.a / 255f) * Mathf.Pow(1f - progress, 1.35f);
                particle.Image.color = color;
            }
            yield return null;
        }

        dustAnimation = null;
    }

    /// <summary>关闭面板时回收全部粉尘表现，不改动 UI 兄弟顺序。</summary>
    private void ResetDustParticles()
    {
        if (dustAnimation != null) StopCoroutine(dustAnimation);
        dustAnimation = null;
        if (dustParticles != null)
            foreach (DustParticle particle in dustParticles)
                if (particle?.Image != null) particle.Image.enabled = false;
    }

    /// <summary>库存事务结束后用实际点击或松手位置投料；已存在的堆叠不搬动。</summary>
    private void OnItemAddedAtPointer(ItemSlot_UI ui, Vector2 screen, Camera camera, bool wasOccupied, float added)
    {
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(DragArea, screen, camera, out Vector2 point)) return;
        SlotBody body = slots[ui.slotIndex];
        if (wasOccupied)
        {
            // 合并时只克隆正式模板作为投料表现，不生成第二份库存数据。
            Image visual = Instantiate(BowlSlot.image, DragArea);
            visual.raycastTarget = false;
            visual.sprite = ui.image.sprite;
            visual.gameObject.SetActive(true);
            body = new SlotBody { Rect = visual.rectTransform, Occupied = true };
            deposits.Add(body);
        }
        float limit = InteriorHalfWidth - 22f;
        point.x = Mathf.Clamp(point.x, BowlShape.anchoredPosition.x - limit, BowlShape.anchoredPosition.x + limit);
        point.y = Mathf.Max(point.y, FloorAt(point.x) + 22f);
        body.Rect.anchoredPosition = point;
        body.HasPose = true;
        body.Velocity = Vector2.zero;
        body.Spin = UnityEngine.Random.Range(-60f, 60f);
        StartMotion();
    }

    private IEnumerable<SlotBody> MovingBodies()
    {
        foreach (SlotBody body in slots) yield return body;
        foreach (SlotBody body in deposits) yield return body;
    }

    private void StartMotion()
    {
        if (!isActiveAndEnabled) return;
        if (impact != null) StopCoroutine(impact);
        impact = StartCoroutine(SimulateContents());
    }

    private void ClearDeposits()
    {
        foreach (SlotBody body in deposits) Destroy(body.Rect.gameObject);
        deposits.Clear();
    }

    // 按用户红线定义较宽、较深的碗内轮廓，物品可落入石碗深色内壁区域。
    private static readonly Vector2[] InnerEdge = {
        new Vector2(-.31f, .4583f), new Vector2(-.30f, .22f),
        new Vector2(-.275f, .08f), new Vector2(-.22f, -.04f),
        new Vector2(-.14f, -.12f), new Vector2(-.06f, -.14f),
        new Vector2(0, -.14f), new Vector2(.06f, -.14f),
        new Vector2(.14f, -.12f), new Vector2(.22f, -.04f),
        new Vector2(.275f, .08f), new Vector2(.30f, .22f),
        new Vector2(.31f, .4583f)
    };

    public float InteriorHalfWidth => BowlShape.rect.width * InnerEdge[InnerEdge.Length - 1].x;
    public float InteriorTop => BowlShape.anchoredPosition.y + BowlShape.rect.height * InnerEdge[0].y;
    public float InteriorBottom => FloorAt(BowlShape.anchoredPosition.x);

    /// <summary>石棒最低锚点：补偿 Sprite 底部透明边距，让实际可见像素与碗底轮廓贴合。</summary>
    private float PestleContactY(float x) => FloorAt(x) - Mathf.Max(0f, PestleBottomVisualPadding);

    public float FloorAt(float x)
    {
        float u = (x - BowlShape.anchoredPosition.x) / BowlShape.rect.width;
        for (int i = 1; i < InnerEdge.Length; i++)
            if (u <= InnerEdge[i].x)
                return BowlShape.anchoredPosition.y + BowlShape.rect.height * Mathf.Lerp(
                    InnerEdge[i - 1].y, InnerEdge[i].y,
                    Mathf.InverseLerp(InnerEdge[i - 1].x, InnerEdge[i].x, u));
        return BowlShape.anchoredPosition.y + BowlShape.rect.height * InnerEdge[InnerEdge.Length - 1].y;
    }

    /// <summary>用小步积分、内壁法线反弹和阻尼模拟滚动，不创建会影响世界物理的刚体。</summary>
    private IEnumerator SimulateContents()
    {
        float quietTime = 0f;
        while (quietTime < .3f)
        {
            float frame = Mathf.Min(Time.unscaledDeltaTime, .04f);
            int steps = Mathf.Max(1, Mathf.CeilToInt(frame / .008f));
            float dt = frame / steps;
            bool settled = true;
            foreach (SlotBody slot in MovingBodies())
            {
                if (!slot.Occupied || !slot.Rect.gameObject.activeSelf) continue;
                Vector2 position = slot.Rect.anchoredPosition;
                const float radius = 22f;
                bool grounded = false;
                for (int i = 0; i < steps; i++)
                {
                    slot.Velocity.y -= 850f * dt;
                    position += slot.Velocity * dt;
                    float limit = InteriorHalfWidth - radius;
                    float centerX = BowlShape.anchoredPosition.x;
                    if (Mathf.Abs(position.x - centerX) > limit)
                    {
                        position.x = Mathf.Clamp(position.x, centerX - limit, centerX + limit);
                        slot.Velocity.x *= -.45f;
                    }
                    float floor = FloorAt(position.x) + radius;
                    if (position.y <= floor)
                    {
                        position.y = floor;
                        float slope = (FloorAt(position.x + 1f) - FloorAt(position.x - 1f)) * .5f;
                        Vector2 normal = new Vector2(-slope, 1f).normalized;
                        float approach = Vector2.Dot(slot.Velocity, normal);
                        if (approach < 0) slot.Velocity -= normal * (1.45f * approach);
                        slot.Velocity *= Mathf.Exp(-8f * dt);
                        slot.Spin = Mathf.Lerp(slot.Spin, -slot.Velocity.x * 3f, dt * 12f);
                        grounded = true;
                    }
                    slot.Velocity *= Mathf.Exp(-.65f * dt);
                    slot.Rect.Rotate(0, 0, slot.Spin * dt);
                }
                slot.Rect.anchoredPosition = position;
                settled &= grounded && slot.Velocity.sqrMagnitude < 225f;
            }
            settled &= !ResolveContentsCollisions();
            quietTime = settled ? quietTime + frame : 0f;
            yield return null;
        }
        // 停稳后保留真实落点，只回收已表现完的合并投料。
        foreach (SlotBody slot in slots) { slot.Velocity = Vector2.zero; slot.Spin = 0; }
        ClearDeposits();
        impact = null;
    }

    /// <summary>物品之间以小圆形接触、分离与传递冲量，避免落在同一点后无法单独拿取。</summary>
    private bool ResolveContentsCollisions()
    {
        bool moving = false;
        for (int i = 0; i < slots.Count; i++)
        for (int j = i + 1; j < slots.Count; j++)
        {
            SlotBody a = slots[i], b = slots[j];
            if (!a.Occupied || !b.Occupied || !a.Rect.gameObject.activeSelf || !b.Rect.gameObject.activeSelf) continue;
            Vector2 delta = b.Rect.anchoredPosition - a.Rect.anchoredPosition;
            float distance = delta.magnitude;
            if (distance >= 44f) continue;
            Vector2 normal = distance > .001f ? delta / distance : Vector2.right;
            Vector2 correction = normal * ((44f - distance) * .5f);
            a.Rect.anchoredPosition = ConstrainBody(a.Rect.anchoredPosition - correction);
            b.Rect.anchoredPosition = ConstrainBody(b.Rect.anchoredPosition + correction);
            float approach = Vector2.Dot(b.Velocity - a.Velocity, normal);
            if (approach < 0)
            {
                a.Velocity += normal * approach * .65f;
                b.Velocity -= normal * approach * .65f;
            }
            moving |= approach < -15f;
        }
        return moving;
    }

    private Vector2 ConstrainBody(Vector2 point)
    {
        float limit = InteriorHalfWidth - 22f;
        point.x = Mathf.Clamp(point.x, BowlShape.anchoredPosition.x - limit, BowlShape.anchoredPosition.x + limit);
        point.y = Mathf.Max(point.y, FloorAt(point.x) + 22f);
        return point;
    }

    /// <summary>关闭面板清除槽位的速度、旋转和位置，原料与即时产物独立保留。</summary>
    public void ResetPresentation()
    {
        StopTransientPresentation();
        LayoutSlots(true);
    }

    /// <summary>失活期间只清理瞬时状态，不修改父节点下的兄弟顺序。</summary>
    private void StopTransientPresentation()
    {
        CancelGesture();
        if (impact != null) StopCoroutine(impact);
        impact = null;
        ClearDeposits();
        ResetDustParticles();
    }
    private void OnDisable() => StopTransientPresentation();
    private void OnApplicationFocus(bool focus) { if (!focus) CancelGesture(); }
    private void OnApplicationPause(bool paused) { if (paused) CancelGesture(); }
    #endregion
}
