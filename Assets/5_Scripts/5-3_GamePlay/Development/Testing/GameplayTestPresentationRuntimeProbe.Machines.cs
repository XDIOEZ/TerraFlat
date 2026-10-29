using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static partial class GameplayTestPresentationRuntimeProbe
{
    #region 全目录冷机器配置与落地表现预备
    // 克隆全部正式机器定义，冷读取和预备表现不得创建逻辑、库存或改写共享定义。
    private static JObject CaptureColdMachineContentAndPreparedVisuals(Player player, JObject args)
    {
        GameRes resources = GameRes.ExistingInstance;
        var root = new GameObject("ColdMachinePresentationProbe");
        root.SetActive(false);
        SceneManager.MoveGameObjectToScene(root, player.gameObject.scene);
        var samples = new JArray();
        try
        {
            foreach (RuntimeItemDefinition item in resources.ItemDefinitions.Values.OrderBy(value => value.Id, StringComparer.Ordinal))
            {
                MachineDefinition shared = MachineCatalog.Get(item.Id);
                if (shared == null) continue;
                MachineContent sharedCache = shared.CachedContent;
                MachineDefinition definition = MachinePersistence.Clone(shared);
                var node = new MachineEntity { Definition = definition, Snapshot = item.CreateItemData() };
                ItemData snapshot = node.Snapshot;
                string specialData = snapshot.ItemSpecialData;
                var row = new JObject { ["id"] = item.Id, ["logicId"] = definition.LogicId,
                    ["initiallyCold"] = definition.CachedContent == null && node.Logic == null && !node.Active };
                MechanicalDepthVisual visual = null;
                try
                {
                    // 先实走降落预备入口，不能由测试提前填好配置掩盖冷读取问题。
                    visual = MechanicalDepthVisual.CreatePrepared(root.transform, definition, item);
                    MachineContent content = definition.Content;
                    row["contentResolved"] = content != null && ReferenceEquals(content.Definition, item);
                    row["contentReused"] = ReferenceEquals(content, definition.Content) &&
                        ReferenceEquals(content, MachineContent.GetOrCreateDefinitionContent(definition));
                    row["allDeclaredModulesAvailable"] = item.ModuleAssemblies.All(module =>
                        !module.Definition.Enabled || module.ModuleType == null || content.Has(module.ModuleType));
                    row["hasFuel"] = content.Has<Mod_Fuel>();
                    row["hasFurnace"] = content.Has<Mod_Furnace>();
                    row["hasDrying"] = content.Has<Mod_Meatrack>();
                    row["hasCultivator"] = content.Has<Mod_Cultivator>();
                    row["hasCombustion"] = content.Has<Mod_CombustionVisual>();
                    row["hasLighting"] = definition.Lighting != null;
                    Light2D light = visual.GetComponentInChildren<Light2D>(true);
                    row["preparedLight"] = light != null;
                    row["preparedInactive"] = !visual.gameObject.activeInHierarchy && (light == null || !light.enabled);
                    row["stillCold"] = !node.Active && node.Logic == null && node.Processor == null && node.State == null;
                    row["snapshotUnchanged"] = ReferenceEquals(snapshot, node.Snapshot) && specialData == snapshot.ItemSpecialData;
                    row["sharedDefinitionUnchanged"] = ReferenceEquals(sharedCache, shared.CachedContent);
                    samples.Add(row);
                }
                finally { if (visual != null) visual.Dispose(); }
            }
        }
        finally { UnityEngine.Object.Destroy(root); }

        var missing = new MachineDefinition { Id = "__missing_cold_machine_content__" };
        bool rejected = false;
        try { _ = missing.Content; }
        catch (InvalidOperationException error) { rejected = error.Message.Contains(missing.Id); }
        bool failedCacheEmpty = missing.CachedContent == null;
        missing.Id = "SpaceCultivator";
        MachineContent retried = missing.Content;
        MachineDefinition serialized = MachinePersistence.Clone(missing);
        bool serializedCacheEmpty = serialized.CachedContent == null;
        bool restored = serialized.Content.Has<Mod_Cultivator>();
        missing.Content = null;
        MachineContent rebound = missing.Content;
        return new JObject
        {
            ["samples"] = samples, ["missingDefinitionRejected"] = rejected,
            ["failedCacheEmpty"] = failedCacheEmpty, ["retrySucceeded"] = retried.Has<Mod_Cultivator>(),
            ["serializedCacheEmpty"] = serializedCacheEmpty, ["restoredContentAvailable"] = restored,
            ["invalidatedContentRebuilt"] = !ReferenceEquals(retried, rebound) && rebound.Has<Mod_Cultivator>()
        };
    }
    #endregion
}
