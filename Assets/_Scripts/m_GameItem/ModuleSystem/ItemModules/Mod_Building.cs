using Force.DeepCloner;
using Sirenix.OdinInspector;
using System;
using UnityEngine;
using static Mod_Building;

public class Mod_Building : Module
{
    #region 数据定义
    [Serializable]
    public class Building_Data
    {
        public float Hp = -1f;
        public GameValue_float MaxHp = new(100f);
        public float maxVisibleDistance = 10f;
        public float minVisibleDistance = 1f;
    }
    #endregion

    #region 公共字段
    public Building_Data Data = new Building_Data();
    public Ex_ModData BuildingData;
    public BuildingShadow GhostShadow;
    public BoxCollider2D boxCollider2D;
    public DamageReceiver damageReceiver;
    #endregion

    #region 属性
    public override ModuleData _Data
    {
        get => BuildingData;
        set => BuildingData = (Ex_ModData)value;
    }

    public bool IsItemInInventory => item.BelongItem != null;
    #endregion

    #region 生命周期
    public override void Load()
    {
        BuildingData.ReadData(ref Data);
        boxCollider2D = item.GetComponentInChildren<BoxCollider2D>();
        damageReceiver = (DamageReceiver)item.itemMods.GetMod_ByID(ModText.Hp);
        damageReceiver.OnAction += OnHit;
        item.OnAct += Install;
    }

    public override void Save()
    {
        BuildingData.WriteData( Data);
        item.itemData.ModuleDataDic[_Data.Name] = BuildingData;
    }

    public override void Action(float deltaTime)
    {
        if (item == null)
        {
            Debug.LogWarning("item 尚未初始化！");
            return;
        }
        // 只有在玩家手上时才显示幽灵投影
        if (IsItemInInventory)
        {
            // 如果已经安装完成，清理幽灵投影
            if (IsInstalled())
            {
                CleanupGhost();
                return;
            }

            // 如果还未安装，继续显示幽灵投影
            HandleGhostShadow();
        }
        else
        {
            // 不在玩家手上时清理幽灵投影
            CleanupGhost();
        }
    }

    public void OnDestroy()
    {
        CleanupGhost();
        Debug.Log($"[BaseBuilding] 组件被销毁，清理GhostShadow");

        if (item != null)
            item.OnAct -= Install;
    }
    #endregion

    #region 伤害处理
    private void OnHit(float hp)
    {
        Data.Hp = hp;
        if (hp <= 0)
        {
            UnInstall();
        }
        Debug.Log("伤害：" + hp);
    }
    #endregion

    #region 建筑安装/卸载
    [Button]
    public virtual void Install()
    {
        // 只有在玩家手上时才能安装
        if (!IsItemInInventory)
        {
            Debug.LogWarning($"[建筑安装] 安装失败: 物品不在玩家手上");
            return;
        }

        if (!CanInstall())
            return;

        ExecuteInstallation();
    }

    [Button]
    public virtual void UnInstall()
    {
        item.transform.localScale *= 0.5f;

        if (boxCollider2D != null)
            boxCollider2D.isTrigger = true;

        if (item.itemData != null)
        {
            item.itemData.Stack.CanBePickedUp = true;
        }

        Data.Hp = 0;

        Vector2 pos = (Vector2)item.transform.position;
        ItemMaker itemMaker = new ItemMaker();
        itemMaker.DropItemWithAnimation(
            item.transform,
            item.transform.position,
            pos + (UnityEngine.Random.insideUnitCircle * 1f),
            item);

        CleanupGhost();
    }
    #endregion

