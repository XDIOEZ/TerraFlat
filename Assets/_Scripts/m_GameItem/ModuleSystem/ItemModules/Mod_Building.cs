using Force.DeepCloner;
using NUnit.Framework.Interfaces;
using Sirenix.OdinInspector;
using System;
using UltEvents;
using UnityEngine;
using static Mod_Building;

public class Mod_Building : Module
{
    #region 数据定义
    [Serializable]
public class Building_Data
{
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
    public UltEvent StartInstall = new UltEvent();
    public UltEvent StartUnInstall = new UltEvent();
    #endregion

    #region 属性
    public override ModuleData _Data
    {
        get => BuildingData;
        set => BuildingData = (Ex_ModData)value;
    }

    public bool IsItemInInventory => item.Owner != null;
    #endregion

    #region 生命周期

    public override void Load()
    {
        BuildingData.ReadData(ref Data);
        boxCollider2D = item.GetComponent<BoxCollider2D>();

        if (damageReceiver == null)
            damageReceiver = (DamageReceiver)item.itemMods.GetMod_ByID(ModText.Hp);

        damageReceiver.Data.DestroyDelay = -1f;

        damageReceiver.OnAction += OnHit;
        item.OnAct += Install;


    }
    public void Start()
    {
        //根据DamageRecver 设置碰撞是否为触发器
        if (damageReceiver.Hp == 0)
            boxCollider2D.isTrigger = true;
        else
            boxCollider2D.isTrigger = false;
    }

    public override void Save()
    {
        BuildingData.WriteData(Data);
        item.itemData.ModuleDataDic[_Data.Name] = BuildingData;
    }

    public override void ModUpdate(float deltaTime)
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
        //  Debug.Log($"[BaseBuilding] 组件被销毁，清理GhostShadow");

        if (item != null)
            item.OnAct -= Install;
    }
    #endregion

    #region 伤害处理
    private void OnHit(float hp)
    {
        if (hp <= 0)
        {
            UnInstall();
        }
        // Debug.Log("伤害：" + hp);
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

        // === 触发开始事件 ===
        StartInstall?.Invoke();
        OnAction_Start?.Invoke(item);

        // === 消耗物品 ===
        ConsumeItem(item);


        // === 实例化建筑 ===
        Item newBuilding = CreateBuildingInstance(item, GhostShadow.transform.position);

        // === 如果物品耗尽，清理原始对象 ===
        if (item.itemData.Stack.Amount <= 0)
        {
            CleanupGhost();
            Destroy(item.gameObject);
        }

        // === 更新寻路区域 ===
        if (newBuilding != null)
        {
            UpdateNavigation(newBuilding.transform.position, 1, 1);
        }
    }

