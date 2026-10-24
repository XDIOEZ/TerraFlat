using System;
using System.Collections.Generic;

internal static class Program
{
    private static int assertions;
    private static int nextGuid;
    private static void Check(bool value, string label)
    {
        assertions++;
        if (!value) throw new Exception(label);
    }
    private static ItemData Item(float amount = 5f, float durability = 7f, float maximum = 20f)
        => new Data_GeneralItem { IDName = "Stick_Wood", Guid = ++nextGuid,
            Durability = durability, MaxDurability = maximum, Stack = new ItemStack { Amount = amount },
            Tags = new List<string> { "Wood" } };
    private static Inventory_Data Inventory(params ItemData[] items)
        => new(items.Select((item, index) => new ItemSlot(index) { itemData = item }).ToList(), "test");
    private static float TotalWear(Inventory_Data inventory) => inventory.itemSlots.Sum(slot =>
        slot.itemData == null ? 0f : slot.itemData.MaxDurability - slot.itemData.Durability);

    private static void Main()
    {
        TestWear();
        TestTransfers();
        TestAddAndSort();
        TestRejections();
        Console.WriteLine($"PASS: {assertions} assertions against production durability and inventory sources.");
    }

    private static void TestWear()
    {
        foreach (float amount in new[] { 1f, 2f, 5f })
        foreach (float wear in new[] { .5f, 1f, 100f })
        {
            ItemData item = Item(amount, 1);
            Inventory_Data inventory = Inventory(item);
            int changed = 0, refreshed = 0;
            inventory.Event_OnDataChanged += _ => changed++;
            inventory.Event_RefreshUI += _ => refreshed++;
            Check(inventory.TryConsumeDurability(item, wear, out bool broke), "valid wear rejected");
            Check(broke == (wear >= 1), "break boundary");
            Check(item.Stack.Amount == (broke ? amount - 1 : amount), "only one unit can break");
            Check(item.Durability == (!broke ? 1 - wear : amount > 1 ? 20 : 0), "next unit durability");
            Check((inventory.itemSlots[0].itemData == null) == (broke && amount == 1), "last unit slot cleanup");
            Check(changed == 1 && refreshed == 1, "publish final state exactly once");
            if (amount == 1 && broke)
                Check(!inventory.TryConsumeDurability(item, 1, out _) && changed == 1, "late duplicate consumption");
        }
    }

    private static void TestTransfers()
    {
        foreach (bool hand in new[] { false, true })
        foreach (int count in new[] { 1, 2, 5 })
        foreach (bool existing in new[] { false, true })
        {
            ItemData source = Item();
            Inventory_Data from = Inventory(source), to = Inventory(existing ? Item(2, 20) : null);
            bool transferred = hand ? from.ChangeItemAmount(from.itemSlots[0], to.itemSlots[0], count)
                : from.TransferItemQuantityTo(from.itemSlots[0], to, to.itemSlots[0], count);
            Check(transferred, "transfer rejected");
            Check(to.itemSlots[0].itemData.Durability == 7, "top wear must travel");
            Check(to.itemSlots[0].itemData.Stack.Amount == count + (existing ? 2 : 0), "target count");
            Check((from.itemSlots[0].itemData?.Stack.Amount ?? 0) == 5 - count, "source count");
            Check(count == 5 || source.Durability == 20, "remaining source must be fresh");
            Check(TotalWear(from) + TotalWear(to) == 13, "wear duplicated or lost");
            Check(count == 5 || to.itemSlots[0].itemData.Guid != source.Guid, "split duplicated identity");
            if (!existing && count == 5) Check(ReferenceEquals(source, to.itemSlots[0].itemData), "whole stack reference");
        }
        var healthy = Item(4, 20);
        var damaged = Item(2, 9);
        var bag = Inventory(healthy, damaged);
        Check(bag.TransferItemQuantity(bag.itemSlots[0], bag.itemSlots[1], 3), "fresh source into worn target");
        Check(damaged.Durability == 9 && TotalWear(bag) == 11, "fresh items repaired worn target");
    }

    private static void TestAddAndSort()
    {
        var source = Item();
        var bag = Inventory(null, null, null);
        foreach (var slot in bag.itemSlots) slot.SlotMaxVolume = 2;
        Check(bag.TryAddItem(source, false, out float preview) && preview == 5, "add preview");
        Check(source.Durability == 7 && bag.itemSlots.All(s => s.itemData == null), "preview mutated state");
        Check(bag.TryAddItem(source, true, out float added) && added == 5, "actual add");
        Check(bag.itemSlots.Select(s => s.itemData.Stack.Amount).SequenceEqual(new[] { 2f, 2f, 1f }), "multi-slot counts");
        Check(bag.itemSlots.Select(s => s.itemData.Durability).SequenceEqual(new[] { 7f, 20f, 20f }), "multi-slot copied damage");
        Check(TotalWear(bag) == 13, "multi-slot wear conservation");

        foreach (bool reverse in new[] { false, true })
        {
            var items = new[] { Item(2, 20), Item(3, 6), Item(4, 9), null };
            if (reverse) Array.Reverse(items);
            var inventory = Inventory(items);
            Check(inventory.Organize(), "organize failed");
            Check(inventory.GetItemCount() == 2, "two worn units merged");
            Check(TotalWear(inventory) == 25 && inventory.itemSlots.Sum(s => s.itemData?.Stack.Amount ?? 0) == 9,
                "organize changed wear or amount");
            Check(inventory.Sort(InventorySortMode.Id) && TotalWear(inventory) == 25, "sort changed wear");
        }
        var limited = Inventory(Item(1, 20), Item(5, 7));
        limited.itemSlots[0].SlotMaxVolume = 2;
        Check(limited.Organize() && limited.itemSlots[0].itemData.Durability == 7 &&
            limited.itemSlots[1].itemData.Durability == 20 && TotalWear(limited) == 13, "partial organize duplicates wear");
    }

    private static void TestRejections()
    {
        foreach (float wear in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
        {
            var source = Item();
            var bag = Inventory(source);
            int count = 0;
            bag.Event_OnDataChanged += _ => count++;
            Check(!bag.TryConsumeDurability(source, wear, out _) && source.Durability == 7 &&
                source.Stack.Amount == 5 && count == 0, "invalid wear mutated inventory");
        }
        var infinite = Item(5, 0, 0);
        Check(!ItemDurabilityRules.TryConsume(infinite, 1, out _) && infinite.Stack.Amount == 5, "infinite durability");
        var first = Item();
        var bag2 = Inventory(first, Item(3, 8));
        Check(!first.CanStackWith(bag2.itemSlots[1].itemData), "two worn stacks compatible");
        Check(bag2.GetTransferableQuantityTo(bag2.itemSlots[0], bag2, bag2.itemSlots[1], 5) == 0,
            "worn merge preview accepted");
        Check(!bag2.TransferItemQuantity(bag2.itemSlots[0], bag2.itemSlots[1], 5), "worn merge committed");
        Check(!bag2.TryConsumeDurability(ItemInstanceDataFactory.CloneRuntime(first), 1, out _), "stale GUID reference");
        Check(bag2.TryConsumeFromSlot(bag2.itemSlots[0], 1, out _) && first.Durability == 20,
            "ordinary consumption didn't advance top");
    }
}
