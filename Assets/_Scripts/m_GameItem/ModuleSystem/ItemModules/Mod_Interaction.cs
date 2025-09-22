using System.Collections;
using System.Collections.Generic;
using UltEvents;
using UnityEngine;

/// <summary>
/// 交互模块（Module）
/// - 负责处理与物品的交互逻辑
/// - 遵循 IInteract 接口
/// </summary>
public class Mod_Interaction : Module, IInteract
{
    [Header("模块数据")]
    public Ex_ModData modData;
    public override ModuleData _Data
    {
        get => modData;
        set => modData = (Ex_ModData)value;
    }

    [Header("交互状态")]
    public Item CurrentInteractItem;   // 当前正在交互的物品

    [Header("事件回调")]
    public UltEvent<Item> FastTest;    // 快速测试事件

    // ─────────────────────────────── 生命周期 ───────────────────────────────
    public override void Awake()
    {
        if (string.IsNullOrEmpty(_Data.ID))
        {
            _Data.ID = ModText.Interact;
        }
    }

    public override void Load()
    {
        // TODO: 根据需求加载交互数据
    }

    public override void Save()
    {
        // TODO: 根据需求保存交互数据
    }

    private void FixedUpdate()
    {
        // 当前有交互对象时，阻止重复检测
        if (CurrentInteractItem != null)
        {
            return;
        }
    }

    // ─────────────────────────────── IInteract接口实现 ───────────────────────────────
    public void Interact_Start(IInteracter interacter = null)
    {
        if (item == null) return;

        // 检查物品是否可拾取 → 可拾取则禁止交互
        if (item.itemData.Stack.CanBePickedUp)
        {
            Debug.LogWarning("该物品能被拾取, 已禁止交互。");
            return;
        }

        // 触发事件
        FastTest?.Invoke(interacter.Item);
        OnAction_Start?.Invoke(interacter.Item);

        // 标记交互物品
        CurrentInteractItem = interacter.Item;
    }

    public void Interact_Update(IInteracter interacter = null)
    {
        // TODO: 实现交互过程中的更新逻辑
    }

    public void Interact_Cancel(IInteracter interacter = null)
    {
        if (interacter?.Item == null) return;

        CurrentInteractItem = null;

        // 触发取消事件
        OnAction_Stop?.Invoke(interacter.Item);
    }
}
