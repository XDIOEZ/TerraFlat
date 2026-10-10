using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using FlatWorld.AIECS;
using Unity.Entities;

namespace FlatWorld.NaturalEntities
{
    /// <summary>从现有 Item 模块定义编译出的 ECS 能力模板；具体物种只组合模块，不写专用运行时代码。</summary>
    internal sealed partial class NaturalEntityEcsProfile
    {
        public RuntimeItemDefinition Definition;
        public NaturalEntityCapability Capabilities;
        public string HealthModuleName;
        public string GrowthModuleName;
        public string ClimateModuleName;
        public Mod_DamageReceiver.DamageReceiver_SaveData HealthDefaults;
        public GrowData GrowthDefaults;
        public float[] GrowthHealthRatios;
        public float MatureMaxHealth;
        public float RainGrowthBonus;
        public PlantClimateConfig Climate;
        public ResourceHarvestConfig Harvest;
        public ContactDamageSettings ContactDamage;
        public ComponentType[] ComponentTypes;
        public ulong RegistryRevision;
        public readonly List<CompiledResourceCapability> Extensions = new();
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
    /// 已声明 Entity 的资源遇到未知模块必须明确报错，不能退回 GameObject。
    /// </summary>
    internal static partial class NaturalEntityEcsProfileCompiler
    {
        #region 编译入口

        private delegate bool BuiltinCompiler(RuntimeItemModuleDefinition module, NaturalEntityEcsProfile profile, out string reason);
        private static readonly Dictionary<string, BuiltinCompiler> builtins = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Module_DamageReciver"] = CompileHealth,
            ["Module_Growth"] = CompileGrowth,
            ["Module_PlantClimate"] = CompileClimate,
            ["Module_ResourceHarvest"] = CompileHarvest,
            ["Module_ContactDamage"] = CompileContactDamage,
            ["Module_Crop"] = CompileCrop,
            ["Module_CropYield"] = CompileCropYield,
            ["Module_CropVisual"] = CompileCropVisual,
            ["Module_Collectable"] = CompileCollectable,
            ["Module_Production"] = CompileProduction,
            ["Module_CanopyFruit"] = CompileCanopyFruit,
            ["Module_TemperatureYield"] = CompileTemperatureYield
        };

        internal static bool HasBuiltin(string prefabId) => builtins.ContainsKey(prefabId);

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

            if (!definition.UsesResourceEntities || definition.IsActor || definition.IsGroundCover || definition.CanBePickedUp ||
                definition.Sprite == null || definition.AnimatorController != null)
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
                if (builtins.TryGetValue(prefab, out BuiltinCompiler compile))
                {
                    if (!compile(module, result, out reason))
                    {
                        reason = $"模块 {module.StableName}/{prefab} 配置无效：{reason}";
                        return false;
                    }
                    continue;
                }
                if (ResourceEntityCapabilityRegistry.TryCompile(module, out CompiledResourceCapability extension,
                    out bool recognized, out reason))
                {
                    result.Extensions.Add(extension);
                    continue;
                }
                if (!recognized) reason = $"模块 {module.StableName}/{prefab} 尚未提供 ECS 能力实现";
                return false;
            }

            if (result.HasCrop && result.HealthDefaults == null)
                result.HealthDefaults = new Mod_DamageReceiver.DamageReceiver_SaveData { Hp = 1f, MaxHp = 1f };
            if ((result.Capabilities & NaturalEntityCapability.Growth) != 0 && result.HealthDefaults == null)
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