    [Button]
    public virtual void UnInstall()
    {
        StartUnInstall.Invoke();
        OnAction_Stop.Invoke(item);
        item.transform.localScale *= 0.5f;

        if (boxCollider2D != null)
            boxCollider2D.isTrigger = true;

        if (item.itemData != null)
        {
            item.itemData.Stack.CanBePickedUp = true;
        }

        Vector2 pos = (Vector2)item.transform.position;
        ItemMaker itemMaker = new ItemMaker();
        itemMaker.DropItemWithAnimation(
            item.transform,
            item.transform.position,
            pos + (UnityEngine.Random.insideUnitCircle * 1f),
            item);

        CleanupGhost();


        UpdateNavigation(transform.position, 1, 1);

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

    /// <summary>
    /// 执行建筑物安装流程
    /// </summary>
    private void ExecuteInstallation()
    {
    }

    #region 私有辅助方法

    /// <summary>
    /// 消耗指定物品（减少数量 + 更新UI）
    /// </summary>
    private void ConsumeItem(Item sourceItem)
    {
        sourceItem.itemData.Stack.Amount--;
        sourceItem.itemData.Stack.CanBePickedUp = false;
        sourceItem.OnUIRefresh?.Invoke();
    }
/// <summary>
/// 实例化建筑（根据是否在房间内，选择父对象规则）
/// </summary>
private Item CreateBuildingInstance(Item sourceItem, Vector3 position)
{
    damageReceiver.Hp = damageReceiver.MaxHp.Value;
    sourceItem.ModuleSave();

    ItemData newitemData = FastCloner.FastCloner.DeepClone(sourceItem.itemData);
    
    // 将位置取整然后向右上角偏移0.5个单位，确保安装时总是落在格子中心
    Vector3 gridPosition = new Vector3(
        Mathf.Floor(position.x) + 0.5f,
        Mathf.Floor(position.y) + 0.5f,
        0f
    );
    
    newitemData._transform.Position = gridPosition;
    
    Item newItem = ItemMgr.Instance.InstantiateItem(
            newitemData,
            position: gridPosition  // 确保实例化位置也在格子中心
        );
        
    damageReceiver.Hp = 0;
    newItem.Load();
    newItem.transform.localScale = Vector3.one;
    newItem.itemData.Stack.Amount = 1;
    newItem.itemData.Stack.CanBePickedUp = false;

    EnableChildColliders(true, newItem.transform);
    return newItem;
}

    /// <summary>
    /// 更新导航区域
    /// </summary>
    private void UpdateNavigation(Vector3 center, int length, int width)
    {
        AstarGameManager.Instance.UpdateArea_Rectangle(center, length, width);
    }



    #endregion

    #endregion

    #region 辅助方法
private void HandleGhostShadow()
{
    Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
    mouseWorldPos.z = 0f;

    // 取整到格子并偏移 0.5，让位置落在格子中心
    mouseWorldPos.x = Mathf.Floor(mouseWorldPos.x) + 0.5f;
    mouseWorldPos.y = Mathf.Floor(mouseWorldPos.y) + 0.5f;

    // 创建 Shadow 实例（如果不存在）
    if (GhostShadow == null)
    {
        CreateGhostShadow();
        // 新创建的幽灵阴影直接设置到鼠标位置，避免从(0,0)移动
        if (GhostShadow != null)
        {
            GhostShadow.transform.position = mouseWorldPos;
        }
    }

    if (GhostShadow == null) return;

    // 计算距离
    float distance = Vector2.Distance(item.transform.position, mouseWorldPos);

    // 定义过渡区间（距离超过最大可见距离后，在这个范围内逐渐消失）
    float transitionRange = 1.5f; // 可根据需要调整这个值

    // 计算基础透明度（在有效范围内的正常渐变）
    float baseAlpha = Mathf.InverseLerp(Data.maxVisibleDistance, Data.minVisibleDistance, distance);

    // 计算超出范围后的衰减因子
    float overDistance = distance - Data.maxVisibleDistance;
    float fadeFactor = 1f;

    // 如果超出最大距离，在过渡区间内逐渐降低透明度
    if (overDistance > 0)
    {
        // 超出越多，透明度衰减越多，超过过渡范围后完全透明
        fadeFactor = 1 - Mathf.InverseLerp(0, transitionRange, overDistance);
    }

    // 最终透明度 = 基础透明度 × 衰减因子（确保在0-1范围内）
    float alpha = Mathf.Clamp01(baseAlpha * fadeFactor);

    GhostShadow.UpdateAlpha(alpha);

    // 只有当阴影可见时才执行移动和颜色更新
    if (alpha > 0f)
    {
        if (GhostShadow.ShadowRenderer != null && GhostShadow.ShadowRenderer.enabled)
        {
            // 直接设置位置而不是平滑移动，确保总是对齐到格子中心
            GhostShadow.transform.position = mouseWorldPos;
        }
        else
        {
            Debug.LogWarning("[Shadow生成] ShadowRenderer 未启用");
        }

        GhostShadow.UpdateColor(GhostShadow.AroundHaveGameObject);
    }
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
        foreach (var col in root.GetComponentsInChildren<Collider2D>())
        {
            col.enabled = enable;
        }
        //     root.GetComponent<BoxCollider2D>().isTrigger = false;
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
    // 添加到Mod_Building.cs文件中，放在合适的位置（比如在其他Button方法附近）

#if UNITY_EDITOR
    [Button("设置为已安装状态(编辑器调试)")]
public void SetAsInstalledEditor()
{
    // 检查必要的组件
    if (item == null)
    {

            item = GetComponentInParent<Item>();
    }

    // 查找DamageReceiver组件（如果还没有引用的话）
    if (damageReceiver == null)
    {
        damageReceiver = item.GetComponent<DamageReceiver>();
        if (damageReceiver == null)
        {
            damageReceiver = item.GetComponentInChildren<DamageReceiver>();
        }
        if (damageReceiver == null)
        {
            Debug.LogError("[编辑器调试] 无法找到DamageReceiver组件");
            return;
        }
    }

    // 设置为最大血量（表示已安装）
    damageReceiver.Hp = damageReceiver.MaxHp.Value;
    
    // 设置缩放为1
    item.transform.localScale = Vector3.one;
        item.itemData.Stack.CanBePickedUp = false;
    // 确保碰撞器设置正确
    BoxCollider2D collider = item.GetComponent<BoxCollider2D>();
    if (collider != null)
    {
        collider.isTrigger = false;
    }
    
    // 更新碰撞器状态
    EnableChildColliders(true, item.transform);
    
    Debug.Log($"[编辑器调试] 成功将 {item.name} 设置为已安装状态");
}
#endif
}
