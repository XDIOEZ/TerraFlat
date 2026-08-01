using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
namespace FlatWorld.Dialogue
{
    /// <summary>
    /// 本地玩家聊天输入控制器。
    /// 绑定动作打开、Enter 提交、Esc 取消；提交后复用角色现有屏幕空间气泡。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Player))]
    [RequireComponent(typeof(GameController))]
    [RequireComponent(typeof(CharacterSoliloquyController))]
    public sealed class PlayerChatInputController : MonoBehaviour
    {
        #region 常量与配置

        public const string ViewName = "PlayerChatInput";
        public const string ChatTopic = "player.chat";
        public const string CommandTopic = "player.chat.command";
        public const string OpenChatActionName = "OpenChat";

        [Header("聊天输入")]
        [SerializeField, Min(1)] private int characterLimit = 160;
        [SerializeField] private CharacterSpeechPriority speechPriority =
            CharacterSpeechPriority.Player;

        

        #endregion

        #region 运行时状态

        private readonly List<IPlayerChatCommandHandler> commandHandlers =
            new List<IPlayerChatCommandHandler>();

        private Player player;
        private GameController gameController;
        private CharacterSoliloquyController speechController;
        private GameObject viewObject;
        private RectTransform viewRect;
        private TMP_InputField inputField;
        private bool isOpen;
        private bool inputSuspended;
        private bool previousGameplayLock;
        private Coroutine focusRoutine;
        private InputAction openChatAction;

        public bool IsOpen => isOpen;
        public TMP_InputField InputField => inputField;

        #endregion

        #region Unity 生命周期

        private void Awake()
        {
            ResolveReferences();
            RebuildCommandHandlers();
        }

        private void OnEnable()
        {
            ResolveReferences();
            BindOpenChatInput();
            if (player != null)
                player.ProfileContextChanged += HandleProfileContextChanged;
        }

        private void Start()
        {
            BindOpenChatInput();
        }

        private void OnDisable()
        {
            UnbindOpenChatInput();
            if (player != null)
                player.ProfileContextChanged -= HandleProfileContextChanged;
            CloseChat(clearText: true);
        }

        private void OnDestroy()
        {
            if (viewObject != null)
                Destroy(viewObject);
        }

        private void Update()
        {
            if (!CanUseChat())
            {
                if (isOpen)
                    CloseChat(clearText: true);
                return;
            }

            if (!isOpen)
                return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                CloseChat(clearText: true);
                return;
            }

            if (keyboard.enterKey.wasPressedThisFrame ||
                keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                if (!string.IsNullOrEmpty(Input.compositionString))
                    return;

                SubmitCurrentText();
            }
        }

        #endregion

        #region 对外接口

        private void BindOpenChatInput()
        {
            UnbindOpenChatInput();
            ResolveReferences();

            InputActionMap actionMap = gameController?.InputAsset?
                .FindActionMap("Win10", false);
            openChatAction = actionMap?.FindAction(OpenChatActionName, false);
            if (openChatAction != null)
                openChatAction.performed += HandleOpenChatPerformed;
        }

        private void UnbindOpenChatInput()
        {
            if (openChatAction == null)
                return;

            openChatAction.performed -= HandleOpenChatPerformed;
            openChatAction = null;
        }

        private void HandleOpenChatPerformed(InputAction.CallbackContext context)
        {
            Keyboard keyboard = context.control?.device as Keyboard;
            if (keyboard != null &&
                context.control == keyboard.tKey &&
                IsControlPressed(keyboard))
            {
                return;
            }

            OpenChat();
        }

