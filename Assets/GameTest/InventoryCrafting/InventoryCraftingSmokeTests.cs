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
