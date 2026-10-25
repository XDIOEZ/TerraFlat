using UnityEngine;

public partial class Mod_InteractSender
{
    #region 光标物品身份查询
    public bool TryGetWorldItemQuickPickTargetAtPointer(Vector2 pointerWorld, out WorldItemQuickPickTarget target)
    {
        target = default;
        if (item == null) return false;
        // 只复用描边的精确落点和同格分区，不能拿朝向附近目标代替鼠标目标。
        IInteractable receiver = FindPreviewReceiverAtPointer(pointerWorld, WorldTopologyRuntime.GetActiveDomain());
        if (receiver == null) return false;
        if (receiver is IWorldInteractionTarget world)
            target = new WorldItemQuickPickTarget(world.ItemDefinitionId);
        else if (receiver is Component component && component != null)
            target = new WorldItemQuickPickTarget(component.GetComponentInParent<Item>()?.itemData?.IDName);
        return target.IsValid;
    }
    #endregion
}
