using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestPresentationRuntimeProbe
{
    #region 正式命中特效预热与复用采样

    private static JObject CaptureCombatEffectPrewarm(Player player, JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        GameRes resources = GameRes.ExistingInstance;
        if (resources == null || !resources.TryGetItemDefinition("Axe_Stone", out RuntimeItemDefinition definition))
            throw new InvalidOperationException("正式石斧定义未就绪。");

        var prefabs = new HashSet<GameEffect>();
        void Collect(GameObject root)
        {
            if (root == null) return;
            foreach (Mod_Damage weapon in root.GetComponentsInChildren<Mod_Damage>(true))
                weapon.CollectHitEffectPrefabs(prefabs);
        }
        Collect(definition.ShellPrefab);
        foreach (RuntimeItemModuleAssemblyPlan module in definition.ModuleAssemblies) Collect(module.ModulePrefab);

        VisualEffectManager manager = VisualEffectManager.Instance;
        var samples = new JArray();
        UnityEngine.Random.State beforeRandom = UnityEngine.Random.state;
        foreach (GameEffect prefab in prefabs)
        {
            int initial = manager.GetInactiveGameEffectCount(prefab);
            bool grew = manager.PrewarmGameEffect(prefab, 3);
            int afterWarmup = manager.GetInactiveGameEffectCount(prefab);
            var rented = new List<GameEffect>();
            var ids = new HashSet<int>();
            int remaining = 0;
            bool active = true;
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    GameEffect effect = manager.GetGameEffectFromPool(prefab);
                    rented.Add(effect);
                    ids.Add(effect.GetInstanceID());
                    active &= effect.gameObject.activeInHierarchy;
                }
                remaining = manager.GetInactiveGameEffectCount(prefab);
            }
            finally
            {
                foreach (GameEffect effect in rented) manager.ReturnGameEffectToPool(effect);
            }
            int returned = manager.GetInactiveGameEffectCount(prefab);
            foreach (GameEffect effect in rented) manager.ReturnGameEffectToPool(effect);
            samples.Add(new JObject
            {
                ["prefab"] = prefab.name, ["type"] = prefab.GetType().Name,
                ["initial"] = initial, ["grew"] = grew, ["afterWarmup"] = afterWarmup,
                ["uniqueRented"] = ids.Count, ["allActive"] = active, ["remaining"] = remaining,
                ["returned"] = returned, ["afterDoubleReturn"] = manager.GetInactiveGameEffectCount(prefab),
                ["allInactive"] = rented.All(effect => effect != null && !effect.gameObject.activeSelf)
            });
        }

        return new JObject
        {
            ["scope"] = "live-isolated-world", ["itemId"] = definition.Id,
            ["samples"] = samples,
            ["hasDamageText"] = prefabs.Any(prefab => prefab is DamageTextEffect),
            ["hasSlicing"] = prefabs.Any(prefab => prefab is SlicingEffect),
            ["nullRejected"] = !manager.PrewarmGameEffect(null, 3),
            ["randomUnchanged"] = beforeRandom.Equals(UnityEngine.Random.state)
        };
    }

    #endregion
}
