using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.Localization;
using FlatWorld.AIECS.Gameplay;
using FlatWorld.Gameplay.Events;
using FlatWorld.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.Events;
using UnityEngine.UI;

public sealed partial class GMReflectionConsole
{
    #region AIECS 实战分页

    /// <summary>AIECS 实战入口只保留模拟宿主，所有开发操作统一嵌入 GM 分页。</summary>
    private void BuildAiecsPage()
    {
        GmPageView page = CreatePage(GmPageId.Aiecs);
        AddPageIntro(
            page.Content,
            "AIECS 实战开发",
            "控制开发入口中的真实 ECS 单位；测试场景、玩家参与开关与运行统计均集中在这里。");

        Transform grid = CreateActionGrid(page.Content, 4, 256f, 60f, 8);
        aiecsPlayerDuelButton = CreateSearchableButton(
            grid,
            GmPageId.Aiecs,
            "玩家对战",
            "AIECS ECS 玩家 对战 duel",
            () => StartAiecsScenario(AiecsPlaygroundMode.PlayerDuel),
            60f);
        aiecsArmiesButton = CreateSearchableButton(
            grid,
            GmPageId.Aiecs,
            "两军交战",
            "AIECS ECS 两军 战斗 增援 armies",
            ReinforceAiecsArmies,
            60f);
        aiecsWanderButton = CreateSearchableButton(
            grid,
            GmPageId.Aiecs,
            "无目标游荡",
            "AIECS ECS 游荡 wander",
            () => StartAiecsScenario(AiecsPlaygroundMode.Wander),
            60f);
        aiecsFleeButton = CreateSearchableButton(
            grid,
            GmPageId.Aiecs,
            "低血量逃跑",
            "AIECS ECS 低血量 逃跑 flee",
            () => StartAiecsScenario(AiecsPlaygroundMode.Flee),
            60f);
        aiecsClearButton = CreateSearchableButton(
            grid,
            GmPageId.Aiecs,
            "清理",
            "AIECS ECS 清理 clear",
            ClearAiecsScenario,
            60f);
        aiecsPlayerParticipatesButton = CreateSearchableButton(
            grid,
            GmPageId.Aiecs,
            "玩家参与感知与战斗：关",
            "AIECS ECS 玩家 参与 感知 战斗 toggle",
            ToggleAiecsPlayerParticipation,
            60f);
        aiecsLocalAvoidanceButton = CreateSearchableButton(
            grid,
            GmPageId.Aiecs,
            "局部避让：关",
            "AIECS ECS 局部 避让 separation avoidance toggle",
            ToggleAiecsLocalAvoidance,
            60f);
        aiecsPopulationButton = CreateSearchableButton(
            grid, GmPageId.Aiecs, "每军数量", "AIECS ECS 压力 数量 population",
            CycleAiecsPopulation, 60f);
        CreateSearchableButton(grid, GmPageId.Aiecs, "生物产出验收", "AIECS ECS 生物 战利品 loot 验收",
            () => { ResolveAiecsPlayground()?.QueueActorLootTest(); RefreshAiecsPage(); }, 60f);

        aiecsStatusText = CreateAiecsReadout(page.Content, "正在连接 AIECS 实战开发入口…", 42f, true);
        aiecsStatisticsText = CreateAiecsReadout(page.Content, "存活 0 / 目标 0 / 游荡 0 / 追击 0 / 逃跑 0 / 攻击 0", 30f);
        aiecsTickText = CreateAiecsReadout(page.Content, "本 Tick：感知请求 0，候选 0，LOS 0，热点格 0", 30f);
        aiecsCumulativeText = CreateAiecsReadout(page.Content, "累计：玩家受击 0（-0.0 HP），武器→ECS 命中 0，死亡 0，掉落 0", 30f);
        aiecsNavigationText = CreateAiecsReadout(page.Content, "共享目标 0 / Chunk 0 / 出口图 0 / 目标图 0 / 区块路线 0", 30f);
        RefreshAiecsPage();
    }

    private static TextMeshProUGUI CreateAiecsReadout(
        Transform parent,
        string initialText,
        float height,
        bool emphasized = false)
    {
        TextMeshProUGUI text = CreateText(
            parent,
            initialText,
            emphasized ? 14f : 13f,
            emphasized ? GmTextPrimary : GmTextSecondary);
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        text.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        return text;
    }