            if (!ValidatePlantComposition(result, out reason)) return false;
            if (!BuildComponentTypes(result, out reason)) return false;
            result.RegistryRevision = ResourceEntityCapabilityRegistry.Revision;
            profile = result;
            return true;
        }

        #endregion

        #region 模块编译

        private static bool CompileContactDamage(RuntimeItemModuleDefinition module, NaturalEntityEcsProfile profile, out string reason)
        {
            reason = null;
            if (profile.ContactDamage != null) { reason = "接触伤害能力重复。"; return false; }
            JObject parameters = ParseParameters(module, out reason);
            if (parameters == null) return false;
            profile.ContactDamage = DeserializeConfiguration<ContactDamageSettings>(parameters["Settings"]) ?? new ContactDamageSettings();
            return profile.ContactDamage.TryValidate(out reason);
        }

        private static bool BuildComponentTypes(NaturalEntityEcsProfile profile, out string reason)
        {
            reason = null;
            var types = new List<ComponentType>
            {
                ComponentType.ReadWrite<NaturalEntityLocation>(), ComponentType.ReadWrite<EntityModuleAppearance>(),
                ComponentType.ReadWrite<EntityModuleActive>()
            };
            if (profile.HealthDefaults != null) types.Add(ComponentType.ReadWrite<AiecsVital>());
            if (profile.HasGrowth)
            {
                types.Add(ComponentType.ReadWrite<EntityGrowth>());
                types.Add(ComponentType.ReadWrite<EntityWeatherInput>());
                types.Add(ComponentType.ReadWrite<EntityPlantLifecycle>());
                types.Add(ComponentType.ReadWrite<EntityPlantSoil>());
            }
            if (profile.HasClimate) types.Add(ComponentType.ReadWrite<EntityClimate>());
            if ((profile.Capabilities & NaturalEntityCapability.Harvest) != 0) types.Add(ComponentType.ReadWrite<EntityHarvestRequirement>());
            if (profile.Collection != null)
            {
                types.Add(ComponentType.ReadWrite<EntityResourceStock>());
                types.Add(ComponentType.ReadWrite<EntityStockProduction>());
            }
            if (profile.Canopy != null) types.Add(ComponentType.ReadWrite<EntityCanopyFruitModule>());
            if (profile.Extensions.Count > 0) types.Add(ComponentType.ReadWrite<ResourceEntityExtensionState>());
            var reserved = new HashSet<Type>
            {
                typeof(NaturalEntityLocation), typeof(EntityModuleAppearance), typeof(EntityModuleActive), typeof(AiecsVital),
                typeof(EntityGrowth), typeof(EntityWeatherInput), typeof(EntityClimate), typeof(EntityHarvestRequirement), typeof(EntityPlantLifecycle),
                typeof(EntityPlantSoil), typeof(EntityResourceStock), typeof(EntityStockProduction), typeof(EntityCanopyFruitModule),
                typeof(ResourceEntityExtensionState)
            };
            var extensionTypes = new HashSet<Type>();
            foreach (CompiledResourceCapability extension in profile.Extensions)
                foreach (ComponentType type in extension.Capability.Types)
                {
                    Type managed = type.GetManagedType();
                    if (type.AccessModeType == ComponentType.AccessMode.Exclude || managed == null ||
                        (!typeof(IComponentData).IsAssignableFrom(managed) && !typeof(IBufferElementData).IsAssignableFrom(managed)) ||
                        reserved.Contains(managed) || !extensionTypes.Add(managed))
                    {
                        reason = $"扩展 {extension.Module.StableName} 的组件 {managed?.Name} 无效、重复或覆盖本体权威。";
                        return false;
                    }
                    types.Add(ComponentType.ReadWrite(managed));
                }
            profile.ComponentTypes = types.ToArray();
            return true;
        }

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
            var data = DeserializeConfiguration<Mod_DamageReceiver.DamageReceiver_SaveData>(parameters["Data"]) ??
                       new Mod_DamageReceiver.DamageReceiver_SaveData();
            if (!FinitePositive(data.MaxHp) || !Finite(data.Hp) || data.UseBodyPartHealth ||
                !Finite(data.DamageInterval) || data.DamageInterval < 0f)
            {
                reason = $"生命模块 {module.StableName} 的 Hp/MaxHp 无效";
                return false;
            }

            data.MaxHp = Math.Max(1f, data.MaxHp);
            data.Hp = Math.Clamp(data.Hp, 0f, data.MaxHp);
            profile.HealthDefaults = data;
            profile.HealthModuleName = module.StableName;
            profile.HealthParameters = parameters;
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
            GrowData data = DeserializeConfiguration<GrowData>(parameters["Data"]) ?? new GrowData();
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
                if (!Finite(threshold) || !FinitePositive(scale) || !FinitePositive(ratio))
                {
                    reason = $"成长模块 {module.StableName} 的第 {i + 1} 阶段阈值/缩放/生命倍率无效：{threshold}/{scale}/{ratio}";
                    return false;
                }
                if (i > 0 && threshold < data.growState_Value[i - 1])
                {
                    reason = $"成长模块 {module.StableName} 的第 {i + 1} 阶段阈值 {threshold} 小于上一阶段 {data.growState_Value[i - 1]}";
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
            ReadTreeExtras(parameters, profile);
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
            float minGrowth = p.Value<float?>("minimumGrowthTemperature") ?? 5f;
            float maxGrowth = p.Value<float?>("maximumGrowthTemperature") ?? 35f;
            float minSurvival = p.Value<float?>("minimumSurvivalTemperature") ?? 0f;
            float maxSurvival = p.Value<float?>("maximumSurvivalTemperature") ?? 40f;
            float fatalHours = p.Value<float?>("fatalExposureHours") ?? 6f;
            float recovery = p.Value<float?>("recoveryRate") ?? 0.5f;
            if (!Finite(minGrowth) || !Finite(maxGrowth) || !Finite(minSurvival) ||
                !Finite(maxSurvival) || !FinitePositive(fatalHours) || !Finite(recovery) ||
                recovery < 0f || minSurvival > minGrowth || minGrowth >= maxGrowth ||
                maxGrowth > maxSurvival)
            {
                reason = $"植物耐候模块 {module.StableName} 的温度区间无效";
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

        private static T DeserializeConfiguration<T>(JToken token) where T : class
        {
            // 显式集合替换构造默认值，缺省字段仍保留默认值，避免成长阶段和提示点被重复追加。
            return token?.ToObject<T>(new JsonSerializer
            {
                ObjectCreationHandling = ObjectCreationHandling.Replace
            });
        }

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
