using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using Force.DeepCloner;
using GameObject = UnityEngine.GameObject;

public class BuildingShadow : MonoBehaviour
{
    public List<GameObject> AroundObjects = new List<GameObject>();
    public SpriteRenderer ShadowRenderer;
    public Color ShadowColor = new Color(1f, 1f, 1f, 0.7f);
    public Color WarringColor = Color.red;

    private Tween moveTween;
    private Tween colorTween;

    // 新增字段记录tween 
    private Tween alphaTween;

    public bool AroundHaveGameObject => AroundObjects.Count > 0;
    public void OnTriggerEnter2D(Collider2D collision)
    {
        // 只处理非Trigger对象
        if (!collision.isTrigger)
        {
            AroundObjects.Add(collision.gameObject);
        }
    }

    public void OnTriggerExit2D(Collider2D collision)
    {
        if (!collision.isTrigger)
        {
            AroundObjects.Remove(collision.gameObject);
        }
    }

    public void OnDestroy()
    {
        AroundObjects.Clear();
        // 清理所有DOTween的tween 
        DOTween.Kill(transform);
        DOTween.Kill(ShadowRenderer);
        // 安全销毁记录的tween 
        moveTween?.Kill();
        colorTween?.Kill();
        alphaTween?.Kill();
    }

    // 封装颜色变更逻辑 
    public void UpdateColor(bool hasOverlap)
    {
        Color targetColor = hasOverlap ? WarringColor : ShadowColor;
        if (ShadowRenderer != null)
        {
            colorTween?.Kill();
            colorTween = DOTween.To(() => ShadowRenderer.color, c => ShadowRenderer.color = c, targetColor, 0.2f);
        }
    }

    // 控制 BoxCollider2D 的缩放系数
    public Vector2 BoxColliderScale = Vector2.one;

    public void InitShadow(SpriteRenderer newRenderer)
    {
        if (newRenderer == null || ShadowRenderer == null) return;

        ShadowRenderer.sprite = newRenderer.sprite;
        ShadowRenderer.sortingOrder = newRenderer.sortingOrder - 1;
        ShadowRenderer.color = ShadowColor;

        Bounds bounds = newRenderer.sprite.bounds;

        BoxCollider2D boxCollider = GetComponent<BoxCollider2D>();
        if (boxCollider != null)
        {
            boxCollider.size = Vector2.Scale(bounds.size, BoxColliderScale);
            boxCollider.offset = bounds.center;
        }
    
}



// 封装透明度变化 
public void UpdateAlpha(float alpha)
    {
        if (ShadowRenderer != null)
        {
            Color color = ShadowRenderer.color;
            color.a = alpha * 0.5f; // 最大 0.5 
            ShadowRenderer.color = color;
            ShadowRenderer.enabled = alpha > 0f;
        }
    }

    // 封装移动逻辑 
    public void SmoothMove(Vector3 targetPosition)
    {
        moveTween?.Kill();
        moveTween = transform.DOMove(targetPosition, 0.1f).SetEase(Ease.OutQuad);
    }
}

[System.Serializable]
public class Building_InstallAndUninstall
{
    private Transform hostTransform;
    private SpriteRenderer hostRenderer;
    private BoxCollider2D boxCollider2D;
    private Item item;
    private Hp Hp;
    private ItemData Item_Data;
    private BuildingShadow GhostShadow;
    [Tooltip("映射的建筑物")]
    IBuilding building;


    [Header("放置检测参数")]

    [Tooltip("允许玩家放置建筑物的最大距离")]
    public float MaxDistance = 2.0f;

    [Tooltip("用于计算透明度的最小可见距离（距离过近时透明度为最大）")]
    [SerializeField] private float minVisibleDistance = 0.3f;

    [Tooltip("用于计算透明度的最大可见距离（距离超过时几乎不可见）")]
    [SerializeField] private float maxVisibleDistance = 3.5f;

    public void Update()
    {
        // 未安装状态下，不处理
        if (building.BePlayerTaken == true && building.IsInstalled == false && Item_Data.Stack.CanBePickedUp == false)
        {
            Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            mouseWorldPos.z = 0f;

            if (GhostShadow == null)
            {
                GhostShadow = GameRes.Instance.InstantiatePrefab("BuildingShadow").GetComponent<BuildingShadow>();
                GhostShadow.InitShadow(hostRenderer);
                // 👇 影子初始化时立即设置在鼠标位置
                GhostShadow.transform.position = mouseWorldPos;
            }

            float distance = Vector2.Distance(hostTransform.position, mouseWorldPos);
            float alpha = Mathf.InverseLerp(maxVisibleDistance, minVisibleDistance, distance);
            alpha = Mathf.Clamp01(alpha);

            GhostShadow.UpdateAlpha(alpha);

            if (GhostShadow.ShadowRenderer.enabled)
            {
                GhostShadow.SmoothMove(mouseWorldPos);
            }

            GhostShadow.UpdateColor(GhostShadow.AroundHaveGameObject);
        }
        else
        {
            CleanupGhost();
        }
    }

