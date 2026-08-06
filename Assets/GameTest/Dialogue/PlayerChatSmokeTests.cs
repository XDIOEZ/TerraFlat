using FlatWorld.Dialogue;
using NUnit.Framework;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.GameTest.Dialogue
{
    /// <summary>玩家聊天冒烟测试：保护本地资格、输入条、气泡提交与命令扩展入口。</summary>
    public sealed class PlayerChatSmokeTests
    {
        #region Prefab 与输入框

        [Test]
        [Category("Dialogue.Smoke")]
        [Category("PlayerInteraction.Smoke")]
        [Category("UI.Smoke")]
        public void PlayerPrefabContainsSingleChatController()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Player/Player.prefab");

            Assert.That(prefab, Is.Not.Null);
            Assert.That(
                prefab.GetComponents<PlayerChatInputController>(),
                Has.Length.EqualTo(1));
        }

        [Test]
        [Category("PlayerInteraction.Smoke")]
        [Category("UI.Smoke")]
        public void OpenAndCloseChat_CreatesSingleLineInputAndRestoresLock()
        {
            Player player = CreateLocalPlayer("ChatInputActor");
            PlayerChatInputController chat =
                player.GetComponent<PlayerChatInputController>();
            GameController controller = player.GetComponent<GameController>();

            try
            {
                Assert.That(chat.OpenChat(), Is.True);
                Assert.That(chat.IsOpen, Is.True);
                Assert.That(controller.IsGameplayInputLocked, Is.True);
                Assert.That(chat.InputField, Is.Not.Null);
                Assert.That(chat.InputField.lineType, Is.EqualTo(TMP_InputField.LineType.SingleLine));
                Assert.That(chat.InputField.characterLimit, Is.EqualTo(160));
                Assert.That(chat.InputField.transform.parent.name, Is.EqualTo("PanelRoot"));
                Assert.That(chat.OpenChat(), Is.False, "重复打开不得创建第二个聊天输入条。");

                chat.CloseChat();
                Assert.That(chat.IsOpen, Is.False);
                Assert.That(controller.IsGameplayInputLocked, Is.False);
                Assert.That(chat.InputField.text, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(player.gameObject);
            }
        }

        [Test]
        [Category("PlayerInteraction.Smoke")]
        public void RemotePlayerCannotOpenOrSubmitChat()
        {
            Player player = CreatePlayer("RemoteChatActor", localProfile: false);
            PlayerChatInputController chat =
                player.GetComponent<PlayerChatInputController>();

            try
            {
                Assert.That(chat.OpenChat(), Is.False);
                Assert.That(chat.TrySubmitText("远程副本不应发言"), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(player.gameObject);
            }
        }

        [Test]
        [Category("PlayerInteraction.Smoke")]
        public void ChatAndAdminTeleportUseDistinctKeyChords()
        {
            string chatSource = File.ReadAllText(
                "Assets/5_Scripts/5-3_GamePlay/Dialogue/PlayerChatInputController.cs");
            string adminSource = File.ReadAllText(
                "Assets/5_Scripts/5-3_GamePlay/Controller/PlayerAdminController.cs");

            Assert.That(chatSource, Does.Contain("context.control == keyboard.tKey"));
            Assert.That(chatSource, Does.Contain("IsControlPressed(keyboard)"));
            Assert.That(adminSource, Does.Contain("Input.GetKey(KeyCode.LeftControl)"));
            Assert.That(adminSource, Does.Contain("Input.GetKey(KeyCode.RightControl)"));
        }

        #endregion

        #region 气泡与命令

        [Test]
        [Category("Dialogue.Smoke")]
        public void SubmitText_UsesExistingSpeechBubbleChain()
        {
            Player player = CreateLocalPlayer("ChatSpeechActor");
            EnsureMainCamera(player);
            PlayerChatInputController chat =
                player.GetComponent<PlayerChatInputController>();
            CharacterSoliloquyController speech =
                player.GetComponent<CharacterSoliloquyController>();
            CharacterSpeechRequest shown = null;
            speech.SpeechShown += request => shown = request;

            try
            {
                Assert.That(chat.TrySubmitText("  你好，世界！  "), Is.True);
                Assert.That(shown, Is.Not.Null);
                Assert.That(shown.Text, Is.EqualTo("你好，世界！"));
                Assert.That(shown.Topic, Is.EqualTo(PlayerChatInputController.ChatTopic));
                Assert.That(shown.Priority, Is.EqualTo(CharacterSpeechPriority.Player));
                Assert.That(chat.TrySubmitText("   "), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(player.gameObject);
            }
        }

        [Test]
        [Category("Dialogue.Smoke")]
        public void SlashCommand_UsesExplicitHandlerAndReturnsFeedback()
        {
            Player player = CreateLocalPlayer("ChatCommandActor");
            EnsureMainCamera(player);
            TestCommandHandler handler =
                player.gameObject.AddComponent<TestCommandHandler>();
            PlayerChatInputController chat =
                player.GetComponent<PlayerChatInputController>();
            CharacterSoliloquyController speech =
                player.GetComponent<CharacterSoliloquyController>();
            CharacterSpeechRequest shown = null;
            speech.SpeechShown += request => shown = request;
            chat.RebuildCommandHandlers();

            try
            {
                Assert.That(chat.TrySubmitText("/ping first second"), Is.True);
                Assert.That(handler.ExecutionCount, Is.EqualTo(1));
                Assert.That(handler.LastCommand, Is.EqualTo("ping"));
                Assert.That(handler.LastArgumentCount, Is.EqualTo(2));
                Assert.That(shown, Is.Not.Null);
                Assert.That(shown.Text, Is.EqualTo("pong"));
                Assert.That(shown.Topic, Is.EqualTo(PlayerChatInputController.CommandTopic));
            }
            finally
            {
                Object.DestroyImmediate(player.gameObject);
            }
        }

        #endregion

        #region 辅助

        private static Player CreateLocalPlayer(string instanceName)
        {
            return CreatePlayer(instanceName, localProfile: true);
        }

        private static Player CreatePlayer(string instanceName, bool localProfile)
        {
            RegisterRuntimeDialoguePrefabs();

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Player/Player.prefab");
            Assert.That(prefab, Is.Not.Null);

            GameObject instance = Object.Instantiate(prefab);
            instance.name = instanceName;
            Player player = instance.GetComponent<Player>();
            Assert.That(player, Is.Not.Null);
            player.SetProfileContext(localProfile, profileDataWasCreated: true);
            return player;
        }

        private static void RegisterRuntimeDialoguePrefabs()
        {
            EnsureRuntimeUiRoot();

            GameRes gameRes = GameRes.Instance;
            Assert.That(gameRes, Is.Not.Null, "运行时资源单例不可用。");

            RegisterRuntimePrefab(
                gameRes,
                RuntimeUIPrefabKeys.PlayerChatInput,
                "Assets/2_Prefabs/2-1_UI/Runtime/Dialogue/UI_PlayerChatInput.prefab");
            RegisterRuntimePrefab(
                gameRes,
                RuntimeUIPrefabKeys.CharacterSpeechBubble,
                "Assets/2_Prefabs/2-1_UI/Runtime/Dialogue/UI_CharacterSpeechBubble.prefab");
        }

        private static void EnsureRuntimeUiRoot()
        {
            UIManager uiManager = UIManager.Instance;
            Assert.That(uiManager, Is.Not.Null, "UI 管理器单例不可用。");
            if (uiManager.panelRoot != null)
                return;

            GameObject rootPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Resources/UI/UIRoot.prefab");
            Assert.That(rootPrefab, Is.Not.Null, "缺少运行时 UI 根 Prefab。");
            GameObject root = Object.Instantiate(rootPrefab);
            root.name = "PanelRoot";
            uiManager.panelRoot = root.transform;
        }

        private static void RegisterRuntimePrefab(GameRes gameRes, string key, string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, $"缺少运行时 UI Prefab：{path}");
            gameRes.AllPrefabs[key] = prefab;
        }

        private static void EnsureMainCamera(Player player)
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                GameObject cameraObject = new GameObject("ChatTestMainCamera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
                camera.orthographic = true;
                camera.transform.position = new Vector3(0f, 0f, -10f);
            }

            Vector3 actorPosition = player.transform.position;
            actorPosition.x = camera.transform.position.x;
            actorPosition.y = camera.transform.position.y;
            player.transform.position = actorPosition;
        }

        private sealed class TestCommandHandler : MonoBehaviour, IPlayerChatCommandHandler
        {
            public int ExecutionCount { get; private set; }
            public string LastCommand { get; private set; }
            public int LastArgumentCount { get; private set; }
            public int CommandOrder => 100;

            public PlayerChatCommandResult Execute(PlayerChatCommandContext context)
            {
                if (!string.Equals(context.CommandName, "ping"))
                    return PlayerChatCommandResult.NotHandled();

                ExecutionCount++;
                LastCommand = context.CommandName;
                LastArgumentCount = context.Arguments.Length;
                return PlayerChatCommandResult.HandledWith("pong");
            }
        }

        #endregion
    }
}
