using Force.DeepCloner;
using NUnit.Framework.Interfaces;
using Sirenix.OdinInspector;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static Mod_Building;

public class Mod_Building : Module
{
    public Building_Data _Data = new Building_Data();
    public Ex_ModData BuildingData;
    public override ModuleData Data { get => BuildingData; set => BuildingData = (Ex_ModData)value; }

    public BuildingShadow GhostShadow;
    public BoxCollider2D boxCollider2D;

    /*
        public float Hp = 100f;
        public GameValue_float MaxHp = new(100f);
        public float maxVisibleDistance = 10f;
        public float minVisibleDistance = 1f;*/

    [Serializable]
    public class Building_Data
    {
        public float Hp = -1f;
        public GameValue_float MaxHp = new(100f);
        public float maxVisibleDistance = 10f;
        public float minVisibleDistance = 1f;
    }
    public bool itemBeTake => item.BelongItem != null;

    public override void Load()
    {
        BuildingData.ReadData(ref _Data);

        boxCollider2D = item.GetComponentInChildren<BoxCollider2D>();
        item.Mods["伤害模块"].OnAction += Hit;
        item.OnAct += Install;
    }

    public void Start()
    {
        if (item == null)
        {
            item = GetComponentInParent<Item>();
        }

    }

    public override void Save()
    {
        BuildingData.WriteData(_Data);
        item.Item_Data.ModuleDataDic[Data.Name] = Data;
    }

    public void Hit(float damage)
    {
        _Data.Hp -= damage;
        if (_Data.Hp <= 0)
        {
            UnInstall();
        }
        Debug.Log("伤害：" + damage);
    }
    [Button]
    public virtual void Install()
    {
        // 1. 检查幽灵投影
        if (GhostShadow == null)
        {
            Debug.LogError($"[建筑安装] 安装失败: 幽灵投影对象不存在 (宿主位置: {item.transform.position})");
            return;
        }

        // 2. 检查周围障碍物
        if (GhostShadow.AroundHaveGameObject)
        {
            string obstacleInfo = GhostShadow.obstacleCollider != null ?
                $"{GhostShadow.obstacleCollider.gameObject.name} (位置: {GhostShadow.obstacleCollider.transform.position})" :
                "未知碰撞体";

            Debug.LogWarning($"[建筑安装] 安装失败: 检测到障碍物 - {obstacleInfo}");
            Debug.DrawLine(item.transform.position, GhostShadow.transform.position, Color.red, 5f);
            return;
        }

        // 3. 检查距离限制
        float distance = Vector2.Distance(item.transform.position, GhostShadow.transform.position);
        if (distance > _Data.maxVisibleDistance)
        {
            Debug.LogWarning($"[建筑安装] 安装失败: 距离超出限制 {distance:F2}m (最大允许: {_Data.maxVisibleDistance:F2}m)");
            Debug.DrawLine(item.transform.position, GhostShadow.transform.position, Color.yellow, 5f);
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

        _Data.Hp = _Data.MaxHp.Value;

        item.Item_Data.Stack.CanBePickedUp = false;

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
            Destroy(item.transform.gameObject);
        }

        Destroy(item.gameObject);
    }

    [Button]
    public virtual void UnInstall()
    {
        item.transform.localScale *= 0.5f;

        if (boxCollider2D != null) boxCollider2D.isTrigger = true;

        if (item.Item_Data != null)
        {
            item.Item_Data.Stack.CanBePickedUp = true;
        }

        _Data.Hp = -1;

        Vector2 pos = (Vector2)item.transform.position;

        ItemMaker itemMaker = new ItemMaker();
        itemMaker.DropItemWithAnimation(
            item.transform,
            item.transform.position,
            pos + (UnityEngine.Random.insideUnitCircle * 1f),
            item);

        CleanupGhost();
    }
    /// <summary>
    /// 安装物体后的初始化设置，包括数据克隆、属性重置、碰撞器设置等。
    /// </summary>
    /// <param name="installed">已实例化的安装物体</param>
    /// <param name="sourceItem">来源物品数据（用于克隆）</param>
    protected virtual void SetupInstalledItem(GameObject installed, Item sourceItem)
    {
        // 安全检查，确保传入参数不为空
        if (installed == null || sourceItem == null) return;

        // 重置安装物体的缩放为默认值（1,1,1）
        installed.transform.localScale = Vector3.one;

        // 尝试获取安装物体上的 Item 组件
        Item installedItem = installed.GetComponentInChildren<Item>();

        if (installedItem != null)
        {
            // 使用深拷贝复制来源物品的数据，避免数据引用冲突
            installedItem.Item_Data = FastCloner.FastCloner.DeepClone(sourceItem.Item_Data);

            // 安装物品堆叠数量默认设为1
            installedItem.Item_Data.Stack.Amount = 1;

            // 启用该物体所有子对象上的碰撞器
            EnableChildColliders(true, installedItem.transform);

            // 将主 BoxCollider2D 的 isTrigger 设为 false，参与物理碰撞
            BoxCollider2D boxCollider = installedItem.GetComponent<BoxCollider2D>();

            if (boxCollider != null) boxCollider.isTrigger = false;

            // 如果该物体实现了建筑接口，标记为“已安装”
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

    public new void OnDestroy()
    {
        base.OnDestroy();
        CleanupGhost();
        Debug.Log($"[BaseBuilding] 组件被销毁，清理GhostShadow");

        // 取消事件订阅
        if (item != null) item.OnAct -= Install;

    }

    public void CleanupGhost()
    {
        if (GhostShadow != null)
        {
            Destroy(GhostShadow.gameObject);
            GhostShadow = null;
        }
    }
    protected virtual void Update()
    {
        // 条件检查
        //bool isPlayerTaken = item.BelongItem != null;//不等于空 表示在人们手上
        bool isNotInstalled = boxCollider2D.isTrigger;//是否是固体? 是固体表示在地上不能再次安装了
        bool isBuildingValid = _Data.Hp > 0f; //是否还能

        if (IsInstalled()==true)
        {
            CleanupGhost();
            return;
        }

        Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mouseWorldPos.z = 0f;

        // 创建 Shadow 实例（如果不存在）
        if (GhostShadow == null)
        {
            GameObject shadowPrefab = null;
            try
            {
                shadowPrefab = GameRes.Instance.InstantiatePrefab("BuildingShadow");
            }
            catch (System.Exception ex)
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

            GhostShadow.transform.position = mouseWorldPos;
        }

        // 计算阴影透明度与位置
        float distance = Vector2.Distance(item.transform.position, mouseWorldPos);
        float alpha = Mathf.InverseLerp(_Data.maxVisibleDistance, _Data.minVisibleDistance, distance);
        alpha = Mathf.Clamp01(alpha);

        if (GhostShadow != null)
        {
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
    }


    public bool IsInstalled()
    {
        //物品所属对象不为空 表示在人们手上
        if(item.BelongItem != null && _Data.Hp <= 0)
        {

            return false;
        }
        else
        {
            return true;
        }
    }
}
