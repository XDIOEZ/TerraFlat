using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Mod_Door : Module, IInteractable
{
    #region 数据定义
    [System.Serializable]
    public class DoorStateData
    {
        public bool IsOpen;
    }

    private const string DoorModId = "Door";

    public Ex_ModData DoorData;
    public override ModuleData _Data
    {
        get => DoorData;
        set => DoorData = (Ex_ModData)value;
    }

    public DoorStateData Data = new DoorStateData();
    #endregion

    #region 门配置
    [Header("门贴图")]
    public Sprite CloseSprite;
    public Sprite OpenSprite;
    #endregion

    #region 运行时缓存
    public SpriteRenderer DoorRenderer;
    public BoxCollider2D DoorCollider;
    #endregion

    #region 生命周期
    public override void Awake()
    {
        EnsureData();
        _Data.ID = DoorModId;
    }

    private void OnValidate()
    {
        EnsureData();
        _Data.ID = DoorModId;
    }

    public override void Load()
    {
        EnsureData();
        CacheReferences();

        DoorData.ReadData(ref Data);
        ApplyDoorState();
    }

    public override void Save()
    {
        EnsureData();
        DoorData.WriteData(Data);

        if (item == null || item.itemData == null)
        {
            Debug.LogError($"[Mod_Door] Save 失败：item 或 itemData 为空，物体：{name}");
            return;
        }

        item.itemData.ModuleDataDic[_Data.Name] = DoorData;
    }

    public override void ModUpdate(float deltaTime)
    {
    }
    #endregion

    #region 交互接口
    public void OnInteractStart(Item playerItem)
    {
        Data.IsOpen = !Data.IsOpen;
        ApplyDoorState();
    }

    public void OnInteractCancel(Item playerItem)
    {
    }
    #endregion

    #region 内部方法
    private void EnsureData()
    {
        if (DoorData == null)
        {
            DoorData = new Ex_ModData();
        }
    }

    private void CacheReferences()
    {
        if (DoorRenderer == null)
        {
            DoorRenderer = GetComponent<SpriteRenderer>();
        }

        if (DoorCollider == null)
        {
            DoorCollider = GetComponent<BoxCollider2D>();
        }
    }

    private void ApplyDoorState()
    {
        if (DoorRenderer == null)
        {
            Debug.LogError($"[Mod_Door] 缺少 SpriteRenderer，无法更新门贴图，物体：{name}");
            return;
        }

        if (DoorCollider == null)
        {
            Debug.LogError($"[Mod_Door] 缺少 BoxCollider2D，无法更新门碰撞状态，物体：{name}");
            return;
        }

        DoorRenderer.sprite = Data.IsOpen ? OpenSprite : CloseSprite;
        DoorCollider.isTrigger = Data.IsOpen;
    }
    #endregion
}