    private void StartAiecsScenario(AiecsPlaygroundMode mode)
    {
        AiecsPlayground playground = ResolveAiecsPlayground();
        if (playground == null)
        {
            SetStatus("当前世界没有可用的 AIECS 正式宿主，无法创建 GM 调试入口。", Color.yellow);
            RefreshAiecsPage();
            return;
        }

        playground.StartScenario(mode);
        SetStatus(playground.Status, GmTextSecondary);
        RefreshAiecsPage();
    }

    private void ReinforceAiecsArmies()
    {
        AiecsPlayground playground = ResolveAiecsPlayground();
        if (playground == null)
        {
            SetStatus("当前世界没有可用的 AIECS 正式宿主，无法创建 GM 调试入口。", Color.yellow);
            RefreshAiecsPage();
            return;
        }

        playground.StartOrReinforceArmies();
        SetStatus(playground.Status, GmTextSecondary);
        RefreshAiecsPage();
    }

    private void ClearAiecsScenario()
    {
        AiecsPlayground playground = ResolveAiecsPlayground();
        if (playground == null)
        {
            SetStatus("当前世界没有可用的 AIECS 正式宿主，无法创建 GM 调试入口。", Color.yellow);
            RefreshAiecsPage();
            return;
        }

        playground.ClearScenario();
        SetStatus(playground.Status, GmTextSecondary);
        RefreshAiecsPage();
    }

    private void ToggleAiecsPlayerParticipation()
    {
        AiecsPlayground playground = ResolveAiecsPlayground();
        if (playground == null || !playground.HasActiveScenario)
        {
            SetStatus("AIECS 当前没有可切换玩家参与状态的开发场景。", Color.yellow);
            RefreshAiecsPage();
            return;
        }

        playground.SetPlayerParticipates(!playground.PlayerParticipates);
        RefreshAiecsPage();
    }

    /// <summary>切换连续邻居 Steering 与密度分流；共享 Flow Field 和地形约束始终保留。</summary>
    private void ToggleAiecsLocalAvoidance()
    {
        AiecsPlayground playground = ResolveAiecsPlayground();
        if (playground == null)
        {
            SetStatus("当前世界没有可用的 AIECS 正式宿主，无法创建 GM 调试入口。", Color.yellow);
            RefreshAiecsPage();
            return;
        }

        playground.SetLocalAvoidanceEnabled(!playground.LocalAvoidanceEnabled);
        RefreshAiecsPage();
    }

    /// <summary>只调整下一波测试请求；不修改正在交战单位或正式生态配置。</summary>
    private void CycleAiecsPopulation()
    {
        AiecsPlayground playground = ResolveAiecsPlayground();
        if (playground == null) return;
        int[] levels = { 100, 500, 1000, 2500, 5000, 10000 };
        int next = levels[0];
        foreach (int level in levels)
            if (level > playground.UnitsPerArmy) { next = level; break; }
        playground.UnitsPerArmy = next;
        RefreshAiecsPage();
    }

    /// <summary>仅在 GM 的 AIECS 分页可见时以 10Hz 更新文本，避免无窗口时制造字符串垃圾。</summary>
    private void RefreshAiecsPageIfNeeded()
    {
        if (windowRoot == null || !windowRoot.activeSelf ||
            !gmPages.TryGetValue(GmPageId.Aiecs, out GmPageView page) ||
            page.Root == null || !IsPageSectionInView(GmPageId.Aiecs) ||
            Time.unscaledTime < nextAiecsPageRefreshTime)
            return;

        nextAiecsPageRefreshTime = Time.unscaledTime + 0.1f;
        RefreshAiecsPage();
    }

