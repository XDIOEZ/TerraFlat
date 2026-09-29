using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace FlatWorld.NaturalEntities
{
    /// <summary>从现有 Item 模块定义编译出的 ECS 能力模板；具体物种只组合模块，不写专用运行时代码。</summary>
    internal sealed class NaturalEntityEcsProfile
    {
        public RuntimeItemDefinition Definition;
        public NaturalEntityCapability Capabilities;
        public string HealthModuleName;
        public string GrowthModuleName;
        public string ClimateModuleName;
        public DamageReceiver.DamageReceiver_SaveData HealthDefaults;
        public GrowData GrowthDefaults;
        public float[] GrowthHealthRatios;
        public float MatureMaxHealth;
        public float RainGrowthBonus;
        public PlantClimateConfig Climate;
        public ResourceHarvestConfig Harvest;
        public bool HasGrowth => (Capabilities & NaturalEntityCapability.Growth) != 0;
        public bool HasClimate => (Capabilities & NaturalEntityCapability.Climate) != 0;
    }

    internal readonly struct PlantClimateConfig
    {
        public PlantClimateConfig(float minimumGrowth, float maximumGrowth,
            float minimumSurvival, float maximumSurvival, float fatalHours, float recoveryRate)
        {
            MinimumGrowth = minimumGrowth;
            MaximumGrowth = maximumGrowth;
            MinimumSurvival = minimumSurvival;
            MaximumSurvival = maximumSurvival;
            FatalHours = fatalHours;
            RecoveryRate = recoveryRate;
        }

        public float MinimumGrowth { get; }
        public float MaximumGrowth { get; }
        public float MinimumSurvival { get; }
        public float MaximumSurvival { get; }
        public float FatalHours { get; }
        public float RecoveryRate { get; }
    }

    internal readonly struct ResourceHarvestConfig
    {
        public ResourceHarvestConfig(ResourceToolKind tool, int minimumTier)
        {
            Tool = tool;
            MinimumTier = minimumTier;
        }

        public ResourceToolKind Tool { get; }
        public int MinimumTier { get; }
    }

    /// <summary>
    /// 自然物 ECS 的冷路径编译器。支持列表按“通用模块能力”扩展；
    /// 出现未知/复杂模块时整件自然物保留 Item 路径，禁止静默丢玩法。
    /// </summary>
    internal static class NaturalEntityEcsProfileCompiler
    {
        private static readonly HashSet<string> PassiveSupportedPrefabs =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "Module_TemperatureYield"
            };

        #region 编译入口

        public static bool TryCompile(RuntimeItemDefinition definition,
            out NaturalEntityEcsProfile profile, out string reason)
        {
            profile = null;
            reason = null;
            if (definition == null)
            {
                reason = "定义为空";
                return false;
            }

            if (definition.IsActor || definition.IsGroundCover || definition.CanBePickedUp ||
                definition.Sprite == null || definition.AnimatorController != null ||
                definition.ShellPrefab == null)
            {
                reason = "不是可由静态自然物 ECS 托管的实体";
                return false;
            }

            var result = new NaturalEntityEcsProfile { Definition = definition };
            IReadOnlyList<RuntimeItemModuleDefinition> modules = definition.ModuleDefinitions;
            for (int i = 0; i < modules.Count; i++)
            {
                RuntimeItemModuleDefinition module = modules[i];
                if (module == null || !module.Enabled)
                    continue;

                string prefab = module.PrefabId?.Trim() ?? string.Empty;
                if (string.Equals(prefab, "Module_DamageReciver", StringComparison.OrdinalIgnoreCase))
                {
                    if (!CompileHealth(module, result, out reason))
                        return false;
                    continue;
                }

                if (string.Equals(prefab, "Module_Growth", StringComparison.OrdinalIgnoreCase))
                {
                    if (!CompileGrowth(module, result, out reason))
                        return false;
                    continue;
                }

                if (string.Equals(prefab, "Module_PlantClimate", StringComparison.OrdinalIgnoreCase))
                {
                    if (!CompileClimate(module, result, out reason))
                        return false;
                    continue;
                }

                if (string.Equals(prefab, "Module_ResourceHarvest", StringComparison.OrdinalIgnoreCase))
                {
                    if (!CompileHarvest(module, result, out reason))
                        return false;
                    continue;
                }

                if (PassiveSupportedPrefabs.Contains(prefab))
                    continue;

                reason = $"模块 {module.StableName}/{prefab} 尚未提供 ECS 能力实现";
                return false;
            }

            if ((result.Capabilities & NaturalEntityCapability.Growth) != 0 &&
                (result.Capabilities & NaturalEntityCapability.Health) == 0)
            {
                reason = "成长能力依赖生命能力";
                return false;
            }

            if ((result.Capabilities & NaturalEntityCapability.Climate) != 0 &&
                (result.Capabilities & NaturalEntityCapability.Growth) == 0)
            {
                reason = "当前植物耐候 ECS 能力依赖成长能力";
                return false;
            }

            profile = result;
            return true;
        }

        #endregion

        #region 模块编译

        private static bool CompileHealth(RuntimeItemModuleDefinition module,
            NaturalEntityEcsProfile profile, out string reason)
        {
            reason = null;
            if ((profile.Capabilities & NaturalEntityCapability.Health) != 0)
            {
                reason = "存在多个生命能力";
                return false;
            }

            JObject parameters = ParseParameters(module, out reason);
            if (parameters == null)
                return false;
            var data = parameters["Data"]?.ToObject<DamageReceiver.DamageReceiver_SaveData>() ??
                       new DamageReceiver.DamageReceiver_SaveData();
            if (!FinitePositive(data.MaxHp) || !Finite(data.Hp) || data.UseBodyPartHealth)
            {
                reason = $"生命模块 {module.StableName} 的 Hp/MaxHp 无效";
                return false;
            }

            data.MaxHp = Math.Max(1f, data.MaxHp);
            data.Hp = Math.Clamp(data.Hp, 0f, data.MaxHp);
            profile.HealthDefaults = data;
            profile.HealthModuleName = module.StableName;
            profile.Capabilities |= NaturalEntityCapability.Health;
            return true;
        }

        private static bool CompileGrowth(RuntimeItemModuleDefinition module,
            NaturalEntityEcsProfile profile, out string reason)
        {
            reason = null;
            if ((profile.Capabilities & NaturalEntityCapability.Growth) != 0)
            {
                reason = "存在多个成长能力";
                return false;
            }

            JObject parameters = ParseParameters(module, out reason);
            if (parameters == null)
                return false;
            GrowData data = parameters["Data"]?.ToObject<GrowData>() ?? new GrowData();
            JArray healthRatios = parameters["growState_MaxHealthRatios"] as JArray;
            float matureMaxHealth = parameters.Value<float?>("matureMaxHealth") ?? 200f;
            if (data.growState_Value == null || data.growState_Scale == null ||
                data.growState_Value.Count == 0 ||
                data.growState_Value.Count != data.growState_Scale.Count ||
                data.growState_Value.Count > 12 ||
                !FinitePositive(data.MaxGrowProgress) || !Finite(data.GrowProgress) ||
                !Finite(data.GrowSpeed) || data.GrowSpeed < 0f ||
                !FinitePositive(matureMaxHealth))
            {
                reason = $"成长模块 {module.StableName} 的阶段或速度配置无效";
                return false;
            }

            var ratios = new float[data.growState_Value.Count];
            for (int i = 0; i < ratios.Length; i++)
            {
                float threshold = data.growState_Value[i];
                float scale = data.growState_Scale[i];
                float ratio = healthRatios != null && i < healthRatios.Count
                    ? healthRatios[i].Value<float>()
                    : (i + 1f) / ratios.Length;
                if (!Finite(threshold) || !FinitePositive(scale) || !FinitePositive(ratio) ||
                    (i > 0 && threshold < data.growState_Value[i - 1]))
                {
                    reason = $"成长模块 {module.StableName} 的阶段阈值/缩放/生命倍率无效";
                    return false;
                }
                ratios[i] = ratio;
            }

            data.GrowProgress = Math.Clamp(data.GrowProgress, 0f, data.MaxGrowProgress);
            profile.GrowthDefaults = data;
            profile.GrowthHealthRatios = ratios;
            profile.MatureMaxHealth = matureMaxHealth;
            profile.RainGrowthBonus = parameters.Value<float?>("rainGrowthBonus") ?? 0.15f;
            profile.GrowthModuleName = module.StableName;
            profile.Capabilities |= NaturalEntityCapability.Growth;
            return true;
        }

        private static bool CompileClimate(RuntimeItemModuleDefinition module,
            NaturalEntityEcsProfile profile, out string reason)
        {
            reason = null;
            if ((profile.Capabilities & NaturalEntityCapability.Climate) != 0)
            {
                reason = "存在多个植物耐候能力";
                return false;
            }

            JObject p = ParseParameters(module, out reason);
            if (p == null)
                return false;
            bool autonomous = p.Value<bool?>("autonomous") ?? false;
            float minGrowth = p.Value<float?>("minimumGrowthTemperature") ?? 5f;
            float maxGrowth = p.Value<float?>("maximumGrowthTemperature") ?? 35f;
            float minSurvival = p.Value<float?>("minimumSurvivalTemperature") ?? 0f;
            float maxSurvival = p.Value<float?>("maximumSurvivalTemperature") ?? 40f;
            float fatalHours = p.Value<float?>("fatalExposureHours") ?? 6f;
            float recovery = p.Value<float?>("recoveryRate") ?? 0.5f;
            if (!autonomous || !Finite(minGrowth) || !Finite(maxGrowth) || !Finite(minSurvival) ||
                !Finite(maxSurvival) || !FinitePositive(fatalHours) || !Finite(recovery) ||
                recovery < 0f || minSurvival > minGrowth || minGrowth >= maxGrowth ||
                maxGrowth > maxSurvival)
            {
                reason = autonomous
                    ? $"植物耐候模块 {module.StableName} 的温度区间无效"
                    : $"植物耐候模块 {module.StableName} 不是自主自然物模式";
                return false;
            }

            profile.Climate = new PlantClimateConfig(
                minGrowth, maxGrowth, minSurvival, maxSurvival, fatalHours, recovery);
            profile.ClimateModuleName = module.StableName;
            profile.Capabilities |= NaturalEntityCapability.Climate;
            return true;
        }

        private static bool CompileHarvest(RuntimeItemModuleDefinition module,
            NaturalEntityEcsProfile profile, out string reason)
        {
            reason = null;
            if ((profile.Capabilities & NaturalEntityCapability.Harvest) != 0)
            {
                reason = "存在多个资源采集能力";
                return false;
            }

            JObject p = ParseParameters(module, out reason);
            if (p == null)
                return false;
            ResourceToolKind tool = (ResourceToolKind)(p.Value<int?>("requiredTool") ?? 0);
            int minimumTier = p.Value<int?>("minimumTier") ?? 0;
            if (!Enum.IsDefined(typeof(ResourceToolKind), tool) ||
                tool == ResourceToolKind.None || minimumTier < 1)
            {
                reason = $"资源采集模块 {module.StableName} 的工具门槛无效";
                return false;
            }

            profile.Harvest = new ResourceHarvestConfig(tool, minimumTier);
            profile.Capabilities |= NaturalEntityCapability.Harvest;
            return true;
        }

        #endregion

        #region 工具

        private static JObject ParseParameters(RuntimeItemModuleDefinition module, out string reason)
        {
            reason = null;
            if (string.IsNullOrWhiteSpace(module.ParametersJson))
                return new JObject();
            try
            {
                return JObject.Parse(module.ParametersJson);
            }
            catch (Exception exception)
            {
                reason = $"模块 {module.StableName} 参数 JSON 无效：{exception.Message}";
                return null;
            }
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool FinitePositive(float value) => Finite(value) && value > 0f;

        #endregion
    }
}
