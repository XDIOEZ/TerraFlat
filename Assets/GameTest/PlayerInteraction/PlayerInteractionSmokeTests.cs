using System.Collections.Generic;
using System.Linq;
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
            AssertBinding(map, "HotbarPrevious", "<Gamepad>/dpad/left");
            AssertBinding(map, "HotbarNext", "<Gamepad>/dpad/right");
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
    }
}
