using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FlatWorld.Localization;
using FlatWorld.WorldModel;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class GMReflectionConsole
{
    #region 地块名称与输入

    private TMP_InputField tileTeleportInput;
    private TextMeshProUGUI tileTeleportHint;
    private Button tileTeleportButton;
    private CancellationTokenSource tileSearchCancellation;
    private uint tileTeleportResultGeneration;

    private void BuildTileTeleportRow(Transform content)
    {
        CreateSectionTitle(content, "地块传送");
        GameObject row = CreateUiObject("Tile Teleport Row", content);
        row.AddComponent<LayoutElement>().preferredHeight = 60f;
        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        tileTeleportInput = CreateInputField(row.transform, "输入地块名称或 ID，回车传送", 540f, false);
        LayoutElement inputLayout = tileTeleportInput.GetComponent<LayoutElement>();
        inputLayout.minWidth = 180f;
        inputLayout.flexibleWidth = 1f;
        tileTeleportInput.textComponent.fontSize = 17f;
        ((TMP_Text)tileTeleportInput.placeholder).fontSize = 17f;
        tileTeleportInput.onSubmit.AddListener(_ => TeleportToNamedTile());
        tileTeleportButton = CreateButton(row.transform, "搜索并传送", TeleportToNamedTile, 190f, 60f);
        SetGmButtonVisual(tileTeleportButton, GmSurfaceRaised, true);
        CreateButton(row.transform, "取消", CancelTileSearch, 82f, 60f);
        tileTeleportHint = AddPageHint(content, "例如：粘土地块、泥炭、沙地；搜索当前维度，找到后自动传送，可随时取消。", 52f);
        tileTeleportHint.fontSize = 17f;
        LayoutElement hintLayout = tileTeleportHint.GetComponent<LayoutElement>();
        hintLayout.minHeight = 52f;
        hintLayout.preferredHeight = -1f;
        RegisterSearchEntry(GmPageId.Structures, "地块传送",
            "地块 地面 tile 传送 搜索 定位 粘土 黏土 泥炭 沙地 石地 草地", row.transform as RectTransform);
    }

    private void TeleportToNamedTile()
    {
        if (tileSearchCancellation != null || biomeSearchCancellation != null)
        {
            SetStatus("正在搜索，请等待或先取消当前搜索。", Color.yellow);
            return;
        }
        string query = tileTeleportInput.text.Trim();
        GameRes resources = GameRes.ExistingInstance;
        if (TileSearchNameRules.Normalize(query).Length == 0 || resources == null)
        {
            SetLocalizedText(tileTeleportHint, "请先输入地块名称，例如：粘土地块。");
            return;
        }
        var matches = new List<(RuntimeTileDefinition Definition, int Score)>();
        foreach (RuntimeTileDefinition definition in resources.TileBlockDict.Values)
        {
            if (definition.RuntimeTileId <= 0) continue;
            int score = TileSearchNameRules.Score(query, definition.Id, definition.DisplayName, definition.SearchAliases);
            if (query == definition.RuntimeTileId.ToString()) score = 3;
            if (score > 0) matches.Add((definition, score));
        }
        int best = matches.Count == 0 ? 0 : matches.Max(match => match.Score);
        RuntimeTileDefinition[] candidates = matches.Where(match => match.Score == best)
            .Select(match => match.Definition).OrderBy(definition => definition.Id, StringComparer.Ordinal).ToArray();
        if (candidates.Length != 1)
        {
            if (candidates.Length == 0)
                SetLocalizedFormat(tileTeleportHint, "没有名为“{0}”的地块，请检查名称或输入地块 ID。", query);
            else
                SetLocalizedProvider(tileTeleportHint, () => FlatWorldLocalizationService.GetUiFormat(
                    "匹配到多个地块，请输入完整名称或 ID：{0}",
                    string.Join(", ", candidates.Take(6).Select(definition =>
                        FlatWorldLocalizationService.GetUiFormat(
                            "{0} [{1}]", FlatWorldLocalizationService.GetUiText(definition.DisplayName),
                            definition.Id)))));
            return;
        }
        Item player = ItemMgr.GetInstance()?.LocalControlledActor;
        ChunkMgr manager = ChunkMgr.ExistingInstance;
        Mod_PlayerTraits traits = player?.itemMods.GetMod_ByID<Mod_PlayerTraits>(Mod_PlayerTraits.ModuleId);
        if (player == null || !ActorProfileRegistry.IsLocallyControlled(player) || player.itemData == null || traits == null ||
            manager == null || !ChunkMgr.IsSurfaceStreamingScene || !manager.IsWorldModelRuntimeActive ||
            !manager.IsAuthoritativeSimulation)
        {
            SetLocalizedText(tileTeleportHint, "请先进入单机或主机的地表/洞穴世界，再执行地块传送。");
            return;
        }
        var cancellation = new CancellationTokenSource();
        tileSearchCancellation = cancellation;
        unchecked { ++tileTeleportResultGeneration; }
        StartCoroutine(LocateTileAndTeleport(player, traits, manager, candidates[0], cancellation,
            tileTeleportResultGeneration));
    }

    #endregion

    #region 可取消地块搜索

    private IEnumerator LocateTileAndTeleport(Item player, Mod_PlayerTraits traits, ChunkMgr manager,
        RuntimeTileDefinition definition, CancellationTokenSource cancellation, uint resultGeneration)
    {
        try
        {
            tileTeleportButton.interactable = false;
            tileTeleportInput.interactable = false;
            string name = definition.DisplayName;
            SetLocalizedProvider(tileTeleportHint, () => FlatWorldLocalizationService.GetUiFormat(
                "正在搜索{0}，找到后自动传送；可以随时取消。", FlatWorldLocalizationService.GetUiText(name)));
            Vector3 logical = WorldLocalPresentation.ToLogical(player.transform.position);
            var anchor = new Int2(Mathf.FloorToInt(logical.x), Mathf.FloorToInt(logical.y));
            string dimension = manager.ResolveWorldAddress(logical).DimensionId;
            WorldRuntime world = manager.WorldRuntime;
            long epoch = world.Epoch;
            var progress = new DeterministicChunkGenerator.BiomeSearchProgress();
            double startedAt = Time.realtimeSinceStartupAsDouble;
            double nextProgressAt = startedAt;
            Task<DeterministicChunkGenerator.BiomeSearchResult> task = null;
            try { task = manager.FindGroundTileAsync(anchor, definition.RuntimeTileId, cancellation.Token, progress); }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetLocalizedText(tileTeleportHint, "无法开始地块搜索，请查看控制台。");
            }
            if (task == null) yield break;
            _ = task.ContinueWith(completed => { _ = completed.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            while (!task.IsCompleted)
            {
                if (cancellation.IsCancellationRequested || !IsSameWorld())
                {
                    cancellation.Cancel();
                    SetLocalizedText(tileTeleportHint, "地块搜索已取消。");
                    yield break;
                }
                if (Time.realtimeSinceStartupAsDouble >= nextProgressAt)
                {
                    int seconds = (int)(Time.realtimeSinceStartupAsDouble - startedAt);
                    var snapshot = progress.Latest;
                    SetLocalizedProvider(tileTeleportHint, () => FlatWorldLocalizationService.GetUiFormat(
                        "{0}：{1}；已检查 {2:N0} 格，耗时 {3}:{4:00}，可取消。",
                        FlatWorldLocalizationService.GetUiText(name),
                        FlatWorldLocalizationService.GetUiText(snapshot.Stage),
                        snapshot.SampledCount, seconds / 60, seconds % 60));
                    nextProgressAt = Time.realtimeSinceStartupAsDouble + .25d;
                }
                yield return null;
            }
            if (task.IsCanceled || cancellation.IsCancellationRequested || !IsSameWorld())
            {
                SetLocalizedText(tileTeleportHint, "地块搜索已取消。");
                yield break;
            }
            if (task.IsFaulted)
            {
                Debug.LogException(task.Exception.GetBaseException());
                SetLocalizedText(tileTeleportHint, "地块搜索失败，请查看控制台。");
                yield break;
            }
            var result = task.Result;
            if (!result.Found)
            {
                SetLocalizedProvider(tileTeleportHint, () => FlatWorldLocalizationService.GetUiFormat(
                    "已搜索完成，当前维度没有可站立的{0}。", FlatWorldLocalizationService.GetUiText(name)));
                yield break;
            }
            Vector3 destination = new(result.Cell.X + .5f, result.Cell.Y + .5f, logical.z);
            if (manager.TryGetRuntimeTerrainTile(destination, out RuntimeTerrainTileSample current) &&
                (!DeterministicChunkGenerator.IsGroundTileDestination(current.Cell, current.LiquidLevel,
                    definition.RuntimeTileId) || TerrainSupportLayer.GetTileId(current.Terrain,
                    current.LocalCell.x, current.LocalCell.y) != 0))
            {
                SetLocalizedText(tileTeleportHint, "目标地块在搜索期间发生了变化，请重新搜索。");
                yield break;
            }
            bool callbackReceived = false;
            if (!PlayerTeleportService.TryTeleportToSurface(player, destination, succeeded =>
            {
                callbackReceived = true;
                if (this == null || resultGeneration != tileTeleportResultGeneration || !IsSameWorld()) return;
                if (succeeded)
                {
                    SetLocalizedProvider(tileTeleportHint, () => FlatWorldLocalizationService.GetUiFormat(
                        "已传送到{0}：({1}, {2})。", FlatWorldLocalizationService.GetUiText(name),
                        result.Cell.X, result.Cell.Y));
                    Debug.Log($"[GM] 已传送到地块 {definition.Id}，逻辑落点={destination}");
                    SetWindowVisible(false);
                }
                else
                    SetLocalizedText(tileTeleportHint, "地块传送未完成，落点尚未应用。");
            }))
            {
                SetLocalizedText(tileTeleportHint, "当前世界尚不能执行传送。");
                yield break;
            }
            if (!callbackReceived)
                SetLocalizedText(tileTeleportHint, "已定位地块，传送已立即执行，正在同步服务器结果。");

            bool IsSameWorld() => this != null && player != null && traits != null && manager != null &&
                ItemMgr.GetInstance()?.LocalControlledActor == player && player.itemData != null &&
                ReferenceEquals(manager.WorldRuntime, world) && world.Epoch == epoch &&
                manager.IsWorldModelRuntimeActive && manager.IsAuthoritativeSimulation &&
                manager.ResolveWorldAddress(player.transform.position).DimensionId == dimension;
        }
        finally
        {
            cancellation.Cancel();
            cancellation.Dispose();
            if (tileSearchCancellation == cancellation) tileSearchCancellation = null;
            if (tileTeleportButton != null) tileTeleportButton.interactable = true;
            if (tileTeleportInput != null) tileTeleportInput.interactable = true;
        }
    }

    private void CancelTileSearch()
    {
        unchecked { ++tileTeleportResultGeneration; }
        if (tileSearchCancellation == null) return;
        tileSearchCancellation.Cancel();
        if (tileTeleportHint != null) SetLocalizedText(tileTeleportHint, "正在取消地块搜索…");
    }

    private void CancelLocationSearches()
    {
        CancelBiomeSearch();
        CancelTileSearch();
    }

    #endregion
}
