using System;
using System.IO;
using System.Linq;
using System.Reflection;
using FlatWorld.GameTest.Shared;
using InputSystem;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FlatWorld.GameTest.UI
{
    /// <summary>UI 基础冒烟测试：保护 UI 管理器、面板和根 Prefab 入口。</summary>
    public sealed class UISmokeTests
    {

        [Test]
        [Category("UI.Smoke")]
        public void RuntimeUiNavigationDoesNotBindKeyboardMovement()
        {
            PlayerInputActions inputActions = new PlayerInputActions();

            try
            {
                EventSystemGuard.SynchronizeUIInputBindings(inputActions.asset);
                InputAction navigate = inputActions.asset
                    .FindActionMap("FlatWorldUI", false)?
                    .FindAction("Navigate", false)
                    ?? throw new AssertionException("运行时 UI 缺少 Navigate 动作。");
                string[] paths = navigate.bindings
                    .Select(binding => binding.effectivePath)
                    .Where(path => !string.IsNullOrEmpty(path))
                    .ToArray();

                Assert.That(
                    paths.Any(path => path.IndexOf("<Gamepad>", StringComparison.OrdinalIgnoreCase) >= 0),
                    Is.True,
                    "手柄导航绑定不应被移除。");
                Assert.That(
                    paths.Any(path => path.IndexOf("<Keyboard>", StringComparison.OrdinalIgnoreCase) >= 0),
                    Is.False,
                    "W/A/S/D 不得进入 UI Navigate 动作并移动背包焦点。");
            }
            finally
            {
                inputActions.Dispose();
            }
        }

        [Test]
        [Category("UI.Smoke")]
        public void GamepadInputFocusDoesNotOpenVirtualKeyboardBeforeSubmit()
        {
            EventSystem previousEventSystem = EventSystem.current;
            GameObject eventSystemObject = new GameObject("输入框焦点测试_EventSystem");
            GameObject inputObject = new GameObject(
                "输入框焦点测试",
                typeof(RectTransform),
                typeof(TMP_InputField));
            GameObject controllerObject = new GameObject("输入框焦点测试_Controller");

            try
            {
                EventSystem eventSystem = eventSystemObject.AddComponent<EventSystem>();
                GamepadUIRuntimeController controller =
                    controllerObject.AddComponent<GamepadUIRuntimeController>();
                EventSystem.current = eventSystem;
                eventSystem.SetSelectedGameObject(inputObject);
                controller.SetGamepadMode(true);

                MethodInfo updateMethod = typeof(GamepadUIRuntimeController).GetMethod(
                    "Update",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(updateMethod, Is.Not.Null, "手柄 UI 控制器必须保留运行时更新入口。");
                updateMethod.Invoke(controller, null);

                Assert.That(
                    GamepadVirtualKeyboardController.IsOpen,
                    Is.False,
                    "手柄焦点移动到输入框时不能自动打开虚拟键盘，必须等待确认键。");

                string controllerSource = File.ReadAllText(
                    "Assets/5_Scripts/5-5_UI/Input/Gamepad/GamepadUIRuntimeController.cs");
                Assert.That(controllerSource, Does.Contain("GamepadInputFieldNavigationBridge"));
                Assert.That(
                    controllerSource,
                    Does.Contain("EventSystem.current?.SetSelectedGameObject(target.gameObject, eventData);"));
                Assert.That(controllerSource, Does.Not.Contain("DeactivateInputField(clearSelection: false);"),
                    "输入框获得手柄焦点时必须保留选中表现，不能直接强制取消焦点。");

                string panelSource = File.ReadAllText("Assets/5_Scripts/5-5_UI/Core/BasePanel.cs");
                Assert.That(panelSource, Does.Contain("EnsureInputFieldNavigationBridges();"));
            }
            finally
            {
                if (GamepadVirtualKeyboardController.IsOpen)
                    GamepadVirtualKeyboardController.Cancel();

                EventSystem.current = previousEventSystem;
                Object.DestroyImmediate(controllerObject);
                Object.DestroyImmediate(inputObject);
                Object.DestroyImmediate(eventSystemObject);
            }
        }

        [Test]
        [Category("UI.Smoke")]
        public void LeftStickFocusInputKeepsGameplayVirtualCursorWithoutUiPanel()
        {
            GameObject controllerObject = new GameObject("虚拟准星隔离测试_Controller");

            try
            {
                GamepadUIRuntimeController controller =
                    controllerObject.AddComponent<GamepadUIRuntimeController>();
                controller.SetGamepadMode(true);
                controller.NotifyCursorPosition(new Vector2(100f, 100f));
                Assert.That(controller.IsVirtualCursorMode, Is.True);

                controller.NotifyFocusInput();

                Assert.That(
                    controller.IsVirtualCursorMode,
                    Is.True,
                    "没有打开 UI 面板时，左摇杆移动不能退出右摇杆虚拟准星模式。");
            }
            finally
            {
                Object.DestroyImmediate(controllerObject);
            }
        }

        [Test]
        [Category("UI.Smoke")]
        public void GamepadGameplayCursorUsesPlayerCenterAndStickDirection()
        {
            MethodInfo radialMethod = typeof(GameController).GetMethod(
                "CalculateGameplayRadialCursorScreenPosition",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(radialMethod, Is.Not.Null, "游戏内手柄准星必须保留径向定位计算入口。");

            Vector2 playerScreenPosition = new Vector2(400f, 300f);
            Vector2 upPosition = (Vector2)radialMethod.Invoke(
                null,
                new object[]
                {
                    playerScreenPosition,
                    Vector2.up,
                    120f,
                    new Vector2(800f, 600f),
                    6f
                });
            Vector2 rightPosition = (Vector2)radialMethod.Invoke(
                null,
                new object[]
                {
                    playerScreenPosition,
                    Vector2.right,
                    120f,
                    new Vector2(800f, 600f),
                    6f
                });

            Assert.That(upPosition.x, Is.EqualTo(400f).Within(0.01f));
            Assert.That(upPosition.y, Is.EqualTo(420f).Within(0.01f));
            Assert.That(rightPosition.x, Is.EqualTo(520f).Within(0.01f));
            Assert.That(rightPosition.y, Is.EqualTo(300f).Within(0.01f));
        }

        [Test]
        [Category("UI.Smoke")]
        public void RuntimeUiCancelUsesGamepadBAndDoesNotBindKeyboardB()
        {
            PlayerInputActions inputActions = new PlayerInputActions();

            try
            {
                EventSystemGuard.SynchronizeUIInputBindings(inputActions.asset);
                InputAction cancel = inputActions.asset
                    .FindActionMap("FlatWorldUI", false)?
                    .FindAction("Cancel", false)
                    ?? throw new AssertionException("运行时 UI 缺少 Cancel 动作。");
                string[] paths = cancel.bindings
                    .Select(binding => binding.effectivePath)
                    .Where(path => !string.IsNullOrEmpty(path))
                    .ToArray();

                Assert.That(paths, Does.Contain("<Keyboard>/escape"));
                Assert.That(paths, Does.Contain("<Gamepad>/buttonEast"),
                    "手柄 B 必须保留为 UI 返回键。");
                Assert.That(paths, Does.Not.Contain("<Keyboard>/b"),
                    "键盘 B 只能交给背包开关，不能同时触发 UI 取消。");

                InputAction gameplayB = inputActions.asset
                    .FindActionMap("Win10", false)?
                    .FindAction("B", false)
                    ?? throw new AssertionException("玩家输入缺少 B 动作。");
                Assert.That(
                    gameplayB.bindings.Any(binding =>
                        binding.groups.IndexOf("Gamepad", StringComparison.OrdinalIgnoreCase) >= 0),
                    Is.False,
                    "手柄 B 不能继续绑定到背包开关动作。");
            }
            finally
            {
                inputActions.Dispose();
            }
        }

        [Test]
        [Category("UI.Smoke")]
        [Category("UI.Layout")]
        public void InputBindingRowProvidesModifyAndClearButtons()
        {
            const string rowPath =
                "Assets/2_Prefabs/2-1_UI/Settings/Components/UI_InputBindingRow.prefab";

            AssertPrefabContains(
                rowPath,
                "操作名称",
                "绑定值",
                "修改按钮",
                "清除按钮");

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(rowPath)
                ?? throw new AssertionException($"缺少按键绑定行 Prefab：{rowPath}");
            Button rebindButton = prefab.GetComponentsInChildren<Button>(true)
                .Single(button => button.name == "修改按钮");
            Button clearButton = prefab.GetComponentsInChildren<Button>(true)
                .Single(button => button.name == "清除按钮");

            Assert.That(rebindButton.transform.parent, Is.EqualTo(clearButton.transform.parent));
            Assert.That(rebindButton.GetComponent<LayoutElement>(), Is.Not.Null);
            Assert.That(clearButton.GetComponent<LayoutElement>(), Is.Not.Null);
        }

        [Test]
        [Category("UI.Smoke")]
        public void SaveItemKeepsConfirmedSelectionWhenGamepadFocusMoves()
        {
            GameObject itemObject = new GameObject(
                "存档条目测试",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(GameSaveItemView));
            GameObject accentObject = new GameObject(
                "选择强调线",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            accentObject.transform.SetParent(itemObject.transform, false);

            try
            {
                GameSaveItemView itemView = itemObject.GetComponent<GameSaveItemView>()
                    ?? throw new AssertionException("测试条目缺少 GameSaveItemView。");
                Image background = itemObject.GetComponent<Image>()
                    ?? throw new AssertionException("测试条目缺少背景 Image。");
                Image accent = accentObject.GetComponent<Image>()
                    ?? throw new AssertionException("测试条目缺少强调线 Image。");
                itemView.Background = background;
                itemView.SelectionAccent = accent;

                itemView.SetSelected(true);
                itemView.OnSelect(new BaseEventData(null));
                itemView.OnDeselect(new BaseEventData(null));

                Assert.That(itemView.SelectionAccent.enabled, Is.True,
                    "手柄焦点离开后，已确认的存档选择不应丢失。");

                itemView.SetSelected(false);
                Assert.That(itemView.SelectionAccent.enabled, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(accentObject);
                Object.DestroyImmediate(itemObject);
            }
        }

        [Test]
        [Category("UI.Smoke")]
        public void SaveItemPrefabUsesAutomaticGamepadNavigation()
        {
            const string prefabPath = "Assets/2_Prefabs/2-1_UI/MainMenu/Save/UI_SaveSelectionButton.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, $"缺少存档条目 Prefab：{prefabPath}");

            Button button = prefab.GetComponent<Button>()
                ?? throw new AssertionException("存档条目 Prefab 缺少 Button。");
            Assert.That(button.transition, Is.EqualTo(Selectable.Transition.None));
            Assert.That(button.navigation.mode, Is.EqualTo(UnityEngine.UI.Navigation.Mode.Automatic));
        }

        [Test]
        [Category("UI.Smoke")]
        [Category("Smoke")]
        public void WorldStreamingSettingsPrefabAndEntryFollowNamingContract()
        {
            AssertPrefabContains(
                "Assets/2_Prefabs/2-1_UI/Settings/Panels/UI_WorldStreamingSettings.prefab",
                "性能模式下拉列表",
                "状态文本",
                "取消按钮",
                "应用按钮");
            AssertPrefabContains(
                "Assets/2_Prefabs/2-1_UI/MainMenu/Core/UI_ActionList.prefab",
                "流送性能");

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/2-1_UI/Settings/Panels/UI_WorldStreamingSettings.prefab");
            RectTransform rect = prefab.GetComponent<RectTransform>();
            Assert.That(rect.sizeDelta.x, Is.LessThanOrEqualTo(680f));
            Assert.That(rect.sizeDelta.y, Is.LessThanOrEqualTo(420f));
            Assert.That(prefab.GetComponent<VerticalLayoutGroup>(), Is.Not.Null);
        }

        [Test]
        [Category("UI.Smoke")]
        [Category("UI.Layout")]
        public void MainMenuSettingsVisualPrefabFollowsNamingAndLayoutContract()
        {
            const string mainMenuPath = "Assets/2_Prefabs/2-1_UI/MainMenu/Core/UI_MainMenu.prefab";
            const string settingsPath = "Assets/2_Prefabs/2-1_UI/Settings/Panels/UI_MainMenuSettings.prefab";

            AssertPrefabContains(mainMenuPath, GameManager.MainMenuSettingsButtonKey);
            AssertPrefabContains(
                settingsPath,
                "关闭按钮",
                "窗口大小下拉列表",
                "显示模式下拉列表",
                "画质预设下拉列表",
                "特效质量下拉列表",
                "游戏语言下拉列表",
                "恢复默认按钮",
                "返回按钮");

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(settingsPath);
            RectTransform rootRect = prefab.GetComponent<RectTransform>();
            Assert.That(rootRect.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(rootRect.anchorMax, Is.EqualTo(Vector2.one));

            RectTransform dialog = prefab.GetComponentsInChildren<RectTransform>(true)
                .Single(rect => rect.name == "设置对话框");
            Assert.That(dialog.sizeDelta, Is.EqualTo(new Vector2(720f, 600f)));
            Assert.That(dialog.GetComponent<VerticalLayoutGroup>(), Is.Not.Null);
        }

        [Test]
        [Category("UI.Smoke")]
        public void InGameSettingsPauseOnlyInSinglePlayerWorld()
        {
            const string sourcePath =
                "Assets/5_Scripts/5-3_GamePlay/Presentation/UI/Module_Setting.cs";
            string source = File.ReadAllText(sourcePath);

            Assert.That(source, Does.Contain("basePanel.Opened += AcquireSettingsPause;"));
            Assert.That(source, Does.Contain("basePanel.Closed += ReleaseSettingsPause;"));
            Assert.That(source, Does.Contain("GameNetwork.IsOnline"));
            Assert.That(source, Does.Contain("gameManager.IsInGameWorld"));
            Assert.That(source, Does.Contain("Time.timeScale = 0f;"));
            Assert.That(source, Does.Contain("Time.timeScale = timeScaleBeforeSettings;"));
        }

        [Test]
        [Category("UI.Smoke")]
        [Category("Smoke")]
        public void PlayerWorldCoordinateHudPrefabAndPlayerBindingFollowContract()
        {
            const string hudPath = "Assets/2_Prefabs/2-1_UI/Gameplay/HUD/UI_PlayerWorldCoordinate.prefab";
            const string playerPath = "Assets/2_Prefabs/Gameplay/Player/Player.prefab";

            AssertPrefabContains(hudPath, "坐标文本");

            GameObject hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(hudPath)
                ?? throw new AssertionException($"缺少坐标 HUD Prefab：{hudPath}");
            RectTransform rootRect = hudPrefab.GetComponent<RectTransform>()
                ?? throw new AssertionException("坐标 HUD 根节点缺少 RectTransform。");
            Assert.That(rootRect.anchorMin, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(rootRect.anchorMax, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(rootRect.pivot, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(rootRect.anchoredPosition, Is.EqualTo(new Vector2(28f, -28f)));
            Assert.That(rootRect.sizeDelta, Is.EqualTo(new Vector2(240f, 30f)));

            foreach (Graphic graphic in hudPrefab.GetComponentsInChildren<Graphic>(true))
                Assert.That(graphic.raycastTarget, Is.False, $"坐标 HUD 不应拦截输入：{graphic.name}");

            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(playerPath)
                ?? throw new AssertionException($"缺少玩家 Prefab：{playerPath}");
            Assert.That(playerPrefab.GetComponent<PlayerWorldCoordinateHUD>(), Is.Not.Null,
                "Player 必须挂载 PlayerWorldCoordinateHUD，才能在进入世界后自动创建坐标面板。");
        }

        [Test]
        [Category("UI.Smoke")]
        [Category("UI.Layout")]
        public void SettingsSessionPageProvidesExitWithoutSavingAction()
        {
            const string prefabPath = "Assets/2_Prefabs/2-1_UI/MainMenu/Core/UI_ActionList.prefab";
            const string settingSourcePath =
                "Assets/5_Scripts/5-3_GamePlay/Presentation/UI/Module_Setting.cs";

            AssertPrefabContains(prefabPath, "设置分页_会话", "不保存直接退出");
            string source = File.ReadAllText(settingSourcePath);
            Assert.That(source, Does.Contain("BindButton(UIText.ExitWithoutSavingButtons, ExitAppWithoutSaving)"));
            Assert.That(source, Does.Contain("saveCurrentGame: false"));
        }

        [Test]
        [Category("UI.Smoke")]
        public void GamepadFocusIsConstrainedToTopmostOpenPanel()
        {
            const string managerPath = "Assets/5_Scripts/5-5_UI/Core/UIManager.cs";
            const string controllerPath =
                "Assets/5_Scripts/5-5_UI/Input/Gamepad/GamepadUIRuntimeController.cs";

            string managerSource = File.ReadAllText(managerPath);
            string controllerSource = File.ReadAllText(controllerPath);

            Assert.That(managerSource, Does.Contain("ConstrainSelectionToTopmostGamepadPanel"));
            Assert.That(managerSource, Does.Contain("selectedObject.transform.IsChildOf(panel.transform)"));
            Assert.That(controllerSource, Does.Contain("private void LateUpdate()"));
            Assert.That(
                controllerSource,
                Does.Contain("ConstrainSelectionToTopmostGamepadPanel();"),
                "EventSystem 完成方向导航后必须阻止焦点停留在背景面板。");
        }

        [Test]
        [Category("UI.Smoke")]
        [Category("UI.Layout")]
        public void SaveStatusHudAndManualSaveFollowAsyncContract()
        {
            const string hudPath =
                "Assets/2_Prefabs/2-1_UI/Gameplay/HUD/UI_SaveStatus.prefab";
            const string gameManagerPath =
                "Assets/5_Scripts/5-3_GamePlay/Core/Lifecycle/GameManager.cs";

            AssertPrefabContains(hudPath, "背景", "强调线", "保存状态文本");
            GameObject hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(hudPath)
                ?? throw new AssertionException($"缺少保存状态 HUD Prefab：{hudPath}");
            RectTransform rootRect = hudPrefab.GetComponent<RectTransform>()
                ?? throw new AssertionException("保存状态 HUD 根节点缺少 RectTransform。");
            Assert.That(rootRect.anchorMin, Is.EqualTo(new Vector2(1f, 1f)));
            Assert.That(rootRect.anchorMax, Is.EqualTo(new Vector2(1f, 1f)));
            Assert.That(rootRect.pivot, Is.EqualTo(new Vector2(1f, 1f)));
            Assert.That(hudPrefab.GetComponent<CanvasGroup>(), Is.Not.Null);
            foreach (Graphic graphic in hudPrefab.GetComponentsInChildren<Graphic>(true))
                Assert.That(graphic.raycastTarget, Is.False, $"保存状态 HUD 不应拦截输入：{graphic.name}");

            string source = File.ReadAllText(gameManagerPath);
            Assert.That(source, Does.Contain("SaveGameInBackgroundCoroutineWithStatus"));
            Assert.That(source, Does.Contain("BeginSaveStatus();"));
            Assert.That(source, Does.Contain("while (!writeTask.IsCompleted)"));
            Assert.That(source, Does.Contain("CompleteSaveStatus(succeeded);"));
        }

        [Test]
        [Category("UI.Smoke")]
        [Category("UI.Layout")]
        public void BuffStatusHudPrefabAndPlayerBindingFollowContract()
        {
            const string hudPath =
                "Assets/2_Prefabs/2-1_UI/Gameplay/Status/Buff/UI_BuffStatus.prefab";
            const string itemPath =
                "Assets/2_Prefabs/2-1_UI/Gameplay/Status/Buff/UI_BuffStatusItem.prefab";
            const string playerPath = "Assets/2_Prefabs/Gameplay/Player/Player.prefab";
            const string sourcePath =
                "Assets/5_Scripts/5-3_GamePlay/Presentation/UI/PlayerBuffStatusHUD.cs";

            AssertPrefabContains(
                hudPath,
                "背景",
                "强调线",
                "标题",
                "数量文本",
                "空状态文本",
                "内容列表",
                "Viewport",
                "Content");
            AssertPrefabContains(itemPath, "占位图标", "占位符文本", "状态名称", "剩余时间");

            GameObject hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(hudPath)
                ?? throw new AssertionException($"缺少 Buff HUD Prefab：{hudPath}");
            RectTransform rootRect = hudPrefab.GetComponent<RectTransform>()
                ?? throw new AssertionException("Buff HUD 根节点缺少 RectTransform。");
            Assert.That(rootRect.anchorMin, Is.EqualTo(new Vector2(0f, 0.5f)));
            Assert.That(rootRect.anchorMax, Is.EqualTo(new Vector2(0f, 0.5f)));
            Assert.That(rootRect.pivot, Is.EqualTo(new Vector2(0f, 0.5f)));
            Assert.That(rootRect.anchoredPosition, Is.EqualTo(new Vector2(32f, 0f)));
            Assert.That(rootRect.sizeDelta, Is.EqualTo(new Vector2(320f, 360f)));
            Assert.That(hudPrefab.GetComponent<CanvasGroup>(), Is.Not.Null);

            ScrollRect scroll = hudPrefab.GetComponentsInChildren<ScrollRect>(true)
                .Single(item => item.name == "内容列表");
            Assert.That(scroll.vertical, Is.True);
            Assert.That(scroll.horizontal, Is.False);
            Assert.That(scroll.viewport, Is.Not.Null);
            Assert.That(scroll.content, Is.Not.Null);
            Assert.That(scroll.content.GetComponent<VerticalLayoutGroup>(), Is.Not.Null);
            Assert.That(scroll.content.GetComponent<ContentSizeFitter>(), Is.Not.Null);

            foreach (Graphic graphic in hudPrefab.GetComponentsInChildren<Graphic>(true))
                Assert.That(graphic.raycastTarget, Is.False, $"Buff HUD 不应拦截输入：{graphic.name}");

            GameObject itemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(itemPath)
                ?? throw new AssertionException($"缺少 Buff 行 Prefab：{itemPath}");
            foreach (Graphic graphic in itemPrefab.GetComponentsInChildren<Graphic>(true))
                Assert.That(graphic.raycastTarget, Is.False, $"Buff 行不应拦截输入：{graphic.name}");

            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(playerPath)
                ?? throw new AssertionException($"缺少玩家 Prefab：{playerPath}");
            Assert.That(playerPrefab.GetComponent<PlayerBuffStatusHUD>(), Is.Not.Null,
                "Player 必须挂载 PlayerBuffStatusHUD，才能显示本地玩家 Buff。");

            string source = File.ReadAllText(sourcePath);
            Assert.That(source, Does.Contain("BuffAdded"));
            Assert.That(source, Does.Contain("BuffRemoved"));
            Assert.That(source, Does.Contain("BuffCountdownChanged"));
            Assert.That(source, Does.Contain("ActiveBuffs"));
            Assert.That(source, Does.Contain("LayoutRebuilder.MarkLayoutForRebuild"));
            Assert.That(source, Does.Not.Contain("private void LateUpdate()"));
            Assert.That(source, Does.Not.Contain("Canvas.ForceUpdateCanvases"));
            Assert.That(source, Does.Not.Contain("LayoutRebuilder.ForceRebuildLayoutImmediate"));
        }

        [Test]
        [Category("UI.Smoke")]
        [Category("UI.Layout")]
        public void QuestTrackerHudPrefabAndPlayerBindingFollowContract()
        {
            const string hudPath =
                "Assets/2_Prefabs/2-1_UI/Gameplay/Status/Quest/UI_QuestTracker.prefab";
            const string itemPath =
                "Assets/2_Prefabs/2-1_UI/Gameplay/Status/Quest/UI_QuestTrackerItem.prefab";
            const string playerPath = "Assets/2_Prefabs/Gameplay/Player/Player.prefab";
            const string sourcePath =
                "Assets/5_Scripts/5-3_GamePlay/Presentation/UI/PlayerQuestTrackerHUD.cs";

            AssertPrefabContains(
                hudPath,
                "背景",
                "强调线",
                "标题",
                "数量文本",
                "空状态文本",
                "任务面板开关按钮",
                "内容列表",
                "Viewport",
                "Content");
            AssertPrefabContains(
                itemPath,
                "状态线",
                "任务标题",
                "任务状态",
                "任务说明",
                "目标文本",
                "进度背景",
                "进度填充");

            GameObject hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(hudPath)
                ?? throw new AssertionException($"缺少任务追踪 HUD Prefab：{hudPath}");
            RectTransform rootRect = hudPrefab.GetComponent<RectTransform>()
                ?? throw new AssertionException("任务追踪 HUD 根节点缺少 RectTransform。");
            Assert.That(rootRect.anchorMin, Is.EqualTo(new Vector2(1f, 1f)));
            Assert.That(rootRect.anchorMax, Is.EqualTo(new Vector2(1f, 1f)));
            Assert.That(rootRect.pivot, Is.EqualTo(new Vector2(1f, 1f)));
            Assert.That(rootRect.anchoredPosition, Is.EqualTo(new Vector2(-24f, -168f)));
            Assert.That(rootRect.sizeDelta, Is.EqualTo(new Vector2(300f, 300f)));

            RectTransform content = hudPrefab.GetComponentsInChildren<RectTransform>(true)
                .Single(item => item.name == "Content");
            Assert.That(content.GetComponent<VerticalLayoutGroup>(), Is.Not.Null);
            Assert.That(content.GetComponent<ContentSizeFitter>(), Is.Not.Null);

            Button toggleButton = hudPrefab.GetComponentsInChildren<Button>(true)
                .Single(item => item.name == "任务面板开关按钮");
            Image toggleImage = toggleButton.GetComponent<Image>();
            Assert.That(toggleImage, Is.Not.Null);
            Assert.That(toggleImage.raycastTarget, Is.True);

            foreach (Graphic graphic in hudPrefab.GetComponentsInChildren<Graphic>(true))
                if (graphic.gameObject.name != "任务面板开关按钮")
                    Assert.That(graphic.raycastTarget, Is.False, $"任务追踪 HUD 不应拦截输入：{graphic.name}");

            GameObject itemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(itemPath)
                ?? throw new AssertionException($"缺少任务追踪行 Prefab：{itemPath}");
            Assert.That(itemPrefab.GetComponent<QuestTrackerRowView>(), Is.Not.Null);
            Image progressFill = itemPrefab.GetComponentsInChildren<Image>(true)
                .Single(item => item.name == "进度填充");
            Assert.That(progressFill.type, Is.EqualTo(Image.Type.Filled));
            foreach (Graphic graphic in itemPrefab.GetComponentsInChildren<Graphic>(true))
                Assert.That(graphic.raycastTarget, Is.False, $"任务追踪行不应拦截输入：{graphic.name}");

            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(playerPath)
                ?? throw new AssertionException($"缺少玩家 Prefab：{playerPath}");
            Assert.That(playerPrefab.GetComponent<PlayerQuestTrackerHUD>(), Is.Not.Null,
                "Player 必须挂载 PlayerQuestTrackerHUD，才能显示本地玩家任务。");

            string source = File.ReadAllText(sourcePath);
            Assert.That(source, Does.Contain("RuntimeReady"));
            Assert.That(source, Does.Contain("RuntimeRemoving"));
            Assert.That(source, Does.Contain("QuestChanged"));
            Assert.That(source, Does.Contain("GetSnapshots"));
            Assert.That(source, Does.Contain("LayoutRebuilder.MarkLayoutForRebuild"));
            Assert.That(source, Does.Not.Contain("private void Update()"));
            Assert.That(source, Does.Not.Contain("private void LateUpdate()"));
            Assert.That(source, Does.Not.Contain("Canvas.ForceUpdateCanvases"));
            Assert.That(source, Does.Not.Contain("LayoutRebuilder.ForceRebuildLayoutImmediate"));
        }

        [Test]
        [Category("UI.Smoke")]
        public void SharedUiRefreshDriversStayIdleUntilEventsArrive()
        {
            const string feedbackPath =
                "Assets/5_Scripts/5-5_UI/Common/Presentation/FlatWorldUIFeedback.cs";
            const string settingsPath =
                "Assets/5_Scripts/5-5_UI/Settings/UIUserSettings.cs";

            string feedbackSource = File.ReadAllText(feedbackPath);
            string settingsSource = File.ReadAllText(settingsPath);

            Assert.That(feedbackSource, Does.Contain("using DG.Tweening;"));
            Assert.That(feedbackSource, Does.Contain("IPointerEnterHandler"));
            Assert.That(feedbackSource, Does.Contain("DOScale"));
            Assert.That(feedbackSource, Does.Contain("SetUpdate(true)"));
            Assert.That(feedbackSource, Does.Contain("KillScaleTween"));
            Assert.That(feedbackSource, Does.Not.Contain("private void Update()"));
            Assert.That(feedbackSource, Does.Not.Contain("FlatWorldUIFeedbackInputRelay"));

            Assert.That(settingsSource, Does.Contain("public static event Action Changed;"));
            Assert.That(settingsSource, Does.Contain("UIUserSettings.Changed += HandleSettingsChanged;"));
            Assert.That(settingsSource, Does.Contain("OnRectTransformDimensionsChange"));
            Assert.That(settingsSource, Does.Not.Contain("private void Update()"));
        }

        [Test]
        [Category("UI.Smoke")]
        public void SharedUiHierarchyAndVirtualCursorUseDirtyCaches()
        {
            const string basePanelPath =
                "Assets/5_Scripts/5-5_UI/Core/BasePanel.cs";
            const string uiManagerPath =
                "Assets/5_Scripts/5-5_UI/Core/UIManager.cs";
            const string gamepadControllerPath =
                "Assets/5_Scripts/5-5_UI/Input/Gamepad/GamepadUIRuntimeController.cs";
            const string inputBindingLauncherPath =
                "Assets/5_Scripts/5-3_GamePlay/Presentation/UI/InputBindingPanelLauncher.cs";

            string basePanelSource = File.ReadAllText(basePanelPath);
            string uiManagerSource = File.ReadAllText(uiManagerPath);
            string gamepadControllerSource = File.ReadAllText(gamepadControllerPath);
            string inputBindingLauncherSource = File.ReadAllText(inputBindingLauncherPath);

            Assert.That(
                basePanelSource,
                Does.Contain("GetComponentsInChildren<Component>(true, hierarchyComponents);"));
            Assert.That(basePanelSource, Does.Contain("HierarchySnapshotRebuildCount"));
            Assert.That(basePanelSource, Does.Contain("ApplySelectionColors(cachedSelectables)"));
            Assert.That(basePanelSource, Does.Not.Contain("GetComponentsInChildren<Button>(true)"));
            Assert.That(basePanelSource, Does.Not.Contain("GetComponentsInChildren<Selectable>(true)"));

            Assert.That(uiManagerSource, Does.Contain("public Canvas RootCanvas"));
            Assert.That(uiManagerSource, Does.Contain("InteractionSurfaceRevision"));
            Assert.That(uiManagerSource, Does.Contain("NotifyInteractionSurfaceChanged"));
            Assert.That(uiManagerSource, Does.Contain("PanelQueryCacheRebuildCount"));
            Assert.That(uiManagerSource, Does.Contain("panelQueryCacheRevision"));
            Assert.That(
                uiManagerSource,
                Does.Contain(
                    "child.GetComponentsInChildren<BasePanel>(true, panelQueryBuffer);"));

            Assert.That(gamepadControllerSource, Does.Contain("hoverTargetDirty"));
            Assert.That(gamepadControllerSource, Does.Contain("StationaryHoverRefreshSeconds"));
            Assert.That(gamepadControllerSource, Does.Contain("HoverRaycastCount"));
            Assert.That(gamepadControllerSource, Does.Not.Contain("FindObjectsOfType<Canvas>"));
            Assert.That(gamepadControllerSource, Does.Not.Contain("GameObject.Find(\"PanelRoot\")"));

            Assert.That(
                inputBindingLauncherSource,
                Does.Contain("bindingPanel?.RefreshUIComponents();"),
                "动态生成按键行后必须显式提交 BasePanel 层级快照。");
            Assert.That(inputBindingLauncherSource, Does.Contain("pooledRows"));
            Assert.That(inputBindingLauncherSource, Does.Contain("RetainedRowCount"));
            Assert.That(inputBindingLauncherSource, Does.Not.Contain("Canvas.ForceUpdateCanvases()"));
            Assert.That(
                inputBindingLauncherSource,
                Does.Not.Contain("LayoutRebuilder.ForceRebuildLayoutImmediate"));
        }

        [Test]
        [Category("UI.Smoke")]
        public void RemainingUiHotPathsUsePoolsEventsAndLocalLayoutMarks()
        {
            string saveListSource = File.ReadAllText(
                "Assets/5_Scripts/5-3_GamePlay/Presentation/UI/SaveDataManager_UI.cs");
            string resizerSource = File.ReadAllText(
                "Assets/5_Scripts/5-5_UI/Common/Controls/UIDragResizer.cs");
            string contentMarkerSource = File.ReadAllText(
                "Assets/5_Scripts/5-5_UI/Gameplay/Inventory/UI_Content.cs");
            string paginationSource = File.ReadAllText(
                "Assets/5_Scripts/5-3_GamePlay/Presentation/UI/SettingsActionListPagination.cs");
            string coordinateHudSource = File.ReadAllText(
                "Assets/5_Scripts/5-3_GamePlay/Presentation/UI/PlayerWorldCoordinateHUD.cs");
            string coordinatePreferencesSource = File.ReadAllText(
                "Assets/5_Scripts/5-3_GamePlay/Presentation/UI/PlayerWorldCoordinateDisplayPreferences.cs");

            Assert.That(saveListSource, Does.Contain("RetainedEntryCount"));
            Assert.That(saveListSource, Does.Contain("pooledRows"));
            Assert.That(saveListSource, Does.Contain("LayoutRebuilder.MarkLayoutForRebuild"));
            Assert.That(saveListSource, Does.Not.Contain("Canvas.ForceUpdateCanvases()"));
            Assert.That(saveListSource, Does.Not.Contain("ClearDynamicButtons"));

            Assert.That(resizerSource, Does.Contain("IPointerMoveHandler"));
            Assert.That(resizerSource, Does.Not.Contain("void Update()"));
            Assert.That(contentMarkerSource, Does.Not.Contain("void Update()"));

            Assert.That(paginationSource, Does.Contain("RefreshGamepadNavigationState"));
            Assert.That(paginationSource, Does.Contain("LayoutRebuilder.MarkLayoutForRebuild"));
            Assert.That(paginationSource, Does.Not.Contain("Canvas.ForceUpdateCanvases()"));
            Assert.That(
                paginationSource,
                Does.Not.Contain("LayoutRebuilder.ForceRebuildLayoutImmediate"));

            Assert.That(coordinatePreferencesSource, Does.Contain("public static event Action Changed;"));
            Assert.That(coordinateHudSource, Does.Contain("WaitForSecondsRealtime"));
            Assert.That(coordinateHudSource, Does.Contain("RefreshIntervalSeconds = 0.1f"));
            Assert.That(coordinateHudSource, Does.Not.Contain("private void LateUpdate()"));
        }

        [Test]
        [Category("UI.Smoke")]
        public void BasePanelReusesHierarchySnapshotUntilExplicitRefresh()
        {
            GameObject panelObject = new GameObject(
                "层级快照测试面板",
                typeof(RectTransform),
                typeof(CanvasGroup));

            try
            {
                BasePanel panel = panelObject.AddComponent<BasePanel>();
                GameObject contentObject = new GameObject("Content", typeof(RectTransform));
                contentObject.transform.SetParent(panelObject.transform, false);
                GameObject buttonObject = new GameObject(
                    "动态按钮",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image),
                    typeof(Button));
                buttonObject.transform.SetParent(contentObject.transform, false);

                panel.RefreshUIComponents();
                int rebuildCount = panel.HierarchySnapshotRebuildCount;

                Assert.That(panel.GetButton("动态按钮"), Is.SameAs(buttonObject.GetComponent<Button>()));
                panel.GetButton("动态按钮");
                panel.PrepareForGamepadNavigation("动态按钮", false, false);
                panel.PrepareForGamepadNavigation("动态按钮", false, false);
                panel.RefreshGamepadNavigationState();

                Assert.That(
                    panel.HierarchySnapshotRebuildCount,
                    Is.EqualTo(rebuildCount),
                    "同一层级版本内的查询和导航准备不得再次扫描层级。");
                Assert.That(panel.CachedSelectableCount, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(panelObject);
            }
        }

        [Test]
        [Category("UI.Smoke")]
        public void GamepadSelectionFollowerCoalescesLatestTargetPerScrollRect()
        {
            const string sourcePath =
                "Assets/5_Scripts/5-5_UI/Input/Gamepad/GamepadUISelectionFollower.cs";
            MethodInfo resetScheduler = typeof(GamepadUISelectionFollower).GetMethod(
                "ResetScheduler",
                BindingFlags.Static | BindingFlags.NonPublic);
            FieldInfo pendingRequestsField = typeof(GamepadUISelectionFollower).GetField(
                "pendingRequests",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(resetScheduler, Is.Not.Null);
            Assert.That(pendingRequestsField, Is.Not.Null);

            GameObject scrollObject = new GameObject(
                "焦点合并测试_ScrollRect",
                typeof(RectTransform),
                typeof(ScrollRect));

            try
            {
                resetScheduler.Invoke(null, null);
                ScrollRect scrollRect = scrollObject.GetComponent<ScrollRect>();
                GameObject viewportObject = new GameObject("Viewport", typeof(RectTransform));
                viewportObject.transform.SetParent(scrollObject.transform, false);
                GameObject contentObject = new GameObject("Content", typeof(RectTransform));
                contentObject.transform.SetParent(viewportObject.transform, false);
                scrollRect.viewport = viewportObject.GetComponent<RectTransform>();
                scrollRect.content = contentObject.GetComponent<RectTransform>();

                GameObject firstTarget = new GameObject("目标一", typeof(RectTransform));
                firstTarget.transform.SetParent(contentObject.transform, false);
                GamepadUISelectionFollower firstFollower =
                    firstTarget.AddComponent<GamepadUISelectionFollower>();
                GameObject latestTarget = new GameObject("目标二", typeof(RectTransform));
                latestTarget.transform.SetParent(contentObject.transform, false);
                GamepadUISelectionFollower latestFollower =
                    latestTarget.AddComponent<GamepadUISelectionFollower>();

                firstFollower.OnSelect(null);
                latestFollower.OnSelect(null);

                System.Collections.IDictionary pendingRequests =
                    pendingRequestsField.GetValue(null) as System.Collections.IDictionary;
                Assert.That(pendingRequests, Is.Not.Null);
                Assert.That(pendingRequests.Count, Is.EqualTo(1));
                Assert.That(pendingRequests[scrollRect], Is.SameAs(latestFollower));

                string source = File.ReadAllText(sourcePath);
                Assert.That(source, Does.Contain("Canvas.willRenderCanvases += FlushPendingRequests;"));
                Assert.That(
                    source,
                    Does.Contain("LayoutRebuilder.ForceRebuildLayoutImmediate(follower.content);"));
                Assert.That(source, Does.Not.Contain("Canvas.ForceUpdateCanvases()"));
                Assert.That(source, Does.Not.Contain("private void Update()"));
                Assert.That(source, Does.Not.Contain("private void LateUpdate()"));
            }
            finally
            {
                resetScheduler?.Invoke(null, null);
                Object.DestroyImmediate(scrollObject);
            }
        }

        [Test]
        [Category("UI.Smoke")]
        [Category("Smoke")]
        public void CoordinateDisplaySettingsAndActionListPagerFollowContract()
        {
            const string displaySettingsPath =
                "Assets/2_Prefabs/2-1_UI/Settings/Panels/UI_CoordinateDisplaySettings.prefab";
            const string actionListPath =
                "Assets/2_Prefabs/2-1_UI/MainMenu/Core/UI_ActionList.prefab";

            AssertPrefabContains(
                displaySettingsPath,
                "世界坐标模式按钮",
                "经纬度模式按钮",
                "状态文本",
                "完成按钮");
            AssertPrefabContains(
                actionListPath,
                SettingsActionListPagination.InterfacePageName,
                SettingsActionListPagination.WorldPageName,
                SettingsActionListPagination.SessionPageName,
                SettingsActionListPagination.PreviousButtonName,
                SettingsActionListPagination.NextButtonName,
                SettingsActionListPagination.PageTextName,
                "显示设置");

            GameObject displaySettings = AssetDatabase.LoadAssetAtPath<GameObject>(displaySettingsPath)
                ?? throw new AssertionException($"缺少显示设置 Prefab：{displaySettingsPath}");
            RectTransform displayRoot = displaySettings.GetComponent<RectTransform>()
                ?? throw new AssertionException("显示设置根节点缺少 RectTransform。");
            Assert.That(displayRoot.sizeDelta, Is.EqualTo(new Vector2(620f, 360f)));
            Assert.That(displaySettings.GetComponent<VerticalLayoutGroup>(), Is.Not.Null);

            GameObject actionList = AssetDatabase.LoadAssetAtPath<GameObject>(actionListPath)
                ?? throw new AssertionException($"缺少设置入口 Prefab：{actionListPath}");
            ScrollRect scroll = actionList.GetComponentsInChildren<ScrollRect>(true)
                .Single(item => item.name == "Scroll View");
            Assert.That(scroll.vertical, Is.False, "分页列表不能继续依赖纵向滚动。");

            RectTransform content = actionList.GetComponentsInChildren<RectTransform>(true)
                .Single(item => item.name == "Content");
            Assert.That(content.GetComponent<GridLayoutGroup>(), Is.Null);
            Assert.That(content.GetComponent<ContentSizeFitter>(), Is.Null);

            string[] pageNames =
            {
                SettingsActionListPagination.InterfacePageName,
                SettingsActionListPagination.WorldPageName,
                SettingsActionListPagination.SessionPageName
            };
            for (int index = 0; index < pageNames.Length; index++)
            {
                RectTransform page = actionList.GetComponentsInChildren<RectTransform>(true)
                    .Single(item => item.name == pageNames[index]);
                Assert.That(page.anchorMin, Is.EqualTo(Vector2.zero), pageNames[index]);
                Assert.That(page.anchorMax, Is.EqualTo(Vector2.one), pageNames[index]);
                Assert.That(page.GetComponent<VerticalLayoutGroup>(), Is.Not.Null, pageNames[index]);
            }

            Button displayEntry = actionList.GetComponentsInChildren<Button>(true)
                .Single(item => item.name == "显示设置");
            Assert.That(
                displayEntry.transform.parent.name,
                Is.EqualTo(SettingsActionListPagination.InterfacePageName));
        }


[Test]
        [Category("UI.Smoke")]
        [Category("Smoke")]
        public void CancelRoutingSelectsTopmostTemporaryPanelAndSkipsHudAndSettings()
        {
            GameObject rootObject = new GameObject("CancelRoutingRoot", typeof(RectTransform));
            try
            {
                BasePanel hud = CreateTestPanel(rootObject.transform, "HUD", true, false);
                BasePanel bag = CreateTestPanel(rootObject.transform, "Bag", true);
                BasePanel settings = CreateTestPanel(rootObject.transform, "Settings", true);

                Assert.That(
                    UIManager.FindTopmostCancelPanel(rootObject.transform, settings),
                    Is.SameAs(bag));

                bag.Close();
                Assert.That(
                    UIManager.FindTopmostCancelPanel(rootObject.transform, settings),
                    Is.Null);
                Assert.That(hud.IsOpen(), Is.True, "常驻 HUD 不应成为 Escape 关闭目标。");
                Assert.That(settings.IsOpen(), Is.True, "设置面板由调用方单独切换，不应在额外面板阶段关闭。");
            }
            finally
            {
                Object.DestroyImmediate(rootObject);
            }
        }






        [Test]
        [Category("UI.Layout")]
        public void CharacterStatusPanelContainsTemperatureAndFitsSupportedScreens()
        {
            const string prefabPath = "Assets/2_Prefabs/2-1_UI/Gameplay/Status/Player/UI_Food.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, $"缺少角色参数面板：{prefabPath}");

            RectTransform rootRect = prefab.GetComponent<RectTransform>();
            RectTransform panelRect = prefab.GetComponentsInChildren<RectTransform>(true)
                .Single(rect => rect.name == "Panel");
            Assert.That(rootRect.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(rootRect.anchorMax, Is.EqualTo(Vector2.one),
                "参数面板根节点应铺满 Canvas，拖拽坐标才能与 Canvas 坐标一致。");
            Assert.That(panelRect.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(panelRect.anchorMax, Is.EqualTo(Vector2.zero));
            Assert.That(panelRect.pivot, Is.EqualTo(Vector2.zero));
            Assert.That(panelRect.anchoredPosition, Is.EqualTo(new Vector2(28f, 28f)));
            Assert.That(panelRect.sizeDelta, Is.EqualTo(new Vector2(326f, 196f)));

            Image panelImage = panelRect.GetComponent<Image>();
            Assert.That(panelImage, Is.Not.Null);
            Assert.That(panelImage.enabled, Is.False, "参数 HUD 不应保留整块背景。");
            Assert.That(panelImage.raycastTarget, Is.False);
            Assert.That(panelRect.GetComponent<Outline>(), Is.Null,
                "参数 HUD 不应保留整块描边。");
            Assert.That(prefab.GetComponent<CanvasGroup>().blocksRaycasts, Is.False,
                "参数 HUD 不应拦截世界输入。");
            Assert.That(panelRect.GetComponentsInChildren<Graphic>(true)
                .All(graphic => !graphic.raycastTarget), Is.True,
                "参数 HUD 的状态条和文字不应拦截世界输入。");

            string[] rowNames = { "碳水", "脂肪", "蛋白质", "水", "维生素", "体温" };
            Slider[] sliders = prefab.GetComponentsInChildren<Slider>(true);
            for (int i = 0; i < rowNames.Length; i++)
            {
                Slider slider = sliders.Single(item => item.name == rowNames[i]);
                RectTransform row = slider.GetComponent<RectTransform>();
                Assert.That(row.anchorMin, Is.EqualTo(new Vector2(0f, 1f)), rowNames[i]);
                Assert.That(row.anchorMax, Is.EqualTo(new Vector2(0f, 1f)), rowNames[i]);
                Assert.That(row.anchoredPosition,
                    Is.EqualTo(new Vector2(0f, -(i * 34f))), rowNames[i]);
                Assert.That(row.sizeDelta, Is.EqualTo(new Vector2(326f, 26f)), rowNames[i]);
                Assert.That(slider.interactable, Is.False, $"{rowNames[i]} 只是状态显示，不应可拖动。");
            }

            TextMeshProUGUI temperatureText = prefab.GetComponentsInChildren<TextMeshProUGUI>(true)
                .SingleOrDefault(text => text.name == "DataText_体温");
            Assert.That(temperatureText, Is.Not.Null);
            Assert.That(temperatureText.raycastTarget, Is.False);
            Assert.That(prefab.GetComponentsInChildren<TextMeshProUGUI>(true)
                .Any(text => text.name == "FWUI_Card标题"), Is.False,
                "参数 HUD 不应保留单独的标题文字。");

            Vector2[] resolutions =
            {
                new Vector2(2560f, 1440f),
                new Vector2(1920f, 1080f),
                new Vector2(1600f, 900f),
                new Vector2(1280f, 720f),
                new Vector2(1024f, 768f)
            };
            foreach (Vector2 resolution in resolutions)
            {
                float widthScale = resolution.x / 1920f;
                float logicalHeight = resolution.y / widthScale;
                Rect canvasBounds = new Rect(-960f, -logicalHeight * 0.5f, 1920f, logicalHeight);
                Rect panelBounds = new Rect(
                    new Vector2(
                        canvasBounds.xMin + panelRect.anchoredPosition.x,
                        canvasBounds.yMin + panelRect.anchoredPosition.y),
                    panelRect.sizeDelta);
                Assert.That(panelBounds.xMin, Is.GreaterThanOrEqualTo(canvasBounds.xMin + 20f), resolution.ToString());
                Assert.That(panelBounds.xMax, Is.LessThanOrEqualTo(canvasBounds.xMax - 20f), resolution.ToString());
                Assert.That(panelBounds.yMin, Is.GreaterThanOrEqualTo(canvasBounds.yMin + 20f), resolution.ToString());
                Assert.That(panelBounds.yMax, Is.LessThanOrEqualTo(canvasBounds.yMax - 20f), resolution.ToString());
            }
        }

        private static void AssertPrefabContains(string prefabPath, params string[] expectedNames)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, $"缺少 UI Prefab：{prefabPath}");

            string[] objectNames = prefab.GetComponentsInChildren<Transform>(true)
                .Select(item => item.name)
                .ToArray();
            foreach (string expectedName in expectedNames)
                Assert.That(objectNames, Does.Contain(expectedName), $"{prefabPath} 缺少节点：{expectedName}");
        }

        [Test]
        [Category("UI.Smoke")]
        [Category("Smoke")]
        public void DimensionLoadingPrefabIsFullScreenBlockingAndThemeReady()
        {
            const string prefabPath =
                "Assets/2_Prefabs/2-1_UI/Gameplay/Loading/UI_DimensionLoading.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, "缺少维度切换专属加载页 Prefab。");

            RectTransform root = prefab.GetComponent<RectTransform>();
            Canvas canvas = prefab.GetComponent<Canvas>();
            CanvasGroup canvasGroup = prefab.GetComponent<CanvasGroup>();
            Image blocker = prefab.GetComponent<Image>();
            Assert.That(root.offsetMin, Is.EqualTo(Vector2.zero));
            Assert.That(root.offsetMax, Is.EqualTo(Vector2.zero));
            Assert.That(canvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            Assert.That(canvas.sortingOrder, Is.EqualTo(32000));
            Assert.That(canvasGroup.interactable, Is.True);
            Assert.That(canvasGroup.blocksRaycasts, Is.True);
            Assert.That(blocker.raycastTarget, Is.True);

            AssertPrefabContains(
                prefabPath,
                GameManager.DimensionLoadingBackgroundKey,
                GameManager.DimensionLoadingTextureKey,
                GameManager.DimensionLoadingIconKey,
                GameManager.DimensionLoadingNameKey,
                GameManager.DimensionLoadingStatusKey,
                GameManager.DimensionLoadingProgressKey,
                GameManager.DimensionLoadingProgressTextKey,
                GameManager.DimensionLoadingHintKey,
                GameManager.DimensionLoadingProgressFillKey);

            string addressables = File.ReadAllText(
                "Assets/AddressableAssetsData/AssetGroups/Default.asset");
            Assert.That(addressables, Does.Contain(prefabPath));
            Assert.That(RuntimeUIPrefabKeys.DimensionLoading, Is.EqualTo("UI_DimensionLoading"));
        }

        private static BasePanel CreateTestPanel(
            Transform parent,
            string name,
            bool prepareForCancel,
            bool closeOnEscape = true)
        {
            GameObject panelObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(BasePanel));
            panelObject.transform.SetParent(parent, false);

            BasePanel panel = panelObject.GetComponent<BasePanel>();
            panel.Init();
            if (prepareForCancel)
                panel.PrepareForGamepadNavigation(closeOnEscape: closeOnEscape);
            panel.Open();
            return panel;
        }
    }
}
