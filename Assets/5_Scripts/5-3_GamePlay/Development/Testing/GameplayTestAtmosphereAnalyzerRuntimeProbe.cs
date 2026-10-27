#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>真实手持仪器走正式使用、窗口和模块卸载入口，结束图之后再清理自有道具。</summary>
public static class GameplayTestAtmosphereAnalyzerRuntimeProbe
{
    #region 真实道具与屏幕流程登记
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void Register() => GameplayTestRuntimeProbe.RegisterAsync(
        "environment", "atmosphere_analyzer_lifecycle", CaptureAnalyzerLifecycle);

    private static IEnumerator CaptureAnalyzerLifecycle(Player player, JObject args, Action<JObject> completed)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        var scope = new AnalyzerScope();
        GameplayTestRuntimeProbe.RegisterCleanup(scope);
        ItemData source = GameRes.ExistingInstance.CreateItemData("AtmosphereAnalyzer");
        scope.HeldItem = ItemMgr.Instance.InstantiateHeldItem(source, player, player.transform);
        scope.HeldItem.Load();
        Mod_AtmosphereAnalyzer analyzer = scope.HeldItem.GetComponentInChildren<Mod_AtmosphereAnalyzer>(true) ??
            throw new InvalidOperationException("大气检测仪没有装配正式测量模块。");
        var result = new JObject
        {
            ["canUseWhileHeld"] = analyzer.CanMeasure(player),
            ["nullActorRejected"] = !analyzer.CanMeasure(null)
        };
        analyzer.PresentLocalUse();
        yield return null;
        AtmosphereAnalyzerPanel first = AtmosphereAnalyzerPanel.Current;
        result["openedByLocalUse"] = first != null && analyzer.IsPanelOpen && AtmosphereAnalyzerPanel.IsShowing(analyzer);
        AtmosphereMeasurementSnapshot initial = analyzer.ReadMeasurement(player);
        result["sourceWorldKey"] = initial.WorldKey;
        result["actualWorldKey"] = GameplayWorldRegistry.GetItemWorldKey(player);
        result["sourceStatus"] = initial.Status.ToString();
        result["totalMoles"] = initial.TotalMoles;
        result["pressureKPa"] = initial.PressureKPa;
        result["componentCount"] = initial.Components.Count;
        result["visibleComponentCount"] = first != null ? first.VisibleComponentCount : -1;
        result["screenUsesMeasuredSnapshot"] = first != null && ReferenceEquals(first.Snapshot, initial);
        result["unchangedInventoryReusesSnapshot"] = ReferenceEquals(initial, analyzer.ReadMeasurement(player));

        BasePanel instrumentPanel = first != null ? first.GetComponent<BasePanel>() : null;
        if (instrumentPanel != null)
        {
            instrumentPanel.OnCancel(new BaseEventData(EventSystem.current));
            yield return null;
            result["cancelEventClosedPanel"] = !instrumentPanel.IsOpen() && !analyzer.IsPanelOpen;
        }
        else result["cancelEventClosedPanel"] = false;
        analyzer.PresentLocalUse();
        yield return null;
        result["cancelEventReopenedSamePanel"] = analyzer.IsPanelOpen &&
            ReferenceEquals(AtmosphereAnalyzerPanel.Current, first);

