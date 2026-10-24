using System;
using System.Collections.Generic;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class GMReflectionConsole
{
    #region 世界生物信息分页

    private readonly MonsterSpawnerManager.WorldCreatureMonitor worldCreatureMonitor = new();
    private readonly List<TextMeshProUGUI> worldCreatureRows = new();
    private TextMeshProUGUI worldCreatureSummary, worldCreatureStatus;
    private Transform worldCreatureContent;
    private float nextWorldCreatureRefreshAt;

    private void BuildWorldCreaturesPage()
    {
        GmPageView page = CreatePage(GmPageId.WorldCreatures);
        worldCreatureContent = page.Content;
        AddPageIntro(page.Content, "世界生物信息", "跟随当前玩家所在世界。所有自然生物共用一个倒计时，到期后从附近已加载区块的合格生物中抽取。");
        worldCreatureSummary = CreateCreatureMonitorText(page.Content, "等待进入世界…", 16f, 108f);
        worldCreatureStatus = CreateCreatureMonitorText(page.Content, "等待读取生成调度…", 14f, 80f);
        RegisterSearchEntry(GmPageId.WorldCreatures, "世界生物信息",
            "生物 数量 计数 刷新 倒计时 上限 生态 预算 卡住 spawn countdown population", page.Content);
        AddPageHint(page.Content,
            "倒计时 / 上限均为游戏秒，直接读取真实进度；重试与调度使用现实秒。合计包含已登记的存活 AI、蜜蜂与休眠居民，不含玩家、尸体和创建中的候选。",
            48f);
    }

    // 仅在信息区可见时读取快照，复用文本行与容器，关闭窗口不扫描生物。
    private void RefreshWorldCreaturesPageIfNeeded()
    {
        if (worldCreatureSummary == null || !IsPageSectionInView(GmPageId.WorldCreatures) ||
            Time.unscaledTime < nextWorldCreatureRefreshAt) return;
        nextWorldCreatureRefreshAt = Time.unscaledTime + 0.25f;
        Player player = ItemMgr.GetExistingInstance()?.User_Player;
        string world = player?.Data?.CurrentSceneName;
        MonsterSpawnerManager manager = MonsterSpawnerManager.GetForWorld(world, create: false);
        if (player == null || manager == null)
        {
            worldCreatureStatus.color = GmTextPrimary;
            if (player == null)
                SetLocalizedText(worldCreatureSummary, "当前没有本地玩家，生物数量暂不可用。");
            else
                SetLocalizedFormat(worldCreatureSummary,
                    "当前世界：{0}\n尚未找到该世界的生成器，生物数量和倒计时暂不可用。", world);
            SetLocalizedText(worldCreatureStatus, "进入世界并等待生成器初始化；持续不出现时请检查控制台。");
            SetCreatureMonitorRowCount(0);
            return;
        }

        manager.CaptureWorldCreatureMonitor(worldCreatureMonitor);
        var data = worldCreatureMonitor;
        Vector3 position = player.transform.position;
        SetLocalizedFormat(
            worldCreatureSummary,
            "当前世界：{0}　玩家：({1:F1}, {2:F1})\n" +
            "存活生物合计：{3}　（活动 GO {4} / 隐藏 GO {5} / 快照休眠 {6} / ECS {7}）\n" +
            "受全局上限约束的已装载居民：{8} / {9}　创建中：{10}\n" +
            "本世界本次会话：出生成功 {11} / 恢复成功 {12} / 创建回滚 {13}",
            data.World, position.x, position.y, data.Total, data.Active, data.Hidden, data.Parked,
            data.Entities, data.LimitedPopulation, data.GlobalLimit, data.Creating, data.Births,
            data.Restores, data.Rollbacks);
        SetLocalizedProvider(worldCreatureStatus, () =>
        {
            string tick = data.TickOverdueSeconds > Mathf.Max(1f, data.TickInterval * 2f)
                ? FlatWorldLocalizationService.GetUiFormat(
                    "调度已逾期 {0:F1} 现实秒", data.TickOverdueSeconds)
                : FlatWorldLocalizationService.GetUiFormat(
                    "下次调度 {0:F1} 现实秒 / 间隔 {1:F2} 秒", data.NextTickSeconds, data.TickInterval);
            return FlatWorldLocalizationService.GetUiFormat(
                "状态：{0}\n{1}　刷新频率倍率 {2:0.##}　游戏时间倍率 {3:0.##}\n{4}",
                FlatWorldLocalizationService.GetUiText(data.Status),
                tick,
                data.FrequencyMultiplier,
                data.GameTimeScale,
                GetSharedCreatureCountdown(data));
        });
        worldCreatureStatus.color = !data.Initialized || !data.Authority || data.TickOverdueSeconds > 5f
            ? GmAccent : GmTextPrimary;

        SetCreatureMonitorRowCount(data.Entries.Count);
        for (int i = 0; i < data.Entries.Count; i++)
        {
            MonsterSpawnerManager.CreatureSpawnMonitorEntry row = data.Entries[i];
            int rowIndex = i;
            SetLocalizedProvider(worldCreatureRows[i], () => FormatCreatureMonitorRow(data, rowIndex));
        }
    }

    private void SetCreatureMonitorRowCount(int count)
    {
        while (worldCreatureRows.Count < count)
            worldCreatureRows.Add(CreateCreatureMonitorText(worldCreatureContent, string.Empty, 14f, 98f));
        for (int i = 0; i < worldCreatureRows.Count; i++)
            if (worldCreatureRows[i].gameObject.activeSelf != (i < count))
                worldCreatureRows[i].gameObject.SetActive(i < count);
    }

    private static TextMeshProUGUI CreateCreatureMonitorText(Transform parent, string value, float fontSize, float minHeight)
    {
        TextMeshProUGUI text = CreateText(parent, value, fontSize, GmTextPrimary);
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Overflow;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.gameObject.AddComponent<LayoutElement>().minHeight = minHeight;
        return text;
    }

    private static string GetSharedCreatureCountdown(MonsterSpawnerManager.WorldCreatureMonitor data)
    {
        if (!data.Authority) return FlatWorldLocalizationService.GetUiText("请在服务器或主机查看真实刷新进度。");
        if (!data.CountdownInitialized) return FlatWorldLocalizationService.GetUiText("世界统一自然刷新倒计时尚未启动。");
        if (data.PendingNaturalSpawns > 0)
            return FlatWorldLocalizationService.GetUiFormat("统一刷新：本轮 {0}，剩余 {1} 只。",
                GetCreatureMonitorName(data.SelectedSpecies), data.PendingNaturalSpawns);
        if (data.RemainingGameSeconds <= 0d)
            return FlatWorldLocalizationService.GetUiText("统一刷新已到期：正在附近已加载区块中寻找合格候选，尚未指定物种。");
        return FlatWorldLocalizationService.GetUiFormat("统一刷新倒计时：{0:0.0} / {1:0.0} 游戏秒",
            data.RemainingGameSeconds, data.IntervalGameSeconds);
    }

    private static string FormatCreatureMonitorRow(
        MonsterSpawnerManager.WorldCreatureMonitor data, int index)
    {
        MonsterSpawnerManager.CreatureSpawnMonitorEntry row = data.Entries[index];
        string clock = FlatWorldLocalizationService.GetUiText(!row.NaturalSpawnEnabled
            ? "自然刷新已关闭（不影响事件或手动生成）"
            : row.Pending > 0 ? "本轮已抽中" : "参与世界统一刷新，等待到期后按已加载区域筛选");
        string limit = row.SpeciesLimit > 0
            ? row.SpeciesLimit.ToString()
            : FlatWorldLocalizationService.GetUiText("无单独上限");
        string budget = row.Budget < 0
            ? FlatWorldLocalizationService.GetUiText("未初始化")
            : row.Budget == int.MaxValue
                ? FlatWorldLocalizationService.GetUiText("无限")
                : row.Budget.ToString();
        string attempt = string.IsNullOrEmpty(row.LastAttempt)
            ? FlatWorldLocalizationService.GetUiText("本规则尚无生成尝试记录")
            : FlatWorldLocalizationService.GetUiFormat(
                "本规则上次尝试：{0}（{1:F1} 现实秒前）",
                FlatWorldLocalizationService.GetUiText(row.LastAttempt), row.LastAttemptAge);
        string pending = data.Authority ? row.Pending.ToString() : "--";
        string lifetime = data.Authority ? row.LifetimeBirths.ToString() : "--";
        string effectiveBudget = data.Authority ? budget : "--";
        return FlatWorldLocalizationService.GetUiFormat(
            "{0} [{1}]　世界存活 {2} / {3}\n{4}　待生成 {5}\n" +
            "规则 {6}　预算 {7}　规则累计出生 {8}　出生距离 {9:0.#}～{10:0.#} 格\n" +
            "{11}　重试等待 {12:F1} 现实秒",
            GetCreatureMonitorName(row.SpeciesId), row.SpeciesId, row.Population, limit,
            clock, pending, row.RuleId, effectiveBudget, lifetime, row.MinDistance,
            row.MaxDistance, attempt, row.RetrySeconds);
    }

    private static string GetCreatureMonitorName(string species)
    {
        GameRes resources = GameRes.ExistingInstance;
        return resources != null && resources.TryGetActorDefinition(species, out RuntimeItemDefinition definition)
            ? definition.DisplayName : species;
    }

    #endregion
}