    #region 安装验证
    private bool CanInstall()
    {
        // 1. 检查幽灵投影
        if (GhostShadow == null)
        {
            Debug.LogError($"[建筑安装] 安装失败: 幽灵投影对象不存在 (宿主位置: {item.transform.position})");
            return false;
        }

        // 2. 检查周围障碍物
        if (GhostShadow.AroundHaveGameObject)
        {
            string obstacleInfo = GhostShadow.obstacleCollider != null ?
                $"{GhostShadow.obstacleCollider.gameObject.name} (位置: {GhostShadow.obstacleCollider.transform.position})" :
                "未知碰撞体";

            Debug.LogWarning($"[建筑安装] 安装失败: 检测到障碍物 - {obstacleInfo}");
            Debug.DrawLine(item.transform.position, GhostShadow.transform.position, Color.red, 5f);
            return false;
        }

        // 3. 检查距离限制
        float distance = Vector2.Distance(item.transform.position, GhostShadow.transform.position);
        if (distance > Data.maxVisibleDistance)
        {
            Debug.LogWarning($"[建筑安装] 安装失败: 距离超出限制 {distance:F2}m (最大允许: {Data.maxVisibleDistance:F2}m)");
            Debug.DrawLine(item.transform.position, GhostShadow.transform.position, Color.yellow, 5f);
            return false;
        }

        // 4. 检查物品数量
        if (item.itemData.Stack.Amount <= 0)
        {
            Debug.LogError($"[建筑安装] 安装失败: 物品数量不足 (当前: {item.itemData.Stack.Amount})");
            return false;
        }

        return true;
    }

    private void ExecuteInstallation()
    {
        // 消耗物品
        item.itemData.Stack.Amount--;

        // 设置血量
        Data.Hp = Data.MaxHp.Value;
        damageReceiver.Hp = damageReceiver.MaxHp.Value;

        // 更新物品状态
        item.itemData.Stack.CanBePickedUp = false;
        item.OnUIRefresh?.Invoke();

        // 实例化建筑
        var runtimeItem = GameItemManager.Instance.InstantiateItem(
            item.itemData.IDName,
            GhostShadow.transform.position
        );

        if (runtimeItem != null)
        {
            // 配置新实例
            runtimeItem.transform.localScale = Vector3.one;
            runtimeItem.itemData = FastCloner.FastCloner.DeepClone(runtimeItem.itemData);
            runtimeItem.itemData.Stack.Amount = 1;
            runtimeItem.itemData.Stack.CanBePickedUp = false;
            EnableChildColliders(true, runtimeItem.transform);
        }

        // 处理物品耗尽
        if (item.itemData.Stack.Amount <= 0)
        {
            CleanupGhost();
            Destroy(item.transform.gameObject);
        }
    }
    #endregion

    #region 辅助方法
    private void HandleGhostShadow()
    {
        Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mouseWorldPos.z = 0f;

        // 创建 Shadow 实例（如果不存在）
        if (GhostShadow == null)
        {
            CreateGhostShadow();
        }

        if (GhostShadow == null) return;

        // 计算阴影透明度与位置
        float distance = Vector2.Distance(item.transform.position, mouseWorldPos);
        float alpha = Mathf.InverseLerp(Data.maxVisibleDistance, Data.minVisibleDistance, distance);
        alpha = Mathf.Clamp01(alpha);

        GhostShadow.UpdateAlpha(alpha);

        if (GhostShadow.ShadowRenderer != null && GhostShadow.ShadowRenderer.enabled)
        {
            GhostShadow.SmoothMove(mouseWorldPos);
        }
        else
        {
            Debug.LogWarning("[Shadow生成] ShadowRenderer 未启用");
        }

        GhostShadow.UpdateColor(GhostShadow.AroundHaveGameObject);
    }

    private void CreateGhostShadow()
    {
        GameObject shadowPrefab = null;
        try
        {
            shadowPrefab = GameRes.Instance.InstantiatePrefab("BuildingShadow");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[Shadow生成] 实例化预制体失败: {ex.Message}");
            return;
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

        if (item.Sprite != null)
        {
            GhostShadow.InitShadow(item.Sprite);
        }
        else
        {
            Debug.LogError("[Shadow生成] hostRenderer为空，无法初始化阴影");
        }
    }

    protected void EnableChildColliders(bool enable, Transform root = null)
    {
        root = root ?? transform;
        foreach (var col in root.GetComponentsInChildren<Collider2D>())
        {
            col.enabled = enable;
        }
        root.GetComponent<BoxCollider2D>().isTrigger = false;
    }

    public void CleanupGhost()
    {
        if (GhostShadow != null)
        {
            Destroy(GhostShadow.gameObject);
            GhostShadow = null;
        }
    }

    public bool IsInstalled()
    {
        // 当血量大于0时，表示建筑已经成功安装
        return damageReceiver.Hp > 0;
    }
    #endregion
}