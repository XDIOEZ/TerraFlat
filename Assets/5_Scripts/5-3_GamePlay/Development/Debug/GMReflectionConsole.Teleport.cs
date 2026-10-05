using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using FlatWorld.WorldModel;
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
    private Mod_GameController teleportInputOwner;
    private Mod_PlayerTraits teleportTargetPlayer;

    /// <summary>由唯一 GM 实例消费 T，不依赖角色显示名或重复管理员模块。</summary>
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

        if (!Mod_PlayerAdminController.TeleportToMouseShortcutEnabled ||
            keyboard?.tKey.wasPressedThisFrame != true)
            return;

        GameObject selected = EventSystem.current?.currentSelectedGameObject;
        if (selected != null &&
            (selected.GetComponent<TMP_InputField>() != null || selected.GetComponent<InputField>() != null))
            return;

        if (TryGetTeleportPlayer(out Mod_GameController controller, out Mod_PlayerTraits traits) &&
            !controller.IsGameplayInputLocked && !controller.IsPointerOverUI())
            traits.TryTeleportToScreenPosition(controller.GetPointerScreenPosition());
    }

    /// <summary>只从当前本地玩家解析模块，不扫描任意场景中的玩家副本。</summary>
    private static bool TryGetTeleportPlayer(out Mod_GameController controller, out Mod_PlayerTraits traits)
    {
        Player player = ItemMgr.Instance?.User_Player;
        controller = null;
        traits = null;
        if (player == null || !player.IsLocalProfile || player.Data == null)
            return false;

        controller = player.GetComponent<Mod_GameController>();
        traits = player.itemMods.GetMod_ByID<Mod_PlayerTraits>(Mod_PlayerTraits.ModuleId);
        return controller != null && traits != null;
    }

    /// <summary>收起 GM 后独占一次鼠标/触屏点选，输入锁同时释放已有摇杆和攻击。</summary>
    private void BeginTeleportTargeting()
    {
        if (!TryGetTeleportPlayer(out Mod_GameController controller, out Mod_PlayerTraits traits) ||
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
    private void OnDisable()
    {
        CancelTeleportTargeting();
        CancelBiomeSearch();
    }

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
        Image toolbarImage = toolbar.AddComponent<Image>();
        toolbarImage.color = GmCanvas;
        StyleGmOutline(toolbar.AddComponent<Outline>(), true);
        HorizontalLayoutGroup layout = toolbar.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(16, 8, 8, 8);
        layout.spacing = 12f;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        TextMeshProUGUI hint = CreateText(toolbar.transform, "点击场景选择传送位置", 20f, GmTextPrimary);
        hint.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        // 提示栏拦截点击，只有其下的场景点选层能够提交落点。
        CreateButton(toolbar.transform, "取消", () => SetWindowVisible(true), 120f, 60f);
        teleportTargetRoot.SetActive(false);
    }

    #endregion

    #region 群系传送

    private readonly SurfaceBiomeKind[] teleportBiomes =
        (SurfaceBiomeKind[])Enum.GetValues(typeof(SurfaceBiomeKind));
    private int selectedBiomeIndex;
    private TextMeshProUGUI biomeSelectionText;
    private TextMeshProUGUI biomeHintText;
    private Button biomeTeleportButton;
    private CancellationTokenSource biomeSearchCancellation;

    // GM 调试分页沿用现有遗迹选择行的布局和主题。
    private void BuildBiomeTeleportRow(Transform content)
    {
        CreateSectionTitle(content, "群系传送");
        GameObject row = CreateUiObject("Biome Teleport Row", content);
        row.AddComponent<LayoutElement>().preferredHeight = 44f;
        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        CreateButton(row.transform, "‹", () => CycleBiome(-1), 40f, 40f);
        biomeSelectionText = CreateValueDisplay(row.transform, "", 540f, 40f);
        LayoutElement selection = biomeSelectionText.transform.parent.GetComponent<LayoutElement>();
        selection.minWidth = 180f;
        selection.flexibleWidth = 1f;
        CreateButton(row.transform, "›", () => CycleBiome(1), 40f, 40f);
        biomeTeleportButton = CreateButton(row.transform, "开始搜索", TeleportToSelectedBiome, 190f, 40f);
        SetGmButtonVisual(biomeTeleportButton, GmSurfaceRaised, true);
        CreateButton(row.transform, "取消", CancelBiomeSearch, 82f, 40f);
        biomeHintText = AddPageHint(content,
            "点击开始搜索，找到后直接传送；搜索可能需要几分钟，可以随时取消。", 26f);
        RegisterSearchEntry(GmPageId.Structures, "群系传送",
            "群系 biome 传送 海洋 河流 沙滩 沙漠 草原 森林 雪原 雪地 石地 山地", row.transform as RectTransform);
        RefreshBiomeSelection();
    }

    private void CycleBiome(int direction)
    {
        CancelBiomeSearch();
        selectedBiomeIndex = (selectedBiomeIndex + direction + teleportBiomes.Length) % teleportBiomes.Length;
        RefreshBiomeSelection();
    }

    private void RefreshBiomeSelection()
    {
        if (biomeSelectionText == null)
            return;
        SurfaceBiomeKind biome = teleportBiomes[selectedBiomeIndex];
        string name = biome == SurfaceBiomeKind.Snow
            ? "雪原" : SurfaceBiomeClassifier.GetLegacyName((int)biome);
        biomeSelectionText.text = $"{selectedBiomeIndex + 1}/{teleportBiomes.Length}  {name}  /  {biome}";
    }

    private void TeleportToSelectedBiome()
    {
        if (biomeSearchCancellation != null)
        {
            SetStatus("正在定位群系，请等待或取消搜索。", Color.yellow);
            return;
        }
        Player player = ItemMgr.GetInstance()?.User_Player;
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        if (player == null || !player.IsLocalProfile || player.Data == null ||
            manager == null || !manager.IsWorldModelRuntimeActive || !manager.IsAuthoritativeSimulation)
        {
            SetStatus("请先进入单机或主机世界，再执行群系传送。", Color.yellow);
            return;
        }
        if (manager.ActiveGenerationProfile?.Settings.Mode != ChunkGenerationMode.Surface)
        {
            SetStatus("群系传送用于星球地表，请先返回地表。", Color.yellow);
            return;
        }
        var cancellation = new CancellationTokenSource();
        biomeSearchCancellation = cancellation;
        StartCoroutine(LocateBiomeAndTeleport(player, manager,
            teleportBiomes[selectedBiomeIndex], cancellation));
    }

    // 等待后台任务时只在主线程更新 UI，并在落点应用前核对世界和玩家身份。
    private IEnumerator LocateBiomeAndTeleport(Player player, ChunkMgr manager,
        SurfaceBiomeKind biome, CancellationTokenSource cancellation)
    {
        bool teleported = false;
        try
        {
            biomeTeleportButton.interactable = false;
            biomeTeleportButton.GetComponentInChildren<TextMeshProUGUI>().text = "搜索中…";
            string name = biome == SurfaceBiomeKind.Snow
                ? "雪原" : SurfaceBiomeClassifier.GetLegacyName((int)biome);
            biomeHintText.text = $"正在搜索{name}，找到后自动传送；可以随时取消。";
            var progress = new DeterministicChunkGenerator.BiomeSearchProgress();
            double startedAt = Time.realtimeSinceStartupAsDouble;
            double nextProgressAt = startedAt;
            var anchor = new Int2(Mathf.FloorToInt(player.transform.position.x),
                Mathf.FloorToInt(player.transform.position.y));
            string dimension = manager.ResolveWorldAddress(player.transform.position).DimensionId;
            long epoch = manager.WorldRuntime.Epoch;
            Task<DeterministicChunkGenerator.BiomeSearchResult> task = null;
            try
            {
                task = manager.FindSurfaceBiomeAsync(anchor, biome, cancellation.Token, progress);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                biomeHintText.text = "生成配置尚未就绪或存在错误，请查看 Console 日志。";
                SetStatus("无法开始群系定位，详细原因已写入日志。", Color.yellow);
            }
            if (task == null)
                yield break;
            // 取消后不再等待的任务仍需观察异常，避免遗失后台失败信息。
            _ = task.ContinueWith(completed => { _ = completed.Exception; },
                TaskContinuationOptions.OnlyOnFaulted);
            yield return null;
            while (!task.IsCompleted)
            {
                if (cancellation.IsCancellationRequested || !IsSameWorld())
                {
                    cancellation.Cancel();
                    biomeHintText.text = "搜索已取消，没有执行传送。";
                    yield break;
                }
                if (Time.realtimeSinceStartupAsDouble >= nextProgressAt)
                {
                    DeterministicChunkGenerator.BiomeSearchSnapshot snapshot = progress.Latest;
                    int seconds = (int)(Time.realtimeSinceStartupAsDouble - startedAt);
                    string step = snapshot.Stride > 0 ? $"，间隔 {snapshot.Stride} 格" : string.Empty;
                    biomeHintText.text = $"{name}：{snapshot.Stage}{step}；已检查 {snapshot.SampledCount:N0} 处，耗时 {seconds / 60}:{seconds % 60:00}，可取消。";
                    nextProgressAt = Time.realtimeSinceStartupAsDouble + 0.25d;
                }
                yield return null;
            }
            if (task.IsCanceled || cancellation.IsCancellationRequested || !IsSameWorld())
            {
                biomeHintText.text = "搜索已取消，没有执行传送。";
                yield break;
            }
            if (task.IsFaulted)
            {
                Debug.LogException(task.Exception.GetBaseException());
                SetStatus("群系定位失败，详细原因已写入日志。", Color.yellow);
                biomeHintText.text = "定位失败，请查看 Console 日志。";
                yield break;
            }
            DeterministicChunkGenerator.BiomeSearchResult result = task.Result;
            if (!result.Found)
            {
                biomeHintText.text = $"{progress.Latest.Stage}；检查 {result.SampledCount:N0} 处，未找到可传送的{name}。";
                SetStatus($"{name}搜索结束：{progress.Latest.Stage}。", Color.yellow);
                yield break;
            }
            Vector3 destination = new(result.Cell.X + 0.5f, result.Cell.Y + 0.5f,
                player.transform.position.z);
            Rigidbody2D body = player.GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.velocity = Vector2.zero;
                body.angularVelocity = 0f;
                body.position = destination;
            }
            player.transform.position = destination;
            player.Data.transform.position = destination;
            manager.ResetChunkLoadQueue();
            Mod_ChunkLoader loader = player.GetComponentInChildren<Mod_ChunkLoader>(true) ??
                player.GetComponentInParent<Mod_ChunkLoader>();
            loader?.RefreshChunksAroundPlayer();
            biomeHintText.text = $"已定位{name}：({result.Cell.X}, {result.Cell.Y})，检查 {result.SampledCount} 个采样位置。";
            Debug.Log($"[GM] 已按正式生成规则传送到群系 {name} ({biome})，落点={destination}，采样={result.SampledCount}");
            teleported = true;

            bool IsSameWorld() => player != null && manager != null &&
                ItemMgr.GetInstance()?.User_Player == player && player.Data != null &&
                manager.WorldRuntime?.Epoch == epoch && manager.IsWorldModelRuntimeActive &&
                manager.ResolveWorldAddress(player.transform.position).DimensionId == dimension;
        }
        finally
        {
            cancellation.Cancel();
            cancellation.Dispose();
            if (biomeSearchCancellation == cancellation)
                biomeSearchCancellation = null;
            if (biomeTeleportButton != null)
            {
                biomeTeleportButton.interactable = true;
                biomeTeleportButton.GetComponentInChildren<TextMeshProUGUI>().text = "开始搜索";
            }
        }
        if (teleported)
            SetWindowVisible(false);
    }

    private void CancelBiomeSearch()
    {
        if (biomeSearchCancellation == null) return;
        biomeSearchCancellation.Cancel();
        if (biomeHintText != null)
            biomeHintText.text = "正在取消搜索…";
    }

    #endregion
}