    private void RefreshAiecsPage()
    {
        if (aiecsStatusText == null)
            return;

        AiecsPlayground playground = ResolveAiecsPlayground();
        bool exists = playground != null;
        bool ready = exists && playground.IsReady;
        bool activeScenario = exists && playground.HasActiveScenario;
        bool playerParticipates = activeScenario && playground.PlayerParticipates;
        bool localAvoidanceEnabled = exists && playground.LocalAvoidanceEnabled;

        if (aiecsPlayerDuelButton != null) aiecsPlayerDuelButton.interactable = ready;
        if (aiecsArmiesButton != null) aiecsArmiesButton.interactable = ready;
        if (aiecsWanderButton != null) aiecsWanderButton.interactable = ready;
        if (aiecsFleeButton != null) aiecsFleeButton.interactable = ready;
        if (aiecsClearButton != null) aiecsClearButton.interactable = activeScenario;
        if (aiecsPopulationButton != null)
        {
            aiecsPopulationButton.interactable = exists;
            if (exists)
                SetLocalizedFormat(aiecsPopulationButton.GetComponentInChildren<TextMeshProUGUI>(true),
                    "每军 {0}（点击切换）", playground.UnitsPerArmy);
            else
                SetButtonLabel(aiecsPopulationButton, "每军数量");
        }
        if (aiecsPlayerParticipatesButton != null)
        {
            aiecsPlayerParticipatesButton.interactable = activeScenario;
            TextMeshProUGUI label = aiecsPlayerParticipatesButton.GetComponentInChildren<TextMeshProUGUI>(true);
            SetLocalizedProvider(label, () => FlatWorldLocalizationService.GetUiFormat(
                "玩家参与感知与战斗：{0}",
                FlatWorldLocalizationService.GetUiText(playerParticipates ? "开" : "关")));
            SetGmButtonVisual(
                aiecsPlayerParticipatesButton,
                playerParticipates ? GmSelection : GmSurfaceRaised,
                playerParticipates);
        }
        if (aiecsLocalAvoidanceButton != null)
        {
            aiecsLocalAvoidanceButton.interactable = exists;
            TextMeshProUGUI label = aiecsLocalAvoidanceButton.GetComponentInChildren<TextMeshProUGUI>(true);
            SetLocalizedProvider(label, () => FlatWorldLocalizationService.GetUiFormat(
                "局部避让：{0}",
                FlatWorldLocalizationService.GetUiText(localAvoidanceEnabled ? "开" : "关")));
            SetGmButtonVisual(
                aiecsLocalAvoidanceButton,
                localAvoidanceEnabled ? GmSelection : GmSurfaceRaised,
                localAvoidanceEnabled);
        }

        if (aiecsArmiesButton != null)
        {
            if (exists)
                SetLocalizedFormat(aiecsArmiesButton.GetComponentInChildren<TextMeshProUGUI>(true),
                    "两军交战（每次 +{0}）", playground.ReinforcementUnitCount);
            else
                SetButtonLabel(aiecsArmiesButton, "两军交战");
        }

        if (!exists)
        {
            SetLocalizedText(aiecsStatusText, "当前尚未进入可用的正式 AIECS 世界；进入世界后本页会自动连接调试入口。");
            SetLocalizedFormat(aiecsStatisticsText,
                "存活 {0} / 目标 {1} / 游荡 {2} / 追击 {3} / 逃跑 {4} / 攻击 {5}", 0, 0, 0, 0, 0, 0);
            SetLocalizedFormat(aiecsTickText,
                "本 Tick：感知请求 {0}，候选 {1}，LOS {2}，热点格 {3}", 0, 0, 0, 0);
            SetLocalizedFormat(aiecsCumulativeText,
                "累计：玩家受击 {0}（-{1:0.0} HP），武器→ECS 命中 {2}，死亡 {3}，掉落 {4}",
                0, 0f, 0, 0, 0);
            SetLocalizedFormat(aiecsNavigationText,
                "共享目标 {0} / Chunk {1} / 出口图 {2} / 目标图 {3} / 区块路线 {4}", 0, 0, 0, 0, 0);
            return;
        }

        SetLocalizedProvider(aiecsStatusText, () => LocalizeAiecsStatus(playground.Status));
        AiecsDebugSnapshot snapshot = playground.CaptureDebugSnapshot();
        SetLocalizedFormat(aiecsStatisticsText,
            "存活 {0} / 目标 {1} / 游荡 {2} / 追击 {3} / 逃跑 {4} / 攻击 {5}",
            snapshot.Alive, snapshot.Targets, snapshot.Wander, snapshot.Chase, snapshot.Flee, snapshot.Attack);
        SetLocalizedFormat(aiecsTickText,
            "本 Tick：感知请求 {0}，候选 {1}，LOS {2}，热点格 {3}",
            snapshot.PerceptionRequests, snapshot.Candidates, snapshot.Los, snapshot.HotBucket);
        SetLocalizedFormat(aiecsCumulativeText,
            "累计：玩家受击 {0}（-{1:0.0} HP），武器→ECS 命中 {2}，死亡 {3}，掉落 {4}",
            snapshot.PlayerHits, snapshot.PlayerDamage, snapshot.WeaponHits, snapshot.Deaths, snapshot.Drops);
        SetLocalizedFormat(aiecsNavigationText,
            "共享目标 {0} / Chunk {1} / 出口图 {2} / 目标图 {3} / 区块路线 {4}",
            snapshot.SharedGoalCount, snapshot.CachedChunkCount, snapshot.ExitFieldBuilds,
            snapshot.TargetFieldBuilds, snapshot.HighLevelRouteBuilds);
    }

