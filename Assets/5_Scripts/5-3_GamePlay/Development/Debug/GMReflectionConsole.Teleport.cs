using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed partial class GMReflectionConsole
{
    #region 传送输入与目标选择

    /// <summary>只在选择传送落点时启用的独占点击层。</summary>
    private GameObject teleportTargetRoot;
    private GameController teleportInputOwner;
    private Mod_PlayerTraits teleportTargetPlayer;

    /// <summary>由唯一 GM 实例消费 Ctrl+T，不依赖角色显示名或重复管理员模块。</summary>
    private void HandleTeleportInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (teleportTargetRoot != null && teleportTargetRoot.activeSelf)
        {
            if (teleportInputOwner == null || teleportTargetPlayer == null)
                CancelTeleportTargeting();
            else if (keyboard?.escapeKey.wasPressedThisFrame == true)
                SetWindowVisible(true);
            return;
        }

        if (!PlayerAdminController.TeleportToMouseShortcutEnabled ||
            keyboard?.tKey.wasPressedThisFrame != true ||
            !(keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed))
            return;

        GameObject selected = EventSystem.current?.currentSelectedGameObject;
        if (selected != null &&
            (selected.GetComponent<TMP_InputField>() != null || selected.GetComponent<InputField>() != null))
            return;

        if (TryGetTeleportPlayer(out GameController controller, out Mod_PlayerTraits traits) &&
            !controller.IsGameplayInputLocked && !controller.IsPointerOverUI())
            traits.TryTeleportToScreenPosition(controller.GetPointerScreenPosition());
    }

    /// <summary>只从当前本地玩家解析模块，不扫描任意场景中的玩家副本。</summary>
    private static bool TryGetTeleportPlayer(out GameController controller, out Mod_PlayerTraits traits)
    {
        Player player = ItemMgr.Instance?.User_Player;
        controller = null;
        traits = null;
        if (player == null || !player.IsLocalProfile || player.Data == null)
            return false;

        controller = player.GetComponent<GameController>();
        traits = player.itemMods.GetMod_ByID<Mod_PlayerTraits>(Mod_PlayerTraits.ModuleId);
        return controller != null && traits != null;
    }

    /// <summary>收起 GM 后独占一次鼠标/触屏点选，输入锁同时释放已有摇杆和攻击。</summary>
    private void BeginTeleportTargeting()
    {
        if (!TryGetTeleportPlayer(out GameController controller, out Mod_PlayerTraits traits) ||
            controller.IsGameplayInputLocked)
        {
            SetStatus("当前无法传送：请进入游戏并关闭其他操作面板。", Color.yellow);
            return;
        }

        SetWindowVisible(false);
        teleportInputOwner = controller;
        teleportTargetPlayer = traits;
        controller.AcquireGameplayInputLock(this);
        teleportTargetRoot.SetActive(true);
    }

    /// <summary>按本次真实触点换算落点，不能读取手机摇杆准线或点击 GM 按钮的位置。</summary>
    private void CompleteTeleportTargeting(Vector2 screenPosition)
    {
        try
        {
            if (teleportTargetPlayer != null)
                teleportTargetPlayer.TryTeleportToScreenPosition(screenPosition);
        }
        finally
        {
            CancelTeleportTargeting();
        }
    }

    /// <summary>结束点选并释放自身输入锁；重复取消不会影响其他面板的锁。</summary>
    private void CancelTeleportTargeting()
    {
        if (teleportTargetRoot != null)
            teleportTargetRoot.SetActive(false);
        if (teleportInputOwner != null)
            teleportInputOwner.ReleaseGameplayInputLock(this);
        teleportInputOwner = null;
        teleportTargetPlayer = null;
    }

    /// <summary>禁用 GM 时结束未完成的点选。</summary>
    private void OnDisable() => CancelTeleportTargeting();

    /// <summary>失焦时释放触点及输入锁。</summary>
    private void OnApplicationFocus(bool hasFocus)
    {
        if (!hasFocus)
            CancelTeleportTargeting();
    }

    /// <summary>手机切入后台时释放触点及输入锁。</summary>
    private void OnApplicationPause(bool paused)
    {
        if (paused)
            CancelTeleportTargeting();
    }

    /// <summary>GM 调试 UI 使用临时全屏点选层，提示与 60 像素取消按钮约束在安全区。</summary>
    private void BuildTeleportTargeting(Transform canvas)
    {
        teleportTargetRoot = CreateUiObject("Teleport Target Selection", canvas);
        RectTransform rootRect = teleportTargetRoot.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = rootRect.offsetMax = Vector2.zero;
        teleportTargetRoot.AddComponent<Image>().color = Color.clear;
        teleportTargetRoot.AddComponent<GMTeleportTargetSurface>().PositionSelected += CompleteTeleportTargeting;

        GameObject content = CreateUiObject("Safe Area", teleportTargetRoot.transform);
        RectTransform contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = Vector2.zero;
        contentRect.anchorMax = Vector2.one;
        contentRect.offsetMin = contentRect.offsetMax = Vector2.zero;
        content.AddComponent<SafeAreaRectController>();

        GameObject toolbar = CreateUiObject("Teleport Instructions", content.transform);
        RectTransform toolbarRect = toolbar.GetComponent<RectTransform>();
        toolbarRect.anchorMin = new Vector2(0f, 1f);
        toolbarRect.anchorMax = Vector2.one;
        toolbarRect.pivot = new Vector2(0.5f, 1f);
        toolbarRect.anchoredPosition = new Vector2(0f, -20f);
        toolbarRect.sizeDelta = new Vector2(-48f, 76f);
        toolbar.AddComponent<Image>().color = new Color(0.03f, 0.08f, 0.11f, 0.96f);
        HorizontalLayoutGroup layout = toolbar.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(16, 8, 8, 8);
        layout.spacing = 12f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        TextMeshProUGUI hint = CreateText(toolbar.transform, "点击场景选择传送位置", 20f, Color.white);
        hint.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        // 提示栏拦截点击，只有其下的场景点选层能够提交落点。
        CreateButton(toolbar.transform, "取消", () => SetWindowVisible(true), 120f, 60f);
        teleportTargetRoot.SetActive(false);
    }

    #endregion
}
