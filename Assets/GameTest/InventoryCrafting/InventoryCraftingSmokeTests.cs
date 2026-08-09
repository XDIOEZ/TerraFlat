using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FlatWorld.GameTest.Shared;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FlatWorld.GameTest.InventoryCrafting
{
    /// <summary>背包制作基础冒烟测试：保护库存、装备和配方入口。</summary>
    public sealed class InventoryCraftingSmokeTests
    {

        [Test]
        [Category("InventoryCrafting.Smoke")]
        [Category("Smoke")]
        public void HotbarSlotsDoNotEnterGamepadNavigation()
        {
            const string prefabPath = "Assets/2_Prefabs/2-1_UI/InventoryUI/UI_HotBar.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, $"缺少快捷栏 Prefab：{prefabPath}");

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                Button[] slots = instance.GetComponentsInChildren<Button>(true);
                Assert.That(slots, Is.Not.Empty, "快捷栏必须包含槽位按钮。");

                FlatWorldUITheme.ApplyGamepadNavigationPolicy(instance.transform);
                foreach (Button slot in slots)
                {
                    Assert.That(
                        FlatWorldUITheme.IsGamepadNavigationExcluded(slot),
                        Is.True,
                        $"快捷栏槽位 {slot.name} 不应成为手柄焦点。");
                    Assert.That(
                        slot.navigation.mode,
                        Is.EqualTo(UnityEngine.UI.Navigation.Mode.None),
                        $"快捷栏槽位 {slot.name} 的导航必须关闭。");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }


        [Test]
        [Category("InventoryCrafting.Smoke")]
        [Category("Smoke")]
        public void HotbarSelectionBoxMovesWithoutChangingItsParent()
        {
            GameObject hotbarObject = new("HotbarSelection_Test");
            GameObject firstSlotObject = new("HotbarSlot_0_Test");
            GameObject secondSlotObject = new("HotbarSlot_1_Test");
            GameObject selectionObject = new("HotbarSelectionBox_Test");

            try
            {
                Inventory_HotBar hotbar = hotbarObject.AddComponent<Inventory_HotBar>();
                ItemSlot_UI firstSlot = firstSlotObject.AddComponent<ItemSlot_UI>();
                ItemSlot_UI secondSlot = secondSlotObject.AddComponent<ItemSlot_UI>();
                firstSlotObject.transform.position = new Vector3(-50f, 20f, 0f);
                secondSlotObject.transform.position = new Vector3(125f, 20f, 0f);

                selectionObject.transform.SetParent(firstSlotObject.transform, false);
                Transform originalParent = selectionObject.transform.parent;

                hotbar.SelectBox = selectionObject;
                hotbar.SelectBoxChangeDuration = 0.1f;
                hotbar.RuntimeInventory.itemSlot_UI.Add(firstSlot);
                hotbar.RuntimeInventory.itemSlot_UI.Add(secondSlot);

                MethodInfo moveSelection = typeof(Inventory_HotBar).GetMethod(
                    "MoveSelectBox",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(moveSelection, Is.Not.Null, "快捷栏选中框必须提供移动入口。");

                MethodInfo resolveTargetPosition = typeof(Inventory_HotBar).GetMethod(
                    "GetSelectBoxTargetPosition",
                    BindingFlags.Static | BindingFlags.NonPublic);
                Assert.That(resolveTargetPosition, Is.Not.Null, "快捷栏选中框必须使用目标槽位的位置。");

                Vector3 targetPosition = (Vector3)resolveTargetPosition.Invoke(
                    null,
                    new object[] { secondSlotObject.transform });
                Assert.That(targetPosition, Is.EqualTo(secondSlotObject.transform.position));

                moveSelection.Invoke(hotbar, new object[] { 1 });

                Assert.That(selectionObject.transform.parent, Is.EqualTo(originalParent), "切换快捷栏位不应改变选中框层级。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(hotbarObject);
                UnityEngine.Object.DestroyImmediate(selectionObject);
                UnityEngine.Object.DestroyImmediate(firstSlotObject);
                UnityEngine.Object.DestroyImmediate(secondSlotObject);
            }
        }

        [Test]
        [Category("InventoryCrafting.Smoke")]
        [Category("Smoke")]
        public void PlayerBagLeftClickTransfersToHotbarInsteadOfHiddenHandSlot()
        {
            GameObject playerObject = new("PlayerBagLeftClick_Test");
            Inventory previousPlayerHand = Inventory_Hand.PlayerHand;

            try
            {
                Player player = playerObject.AddComponent<Player>();
                Inventory_HotBar hotbar = playerObject.AddComponent<Inventory_HotBar>();
                hotbar.RuntimeInventory = new Inventory_HotBar.HotBarRuntimeInventory
                {
                    Data = new Inventory_Data(
                        new List<ItemSlot> { new ItemSlot(0) },
                        ModText.Hotbar)
                };
                hotbar.RuntimeInventory.Owner = null;

                player.itemMods.BindOwner(player);
                player.itemMods.Mods_List[ModText.Hotbar] = new List<Module> { hotbar };

                Inventory hiddenHand = new Inventory
                {
                    Data = new Inventory_Data(
                        new List<ItemSlot> { new ItemSlot(0) },
                        ModText.Hand)
                };

                Inventory bag = new Inventory
                {
                    item = player,
                    Data = new Inventory_Data(
                        new List<ItemSlot> { new ItemSlot(0) },
                        ModText.Bag),
                    DefaultTarget_Inventory = hiddenHand
                };
                bag.Data.itemSlots[0].itemData = new Data_GeneralItem
                {
                    IDName = "Regression_Item",
                    Stack = new ItemStack { Amount = 1f, Volume = 1f }
                };

                Inventory_Hand.PlayerHand = null;
                bag.OnLeftClick(0);

                Assert.That(bag.Data.itemSlots[0].itemData, Is.Null, "玩家背包槽位应转出物品。");
                Assert.That(hiddenHand.Data.itemSlots[0].itemData, Is.Null, "物品不应落入不可见的手部缓冲槽。");
                Assert.That(
                    hotbar.RuntimeInventory.Data.itemSlots[0].itemData?.IDName,
                    Is.EqualTo("Regression_Item"),
                    "玩家背包左键应将物品放入当前快捷栏槽位。");
            }
            finally
            {
                Inventory_Hand.PlayerHand = previousPlayerHand;
                UnityEngine.Object.DestroyImmediate(playerObject);
            }
        }

        [Test]
        [Category("InventoryCrafting.Smoke")]
        [Category("Smoke")]
        public void PlayerBagKeyboardMouseClickSwapsWithHeldHandItem()
        {
            GameObject playerObject = new("PlayerBagKeyboardMouseSwap_Test");
            Inventory previousPlayerHand = Inventory_Hand.PlayerHand;

            try
            {
                Player player = playerObject.AddComponent<Player>();
                Inventory hand = new Inventory
                {
                    Data = new Inventory_Data(
                        new List<ItemSlot> { new ItemSlot(0) },
                        ModText.Hand)
                };
                hand.Data.itemSlots[0].itemData = new Data_GeneralItem
                {
                    IDName = "Held_Item",
                    Stack = new ItemStack { Amount = 1f, Volume = 1f }
                };

                Inventory bag = new Inventory
                {
                    item = player,
                    Data = new Inventory_Data(
                        new List<ItemSlot> { new ItemSlot(0) },
                        ModText.Bag)
                };
                bag.Data.itemSlots[0].itemData = new Data_GeneralItem
                {
                    IDName = "Bag_Item",
                    Stack = new ItemStack { Amount = 1f, Volume = 1f }
                };

                Inventory_Hand.PlayerHand = hand;
                bag.OnLeftClick(0);

                Assert.That(bag.Data.itemSlots[0].itemData?.IDName, Is.EqualTo("Held_Item"),
                    "键鼠点击时，背包槽位应接收手上原有物品。");
                Assert.That(hand.Data.itemSlots[0].itemData?.IDName, Is.EqualTo("Bag_Item"),
                    "键鼠点击时，背包物品应交换到手上槽位。");
            }
            finally
            {
                Inventory_Hand.PlayerHand = previousPlayerHand;
                UnityEngine.Object.DestroyImmediate(playerObject);
            }
        }

        [Test]
        [Category("InventoryCrafting.Smoke")]
        [Category("Smoke")]
        public void PlayerBagGamepadSubmitUsesHotbarWithoutOccupyingMouseHandSwap()
        {
            GameObject playerObject = new("PlayerBagGamepadSwap_Test");
            Inventory previousPlayerHand = Inventory_Hand.PlayerHand;

            try
            {
                Player player = playerObject.AddComponent<Player>();
                Inventory_HotBar hotbar = playerObject.AddComponent<Inventory_HotBar>();
                hotbar.RuntimeInventory = new Inventory_HotBar.HotBarRuntimeInventory
                {
                    Data = new Inventory_Data(
                        new List<ItemSlot> { new ItemSlot(0) },
                        ModText.Hotbar)
                };
                hotbar.RuntimeInventory.Owner = null;
                hotbar.RuntimeInventory.Data.itemSlots[0].itemData = new Data_GeneralItem
                {
                    IDName = "Hotbar_Item",
                    Stack = new ItemStack { Amount = 1f, Volume = 1f }
                };

                Inventory hand = new Inventory
                {
                    Data = new Inventory_Data(
                        new List<ItemSlot> { new ItemSlot(0) },
                        ModText.Hand)
                };
                hand.Data.itemSlots[0].itemData = new Data_GeneralItem
                {
                    IDName = "Held_Item",
                    Stack = new ItemStack { Amount = 1f, Volume = 1f }
                };

                player.itemMods.BindOwner(player);
                player.itemMods.Mods_List[ModText.Hotbar] = new List<Module> { hotbar };

                Inventory bag = new Inventory
                {
                    item = player,
                    Data = new Inventory_Data(
                        new List<ItemSlot> { new ItemSlot(0) },
                        ModText.Bag),
                    DefaultTarget_Inventory = hand
                };
                bag.Data.itemSlots[0].itemData = new Data_GeneralItem
                {
                    IDName = "Bag_Item",
                    Stack = new ItemStack { Amount = 1f, Volume = 1f }
                };

                Inventory_Hand.PlayerHand = hand;
                bag.OnGamepadSubmit(0);

                Assert.That(bag.Data.itemSlots[0].itemData?.IDName, Is.EqualTo("Hotbar_Item"),
                    "手柄确认应与当前快捷栏槽位交换。");
                Assert.That(hotbar.RuntimeInventory.Data.itemSlots[0].itemData?.IDName, Is.EqualTo("Bag_Item"),
                    "手柄确认应把背包物品放入当前快捷栏槽位。");
                Assert.That(hand.Data.itemSlots[0].itemData?.IDName, Is.EqualTo("Held_Item"),
                    "手柄交换不应占用键鼠的手上槽位。");
            }
            finally
            {
                Inventory_Hand.PlayerHand = previousPlayerHand;
                UnityEngine.Object.DestroyImmediate(playerObject);
            }
        }


        private static void AssertCraftingPreviewLayers(ItemSlot_UI slot, string prefabPath)
        {
            Assert.That(slot, Is.Not.Null, $"{prefabPath} 缺少 ItemSlot_UI");
            Assert.That(slot.image, Is.Not.Null, $"{prefabPath}/{slot.name} 缺少物品图标引用");

            Image[] images = slot.GetComponentsInChildren<Image>(true);
            Image ghost = Array.Find(images, image => image != null && image.name == "Crafting Output Ghost");
            Image reveal = Array.Find(images, image => image != null && image.name == "Crafting Output Reveal");
            string context = $"{prefabPath}/{slot.name}";

            Assert.That(ghost, Is.Not.Null, $"{context} 缺少 Crafting Output Ghost");
            Assert.That(reveal, Is.Not.Null, $"{context} 缺少 Crafting Output Reveal");
            Assert.That(ghost.gameObject.activeSelf, Is.False, $"{context} Ghost 默认应隐藏");
            Assert.That(reveal.gameObject.activeSelf, Is.False, $"{context} Reveal 默认应隐藏");
            Assert.That(ghost.raycastTarget, Is.False, $"{context} Ghost 不应拦截射线");
            Assert.That(reveal.raycastTarget, Is.False, $"{context} Reveal 不应拦截射线");
            Assert.That(ghost.preserveAspect, Is.True, $"{context} Ghost 应保持宽高比");
            Assert.That(reveal.preserveAspect, Is.True, $"{context} Reveal 应保持宽高比");
            Assert.That(reveal.type, Is.EqualTo(Image.Type.Filled), $"{context} Reveal 类型错误");
            Assert.That(reveal.fillMethod, Is.EqualTo(Image.FillMethod.Vertical), $"{context} Reveal 填充方向错误");
            Assert.That(reveal.fillOrigin, Is.EqualTo((int)Image.OriginVertical.Bottom), $"{context} Reveal 应从下方填充");
            Assert.That(ghost.transform.parent, Is.EqualTo(slot.image.transform.parent), $"{context} Ghost 层级错误");
            Assert.That(reveal.transform.parent, Is.EqualTo(slot.image.transform.parent), $"{context} Reveal 层级错误");
        }

    }
}