    public void Init(Transform owner)
    {
        hostTransform = owner.transform;
        hostRenderer = owner.GetComponentInChildren<SpriteRenderer>();
        boxCollider2D = owner.GetComponent<BoxCollider2D>();
        item = owner.GetComponent<Item>();
        Hp = owner.GetComponent<IHealth>().Hp;
        Item_Data = item?.Item_Data;
        building = owner.GetComponent<IBuilding>();

        if (building.IsInstalled == true && owner.GetComponent<BoxCollider2D>().isTrigger == true)
        {
            SetupInstalledItem(owner.gameObject, item);
        }

        //TODO 根据血量判断是否安装
        if (Hp.Value > 0)
        {
            boxCollider2D.isTrigger = false;
            building.IsInstalled = true;
            // 激活子对象上的所有碰撞体（传送门等）
            foreach (var col in owner.GetComponentsInChildren<Collider2D>())
            {
                col.enabled = true;
            }
        }
        else
        {
            boxCollider2D.isTrigger = true;
            building.IsInstalled = false;
        }
    }
    public void Install()
    {
        // 检查 GhostShadow 是否为空 或者 附近有其他物品阻挡
        if (GhostShadow == null || GhostShadow.AroundHaveGameObject)
        {
            Debug.Log("附近有物品，无法安装。");
            return;
        }

        // 检查与目标位置的距离是否超出允许范围
        float distance = Vector2.Distance(hostTransform.position, GhostShadow.transform.position);
        if (distance > MaxDistance)
        {
            Debug.Log("超出最大放置距离，无法安装。");
            return;
        }

        // 检查当前是否有可用的 item
        if (item == null)
        {
            Debug.Log("没有可安装的物品。");
            return;
        }

        // 减少物品数量并更新 UI
        item.Item_Data.Stack.Amount--;
        item.UpdatedUI_Event?.Invoke();

        // 实例化预制体
        GameObject installed = RunTimeItemManager.Instance.InstantiateItem(item.Item_Data.IDName, GhostShadow.transform.position).gameObject;

        // 将安装相关的所有逻辑都转移到 SetupInstalledItem
        SetupInstalledItem(installed, item);

        if (item.Item_Data.Stack.Amount <= 0)
        {
            CleanupGhost();
            GameObject.Destroy(hostTransform.gameObject);
        }
    }

    public void SetupInstalledItem(GameObject installed, Item sourceItem)
    {
        if (installed == null || sourceItem == null)
        {
            Debug.LogWarning("安装失败：目标对象或源物品为空。");
            return;
        }

        // 设置缩放
        installed.transform.localScale = Vector3.one;

        // 设置 Item 数据（提前克隆）
        Item installedItem = installed.GetComponent<Item>();
        if (installedItem != null)
        {
            // 深拷贝数据
            installedItem.Item_Data = sourceItem.Item_Data.DeepClone();

            installedItem.Item_Data.Stack.Amount = 1;
            // 重置血量
            var Hp_ = installedItem.GetComponent<IHealth>().Hp;
            Hp_.Value = Hp_.maxValue;

            // 激活子对象上的所有碰撞体（传送门等）
            foreach (var col in installedItem.GetComponentsInChildren<Collider2D>())
            {
                col.enabled = true;
            }

            // 配置本体碰撞体
            BoxCollider2D boxCollider = installedItem.GetComponent<BoxCollider2D>();
            boxCollider.isTrigger = false;

            // 设置建筑安装状态（如果存在 IBuilding 接口）
            var building = installed.GetComponent<IBuilding>();
            if (building != null)
            {
                building.IsInstalled = true;
            }
        }

       
    }



    public void UnInstall()
    {
        hostTransform.localScale *= 0.2f;
        boxCollider2D.isTrigger = true;
        Item_Data.Stack.CanBePickedUp = true;
        Hp.Value = -1;
        Item_Data.Durability -= 1;

        var building = item.GetComponent<IBuilding>();
        if (building != null)
        {
            building.IsInstalled = false;
        }

        Vector2 pos = (Vector2)hostTransform.position;
        ItemMaker itemMaker = new ItemMaker();

        itemMaker.DropItemWithAnimation(
            hostTransform,
            hostTransform.position,
            pos + (Random.insideUnitCircle * 1f),
            item);

        CleanupGhost();
    }

    public void CleanupGhost()
    {
        if (GhostShadow != null)
        {
            GameObject.Destroy(GhostShadow.gameObject);
            GhostShadow = null;
        }
    }
}
