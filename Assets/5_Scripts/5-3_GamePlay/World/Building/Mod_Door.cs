using System.Collections;
using System.Collections.Generic;
using FlatWorld.Audio;
using UnityEngine;

public class Mod_Door : Module, IInteractable
{
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;

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

    [Tooltip("关闭时用于阻挡移动的碰撞体；门打开时会被禁用。")]
    public BoxCollider2D DoorCollider;

    [Tooltip("专用于交互检测的 Trigger。为空时会在运行时自动创建，不参与物理阻挡。")]
    public BoxCollider2D InteractionCollider;
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
        if (DoorRenderer == null || DoorCollider == null)
            CacheReferences();

        Data.IsOpen = !Data.IsOpen;
        ApplyDoorState();
        Save();
        AudioService.Instance?.PlayAt(
            Data.IsOpen ? AudioEventIds.DoorOpen : AudioEventIds.DoorClose,
            transform.position);
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
            BoxCollider2D[] colliders = GetComponents<BoxCollider2D>();
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null && !colliders[i].isTrigger)
                {
                    DoorCollider = colliders[i];
                    break;
                }
            }

            if (DoorCollider == null && colliders.Length > 0)
                DoorCollider = colliders[0];
        }

        EnsureInteractionCollider();
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

        // Trigger 在部分移动/寻路检测中仍会被视为可碰撞目标，不能作为“打开门”的唯一实现。
        // 阻挡碰撞体彻底关闭；独立的 InteractionCollider 保持 Trigger，以支持从门两侧再次交互。
        DoorCollider.isTrigger = false;
        DoorCollider.enabled = !Data.IsOpen;

        if (InteractionCollider != null)
        {
            InteractionCollider.isTrigger = true;
            InteractionCollider.enabled = true;
        }

        // 让当前物理步之前的 Cast/Overlap 立即看到新的碰撞状态。
        Physics2D.SyncTransforms();
    }

    private void EnsureInteractionCollider()
    {
        if (DoorCollider == null)
            return;

        if (InteractionCollider == DoorCollider)
            InteractionCollider = null;

        if (InteractionCollider == null)
        {
            BoxCollider2D[] colliders = GetComponents<BoxCollider2D>();
            for (int i = 0; i < colliders.Length; i++)
            {
                BoxCollider2D collider = colliders[i];
                if (collider != null && collider != DoorCollider && collider.isTrigger)
                {
                    InteractionCollider = collider;
                    break;
                }
            }
        }

        if (InteractionCollider == null)
        {
            InteractionCollider = gameObject.AddComponent<BoxCollider2D>();
        }

        InteractionCollider.size = DoorCollider.size;
        InteractionCollider.offset = DoorCollider.offset;
        InteractionCollider.isTrigger = true;
    }
    #endregion
}
