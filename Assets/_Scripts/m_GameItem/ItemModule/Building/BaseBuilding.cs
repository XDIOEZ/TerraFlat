using UnityEngine;
using Force.DeepCloner;
using Sirenix.OdinInspector;
using System.Collections.Generic;

[System.Serializable]
public class BaseBuilding : MonoBehaviour
{
    protected Transform hostTransform;
    protected SpriteRenderer hostRenderer;
    protected BoxCollider2D boxCollider2D;
    protected Item item;
    protected Hp Hp;
    protected IHealth health;
    [ShowInInspector]
    public ItemData Item_Data;
    protected BuildingShadow GhostShadow;
    [Tooltip("映射的建筑物")]
    protected IBuilding building;

    [Header("放置检测参数")]
    [Tooltip("允许玩家放置建筑物的最大距离")]
    public float MaxDistance = 2.0f;

    [Tooltip("用于计算透明度的最小可见距离（距离过近时透明度为最大）")]
    [SerializeField] protected float minVisibleDistance = 0.3f;

    [Tooltip("用于计算透明度的最大可见距离（距离超过时几乎不可见）")]
    [SerializeField] protected float maxVisibleDistance = 3.5f;

    public virtual void Awake()
    {
        hostRenderer = GetComponentInChildren<SpriteRenderer>();
        boxCollider2D = GetComponent<BoxCollider2D>();
        item = GetComponent<Item>();
        building = GetComponent<IBuilding>();
        health = GetComponent<IHealth>();
    }
    protected virtual void Start()
    {
        hostTransform = transform;
       

        if (item != null)
        {
            Item_Data = item.Item_Data;
        }

     
        if (health != null)
        {
            Hp = health.Hp;
        }

        // 根据安装状态初始化
        if (building != null && building.IsInstalled && boxCollider2D != null && boxCollider2D.isTrigger)
        {
            SetupInstalledItem(gameObject, item);
        }

        // 根据血量判断是否安装
        if (Hp != null && Hp.Value > 0)
        {
            if (boxCollider2D != null) boxCollider2D.isTrigger = false;
            if (building != null) building.IsInstalled = true;
            EnableChildColliders(true);
        }
        else
        {
            if (boxCollider2D != null) boxCollider2D.isTrigger = true;
            if (building != null) building.IsInstalled = false;
        }

        item.OnAct += Install;
        health.OnDeath += UnInstall;
    }

    private void OnDestroy()
    {
        CleanupGhost();
        Debug.Log($"[BaseBuilding] 组件被销毁，清理GhostShadow");

        // 取消事件订阅
        if (item != null) item.OnAct -= Install;
        if (health != null) health.OnDeath -= UnInstall;
    }

    protected virtual void Update()
    {
        // 详细检查每个条件
        bool isBuildingValid = building != null;
        bool isPlayerTaken = isBuildingValid && building.BePlayerTaken;
        bool isNotInstalled = isBuildingValid && !building.IsInstalled;
        bool hasItemData = Item_Data != null;
        bool cannotBePickedUp = hasItemData && !Item_Data.Stack.CanBePickedUp;

        bool canPlace = isBuildingValid && isPlayerTaken && isNotInstalled && hasItemData && cannotBePickedUp;

        // 调试输出
        if (!canPlace)
        {
            string debugMsg = "[Shadow生成] 条件不满足: ";
            if (!isBuildingValid) debugMsg += "建筑接口无效; ";
            if (!isPlayerTaken) debugMsg += "未被玩家持有; ";
            if (!isNotInstalled) debugMsg += "已安装; ";
            if (!hasItemData) debugMsg += "物品数据缺失; ";
            if (!cannotBePickedUp) debugMsg += "可被拾取; ";

           // Debug.Log(debugMsg);
            CleanupGhost();
            return;
        }

        // 获取鼠标世界位置
        Vector3 mouseWorldPos;
        if (Camera.main == null)
        {
            Debug.LogError("[Shadow生成] 主相机缺失");
            return;
        }
        else
        {
            mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            mouseWorldPos.z = 0f;
        }

        // 确保GhostShadow存在
        if (GhostShadow == null)
        {
            Debug.Log("[Shadow生成] 尝试创建新Shadow实例");

            GameObject shadowPrefab = null;
            try
            {
                shadowPrefab = GameRes.Instance.InstantiatePrefab("BuildingShadow");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[Shadow生成] 实例化预制体失败: {ex.Message}");
            }

            if (shadowPrefab == null)
            {
                Debug.LogError("[Shadow生成] 无法实例化BuildingShadow预制体");
                return;
            }

            GhostShadow = shadowPrefab.GetComponent<BuildingShadow>();

            if (GhostShadow == null)
            {
                Debug.LogError("[Shadow生成] BuildingShadow预制体缺少BuildingShadow组件");
                Destroy(shadowPrefab);
                return;
            }

            Debug.Log($"[Shadow生成] Shadow实例创建成功: {GhostShadow.name}");

            // 初始化阴影
            if (hostRenderer != null)
            {
                GhostShadow.InitShadow(hostRenderer);
                Debug.Log($"[Shadow生成] Shadow初始化完成 (精灵: {hostRenderer.sprite?.name})");
            }
            else
            {
                Debug.LogError("[Shadow生成] hostRenderer为空，无法初始化阴影");
            }

            GhostShadow.transform.position = mouseWorldPos;
        }

        // 计算与鼠标的距离
        float distance = Vector2.Distance(hostTransform.position, mouseWorldPos);

        // 计算透明度（距离越近越透明）
        float alpha = Mathf.InverseLerp(maxVisibleDistance, minVisibleDistance, distance);
        alpha = Mathf.Clamp01(alpha);

        // 更新阴影透明度
        if (GhostShadow != null)
        {
            GhostShadow.UpdateAlpha(alpha);

            // 移动阴影到鼠标位置
            if (GhostShadow.ShadowRenderer != null && GhostShadow.ShadowRenderer.enabled)
            {
                GhostShadow.SmoothMove(mouseWorldPos);
            }
            else
            {
                Debug.LogWarning($"[Shadow生成] ShadowRenderer未启用 (状态: {GhostShadow.ShadowRenderer?.enabled})");
            }

            // 更新阴影颜色（根据是否有障碍物）
            GhostShadow.UpdateColor(GhostShadow.AroundHaveGameObject);
        }
    }

