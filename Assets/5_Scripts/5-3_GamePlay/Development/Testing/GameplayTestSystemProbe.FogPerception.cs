using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 雾天主动感知限制采样

    private static JObject EnvironmentFogPerception(JObject args)
    {
        if (Application.isPlaying || ItemMgr.GetExistingInstance() != null)
            throw new InvalidOperationException("雾天感知规则只允许在未加载世界的编辑态执行。");
        return WithEntityProbeItem(creature => EntityWithQuestPlayer(target =>
        {
            creature.BindData(EntityProbeData("test:fog_creature", 1));
            target.BindData(new Data_Player { PerceptionRadiusMultiplier = 3f });
            GameplayWorldRegistry.AssignItemToWorld(creature, "probe:fog-perception");
            GameplayWorldRegistry.AssignItemToWorld(target, "probe:fog-perception");
            var detector = creature.gameObject.AddComponent<Mod_ItemDetector>();
            detector.ModuleInit(creature, new Ex_ModData_MemoryPackable { ID = ModText.Detector, Enabled = true });
            creature.itemMods.AddMod(detector);
            detector.LoadRuntime();
            detector.wallsBlockPerception = false;
            detector.DetectionRadius = 20f;
            target.transform.position = new Vector3(6f, 0f);

            var config = WorldRenderingConfigCatalog.Default.denseFog;
            var source = new object();
            var otherSource = new object();
            var fog = new WeatherMgr.FogPerceptionRegion(source);
            var clear = new WeatherMgr.FogPerceptionRegion(source);
            var result = new JObject { ["beforeFog"] = detector.GetEffectiveDetectionRadius(target) };
            fog.Update(1f, config);
            fog.Add(creature);
            result["denseRadius"] = detector.GetEffectiveDetectionRadius(target);
            result["outsideBlockedWithoutWalls"] = !detector.HasLineOfSight(target);
            target.transform.position = new Vector3(config.clearRadius, 0f);
            result["boundaryVisible"] = detector.HasLineOfSight(target);
            result["basePreserved"] = detector.DetectionRadius;
            detector.DetectionRadius = 1f;
            result["shortRange"] = detector.GetEffectiveDetectionRadius(target);
            detector.DetectionRadius = 40f;
            result["baseChangeStillLimited"] = detector.GetEffectiveDetectionRadius(target);
            detector.DetectionRadius = 20f;

            fog.Update(.5f, config);
            result["lightFogRadius"] = detector.GetEffectiveDetectionRadius(target);
            fog.Update(0f, config);
            result["afterFog"] = detector.GetEffectiveDetectionRadius(target);
            fog.Update(1f, config);
            creature.SetPerceptionRadiusLimit(otherSource, 2f);
            fog.Update(0f, config);
            result["otherSourcePreserved"] = detector.GetEffectiveDetectionRadius(target);
            creature.RemovePerceptionRadiusLimit(otherSource);

            fog.Update(1f, config);
            fog.Remove(creature);
            clear.Add(creature);
            result["afterRegionMove"] = detector.GetEffectiveDetectionRadius(target);
            fog.Update(.25f, config);
            result["oldRegionIsolated"] = detector.GetEffectiveDetectionRadius(target);
            clear.Remove(creature);
            fog.Add(creature);
            result["newCreatureInFog"] = detector.GetEffectiveDetectionRadius(target);
            creature.PrepareForPoolReuse();
            result["poolCleared"] = float.IsPositiveInfinity(creature.MaximumPerceptionRadius);
            result["zeroFogUnlimited"] = float.IsPositiveInfinity(WeatherMgr.ResolveFogPerceptionRadius(0f, config));
            result["invalidFogUnlimited"] = float.IsPositiveInfinity(WeatherMgr.ResolveFogPerceptionRadius(float.NaN, config));
            return result;
        }));
    }

    #endregion
}
