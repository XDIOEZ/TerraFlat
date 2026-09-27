using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 木桶剖面中的真实库存视图：每个图标就是一个 ItemSlot_UI，取放、合并与持久化由库存负责；
/// 图标在内腔遮罩内按局部重力、桶壁碰撞和水中阻尼移动，运动坐标本身不作为物品存档。
/// </summary>
public sealed class VesselContentsView : MonoBehaviour
{
    #region 配置与绑定

    public ItemSlot_UI SlotTemplate; // 正式 UI_Slot 模板，运行时只克隆为真实库存槽位。
    public RectTransform Interior; // 罐内遮罩，也是槽位的局部运动坐标系。
    public RectTransform VesselArt; // 当前剖面的倾角来源。
    public Rect StorageBounds = new Rect(0.26f, 0.12f, 0.48f, 0.62f); // 木桶可见内腔的归一化边界。

    private readonly List<Body> bodies = new();
    private readonly List<Body> deposits = new(); // 合并到旧堆叠时的无射线临时投料图标。
    private Mod_VesselContents target;
    private Coroutine motion;
    private float liquidFraction;
    private float previousTilt;

    private sealed class Body
    {
        public ItemSlot_UI Slot;
        public RectTransform Rect;
        public Vector2 Velocity;
        public float Spin;
        public bool Occupied;
        public bool HasPose;
    }

    /// <summary>切换木桶时解除旧槽位绑定；同一木桶的库存变化保留已有物品落点。</summary>
    public void Bind(Mod_VesselContents contents)
    {
        if (ReferenceEquals(target, contents)) { SyncSlots(); return; }
        Unbind();
        target = contents;
        if (target == null) return;
        target.Changed += SyncSlots;
        SyncSlots();
    }

    /// <summary>关窗只销毁 UI 克隆，不清空模块中的任何库存数据。</summary>
    public void Unbind()
    {
        if (motion != null) StopCoroutine(motion);
        motion = null;
        ClearDeposits();
        if (target != null)
        {
            target.Changed -= SyncSlots;
            target.Contents.itemSlot_UI.Clear();
        }
        foreach (Body body in bodies)
        {
            if (body.Slot == null) continue;
            body.Slot.ItemAddedAtPointer -= OnItemAddedAtPointer;
            body.Slot.gameObject.SetActive(false);
            Destroy(body.Slot.gameObject);
        }
        bodies.Clear();
        target = null;
    }

    /// <summary>液体份数只改变图标阻尼，不参与库存权威状态。</summary>
    public void SetLiquidFraction(float value)
    {
        float next = Mathf.Clamp01(value);
        if (Mathf.Abs(next - liquidFraction) < 0.01f) return;
        liquidFraction = next;
        if (target != null) StartMotion();
    }

    /// <summary>倾斜桶时重新推进真实槽位图标，让物品沿当前重力方向滑动。</summary>
    public void SetVesselTilt(float degrees)
    {
        if (target == null || Mathf.Abs(Mathf.DeltaAngle(previousTilt, degrees)) < 1f) return;
        previousTilt = degrees;
        StartMotion();
    }

    #endregion

    #region 槽位表现