        bool hasMeasuredPlanet = AtmosphereMeasurementService.TryResolvePlanetForWorld(initial.WorldKey,
            SaveDataMgr.Instance?.SaveData?.PlanetData_Dict, out PlanetData measuredPlanet) &&
            measuredPlanet?.Atmosphere != null;
        AtmosphereState measuredAtmosphere = hasMeasuredPlanet ? measuredPlanet.Atmosphere : null;
        decimal originalTotalMoles = initial.TotalMoles;
        decimal originalComponentMoleSum = 0m;
        foreach (AtmosphereMeasurementComponent component in initial.Components)
            originalComponentMoleSum += component.Moles;
        ulong sampleRandomState = 0x5A17UL;
        FluidBatch extractedBatch = default;
        bool atmosphereExtracted = measuredAtmosphere != null &&
            AtmosphereService.TryExtract(measuredAtmosphere, 1m, ref sampleRandomState, out extractedBatch);
        result["atmosphereExtracted"] = atmosphereExtracted;
        if (atmosphereExtracted)
        {
            try
            {
                AtmosphereMeasurementSnapshot extractedMeasurement = analyzer.ReadMeasurement(player);
                result["extractionRevisionAdvanced"] = extractedMeasurement.Revision > initial.Revision;
                result["extractionSnapshotReplaced"] = !ReferenceEquals(extractedMeasurement, initial);
                result["extractionReducedTotal"] = extractedMeasurement.TotalMoles < originalTotalMoles;
                decimal oldSnapshotComponentMoleSum = 0m;
                foreach (AtmosphereMeasurementComponent component in initial.Components)
                    oldSnapshotComponentMoleSum += component.Moles;
                result["oldSnapshotUnchangedDuringExtraction"] = initial.TotalMoles == originalTotalMoles &&
                    oldSnapshotComponentMoleSum == originalComponentMoleSum;
                result["extractedTotalMoles"] = extractedMeasurement.TotalMoles;
                yield return null;
                AtmosphereAnalyzerPanel extractedPanel = AtmosphereAnalyzerPanel.Current;
                result["panelSnapshotUpdatedAfterExtraction"] = extractedPanel != null &&
                    ReferenceEquals(extractedPanel.Snapshot, analyzer.ReadMeasurement(player)) &&
                    extractedPanel.Snapshot.TotalMoles < originalTotalMoles;
            }
            finally
            {
                AtmosphereEmissionResult restoration = AtmosphereService.Emit(measuredAtmosphere, extractedBatch);
                result["atmosphereRestoredByEmission"] = restoration.RetainedMoles == extractedBatch.GasMoles;
            }
            yield return null;
            AtmosphereMeasurementSnapshot restoredMeasurement = analyzer.ReadMeasurement(player);
            result["restoredTotalMatchesBefore"] = restoredMeasurement.TotalMoles == originalTotalMoles;
            result["restoredRevisionAdvanced"] = restoredMeasurement.Revision > initial.Revision;
            result["panelSnapshotUpdatedAfterRestore"] = first != null &&
                ReferenceEquals(first.Snapshot, restoredMeasurement);
        }
        else
        {
            result["extractionRevisionAdvanced"] = false;
            result["extractionSnapshotReplaced"] = false;
            result["extractionReducedTotal"] = false;
            result["oldSnapshotUnchangedDuringExtraction"] = false;
            result["panelSnapshotUpdatedAfterExtraction"] = false;
            result["atmosphereRestoredByEmission"] = false;
            result["restoredTotalMatchesBefore"] = false;
            result["restoredRevisionAdvanced"] = false;
            result["panelSnapshotUpdatedAfterRestore"] = false;
        }

        analyzer.PresentLocalUse();
        yield return null;
        result["secondUseClosed"] = !analyzer.IsPanelOpen;
        analyzer.I_ShowPanel();
        yield return null;
        result["reopenedExistingPanel"] = analyzer.IsPanelOpen && AtmosphereAnalyzerPanel.Current == first;
        scope.HeldItem.SetInHand(false);
        yield return null;
        result["leavingHandClosesPanel"] = !analyzer.IsPanelOpen;
        result["cannotReadAfterLeavingHand"] = !analyzer.CanMeasure(player) &&
            analyzer.ReadMeasurement(player).Status == AtmosphereMeasurementStatus.Unavailable;
        analyzer.I_ShowPanel();
        result["cannotOpenAfterLeavingHand"] = !analyzer.IsPanelOpen;
        scope.HeldItem.SetInHand(true);
        analyzer.I_ShowPanel();
        yield return null;
        result["returnedToHandReopens"] = analyzer.IsPanelOpen;
        analyzer.Unload();
        result["moduleUnloadClosesPanel"] = !analyzer.IsPanelOpen;
        analyzer.Load();
        analyzer.I_ShowPanel();
        yield return null;
        result["moduleReloadReopens"] = analyzer.IsPanelOpen;
        result["finalScreenSnapshotBound"] = AtmosphereAnalyzerPanel.Current != null &&
            ReferenceEquals(AtmosphereAnalyzerPanel.Current.Snapshot, analyzer.ReadMeasurement(player));
        completed(result);
    }

    private sealed class AnalyzerScope : IDisposable
    {
        public Item HeldItem;
        public void Dispose()
        {
            if (HeldItem == null || HeldItem.DestructionHandled) return;
            ItemMgr.Instance.DespawnItem(HeldItem, saveData: false);
            HeldItem = null;
        }
    }
    #endregion
}
#endif
