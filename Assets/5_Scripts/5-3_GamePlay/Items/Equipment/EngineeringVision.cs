using UnityEngine;

/// <summary>工程视野只读手持与头部装备能力，不影响隐藏线路的电力模拟。</summary>
public static class EngineeringVision
{
    #region 工程视野资格
    public const string CapabilityTag = "EngineeringVision";
    private static int sampledFrame = -1;
    private static bool localCanSee;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        sampledFrame = -1;
        localCanSee = false;
    }

    public static bool LocalCanSee
    {
        get
        {
            if (sampledFrame != Time.frameCount)
            {
                sampledFrame = Time.frameCount;
                localCanSee = CanSee(ItemMgr.GetInstance()?.User_Player);
            }
            return localCanSee;
        }
    }

    public static bool HasCapability(ItemData held, ItemData head, ItemData carried = null)
        => Active(held) || Active(head) || Active(carried);

    private static bool Active(ItemData data)
        => data != null && data.Stack?.Amount > 0f && data.Durability > 0f &&
           data.Tags?.Contains(CapabilityTag) == true;

    public static bool CanSee(Item actor)
    {
        if (actor == null || actor.itemMods == null) return false;
        var hotbar = actor.itemMods.GetMod_ByID<Mod_HotBar>(ModText.Hotbar);
        var equipment = actor.itemMods.GetMod_ByID<Mod_Equipment>(ModText.Equipment_Module);
        var hand = actor.itemMods.GetMod_ByID<Mod_Hand>(ModText.Hand)?.HandInventory?.Data?.GetItemSlot(0)?.itemData;
        return HasCapability(hotbar?.CurrentSelectItemSlot?.itemData,
            equipment?.GetEquippedItem(EquipmentSlotCatalog.Head), hand);
    }
    #endregion
}