    public virtual void Install()
    {
        // 1. 检查幽灵投影
        if (GhostShadow == null)
        {
            Debug.LogError($"[建筑安装] 安装失败: 幽灵投影对象不存在 (宿主位置: {hostTransform.position})");
            return;
        }

        // 2. 检查周围障碍物
        if (GhostShadow.AroundHaveGameObject)
        {
            string obstacleInfo = GhostShadow.obstacleCollider != null ?
                $"{GhostShadow.obstacleCollider.gameObject.name} (位置: {GhostShadow.obstacleCollider.transform.position})" :
                "未知碰撞体";

            Debug.LogWarning($"[建筑安装] 安装失败: 检测到障碍物 - {obstacleInfo}");
            Debug.DrawLine(hostTransform.position, GhostShadow.transform.position, Color.red, 5f);
            return;
        }

        // 3. 检查距离限制
        float distance = Vector2.Distance(hostTransform.position, GhostShadow.transform.position);
        if (distance > MaxDistance)
        {
            Debug.LogWarning($"[建筑安装] 安装失败: 距离超出限制 {distance:F2}m (最大允许: {MaxDistance:F2}m)");
            Debug.DrawLine(hostTransform.position, GhostShadow.transform.position, Color.yellow, 5f);
            return;
        }

        // 4. 检查物品组件
        if (item == null)
        {
            Debug.LogError("[建筑安装] 安装失败: 物品组件未分配");
            return;
        }

        // 5. 检查物品数量
        if (item.Item_Data.Stack.Amount <= 0)
        {
            Debug.LogError($"[建筑安装] 安装失败: 物品数量不足 (当前: {item.Item_Data.Stack.Amount})");
            return;
        }

        // 正式安装流程
        item.Item_Data.Stack.Amount--;

        if (item.UpdatedUI_Event != null)
        {
            item.UpdatedUI_Event.Invoke();
        }

        // 实例化建筑
        var runtimeItem = RunTimeItemManager.Instance.InstantiateItem(
            item.Item_Data.IDName,
            GhostShadow.transform.position
        );

        if (runtimeItem == null || runtimeItem.gameObject == null)
        {
            Debug.LogError("[建筑安装] 安装失败: 实例化返回空对象");
            // 回滚物品数量
            item.Item_Data.Stack.Amount++;
            return;
        }

        SetupInstalledItem(runtimeItem.gameObject, item);

        // 处理物品耗尽情况
        if (item.Item_Data.Stack.Amount <= 0)
        {
            CleanupGhost();
            Destroy(hostTransform.gameObject);
        }

        Destroy(item.gameObject);
    }


    /*    private void SetupInstalledItem(GameObject newItem, ItemBase sourceItem)
        {
            Debug.Log($"[建筑安装] 正在配置新建筑 {newItem.name}...");

            // 这里可以添加具体的配置逻辑
            // 例如：newItem.GetComponent<Building>().Initialize(sourceItem);

            Debug.Log("[建筑安装] 建筑配置完成");
        }*/

    public virtual void UnInstall()
    {
        hostTransform.localScale *= 0.2f;
        if (boxCollider2D != null) boxCollider2D.isTrigger = true;

        if (Item_Data != null)
        {
            Item_Data.Stack.CanBePickedUp = true;
            Item_Data.Durability -= 1;
        }

        if (Hp != null) Hp.Value = -1;

        if (building != null) building.IsInstalled = false;

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
            Destroy(GhostShadow.gameObject);
            GhostShadow = null;
        }
    }

    protected virtual void SetupInstalledItem(GameObject installed, Item sourceItem)
    {
        if (installed == null || sourceItem == null) return;

        installed.transform.localScale = Vector3.one;

        Item installedItem = installed.GetComponent<Item>();
        if (installedItem != null)
        {
            installedItem.Item_Data = sourceItem.Item_Data.DeepClone();
            installedItem.Item_Data.Stack.Amount = 1;

            var health = installedItem.GetComponent<IHealth>();
            if (health != null)
            {
                health.Hp.Value = health.Hp.maxValue;
            }

            EnableChildColliders(true, installedItem.transform);

            BoxCollider2D boxCollider = installedItem.GetComponent<BoxCollider2D>();
            if (boxCollider != null) boxCollider.isTrigger = false;

            var buildingComp = installed.GetComponent<IBuilding>();
            if (buildingComp != null)
            {
                buildingComp.IsInstalled = true;
            }
        }
    }

    protected void EnableChildColliders(bool enable, Transform root = null)
    {
        root = root ?? transform;
        foreach (var col in root.GetComponentsInChildren<Collider2D>())
        {
            col.enabled = enable;
        }
    }
}