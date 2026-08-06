using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FlatWorld.GameTest.Shared;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FlatWorld.GameTest.PlayerInteraction
{
    /// <summary>玩家交互基础冒烟测试：保护玩家、输入与交互入口。</summary>
    public sealed class PlayerInteractionSmokeTests
    {
        private const string InputActionsPath =
            "Assets/PlayerInput/PlayerInputActions.inputactions";

        [Test]
        [Category("PlayerInteraction.Smoke")]
        public void RequiredEntryPointsAndAssetsExist()
        {
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Item/Player.cs", "Player");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Controller/GameController.cs", "GameController");
            GameTestAssertions.AssertScriptType("Assets/5_Scripts/5-3_GamePlay/Controller/InputBindingService.cs", "InputBindingService");
            GameTestAssertions.AssertFolderContainsAsset("Assets/2_Prefabs/Player", "t:Prefab");
        }

        [Test]
        [Category("PlayerInteraction.Smoke")]
        public void PlayerPhysicsLayerDisablesOnlyPlayerBodyCollisions()
        {
            const int expectedPlayerLayer = 10;
            int playerLayer = LayerMask.NameToLayer("Player");
            Assert.That(playerLayer, Is.EqualTo(expectedPlayerLayer),
                "Player 物理层必须固定为 Layer 10，避免占用现有的未命名 Layer 9。");

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Player/Player.prefab");
            Assert.That(prefab, Is.Not.Null);

            Collider2D[] bodyColliders = prefab
                .GetComponentsInChildren<Collider2D>(true)
                .Where(collider => collider != null && !collider.isTrigger)
                .ToArray();
            Assert.That(bodyColliders, Is.Not.Empty, "Player Prefab 缺少实体碰撞体。");
            Assert.That(
                bodyColliders.All(collider => collider.gameObject.layer == playerLayer),
                Is.True,
                "Player 的非 Trigger 实体碰撞体必须位于 Player 层。");

            Assert.That(Physics2D.GetIgnoreLayerCollision(playerLayer, playerLayer), Is.True,
                "玩家之间必须允许互相穿过。");

            string[] retainedCollisionLayers =
            {
                "Default",
                "Collider",
                "DamageReciver",
                "DamageSender"
            };
            foreach (string layerName in retainedCollisionLayers)
            {
                int otherLayer = LayerMask.NameToLayer(layerName);
                Assert.That(otherLayer, Is.GreaterThanOrEqualTo(0), $"缺少物理层：{layerName}");
                Assert.That(
                    Physics2D.GetIgnoreLayerCollision(playerLayer, otherLayer),
                    Is.False,
                    $"Player 与 {layerName} 的碰撞不能被关闭。");
            }
        }

        [Test]
        [Category("PlayerInteraction.Smoke")]
        public void InputAssetContainsStableGamepadFoundation()
        {
            InputActionAsset asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                InputActionsPath);
            Assert.That(asset, Is.Not.Null, $"无法加载输入资产：{InputActionsPath}");

            Assert.That(asset.controlSchemes.Any(scheme => scheme.name == "Keyboard&Mouse"), Is.True);
            Assert.That(asset.controlSchemes.Any(scheme => scheme.name == "Gamepad"), Is.True);

            InputActionMap map = asset.FindActionMap("Win10", true);
            AssertBinding(map, "Move_Player", "<Gamepad>/leftStick");
            AssertBinding(map, "GamepadCursor", "<Gamepad>/rightStick");
            AssertBinding(map, "LeftClick", "<Gamepad>/rightTrigger");
            AssertBinding(map, "RightClick", "<Gamepad>/leftTrigger");
            AssertBinding(map, "E", "<Gamepad>/buttonWest");
            AssertBinding(map, "F", "<Gamepad>/buttonNorth");
            AssertBinding(map, "B", "<Gamepad>/buttonEast");
            AssertBinding(map, "P", "<Gamepad>/dpad/up");
            AssertBinding(map, "H", "<Gamepad>/dpad/down");
            AssertBinding(map, "Tab", "<Keyboard>/tab");
            AssertBinding(map, "HotbarPrevious", "<Gamepad>/dpad/left");
            AssertBinding(map, "HotbarNext", "<Gamepad>/dpad/right");
            AssertBinding(map, "OpenChat", "<Keyboard>/t");
            Assert.That(
                map.FindAction("SwitchHotBar_Player", true).bindings.Count,
                Is.EqualTo(9),
                "快捷栏直选动作必须提供 1-9 九个绑定。");
            AssertBinding(map, "ESC", "<Gamepad>/start");

            InputAction zoomAction = map.FindAction("CtrlMouse", true);
            Assert.That(
                zoomAction.bindings.Any(binding => binding.groups.Contains("Gamepad")),
                Is.False,
                "相机缩放不能与手柄十字键快捷栏动作重复绑定。");
        }

        [Test]
        [Category("PlayerInteraction.Smoke")]
        public void BindingServiceExposesSeparateKeyboardAndGamepadPages()
        {
            InputActionAsset actions = CreateRuntimeInputAsset();
            MemoryBindingStore store = new MemoryBindingStore();
            using InputBindingService service = new InputBindingService(actions, store);

            try
            {
                IReadOnlyList<InputBindingEntry> keyboardEntries =
                    service.GetEntries(InputBindingDeviceGroup.KeyboardMouse);
                IReadOnlyList<InputBindingEntry> gamepadEntries =
                    service.GetEntries(InputBindingDeviceGroup.Gamepad);

                Assert.That(keyboardEntries, Is.Not.Empty);
                Assert.That(gamepadEntries, Is.Not.Empty);
                Assert.That(keyboardEntries.All(entry => entry.BindingGroup == "Keyboard&Mouse"), Is.True);
                Assert.That(gamepadEntries.All(entry => entry.BindingGroup == "Gamepad"), Is.True);
                Assert.That(gamepadEntries.Any(entry =>
                    entry.Action.name == "Move_Player" &&
                    entry.ExpectedControlLayout == "Vector2"), Is.True);
                Assert.That(gamepadEntries.Any(entry => entry.Action.name == "B"), Is.True);
                Assert.That(gamepadEntries.Any(entry => entry.Action.name == "HotbarPrevious"), Is.True);
                Assert.That(gamepadEntries.Any(entry => entry.Action.name == "HotbarNext"), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(actions);
            }
        }

        [Test]
        [Category("PlayerInteraction.Smoke")]
        public void InputBindingSettingsExposeChatAndAllNineHotbarSlots()
        {
            InputActionAsset asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                InputActionsPath);
            Assert.That(asset, Is.Not.Null, $"无法加载输入资产：{InputActionsPath}");

            using InputBindingService service = new InputBindingService(
                asset,
                new EmptyInputBindingStore());

            string[] hotbarEntries = service.Entries
                .Select(entry => entry.DisplayName)
                .Where(displayName => displayName.StartsWith("快捷栏 "))
                .ToArray();

            Assert.That(
                hotbarEntries,
                Is.EqualTo(Enumerable.Range(1, 9).Select(index => $"快捷栏 {index}").ToArray()));
            Assert.That(
                service.Entries.Select(entry => entry.DisplayName),
                Does.Contain("打开聊天框"));
            Assert.That(
                service.Entries.Select(entry => entry.DisplayName),
                Does.Contain("角色参数面板"));
        }

        [Test]
        [Category("PlayerInteraction.Smoke")]
        public void ResettingGamepadBindingsKeepsKeyboardOverrides()
        {
            InputActionAsset actions = CreateRuntimeInputAsset();
            MemoryBindingStore store = new MemoryBindingStore();
            using InputBindingService service = new InputBindingService(actions, store);

            try
            {
                InputBindingEntry keyboardBag = service
                    .GetEntries(InputBindingDeviceGroup.KeyboardMouse)
                    .Single(entry => entry.Action.name == "B");
                InputBindingEntry gamepadBag = service
                    .GetEntries(InputBindingDeviceGroup.Gamepad)
                    .Single(entry => entry.Action.name == "B");
                keyboardBag.Action.ApplyBindingOverride(keyboardBag.BindingIndex, "<Keyboard>/i");
                gamepadBag.Action.ApplyBindingOverride(gamepadBag.BindingIndex, "<Gamepad>/buttonSouth");

                service.ResetToDefaults(InputBindingDeviceGroup.Gamepad);

                Assert.That(
                    keyboardBag.Action.bindings[keyboardBag.BindingIndex].overridePath,
                    Is.EqualTo("<Keyboard>/i"));
                Assert.That(
                    gamepadBag.Action.bindings[gamepadBag.BindingIndex].overridePath,
                    Is.Null.Or.Empty);
                Assert.That(store.SavedJson, Is.Not.Empty);
            }
            finally
            {
                Object.DestroyImmediate(actions);
            }
        }

        [Test]
        [Category("PlayerInteraction.Smoke")]
        public void SavingFoodModuleKeepsCharacterPanelTabBinding()
        {
            GameObject owner = new GameObject("FoodModuleSave_Test");
            InputAction tabAction = new InputAction("Tab", InputActionType.Button, "<Keyboard>/tab");
            try
            {
                Mod_Food food = owner.AddComponent<Mod_Food>();
                FieldInfo tabField = typeof(Mod_Food).GetField(
                    "_tabAction",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(tabField, Is.Not.Null);

                tabField.SetValue(food, tabAction);
                food.Save();

                Assert.That(tabField.GetValue(food), Is.SameAs(tabAction),
                    "普通存档不能解绑仍在运行的 Tab 参数面板快捷键。");
            }
            finally
            {
                Object.DestroyImmediate(owner);
                tabAction.Dispose();
            }
        }

        private static void AssertBinding(
            InputActionMap map,
            string actionName,
            string expectedPath)
        {
            InputAction action = map.FindAction(actionName, true);
            Assert.That(
                action.bindings.Any(binding => binding.path == expectedPath),
                Is.True,
                $"动作 {actionName} 缺少绑定 {expectedPath}。");
        }

        private static InputActionAsset CreateRuntimeInputAsset()
        {
            InputActionAsset source = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                InputActionsPath);
            Assert.That(source, Is.Not.Null, $"无法加载输入资产：{InputActionsPath}");
            return Object.Instantiate(source);
        }

        private sealed class MemoryBindingStore : IInputBindingStore
        {
            public string SavedJson { get; private set; } = string.Empty;

            public string Load()
            {
                return SavedJson;
            }

            public void Save(string json)
            {
                SavedJson = json ?? string.Empty;
            }

            public void Clear()
            {
                SavedJson = string.Empty;
            }
        }

        private sealed class EmptyInputBindingStore : IInputBindingStore
        {
            public string Load() => string.Empty;
            public void Save(string json) { }
            public void Clear() { }
        }
    }
}
