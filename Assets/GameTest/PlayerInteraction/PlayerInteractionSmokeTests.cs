using System.Linq;
using FlatWorld.GameTest.Shared;
using NUnit.Framework;
using UnityEditor;
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

        private sealed class EmptyInputBindingStore : IInputBindingStore
        {
            public string Load() => string.Empty;
            public void Save(string json) { }
            public void Clear() { }
        }
    }
}
