using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using TMPro;
using FlatWorld.Localization;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed partial class GMReflectionConsole
{
    #region 空投与生物目录控制

    private void OpenAirdropBrowser()
    {
        windowRoot.SetActive(true);
        aiCreatureBrowserRoot.SetActive(false);
        airdropBrowserRoot.SetActive(true);
        SyncBrowserRootsToWindow();
        RefreshItemIds();
        SetAirdropBrowserStatus(
            () => FlatWorldLocalizationService.GetUiText("点击任意物品即可空投到玩家附近。"),
            new Color(0.66f, 0.71f, 0.71f));
    }

    private void CloseAirdropBrowser()
    {
        airdropBrowserRoot.SetActive(false);
        windowRoot.SetActive(true);
        ClampTabbedWindowToCanvas();
    }

    private void OpenAiCreatureBrowser()
    {
        windowRoot.SetActive(true);
        airdropBrowserRoot.SetActive(false);
        aiCreatureBrowserRoot.SetActive(true);
        SyncBrowserRootsToWindow();
        RefreshAiCreatureIds();
        SetAiCreatureBrowserStatus(
            () => FlatWorldLocalizationService.GetUiText("点击任意生物即可召唤；批量召唤会自动散布，避免重叠。"),
            new Color(0.66f, 0.71f, 0.71f));
    }

    private void CloseAiCreatureBrowser()
    {
        aiCreatureBrowserRoot.SetActive(false);
        windowRoot.SetActive(true);
        ClampTabbedWindowToCanvas();
    }

    private void RefreshItemIds()
    {
        availableAirdropItems.Clear();
        GameRes gameRes = GameRes.ExistingInstance;
        if (gameRes != null)
        {
            HashSet<string> discoveredIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string itemId in gameRes.GetAllItemIds())
            {
                if (string.IsNullOrWhiteSpace(itemId) ||
                    !discoveredIds.Add(itemId) ||
                    gameRes.TryGetActorDefinition(itemId, out _) ||
                    !gameRes.TryGetItemPresentation(
                        itemId,
                        out string displayName,
                        out Sprite icon))
                {
                    continue;
                }

                availableAirdropItems.Add(new AirdropItemEntry
                {
                    ItemId = itemId,
                    DisplayName = displayName,
                    Icon = icon
                });
            }
        }

        availableAirdropItems.Sort((left, right) =>
        {
            int nameComparison = StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName, right.DisplayName);
            return nameComparison != 0
                ? nameComparison
                : StringComparer.OrdinalIgnoreCase.Compare(left.ItemId, right.ItemId);
        });

        if (itemIdInput != null &&
            availableAirdropItems.Count > 0 &&
            string.IsNullOrWhiteSpace(itemIdInput.text))
        {
            itemIdInput.text = availableAirdropItems[0].ItemId;
        }

        UpdateSummonHint();

        if (airdropBrowserRoot != null && airdropBrowserRoot.activeSelf)
            RebuildAirdropItemGrid();
    }

    private void RefreshAiCreatureIds()
    {
        availableAiCreatures.Clear();
        GameRes resources = GameRes.ExistingInstance;
        var ecology = FlatWorld.AIECS.Gameplay.AiecsEcologyRuntimeHost.Active;
        if (resources != null)
        {
            foreach (RuntimeItemDefinition definition in resources.ActorDefinitions.Values)
            {
                if (definition == null) continue;
                Texture iconAtlas = null;
                Rect iconUv = default;
                if (!AiRuntimeBackendService.UsesEntities(definition.Id) || ecology == null ||
                    !ecology.TryGetCatalogIcon(definition.Id, out iconAtlas, out iconUv))
                    iconAtlas = null;
                availableAiCreatures.Add(new AiCreatureEntry
                {
                    ItemId = definition.Id,
                    DisplayName = definition.DisplayName,
                    Icon = iconAtlas == null ? ResolveAiCreaturePrefabIcon(definition) : null,
                    IconAtlas = iconAtlas,
                    IconUv = iconUv
                });
            }
        }

        availableAiCreatures.Sort((left, right) =>
        {
            int nameComparison = StringComparer.OrdinalIgnoreCase.Compare(left.DisplayName, right.DisplayName);
            return nameComparison != 0
                ? nameComparison
                : StringComparer.OrdinalIgnoreCase.Compare(left.ItemId, right.ItemId);
        });

        UpdateSummonHint();
        if (aiCreatureBrowserRoot != null && aiCreatureBrowserRoot.activeSelf)
            RebuildAiCreatureGrid();
    }

    /// <summary>动画图集不可用时，从 Actor 外壳的身体渲染器取目录图标。</summary>
    private static Sprite ResolveAiCreaturePrefabIcon(RuntimeItemDefinition definition)
    {
        if (definition.Sprite != null)
            return definition.Sprite;
        GameObject shell = definition.ShellPrefab;
        if (shell == null)
            return null;

        if (!string.IsNullOrWhiteSpace(definition.RendererPath))
        {
            Transform body = shell.transform.Find(definition.RendererPath);
            Sprite sprite = body != null ? body.GetComponent<SpriteRenderer>()?.sprite : null;
            if (sprite != null)
                return sprite;
        }

        SpriteRenderer[] renderers = shell.GetComponentsInChildren<SpriteRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i].sprite != null)
                return renderers[i].sprite;
        return null;
    }

    private void UpdateSummonHint()
    {
        if (itemHintText == null)
            return;

        if (availableAirdropItems.Count == 0 && availableAiCreatures.Count == 0)
        {
            SetLocalizedText(itemHintText, "打开上方目录载入可用内容；若目录为空，请先进入游戏世界。");
            return;
        }

        SetLocalizedFormat(
            itemHintText,
            "已发现 {0} 个物品、{1} 个 AI 生物。",
            availableAirdropItems.Count, availableAiCreatures.Count);
    }

    private void RebuildAirdropItemGrid()
    {
        if (airdropItemGrid == null)
            return;

        for (int i = airdropItemGrid.childCount - 1; i >= 0; i--)
            Destroy(airdropItemGrid.GetChild(i).gameObject);

        string query = airdropSearchInput != null ? airdropSearchInput.text.Trim() : string.Empty;
        List<AirdropItemEntry> visibleItems = string.IsNullOrWhiteSpace(query)
            ? availableAirdropItems
            : availableAirdropItems
                .Where(item =>
                    GetCatalogDisplayName(item.ItemId, item.DisplayName, false).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.ItemId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

        for (int i = 0; i < visibleItems.Count; i++)
            CreateAirdropItemButton(visibleItems[i]);

        if (visibleItems.Count == 0)
        {
            TextMeshProUGUI emptyText = CreateText(
                airdropItemGrid,
                availableAirdropItems.Count == 0 ? "暂无可空投物品" : "没有匹配的物品",
                13f,
                new Color(0.66f, 0.71f, 0.71f));
            emptyText.alignment = TextAlignmentOptions.Center;
        }

        if (airdropBrowserCountText != null)
        {
            if (string.IsNullOrWhiteSpace(query))
                SetLocalizedFormat(airdropBrowserCountText, "全部 {0}", availableAirdropItems.Count);
            else
                SetLocalizedFormat(airdropBrowserCountText, "显示 {0} / {1}", visibleItems.Count, availableAirdropItems.Count);
        }
    }

    private void CreateAirdropItemButton(AirdropItemEntry entry)
    {
        CreateCatalogButton(
            airdropItemGrid,
            "Item " + entry.ItemId,
            entry.ItemId,
            entry.DisplayName,
            entry.Icon,
            () => StartAirdrop(entry.ItemId));
    }

    private void RebuildAiCreatureGrid()
    {
        if (aiCreatureGrid == null)
            return;

        for (int i = aiCreatureGrid.childCount - 1; i >= 0; i--)
            Destroy(aiCreatureGrid.GetChild(i).gameObject);

        string query = aiCreatureSearchInput != null ? aiCreatureSearchInput.text.Trim() : string.Empty;
        List<AiCreatureEntry> visibleCreatures = string.IsNullOrWhiteSpace(query)
            ? availableAiCreatures
            : availableAiCreatures
                .Where(creature =>
                    GetCatalogDisplayName(creature.ItemId, creature.DisplayName, true).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    creature.ItemId.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

        for (int i = 0; i < visibleCreatures.Count; i++)
        {
            AiCreatureEntry creature = visibleCreatures[i];
            CreateCatalogButton(
                aiCreatureGrid,
                "AI Creature " + creature.ItemId,
                creature.ItemId,
                creature.DisplayName,
                creature.Icon,
                () => SpawnAiCreature(creature),
                creature.IconAtlas,
                creature.IconUv,
                isActor: true);
        }

        if (visibleCreatures.Count == 0)
        {
            TextMeshProUGUI emptyText = CreateText(
                aiCreatureGrid,
                availableAiCreatures.Count == 0 ? "暂无可召唤 AI 生物" : "没有匹配的生物",
                13f,
                GmTextSecondary);
            emptyText.alignment = TextAlignmentOptions.Center;
        }

        if (aiCreatureBrowserCountText != null)
        {
            if (string.IsNullOrWhiteSpace(query))
                SetLocalizedFormat(aiCreatureBrowserCountText, "全部 {0}", availableAiCreatures.Count);
            else
                SetLocalizedFormat(aiCreatureBrowserCountText, "显示 {0} / {1}", visibleCreatures.Count, availableAiCreatures.Count);
        }
    }

    private static void CreateCatalogButton(
        Transform parent,
        string objectName,
        string itemId,
        string displayName,
        Sprite icon,
        UnityAction onClick,
        Texture iconAtlas = null,
        Rect iconUv = default,
        bool isActor = false)
    {
        GameObject tile = CreateUiObject(objectName, parent);
        Image tileImage = tile.AddComponent<Image>();
        tileImage.color = GmSurface;
        Outline outline = tile.AddComponent<Outline>();
        StyleGmOutline(outline);

        Button button = tile.AddComponent<Button>();
        button.targetGraphic = tileImage;
        button.onClick.AddListener(onClick);
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
        colors.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        colors.selectedColor = new Color(1.04f, 1.04f, 1.04f, 1f);
        colors.disabledColor = GmDisabledTint;
        colors.colorMultiplier = 1f;
        colors.fadeDuration = 0.11f;
        button.colors = colors;

        GameObject iconObject = CreateUiObject("Icon", tile.transform);
        RectTransform iconRect = iconObject.GetComponent<RectTransform>();
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 1f);
        iconRect.pivot = new Vector2(0.5f, 1f);
        iconRect.anchoredPosition = new Vector2(0f, -8f);
        iconRect.sizeDelta = new Vector2(64f, 64f);
        if (iconAtlas != null)
        {
            float aspect = iconUv.width * iconAtlas.width / (iconUv.height * iconAtlas.height);
            iconRect.sizeDelta = aspect >= 1f
                ? new Vector2(64f, 64f / aspect)
                : new Vector2(64f * aspect, 64f);
            RawImage atlasImage = iconObject.AddComponent<RawImage>();
            atlasImage.texture = iconAtlas;
            atlasImage.uvRect = iconUv;
            atlasImage.raycastTarget = false;
        }
        else
        {
            Image iconImage = iconObject.AddComponent<Image>();
            iconImage.sprite = icon;
            iconImage.preserveAspect = true;
            iconImage.raycastTarget = false;
            iconImage.color = icon != null ? Color.white : Color.clear;
        }

        if (iconAtlas == null && icon == null)
        {
            TextMeshProUGUI placeholder = CreateText(
                iconObject.transform,
                "?",
                30f,
                GmTextSecondary);
            placeholder.alignment = TextAlignmentOptions.Center;
            RectTransform placeholderRect = placeholder.rectTransform;
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = placeholderRect.offsetMax = Vector2.zero;
        }

        TextMeshProUGUI nameText = CreateText(
            tile.transform,
            displayName,
            12f,
            GmTextPrimary);
        SetLocalizedProvider(nameText, () => FlatWorldLocalizationService.GetUiText(
            GetCatalogDisplayName(itemId, displayName, isActor)));
        nameText.alignment = TextAlignmentOptions.Center;
        nameText.enableWordWrapping = false;
        nameText.overflowMode = TextOverflowModes.Ellipsis;
        RectTransform nameRect = nameText.rectTransform;
        nameRect.anchorMin = new Vector2(0f, 0f);
        nameRect.anchorMax = new Vector2(1f, 0f);
        nameRect.offsetMin = new Vector2(5f, 23f);
        nameRect.offsetMax = new Vector2(-5f, 45f);

        TextMeshProUGUI idText = CreateText(
            tile.transform,
            itemId,
            9f,
            GmTextSecondary);
        idText.alignment = TextAlignmentOptions.Center;
        idText.enableWordWrapping = false;
        idText.overflowMode = TextOverflowModes.Ellipsis;
        RectTransform idRect = idText.rectTransform;
        idRect.anchorMin = new Vector2(0f, 0f);
        idRect.anchorMax = new Vector2(1f, 0f);
        idRect.offsetMin = new Vector2(5f, 4f);
        idRect.offsetMax = new Vector2(-5f, 22f);
    }

    private static string GetCatalogDisplayName(string itemId, string fallback, bool isActor)
    {
        GameRes resources = GameRes.ExistingInstance;
        if (resources == null) return fallback ?? string.Empty;
        if (isActor && resources.TryGetActorDefinition(itemId, out RuntimeItemDefinition actorDefinition))
            return actorDefinition.DisplayName;
        if (!isActor && resources.TryGetItemPresentation(itemId, out string itemName, out _))
            return itemName;
        return fallback ?? string.Empty;
    }
    private void SpawnAiCreature(AiCreatureEntry entry)
    {
        if (entry == null || string.IsNullOrWhiteSpace(entry.ItemId))
        {
            SetAiCreatureResult("请选择要召唤的 AI 生物。", Color.yellow);
            return;
        }

        if (aiCreatureAmountInput == null ||
            !int.TryParse(aiCreatureAmountInput.text, out int amount))
        {
            amount = 1;
        }

        amount = Mathf.Clamp(amount, 1, 20);
        if (aiCreatureAmountInput != null)
            aiCreatureAmountInput.text = amount.ToString();

        Transform player = GetLocalPlayerTransform();
        if (player == null)
        {
            SetAiCreatureResult("未找到本地玩家，无法确定召唤位置。", Color.yellow);
            return;
        }

        int spawnedCount = 0;
        string firstFailure = null;
        var occupiedPositions = new List<Vector3>(amount);
        for (int i = 0; i < amount; i++)
        {
            if (TrySpawnCreatureNearPlayer(
                    entry.ItemId,
                    player.position,
                    i,
                    occupiedPositions,
                    out string spawnError))
            {
                spawnedCount++;
            }
            else if (firstFailure == null)
            {
                firstFailure = spawnError;
            }
        }

        if (spawnedCount == amount)
        {
            SetAiCreatureResult(
                $"召唤成功：{entry.DisplayName} × {spawnedCount}",
                new Color(0.35f, 0.95f, 0.85f));
            return;
        }

        string result = spawnedCount > 0
            ? $"部分召唤成功：{entry.DisplayName} {spawnedCount}/{amount}；{firstFailure}"
            : $"召唤失败：{firstFailure}";
        SetAiCreatureResult(result, new Color(1f, 0.42f, 0.38f));
    }

    private static Vector3 GetAiCreatureSpawnPosition(Vector3 playerPosition, int index, int attempt)
    {
        const float RingStepRadians = Mathf.PI * 0.25f;
        float angle = index * 2.39996323f + attempt * RingStepRadians;
        float radius = 2.5f + attempt / 8 * 1.25f + Mathf.Sqrt(index) * 0.4f;
        Vector3 offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
        Vector3 position = playerPosition + offset;
        position.z = playerPosition.z;
        return position;
    }

    /// <summary>GM 在玩家周围生成完整生物，具体后端由统一物种路由决定。</summary>
    private static bool TrySpawnCreatureNearPlayer(
        string itemId,
        Vector3 playerPosition,
        int index,
        List<Vector3> occupiedPositions,
        out string error)
    {
        IAiEcologyBackend backend = AiRuntimeBackendService.Ecology;
        bool usesEntities = AiRuntimeBackendService.UsesEntities(itemId);
        if (usesEntities && (!AiRuntimeBackendService.UseEntities || backend == null || !backend.SupportsSpecies(itemId)))
        {
            error = "该物种的 ECS 后端未启用或尚未就绪。";
            return false;
        }

        for (int attempt = 0; attempt < 32; attempt++)
        {
            Vector3 position = GetAiCreatureSpawnPosition(playerPosition, index, attempt);
            bool overlaps = false;
            for (int i = 0; i < occupiedPositions.Count; i++)
            {
                if ((position - occupiedPositions[i]).sqrMagnitude >= 1.44f) continue;
                overlaps = true;
                break;
            }
            if (overlaps) continue;

            try
            {
                if (AiRuntimeBackendService.TrySpawnDirect(itemId, position, 0, out _))
                {
                    occupiedPositions.Add(position);
                    error = null;
                    return true;
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                error = $"生物生成异常：{exception.Message}";
                return false;
            }

            if (usesEntities && backend.IsReady) continue;
            error = "生物创建失败，请检查 Item/AI 初始化日志及世界是否就绪。";
            return false;
        }

        error = "玩家附近没有适合该生物的已加载导航位置。";
        return false;
    }

    private void SetAiCreatureResult(string message, Color color)
    {
        Func<string> provider = () => LocalizeAiCreatureResult(message);
        SetStatusProvider(provider, color);
        SetAiCreatureBrowserStatus(provider, color);
    }

    // 按稳定结果模板重排文案，并保留异常详情原文。
    private static string LocalizeAiCreatureResult(string message)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;
        const string successPrefix = "召唤成功：";
        if (message.StartsWith(successPrefix, StringComparison.Ordinal))
        {
            string payload = message.Substring(successPrefix.Length);
            int separator = payload.LastIndexOf(" × ", StringComparison.Ordinal);
            if (separator >= 0)
                return FlatWorldLocalizationService.GetUiFormat("召唤成功：{0} × {1}",
                    FlatWorldLocalizationService.GetUiText(payload.Substring(0, separator)),
                    payload.Substring(separator + 3));
        }

        const string partialPrefix = "部分召唤成功：";
        if (message.StartsWith(partialPrefix, StringComparison.Ordinal))
        {
            string payload = message.Substring(partialPrefix.Length);
            int reasonSeparator = payload.IndexOf('；');
            int countSeparator = reasonSeparator > 0 ? payload.LastIndexOf(' ', reasonSeparator - 1) : -1;
            int ratioSeparator = countSeparator >= 0 ? payload.IndexOf('/', countSeparator + 1) : -1;
            if (reasonSeparator >= 0 && countSeparator >= 0 && ratioSeparator > countSeparator)
                return FlatWorldLocalizationService.GetUiFormat("部分召唤成功：{0} {1}/{2}；{3}",
                    FlatWorldLocalizationService.GetUiText(payload.Substring(0, countSeparator)),
                    payload.Substring(countSeparator + 1, ratioSeparator - countSeparator - 1),
                    payload.Substring(ratioSeparator + 1, reasonSeparator - ratioSeparator - 1),
                    LocalizeAiSpawnFailure(payload.Substring(reasonSeparator + 1)));
        }

        const string failurePrefix = "召唤失败：";
        if (message.StartsWith(failurePrefix, StringComparison.Ordinal))
            return FlatWorldLocalizationService.GetUiFormat("召唤失败：{0}",
                LocalizeAiSpawnFailure(message.Substring(failurePrefix.Length)));
        return FlatWorldLocalizationService.GetUiText(message);
    }

    private static string LocalizeAiSpawnFailure(string message)
    {
        const string exceptionPrefix = "生物生成异常：";
        if (message.StartsWith(exceptionPrefix, StringComparison.Ordinal))
            return FlatWorldLocalizationService.GetUiFormat("生物生成异常：{0}", message.Substring(exceptionPrefix.Length));
        return message == "该物种的 ECS 后端未启用或尚未就绪。" ||
               message == "生物创建失败，请检查 Item/AI 初始化日志及世界是否就绪。" ||
               message == "玩家附近没有适合该生物的已加载导航位置。"
            ? FlatWorldLocalizationService.GetUiText(message)
            : message;
    }

    private void SetAiCreatureBrowserStatus(Func<string> provider, Color color)
    {
        if (aiCreatureBrowserStatusText == null)
            return;

        SetLocalizedProvider(aiCreatureBrowserStatusText, provider);
        aiCreatureBrowserStatusText.color = color;
    }

    private void StartAirdrop()
    {
        StartAirdrop(itemIdInput != null ? itemIdInput.text : string.Empty);
    }

    private void StartAirdrop(string itemId)
    {
        itemId = itemId?.Trim();
        if (string.IsNullOrWhiteSpace(itemId))
        {
            SetAirdropResult("请选择要空投的物品。", Color.yellow);
            return;
        }

        if (amountInput == null || !int.TryParse(amountInput.text, out int amount))
            amount = 1;
        amount = Mathf.Clamp(amount, 1, 9999);
        if (amountInput != null)
            amountInput.text = amount.ToString();

        Transform player = GetLocalPlayerTransform();
        if (player == null)
        {
            SetAirdropResult("未找到本地玩家，无法确定空投位置。", Color.yellow);
            return;
        }

        Vector3 sideOffset = UnityEngine.Random.insideUnitCircle.normalized * 1.25f;
        Vector3 landingPosition = player.position + sideOffset;
        landingPosition.z = player.position.z;
        StartCoroutine(AirdropRoutine(itemId, amount, landingPosition));
    }

    private IEnumerator AirdropRoutine(string itemId, int amount, Vector3 landingPosition)
    {
        GameObject marker = CreateAirdropMarker(landingPosition + Vector3.up * 5f);
        float duration = 0.55f;
        float elapsed = 0f;
        Vector3 start = marker.transform.position;

        while (elapsed < duration && marker != null)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            marker.transform.position = Vector3.Lerp(start, landingPosition, t * t);
            marker.transform.localScale = Vector3.one * Mathf.Lerp(1.15f, 0.65f, t);
            yield return null;
        }

        if (marker != null)
            Destroy(marker);

        if (TrySpawnItemThroughReflection(itemId, amount, landingPosition, out string result))
            SetAirdropResult(result, new Color(0.35f, 0.95f, 0.85f));
        else
            SetAirdropResult(result, new Color(1f, 0.42f, 0.38f));
    }

    private void SetAirdropResult(string message, Color color)
    {
        Func<string> provider = () => LocalizeAirdropResult(message);
        SetStatusProvider(provider, color);
        SetAirdropBrowserStatus(provider, color);
    }

    private static string LocalizeAirdropResult(string message)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;
        const string successPrefix = "空投成功：";
        if (message.StartsWith(successPrefix, StringComparison.Ordinal))
        {
            string payload = message.Substring(successPrefix.Length);
            int separator = payload.LastIndexOf(" × ", StringComparison.Ordinal);
            if (separator >= 0)
                return FlatWorldLocalizationService.GetUiFormat("空投成功：{0} × {1}",
                    payload.Substring(0, separator), payload.Substring(separator + 3));
        }

        const string failurePrefix = "空投失败：";
        if (message.StartsWith(failurePrefix, StringComparison.Ordinal))
            return FlatWorldLocalizationService.GetUiFormat("空投失败：{0}",
                LocalizeAirdropFailureReason(message.Substring(failurePrefix.Length)));
        return FlatWorldLocalizationService.GetUiText(message);
    }

    private static string LocalizeAirdropFailureReason(string message)
    {
        return message == "未找到 ItemMgr。" ||
               message == "ItemMgr 未提供 string ItemId 实例化入口。" ||
               message == "ItemMgr 未返回 Item 实例。"
            ? FlatWorldLocalizationService.GetUiText(message)
            : message;
    }

    private void SetAirdropBrowserStatus(Func<string> provider, Color color)
    {
        if (airdropBrowserStatusText == null)
            return;

        SetLocalizedProvider(airdropBrowserStatusText, provider);
        airdropBrowserStatusText.color = color;
    }

    private bool TrySpawnItemThroughReflection(string itemId, int amount, Vector3 position, out string result)
    {
        if (!TryResolveItemSpawner(out Component itemManager, out MethodInfo spawnMethod, out string error))
        {
            result = $"空投失败：{error}";
            return false;
        }

        if (!TryInvokeItemSpawner(
                itemManager,
                spawnMethod,
                itemId,
                position,
                out object item,
                out error))
        {
            result = $"空投失败：{error}";
            return false;
        }

        try
        {
            object itemData = ReadMember(item, "itemData");
            object stack = ReadMember(itemData, "Stack");
            if (amount > 1 && stack != null)
                WriteMember(stack, "Amount", Convert.ChangeType(amount, GetMemberType(stack, "Amount") ?? typeof(float)));

            result = $"空投成功：{itemId} × {amount}";
            return true;
        }
        catch (TargetInvocationException exception)
        {
            result = $"空投失败：{exception.InnerException?.Message ?? exception.Message}";
            Debug.LogException(exception.InnerException ?? exception);
            return false;
        }
        catch (Exception exception)
        {
            result = $"空投失败：{exception.Message}";
            Debug.LogException(exception);
            return false;
        }
    }

    private static bool TryResolveItemSpawner(
        out Component itemManager,
        out MethodInfo spawnMethod,
        out string error)
    {
        itemManager = FindFirstComponent("ItemMgr");
        spawnMethod = null;
        if (itemManager == null)
        {
            error = "未找到 ItemMgr。";
            return false;
        }

        spawnMethod = itemManager.GetType().GetMethods(InstanceFlags)
            .FirstOrDefault(method =>
            {
                if (method.Name != "InstantiateItem")
                    return false;

                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == 5 && parameters[0].ParameterType == typeof(string);
            });

        if (spawnMethod == null)
        {
            error = "ItemMgr 未提供 string ItemId 实例化入口。";
            return false;
        }

        error = null;
        return true;
    }

    private static bool TryInvokeItemSpawner(
        Component itemManager,
        MethodInfo spawnMethod,
        string itemId,
        Vector3 position,
        out object item,
        out string error)
    {
        item = null;
        try
        {
            item = spawnMethod.Invoke(itemManager, new object[]
            {
                itemId,
                position,
                Quaternion.identity,
                Vector3.one,
                null
            });

            if (item != null)
            {
                error = null;
                return true;
            }

            error = "ItemMgr 未返回 Item 实例。";
            return false;
        }
        catch (TargetInvocationException exception)
        {
            error = exception.InnerException?.Message ?? exception.Message;
            Debug.LogException(exception.InnerException ?? exception);
            return false;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            Debug.LogException(exception);
            return false;
        }
    }

    private GameObject CreateAirdropMarker(Vector3 position)
    {
        if (dropMarkerSprite == null)
        {
            Texture2D texture = new Texture2D(12, 18, TextureFormat.RGBA32, false);
            Color[] pixels = Enumerable.Repeat(new Color(1f, 0.56f, 0.14f, 1f), texture.width * texture.height).ToArray();
            texture.SetPixels(pixels);
            texture.Apply();
            dropMarkerSprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 14f);
        }

        GameObject marker = new GameObject("GM Airdrop Marker");
        marker.transform.position = position;
        SpriteRenderer renderer = marker.AddComponent<SpriteRenderer>();
        renderer.sprite = dropMarkerSprite;
        renderer.color = new Color(1f, 0.65f, 0.2f, 0.92f);
        WorldRenderingManager.Instance.ApplyRendererSorting(renderer, WorldSortingManager.WorldEffectCategory, short.MaxValue);
        return marker;
    }

    #endregion

}