    /// <summary>槽位增减只更新对应图标；一个空槽覆盖内腔供玩家投料。</summary>
    private void SyncSlots()
    {
        Inventory inventory = target?.Contents;
        if (inventory?.Data?.itemSlots == null) return;
        while (bodies.Count < inventory.Data.itemSlots.Count)
        {
            int index = bodies.Count;
            ItemSlot_UI slot = Instantiate(SlotTemplate, Interior);
            slot.name = $"桶内物品_{index}";
            RectTransform rect = (RectTransform)slot.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.localRotation = Quaternion.identity;
            Image background = slot.GetComponent<Image>();
            background.color = Color.clear;
            background.raycastTarget = true;
            slot.selectionGraphic = slot.image;
            slot.ItemAddedAtPointer += OnItemAddedAtPointer;
            inventory.BindSlotUI(slot, index);
            bodies.Add(new Body { Slot = slot, Rect = rect });
        }

        int firstEmpty = -1;
        int occupiedOrdinal = 0;
        for (int i = 0; i < bodies.Count; i++)
        {
            Body body = bodies[i];
            bool occupied = inventory.Data.itemSlots[i].itemData != null;
            if (!occupied && firstEmpty < 0) firstEmpty = i;
            if (occupied && !body.HasPose)
            {
                body.Rect.anchoredPosition = InitialRestPosition(occupiedOrdinal);
                body.Velocity = Vector2.zero;
                body.Spin = 0f;
                body.HasPose = true;
            }
            if (!occupied) body.HasPose = false;
            if (occupied) occupiedOrdinal++;
            body.Occupied = occupied;
            body.Slot.RefreshUI();
        }

        Rect area = LocalBounds();
        for (int i = 0; i < bodies.Count; i++)
        {
            Body body = bodies[i];
            bool dropTarget = i == firstEmpty;
            body.Rect.gameObject.SetActive(body.Occupied || dropTarget);
            body.Rect.sizeDelta = dropTarget
                ? new Vector2(area.width, area.height)
                : new Vector2(54f, 54f);
            if (dropTarget)
            {
                body.Rect.anchoredPosition = area.center;
                body.Rect.SetAsFirstSibling();
            }
            else if (body.Occupied) body.Rect.SetAsLastSibling();
        }
    }

    private Rect LocalBounds()
    {
        Rect rect = Interior.rect;
        return new Rect(
            rect.xMin + rect.width * StorageBounds.x,
            rect.yMin + rect.height * StorageBounds.y,
            rect.width * StorageBounds.width,
            rect.height * StorageBounds.height);
    }

    private Vector2 InitialRestPosition(int index)
    {
        Rect bounds = LocalBounds();
        int column = index % 3;
        int row = index / 3;
        return new Vector2(
            Mathf.Lerp(bounds.xMin + 30f, bounds.xMax - 30f, column / 2f),
            bounds.yMin + 25f + row * 48f);
    }

    /// <summary>成功投料后从实际松手位置启动重力；合并到旧堆叠时不搬动原物品。</summary>
    private void OnItemAddedAtPointer(ItemSlot_UI slot, Vector2 screen, Camera camera, bool wasOccupied, float added)
    {
        if (slot.slotIndex < 0 || slot.slotIndex >= bodies.Count ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(Interior, screen, camera, out Vector2 point))
            return;
        Body body = bodies[slot.slotIndex];
        if (wasOccupied)
        {
            Image visual = Instantiate(slot.image, Interior);
            visual.raycastTarget = false;
            visual.gameObject.SetActive(true);
            RectTransform rect = visual.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(50f, 50f);
            body = new Body { Rect = rect, Occupied = true };
            deposits.Add(body);
        }
        Rect bounds = LocalBounds();
        body.Rect.anchoredPosition = new Vector2(
            Mathf.Clamp(point.x, bounds.xMin + 25f, bounds.xMax - 25f),
            Mathf.Clamp(point.y, bounds.yMin + 25f, bounds.yMax - 25f));
        body.Velocity = Vector2.zero;
        body.Spin = UnityEngine.Random.Range(-90f, 90f);
        StartMotion();
    }

    #endregion

    #region 局部物理

    private void StartMotion()
    {
        if (!isActiveAndEnabled) return;
        if (motion != null) StopCoroutine(motion);
        motion = StartCoroutine(SimulateContents());
    }

    private IEnumerable<Body> MovingBodies()
    {
        foreach (Body body in bodies) yield return body;
        foreach (Body body in deposits) yield return body;
    }

    private void ClearDeposits()
    {
        foreach (Body body in deposits)
            if (body.Rect != null)
            {
                body.Rect.gameObject.SetActive(false);
                Destroy(body.Rect.gameObject);
            }
        deposits.Clear();
    }

