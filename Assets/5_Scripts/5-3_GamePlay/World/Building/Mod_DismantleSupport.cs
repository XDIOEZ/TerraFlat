using System;
using FlatWorld.Networking;
using UnityEngine;

/// <summary>拆平台工具：手持后对准附近空平台使用，复用现有鼠标与移动端统一使用入口。</summary>
public sealed class Mod_DismantleSupport : Module
{
    public Ex_ModData ModData = new(); // 仅包含静态能力。
    public float reach = 2f; // 可操作距离。
    private float nextUseTime; // 防止长按重复提示。
    public override string CanonicalModuleId => "Mod_DismantleSupport";
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData ?? throw new ArgumentException("拆平台工具数据类型错误。");
    }
    /// <summary>绑定统一使用动作。</summary>
    public override void Load() { item.OnAct += Act; nextUseTime = 0f; }
    /// <summary>解除对象池复用前的使用回调。</summary>
    public override void Unload() { if (item != null) item.OnAct -= Act; }
    /// <summary>无独立运行状态。</summary>
    public override void Save() { }
    /// <summary>检查手持、距离和世界权威，再请求一次主动拆除。</summary>
    public override void Act()
    {
        if (!GameNetwork.HasStateAuthority || !item.InHand || item.Owner == null || Time.time < nextUseTime)
            return;
        GameController controller = item.Owner.itemMods.GetMod_ByID<GameController>(ModText.Controller);
        if (controller == null)
            return;
        Vector3 pointer = WorldTopologyRuntime.NormalizePosition(controller.GetMouseWorldPosition());
        Vector2Int cell = new(Mathf.FloorToInt(pointer.x), Mathf.FloorToInt(pointer.y));
        nextUseTime = Time.time + 0.5f;
        if (!FarmlandSystem.IsWithinReach(item.Owner.transform.position, cell, reach))
        {
            ItemActionFeedback.Show(item.Owner, "距离太远了。");
            return;
        }
        if (!TileBuildingSystem.TryDismantleSupport(cell, out string reason))
            ItemActionFeedback.Show(item.Owner, reason);
    }
}
