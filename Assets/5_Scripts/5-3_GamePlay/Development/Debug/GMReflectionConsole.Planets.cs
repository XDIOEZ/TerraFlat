using System;
using System.Collections.Generic;
using FlatWorld.Networking;
using FlatWorld.Spaceflight;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class GMReflectionConsole
{
    #region 星球旁传送

    private const double PlanetTeleportAltitudeMeters = 100d;
    private readonly List<BodyState> teleportBodies = new();
    private string selectedTeleportBodyId;
    private TextMeshProUGUI planetSelectionText;
    private Button planetTeleportButton;
    private bool planetTeleportRunning;

    // 星球列表来自当前存档的宇宙会话，不依赖是否已经加载星球外观。
    private void BuildPlanetTeleportRow(Transform content)
    {
        CreateSectionTitle(content, "星球旁传送");
        GameObject row = CreateUiObject("Planet Teleport Row", content);
        row.AddComponent<LayoutElement>().preferredHeight = 44f;
        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 8f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        CreateButton(row.transform, "‹", () => CycleTeleportPlanet(-1), 40f, 40f);
        planetSelectionText = CreateValueDisplay(row.transform, "进入单机世界后读取星球", 540f, 40f);
        LayoutElement selection = planetSelectionText.transform.parent.GetComponent<LayoutElement>();
        selection.minWidth = 180f;
        selection.flexibleWidth = 1f;
        CreateButton(row.transform, "›", () => CycleTeleportPlanet(1), 40f, 40f);
        planetTeleportButton = CreateButton(row.transform, "传送到星球旁", TeleportToSelectedPlanet, 190f, 40f);
        SetGmButtonVisual(planetTeleportButton, GmSurfaceRaised, true);
        CreateButton(row.transform, "刷新", RefreshPlanetTeleportOptions, 82f, 40f);
        AddPageHint(content, "单机：从地表、地下或太空传送至目标表面外 100 米，带环绕速度；仅移动玩家，自动退出驾驶。", 44f);
        RegisterSearchEntry(GmPageId.Structures, "传送到星球旁",
            "星球 行星 恒星 月球 太阳 太空 planet space 传送", row.transform as RectTransform);
    }

    private void RefreshPlanetTeleportOptions()
    {
        teleportBodies.Clear();
        if (!GameNetwork.IsOnline && GameManager.Instance?.IsGameplayReady == true)
        {
            SpaceSession session = SpaceSession.EnsureLoaded();
            if (session.Universe != null) teleportBodies.AddRange(session.Universe.State.Bodies);
        }
        int index = teleportBodies.FindIndex(body => body.BodyId == selectedTeleportBodyId);
        if (index < 0 && teleportBodies.Count > 0)
        {
            index = teleportBodies.FindIndex(body => body.PlanetId == GameManager.Instance.ReadyPlanetData?.Name);
            if (index < 0) index = 0;
        }
        selectedTeleportBodyId = index >= 0 ? teleportBodies[index].BodyId : null;
        if (planetSelectionText != null)
            planetSelectionText.text = index >= 0
                ? $"{index + 1}/{teleportBodies.Count}  {teleportBodies[index].DisplayName}  /  {selectedTeleportBodyId}"
                : "进入单机世界后读取星球";
        if (planetTeleportButton != null)
            planetTeleportButton.interactable = index >= 0 && !planetTeleportRunning;
    }

    private void CycleTeleportPlanet(int direction)
    {
        RefreshPlanetTeleportOptions();
        if (teleportBodies.Count == 0 || planetTeleportRunning) return;
        int index = teleportBodies.FindIndex(body => body.BodyId == selectedTeleportBodyId);
        selectedTeleportBodyId = teleportBodies[(index + direction + teleportBodies.Count) % teleportBodies.Count].BodyId;
        RefreshPlanetTeleportOptions();
    }

    private void TeleportToSelectedPlanet()
    {
        if (planetTeleportRunning) return;
        RefreshPlanetTeleportOptions();
        SpaceSession session = SpaceSession.Current;
        Player player = ItemMgr.Instance?.User_Player;
        if (GameNetwork.IsOnline || GameManager.Instance?.IsGameplayReady != true || player == null ||
            session?.Universe == null || !session.Universe.TryGetBody(selectedTeleportBodyId, out BodyState body))
        {
            SetStatus("请先进入单机世界，再选择目标星球。", Color.yellow);
            return;
        }

        OrbitState motion = FindPlanetTeleportOrbit(session.Universe, body);
        if (motion == null)
        {
            SetStatus("目标星球附近被其他星体覆盖，暂时无法传送。", Color.yellow);
            return;
        }
        CancelBiomeSearch();
        planetTeleportRunning = true;
        planetTeleportButton.interactable = false;
        bool accepted = session.TryTeleportPlayerToSpace(player, motion, succeeded =>
        {
            planetTeleportRunning = false;
            if (this == null) return;
            RefreshPlanetTeleportOptions();
            SetStatus(succeeded ? $"已传送至{body.DisplayName}旁边。" : "传送未完成，请查看场景加载提示或日志。",
                succeeded ? Color.green : Color.yellow);
            if (succeeded) SetWindowVisible(false);
        });
        if (!accepted)
        {
            planetTeleportRunning = false;
            RefreshPlanetTeleportOptions();
            SetStatus("当前无法传送，请等待世界加载或场景切换完成。", Color.yellow);
        }
        else if (planetTeleportRunning)
            SetStatus($"正在传送至{body.DisplayName}旁边…", Color.white);
    }

    private static OrbitState FindPlanetTeleportOrbit(UniverseSimulation universe, BodyState body)
    {
        double speed = Math.Sqrt(body.GravityParameter / (body.RadiusMeters + PlanetTeleportAltitudeMeters));
        for (int i = 0; i < 16; i++)
        {
            OrbitState motion = universe.CreateLaunchState(body.BodyId, i * Math.PI * 2d / 16d,
                PlanetTeleportAltitudeMeters, speed);
            motion.RadiusMeters = .3d;
            bool blocked = false;
            foreach (BodyState other in universe.State.Bodies)
                if ((motion.PositionMeters - other.PositionMeters).Magnitude <= other.RadiusMeters + motion.RadiusMeters)
                { blocked = true; break; }
            if (!blocked) return motion;
        }
        return null;
    }

    #endregion
}