    // 已知状态模板按当前语言显示，异常正文和稳定 ID 保留原值。
    private static string LocalizeAiecsStatus(string status)
    {
        if (string.IsNullOrEmpty(status)) return string.Empty;
        if (status.StartsWith("模拟已停止：", StringComparison.Ordinal))
            return FlatWorldLocalizationService.GetUiFormat("模拟已停止：{0}", status.Substring("模拟已停止：".Length));
        if (status.StartsWith("创建失败：", StringComparison.Ordinal))
            return FlatWorldLocalizationService.GetUiFormat("创建失败：{0}", status.Substring("创建失败：".Length));
        if (TryReadStatusNumber(status, "本场累计生成 ", " 个 ECS 单位", out int spawned))
            return FlatWorldLocalizationService.GetUiFormat(
                "本场累计生成 {0} 个 ECS 单位；当前存活与行为见下方实时统计。", spawned);
        if (TryReadStatusNumber(status, "两军交战：累计生成 ", "；本次增援 ", out int total) &&
            TryReadStatusNumber(status, "；本次增援 ", " / 目标 ", out int added) &&
            TryReadStatusNumber(status, " / 目标 ", "。当前存活见下方统计。", out int target))
        {
            return FlatWorldLocalizationService.GetUiFormat(
                "两军交战：累计生成 {0}；本次增援 {1} / 目标 {2}。当前存活见下方统计。",
                total, added, target);
        }
        const string lootPrefix = "已提交 1 个生物产出验收请求：";
        if (status.StartsWith(lootPrefix, StringComparison.Ordinal))
        {
            int end = status.IndexOf('；', lootPrefix.Length);
            if (end >= lootPrefix.Length)
                return FlatWorldLocalizationService.GetUiFormat(
                    "已提交 1 个生物产出验收请求：{0}；等待真实占格与生成。未修改死亡数量或掉落概率。",
                    status.Substring(lootPrefix.Length, end - lootPrefix.Length));
        }
        return FlatWorldLocalizationService.GetUiText(status);
    }

    private static bool TryReadStatusNumber(string source, string prefix, string suffix, out int value)
    {
        value = 0;
        int start = source.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0) return false;
        start += prefix.Length;
        int end = source.IndexOf(suffix, start, StringComparison.Ordinal);
        return end > start && int.TryParse(source.Substring(start, end - start), out value);
    }
    /// <summary>正常 GameStart 世界按需创建空闲调试入口；只有场景按钮真正触发后才暂停正式生态并创建开发模拟。</summary>
    private static AiecsPlayground ResolveAiecsPlayground()
    {
        if (AiecsPlayground.Active != null)
            return AiecsPlayground.Active;

        GameManager manager = GameManager.Instance;
        if (manager == null || !manager.IsInGameWorld)
            return null;

        return AiecsEcologyRuntimeHost.Active?.EnsureDebugPlayground();
    }

    private static void SetButtonLabel(Button button, string value)
    {
        if (button == null)
            return;

        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
            SetLocalizedText(label, value);
    }

    #endregion

}