    /// <summary>桶旋转时由屏幕重力映射到内腔局部坐标；只有运动阶段推进图标。</summary>
    private IEnumerator SimulateContents()
    {
        float quiet = 0f;
        while (quiet < 0.3f)
        {
            float frame = Mathf.Min(Time.unscaledDeltaTime, 0.04f);
            int steps = Mathf.Max(1, Mathf.CeilToInt(frame / 0.008f));
            float dt = frame / steps;
            bool settled = true;
            Rect bounds = LocalBounds();
            Vector2 gravity = Quaternion.Inverse(VesselArt.localRotation) * (Vector3.down * 850f);
            float surface = Mathf.Lerp(bounds.yMin, bounds.yMax, liquidFraction);
            foreach (Body body in MovingBodies())
            {
                if (!body.Occupied || !body.Rect.gameObject.activeSelf) continue;
                Vector2 position = body.Rect.anchoredPosition;
                bool touchingBoundary = false;
                for (int step = 0; step < steps; step++)
                {
                    body.Velocity += gravity * dt;
                    position += body.Velocity * dt;
                    bool submerged = liquidFraction > 0f && position.y < surface;
                    body.Velocity *= Mathf.Exp(-(submerged ? 3f : 0.65f) * dt);
                    if (position.x < bounds.xMin + 24f || position.x > bounds.xMax - 24f)
                    {
                        position.x = Mathf.Clamp(position.x, bounds.xMin + 24f, bounds.xMax - 24f);
                        body.Velocity.x *= -0.45f;
                        touchingBoundary = true;
                    }
                    if (position.y < bounds.yMin + 24f)
                    {
                        position.y = bounds.yMin + 24f;
                        body.Velocity.y *= -0.35f;
                        body.Velocity.x *= Mathf.Exp(-7f * dt);
                        touchingBoundary = true;
                    }
                    if (position.y > bounds.yMax - 24f)
                    {
                        position.y = bounds.yMax - 24f;
                        body.Velocity.y *= -0.35f;
                        touchingBoundary = true;
                    }
                    body.Rect.Rotate(0f, 0f, body.Spin * dt);
                    body.Spin = Mathf.Lerp(body.Spin, -body.Velocity.x * 2f, dt * 12f);
                }
                body.Rect.anchoredPosition = position;
                settled &= touchingBoundary && body.Velocity.sqrMagnitude < 225f;
            }
            settled &= !ResolveBodyCollisions(bounds);
            quiet = settled ? quiet + frame : 0f;
            yield return null;
        }
        foreach (Body body in bodies) { body.Velocity = Vector2.zero; body.Spin = 0f; }
        ClearDeposits();
        motion = null;
    }

    /// <summary>相邻真实槽位分离并交换冲量，使桶内多种物品能分别点选取出。</summary>
    private bool ResolveBodyCollisions(Rect bounds)
    {
        bool moving = false;
        for (int i = 0; i < bodies.Count; i++)
        for (int j = i + 1; j < bodies.Count; j++)
        {
            Body a = bodies[i], b = bodies[j];
            if (!a.Occupied || !b.Occupied || !a.Rect.gameObject.activeSelf || !b.Rect.gameObject.activeSelf) continue;
            Vector2 delta = b.Rect.anchoredPosition - a.Rect.anchoredPosition;
            float distance = delta.magnitude;
            if (distance >= 48f) continue;
            Vector2 normal = distance > 0.001f ? delta / distance : Vector2.right;
            Vector2 correction = normal * ((48f - distance) * 0.5f);
            a.Rect.anchoredPosition = ClampToBounds(a.Rect.anchoredPosition - correction, bounds);
            b.Rect.anchoredPosition = ClampToBounds(b.Rect.anchoredPosition + correction, bounds);
            float approach = Vector2.Dot(b.Velocity - a.Velocity, normal);
            if (approach < 0f)
            {
                a.Velocity += normal * approach * 0.65f;
                b.Velocity -= normal * approach * 0.65f;
            }
            moving |= approach < -15f;
        }
        return moving;
    }

    private static Vector2 ClampToBounds(Vector2 point, Rect bounds) => new(
        Mathf.Clamp(point.x, bounds.xMin + 24f, bounds.xMax - 24f),
        Mathf.Clamp(point.y, bounds.yMin + 24f, bounds.yMax - 24f));

    /// <summary>失活时只停掉运动，库存与槽位内容仍保存在模块数据中。</summary>
    private void OnDisable()
    {
        if (motion != null) StopCoroutine(motion);
        motion = null;
        ClearDeposits();
    }

    private void OnDestroy() => Unbind();

    #endregion
}