        /// <summary>重新扫描玩家节点上的显式命令处理器。</summary>
        public void RebuildCommandHandlers()
        {
            commandHandlers.Clear();
            MonoBehaviour[] behaviours = GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IPlayerChatCommandHandler handler)
                    commandHandlers.Add(handler);
            }

            commandHandlers.Sort((left, right) =>
                right.CommandOrder.CompareTo(left.CommandOrder));
        }

        public bool OpenChat()
        {
            ResolveReferences();
            if (isOpen || !CanUseChat() || gameController.IsGameplayInputLocked || !EnsureView())
                return false;

            previousGameplayLock = gameController.IsGameplayInputLocked;
            gameController.SetGameplayInputLocked(true);
            gameController.InputBindings?.SuspendGameplayInput();
            inputSuspended = gameController.InputBindings != null;

            isOpen = true;
            viewObject.SetActive(true);
            viewRect.SetAsLastSibling();
            inputField.text = string.Empty;
            focusRoutine = StartCoroutine(FocusInputNextFrame());
            return true;
        }

        public void CloseChat(bool clearText = true)
        {
            if (!isOpen && !inputSuspended)
            {
                if (clearText && inputField != null)
                    inputField.text = string.Empty;
                if (viewObject != null)
                    viewObject.SetActive(false);
                return;
            }

            isOpen = false;
            if (focusRoutine != null)
            {
                StopCoroutine(focusRoutine);
                focusRoutine = null;
            }

            if (inputField != null)
            {
                inputField.DeactivateInputField();
                if (clearText)
                    inputField.text = string.Empty;
            }

            if (viewObject != null)
                viewObject.SetActive(false);

            if (inputSuspended)
            {
                gameController?.InputBindings?.ResumeGameplayInput();
                inputSuspended = false;
            }

            gameController?.SetGameplayInputLocked(previousGameplayLock);
        }

        public bool SubmitCurrentText()
        {
            if (!isOpen || inputField == null)
                return false;

            string text = inputField.text;
            bool submitted = TrySubmitText(text);
            if (submitted)
                CloseChat(clearText: true);
            else
            {
                inputField.Select();
                inputField.ActivateInputField();
            }

            return submitted;
        }

        private IEnumerator FocusInputNextFrame()
        {
            yield return null;
            focusRoutine = null;
            if (!isOpen || inputField == null)
                yield break;

            inputField.Select();
            inputField.ActivateInputField();
            EventSystem.current?.SetSelectedGameObject(inputField.gameObject);
        }

        /// <summary>
        /// 提交文本。斜杠内容优先交给显式命令处理器，未识别时仍作为普通发言显示。
        /// </summary>
        public bool TrySubmitText(string text)
        {
            ResolveReferences();
            if (!CanUseChat() || speechController == null)
                return false;

            string normalized = NormalizeText(text);
            if (string.IsNullOrEmpty(normalized))
                return false;

            if (normalized[0] == '/' && TryExecuteCommand(normalized))
                return true;

            return speechController.Present(new CharacterSpeechRequest(
                normalized,
                ChatTopic,
                speechPriority)
            {
                SourceId = ChatTopic
            });
        }

        #endregion

        #region 命令扩展

        private bool TryExecuteCommand(string rawText)
        {
            string commandLine = rawText.Substring(1).Trim();
            if (commandLine.Length == 0)
                return false;

            string[] tokens = commandLine.Split(
                new[] { ' ', '\t' },
                StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                return false;

            string commandName = tokens[0].ToLowerInvariant();
            string[] arguments = new string[Mathf.Max(0, tokens.Length - 1)];
            if (arguments.Length > 0)
                Array.Copy(tokens, 1, arguments, 0, arguments.Length);

            PlayerChatCommandContext context = new PlayerChatCommandContext(
                player,
                speechController,
                rawText,
                commandName,
                arguments);

            for (int i = 0; i < commandHandlers.Count; i++)
            {
                try
                {
                    PlayerChatCommandResult result = commandHandlers[i].Execute(context);
                    if (!result.Handled)
                        continue;

                    if (!string.IsNullOrWhiteSpace(result.Feedback))
                    {
                        speechController.Present(new CharacterSpeechRequest(
                            result.Feedback.Trim(),
                            CommandTopic,
                            result.FeedbackPriority)
                        {
                            SourceId = CommandTopic
                        });
                    }

                    return true;
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }

            return false;
        }

        #endregion

        #region UI 构建

private bool EnsureView()
        {
            Transform panelRoot = UIManager.Instance?.panelRoot;
            if (panelRoot == null)
                return false;

            RectTransform rootRect = panelRoot as RectTransform ??
                                     panelRoot.GetComponent<RectTransform>();
            if (rootRect == null)
                return false;

            if (viewObject != null)
            {
                if (viewRect.parent != rootRect)
                    viewRect.SetParent(rootRect, false);
                return true;
            }

            GameObject prefab = GameRes.Instance?.GetPrefab(RuntimeUIPrefabKeys.PlayerChatInput);
            if (prefab == null)
            {
                Debug.LogError(
                    $"[PlayerChatInputController] 缺少 Prefab：{RuntimeUIPrefabKeys.PlayerChatInput}。",
                    this);
                return false;
            }

            viewObject = Instantiate(prefab, rootRect, false);
            viewObject.name = ViewName;
            viewRect = viewObject.GetComponent<RectTransform>();
            inputField = viewObject.GetComponent<TMP_InputField>() ??
                         viewObject.GetComponentInChildren<TMP_InputField>(true);
            if (viewRect == null || inputField == null)
            {
                Debug.LogError("[PlayerChatInputController] 聊天输入 Prefab 控件命名契约不完整。", viewObject);
                Destroy(viewObject);
                viewObject = null;
                return false;
            }

            inputField.characterLimit = Mathf.Max(1, characterLimit);
            inputField.lineType = TMP_InputField.LineType.SingleLine;
            inputField.contentType = TMP_InputField.ContentType.Standard;
            inputField.richText = false;
            inputField.restoreOriginalTextOnEscape = false;
            viewObject.SetActive(false);
            return true;
        }





        #endregion

        #region 资格与辅助

        private void ResolveReferences()
        {
            if (player == null)
                player = GetComponent<Player>();
            if (gameController == null)
                gameController = GetComponent<GameController>();
            if (speechController == null)
                speechController = GetComponent<CharacterSoliloquyController>();
        }

        private bool CanUseChat()
        {
            return isActiveAndEnabled &&
                   player != null &&
                   player.IsLocalProfile &&
                   gameController != null &&
                   speechController != null;
        }

        private void HandleProfileContextChanged()
        {
            if (!CanUseChat())
                CloseChat(clearText: true);
        }

        private string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            string normalized = text.Trim();
            int limit = Mathf.Max(1, characterLimit);
            return normalized.Length <= limit
                ? normalized
                : normalized.Substring(0, limit);
        }

            private static bool IsControlPressed(Keyboard keyboard)
            {
                return keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
            }

        #endregion
    }
}
