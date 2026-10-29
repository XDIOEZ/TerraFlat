using System;
using UnityEngine;

internal static class Program
{
    #region 配置与绑定事务回归
    private static int assertions;
    private static void Check(bool value, string name)
    {
        if (!value) throw new InvalidOperationException("FAIL: " + name);
        assertions++;
        Console.WriteLine("PASS: " + name);
    }
    private static void Reject(Action action, string name)
    {
        try { action(); }
        catch (InvalidOperationException) { Check(true, name); return; }
        throw new InvalidOperationException("FAIL: " + name);
    }
    private static void Main()
    {
        var definition = new MachineDefinition { Id = "test:cultivator" };
        GameRes.ExistingInstance = null;
        Reject(() => _ = definition.Content, "resources unavailable is contextual failure, not null content");
        Check(definition.CachedContent == null, "failed resource lookup does not publish cache");
        var resources = new GameRes();
        GameRes.ExistingInstance = resources;
        Reject(() => _ = definition.Content, "missing item definition rejects direct property read");
        var item = new RuntimeItemDefinition { Id = definition.Id };
        item.ModuleDefinitions.Add(new() { PrefabId = "cultivator" });
        item.ModuleDefinitions.Add(new() { PrefabId = "auxiliary" });
        item.ModuleDefinitions.Add(new() { PrefabId = "disabled-missing", Enabled = false });
        resources.Definitions.Add(item.Id, item);
        resources.Prefabs.Add("cultivator", new GameObject { Module = new Mod_Cultivator() });
        Reject(() => _ = definition.Content, "partial module resolution rejects missing prefab");
        Check(definition.CachedContent == null, "partial module construction remains unpublished");
        resources.Prefabs.Add("auxiliary", new GameObject { Module = new AuxiliaryModule() });
        MachineContent content = definition.Content;
        Check(content.Has<Mod_Cultivator>() && content.Has<AuxiliaryModule>(), "direct cold property resolves all enabled modules");
        Check(content.Find<Module>() != null && !content.Has(typeof(string)), "required and absent module queries remain distinct");
        int lookups = resources.Lookups;
        for (int i = 0; i < 8192; i++)
            if (!ReferenceEquals(content, definition.Content)) throw new InvalidOperationException("cache changed");
        Check(resources.Lookups == lookups, "8192 hot reads do not query resources or rebuild content");
        GameRes.ExistingInstance = null;
        var prewarm = new MachineDefinition { Id = definition.Id };
        Check(MachineContent.GetOrCreateDefinitionContent(prewarm, resources).Has<Mod_Cultivator>(),
            "prewarm uses explicit resource owner before global resource publication");
        GameRes.ExistingInstance = resources;

        var state = new CultivatorState();
        Sprite plant = new(), body = new();
        state.Plots.Add(plant);
        var binary = new Ex_ModData_MemoryPackable { ID = Mod_Cultivator.ModuleId };
        binary.WriteData(state);
        var snapshot = new ItemData();
        snapshot.ModuleDataDic.Add("cultivator", binary);
        var node = new MachineEntity { Definition = definition, Snapshot = snapshot };
        var view = new CultivatorPlotPresentation();
        view.Refresh(node);
        Check(view.PlotCount == 1 && view.TryGetPlot(0, body, out var shown, out _, out _) && shown == plant,
            "cold state projects without activation");
        Check(node.Logic == null && !node.Active && ReferenceEquals(snapshot, node.Snapshot), "projection preserves authority state");
        Check(!view.TryGetPlot(-1, body, out _, out _, out _) && !view.TryGetPlot(1, body, out _, out _, out _),
            "out of range plots remain hidden");
        view.Refresh(null);
        Check(view.PlotCount == 0 && !view.TryGetPlot(0, body, out _, out _, out _), "unbind clears all plot state");
        view.Refresh(node);
        Check(view.PlotCount == 1, "rebind restores cold snapshot");

        var missingNode = new MachineEntity { Definition = new MachineDefinition { Id = "missing" }, Snapshot = snapshot };
        Reject(() => view.Refresh(missingNode), "failed second binding rejects missing configuration");
        Check(view.PlotCount == 0 && !view.TryGetPlot(0, body, out _, out _, out _), "failed second binding cannot expose old plant");
        missingNode.Definition.Id = definition.Id;
        view.Refresh(missingNode);
        Check(view.PlotCount == 1, "same failed definition can be repaired and retried");

        ItemData badSnapshot = new();
        node.Snapshot = badSnapshot;
        Reject(() => view.Refresh(node), "missing snapshot module rejects binding");
        Check(view.PlotCount == 0, "snapshot failure clears previously valid state");
        node.Snapshot = snapshot;
        view.Refresh(node);
        Check(view.PlotCount == 1, "snapshot repair allows full retry");

        definition.Content = null;
        resources.Prefabs["cultivator"] = new GameObject { Module = new AuxiliaryModule() };
        Reject(() => view.Refresh(node), "same machine definition reload rechecks replaced content modules");
        Check(view.PlotCount == 0, "removed module does not retain old layout");
        definition.Content = null;
        resources.Prefabs["cultivator"] = new GameObject { Module = new Mod_Cultivator { Visual = null } };
        Reject(() => view.Refresh(node), "missing layout is contextual failure");
        definition.Content = null;
        resources.Prefabs["cultivator"] = new GameObject { Module = new Mod_Cultivator() };
        view.Refresh(node);
        Check(view.PlotCount == 1 && !ReferenceEquals(content, definition.Content), "content invalidation restores corrected layout");

        var liveState = new CultivatorState();
        liveState.Plots.Add(null);
        node.Logic = new CultivatorLogic { State = liveState };
        view.Refresh(node);
        Check(!view.TryGetPlot(0, body, out _, out _, out _), "active logic takes precedence over saved plant");
        node.Logic = null;
        view.Refresh(node);
        Check(view.TryGetPlot(0, body, out _, out _, out _), "return to cold state reads snapshot again");
        Console.WriteLine($"PASS: {assertions} assertions using production content and binding code; Unity boundaries are doubles.");
    }
    #endregion
}
