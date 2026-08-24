using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

#region 玩家创建配置模型

/// <summary>玩家创建 JSON 配置目录；只在无玩家存档的创建阶段应用。</summary>
[Serializable]
public sealed class PlayerCreationTemplateCatalogConfig
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion = 1;

    [JsonProperty("defaultProfileId")]
    public string DefaultProfileId = "default";

    [JsonProperty("profiles")]
    public List<PlayerCreationTemplateConfig> Profiles = new();
}

/// <summary>单个玩家创建模板；字段保持普通 JSON DTO，不依赖 Unity 资产。</summary>
[Serializable]
public sealed class PlayerCreationTemplateConfig
{
    [JsonProperty("id", Required = Required.Always)]
    public string Id;

    [JsonProperty("parent")]
    public string Parent;

    [JsonProperty("core")]
    public CoreSettings Core = new();

    [JsonProperty("movement")]
    public MovementSettings Movement = new();

    [JsonProperty("food")]
    public FoodSettings Food = new();

    [JsonProperty("stamina")]
    public StaminaSettings Stamina = new();

    [Serializable]
    public sealed class CoreSettings
    {
        [JsonProperty("dataSpeed")] public float DataSpeed = 8f;
        [JsonProperty("playerPov")] public float PlayerPov = 10f;
        [JsonProperty("perceptionRadiusMultiplier")] public float PerceptionRadiusMultiplier = 1f;
        [JsonProperty("initialStamina")] public float InitialStamina = 100f;
        [JsonProperty("maxStamina")] public float MaxStamina = 100f;
        [JsonProperty("staminaRecoverySpeed")] public float StaminaRecoverySpeed = 10f;
    }

    [Serializable]
    public sealed class MovementSettings
    {
        [JsonProperty("speed")] public float Speed = 5f;
        [JsonProperty("slowDownSpeed")] public float SlowDownSpeed = 5f;
        [JsonProperty("endSpeed")] public float EndSpeed = 0.1f;
        [JsonProperty("moveStaminaConsume")] public float MoveStaminaConsume;
        [JsonProperty("runStaminaConsume")] public float RunStaminaConsume = 2f;
        [JsonProperty("runSpeedRate")] public float RunSpeedRate = 1.5f;
        [JsonProperty("runStaminaThreshold")] public float RunStaminaThreshold = 2f;
        [JsonProperty("speedTransitionDuration")] public float SpeedTransitionDuration = 0.24f;
        [JsonProperty("stopTransitionDuration")] public float StopTransitionDuration = 0.07f;
        [JsonProperty("hungerActionEnabled")] public bool HungerActionEnabled = true;
        [JsonProperty("moveNutritionConsumeMultiplier")] public float MoveNutritionConsumeMultiplier = 1.6f;
        [JsonProperty("runNutritionConsumeMultiplier")] public float RunNutritionConsumeMultiplier = 2f;
        [JsonProperty("runWaterConsumeMultiplier")] public float RunWaterConsumeMultiplier = 0.25f;
    }

    [Serializable]
    public sealed class FoodSettings
    {
        [JsonProperty("nutrition")] public NutritionSettings Nutrition = new();
        [JsonProperty("nutritionConsumeSpeed")] public float NutritionConsumeSpeed = 0.05f;
        [JsonProperty("waterConsumeSpeedRate")] public float WaterConsumeSpeedRate = 0.1f;
        [JsonProperty("nutritionConsumeRate")] public float NutritionConsumeRate = 1f;
        [JsonProperty("staminaRecoverSpeed")] public float StaminaRecoverSpeed = 1f;
        [JsonProperty("staminaConsumeSpeed")] public float StaminaConsumeSpeed = 0.5f;
        [JsonProperty("healthEnabled")] public bool HealthEnabled = true;
        [JsonProperty("healSpeed")] public float HealSpeed = 0.01f;
        [JsonProperty("waterSelfHurt")] public float WaterSelfHurt = 1f;
        [JsonProperty("proteinSelfHurt")] public float ProteinSelfHurt = 1f;
        [JsonProperty("vitaminSelfHurt")] public float VitaminSelfHurt = 1f;
        [JsonProperty("healNeedRatio")] public float HealNeedRatio = 0.6f;
        [JsonProperty("proteinHealThreshold")] public float ProteinHealThreshold = 60f;
    }

    [Serializable]
    public sealed class NutritionSettings
    {
        [JsonProperty("carbohydrates")] public float Carbohydrates = 50f;
        [JsonProperty("maxCarbohydrates")] public float MaxCarbohydrates = 100f;
        [JsonProperty("fat")] public float Fat = 50f;
        [JsonProperty("maxFat")] public float MaxFat = 100f;
        [JsonProperty("protein")] public float Protein = 50f;
        [JsonProperty("maxProtein")] public float MaxProtein = 100f;
        [JsonProperty("water")] public float Water = 150f;
        [JsonProperty("maxWater")] public float MaxWater = 150f;
        [JsonProperty("vitamins")] public float Vitamins = 50f;
        [JsonProperty("maxVitamins")] public float MaxVitamins = 100f;
    }

    [Serializable]
    public sealed class StaminaSettings
    {
        [JsonProperty("currentStamina")] public float CurrentStamina = 100f;
        [JsonProperty("maxStamina")] public float MaxStamina = 100f;
    }

    #region 应用配置

    /// <summary>将模板应用到尚未执行 Load 的玩家实例。</summary>
    public void ApplyTo(Player player)
    {
        if (player == null)
            throw new ArgumentNullException(nameof(player));
        if (player.Data == null)
            throw new InvalidOperationException("玩家创建 JSON 配置无法应用：Player.Data 为空。");
        if (Core == null || Movement == null || Food == null || Food.Nutrition == null || Stamina == null)
            throw new InvalidOperationException($"玩家创建 JSON 配置不完整：{Id}");

        ApplyCore(player.Data);
        ApplyMovement(player.GetComponentInChildren<Mover>(true));
        ApplyFood(player.GetComponentInChildren<Mod_Food>(true));
        ApplyStamina(player.GetComponentInChildren<Mod_Stamina>(true));

        ItemData templateData = player.Get_NewItemData();
        player.Data.ModuleDataDic = templateData.ModuleDataDic;
    }

    private void ApplyCore(Data_Player data)
    {
        float maxStamina = Mathf.Max(0f, Core.MaxStamina);
        data.Speed = new GameValue_float(Mathf.Max(0f, Core.DataSpeed));
        data.PlayerPov = Mathf.Max(0f, Core.PlayerPov);
        data.PerceptionRadiusMultiplier = Mathf.Max(0f, Core.PerceptionRadiusMultiplier);
        data.stamina = Mathf.Clamp(Core.InitialStamina, 0f, maxStamina);
        data.staminaMax = maxStamina;
        data.staminaRecoverySpeed = Mathf.Max(0f, Core.StaminaRecoverySpeed);
    }

    private void ApplyMovement(Mover mover)
    {
        if (mover == null)
            return;

        mover.Data = new Mover.Mover_SaveData
        {
            Speed = new GameValue_float(Mathf.Max(0f, Movement.Speed)),
            slowDownSpeed = Mathf.Max(0f, Movement.SlowDownSpeed),
            endSpeed = Mathf.Max(0f, Movement.EndSpeed),
            moveStaminaConsume = Mathf.Max(0f, Movement.MoveStaminaConsume),
            runStaminaConsume = Mathf.Max(0f, Movement.RunStaminaConsume),
            runSpeedRate = Mathf.Max(0.01f, Movement.RunSpeedRate),
            isRunning = false,
            RunStaminaThreshold = Mathf.Max(0f, Movement.RunStaminaThreshold)
        };
        mover.speedTransitionDuration = Mathf.Max(0.01f, Movement.SpeedTransitionDuration);
        mover.stopTransitionDuration = Mathf.Max(0.01f, Movement.StopTransitionDuration);
        mover.hungerAction = new MovementHungerActionDefinition
        {
            enabled = Movement.HungerActionEnabled,
            moveNutritionConsumeMultiplier = Mathf.Max(0f, Movement.MoveNutritionConsumeMultiplier),
            runNutritionConsumeMultiplier = Mathf.Max(0f, Movement.RunNutritionConsumeMultiplier),
            runWaterConsumeMultiplier = Mathf.Max(0f, Movement.RunWaterConsumeMultiplier)
        };
        mover.ModDataMemoryPack ??= new Ex_ModData_MemoryPackable();
        mover.ModDataMemoryPack.WriteData(mover.Data);
    }

    private void ApplyFood(Mod_Food foodModule)
    {
        if (foodModule == null)
            return;

        Food foodData = foodModule.Data;
        foodData.nutrition = new Nutrition
        {
            Carbohydrates = Mathf.Max(0f, Food.Nutrition.Carbohydrates),
            Max_Carbohydrates = Mathf.Max(0f, Food.Nutrition.MaxCarbohydrates),
            Fat = Mathf.Max(0f, Food.Nutrition.Fat),
            Max_Fat = Mathf.Max(0f, Food.Nutrition.MaxFat),
            Protein = Mathf.Max(0f, Food.Nutrition.Protein),
            Max_Protein = Mathf.Max(0f, Food.Nutrition.MaxProtein),
            Water = Mathf.Max(0f, Food.Nutrition.Water),
            Max_Water = Mathf.Max(0f, Food.Nutrition.MaxWater),
            Vitamins = Mathf.Max(0f, Food.Nutrition.Vitamins),
            Max_Vitamins = Mathf.Max(0f, Food.Nutrition.MaxVitamins)
        };
        foodData.nutritionConsumeSpeed = new GameValue_float(Mathf.Max(0f, Food.NutritionConsumeSpeed));
        foodData.WaterConsumeSpeedRate = Mathf.Max(0f, Food.WaterConsumeSpeedRate);
        foodData.nutritionConsumeRate = Mathf.Max(0f, Food.NutritionConsumeRate);
        foodModule.Data = foodData;
        foodModule.StaminaState = new Mod_Food.FoodStaminaState
        {
            StaminaRecoverSpeed = Mathf.Max(0f, Food.StaminaRecoverSpeed),
            StaminaConsumeSpeed = Mathf.Max(0f, Food.StaminaConsumeSpeed)
        };
        foodModule.HealthState = new Mod_Food.FoodHealthState
        {
            Enabled = Food.HealthEnabled,
            HealSpeed = Mathf.Max(0f, Food.HealSpeed),
            WaterSelfHurt = Mathf.Max(0f, Food.WaterSelfHurt),
            ProteinSelfHurt = Mathf.Max(0f, Food.ProteinSelfHurt),
            VitaminSelfHurt = Mathf.Max(0f, Food.VitaminSelfHurt),
            HealNeedRatio = Mathf.Clamp01(Food.HealNeedRatio),
            PlayerProteinHealThreshold = Mathf.Max(0f, Food.ProteinHealThreshold)
        };
    }

    private void ApplyStamina(Mod_Stamina staminaModule)
    {
        if (staminaModule == null)
            return;

        float maxStamina = Mathf.Max(0f, Stamina.MaxStamina);
        staminaModule.Data = new Mod_Stamina.StaminaData
        {
            CurrentStamina = Mathf.Clamp(Stamina.CurrentStamina, 0f, maxStamina),
            MaxStamina = maxStamina
        };
        staminaModule.modData ??= new Ex_ModData_MemoryPackable();
        staminaModule.modData.WriteData(staminaModule.Data);
    }

    #endregion
}

#endregion

#region 玩家创建 JSON 加载

/// <summary>读取内建玩家创建 JSON；路径兼容 Windows、Android 和 WebGL。</summary>
public static class PlayerCreationTemplateJsonLoader
{
    public const int SupportedSchemaVersion = 1;
    public const string RelativePlayerRoot = "GameConfig/Players";
    public const string ManifestFileName = "player-creation-manifest.json";
    public const string RelativeManifestPath = RelativePlayerRoot + "/" + ManifestFileName;
    public const long MaximumConfigBytes = 1024 * 1024;

    private static readonly JsonSerializerSettings StrictJsonSettings = new()
    {
        MissingMemberHandling = MissingMemberHandling.Error,
        DateParseHandling = DateParseHandling.None
    };

    public static string BuiltInManifestPath =>
        StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RelativeManifestPath);

    /// <summary>同步读取内建配置，供编辑器和静态校验使用。</summary>
    public static PlayerCreationTemplateCatalogConfig LoadBuiltIn()
    {
        return Deserialize(StreamingAssetsTextLoader.ReadAllText(BuiltInManifestPath));
    }

    /// <summary>异步读取内建配置，兼容 Android/WebGL 的 StreamingAssets。</summary>
    public static IEnumerator LoadBuiltInAsync(
        Action<PlayerCreationTemplateCatalogConfig> onCompleted,
        Action<Exception> onFailed)
    {
        string json = null;
        Exception readError = null;
        yield return StreamingAssetsTextLoader.ReadAllTextAsync(
            BuiltInManifestPath,
            text => json = text,
            exception => readError = exception);

        if (readError != null)
        {
            onFailed?.Invoke(readError);
            yield break;
        }

        try
        {
            onCompleted?.Invoke(Deserialize(json));
        }
        catch (Exception exception)
        {
            onFailed?.Invoke(exception);
        }
    }

    /// <summary>反序列化并校验玩家创建目录。</summary>
    public static PlayerCreationTemplateCatalogConfig Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("玩家创建 JSON 为空");

        PlayerCreationTemplateCatalogConfig catalog = JsonConvert.DeserializeObject<PlayerCreationTemplateCatalogConfig>(
            json,
            StrictJsonSettings);
        Validate(catalog);
        return catalog;
    }

    /// <summary>验证 schema、默认模板和数值边界。</summary>
    public static void Validate(PlayerCreationTemplateCatalogConfig catalog)
    {
        if (catalog == null)
            throw new InvalidDataException("玩家创建 JSON 根对象为空");
        if (catalog.SchemaVersion != SupportedSchemaVersion)
            throw new InvalidDataException($"不支持的玩家创建 JSON schemaVersion：{catalog.SchemaVersion}");
        if (catalog.Profiles == null || catalog.Profiles.Count == 0)
            throw new InvalidDataException("玩家创建 JSON 至少需要一个 profile");

        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        foreach (PlayerCreationTemplateConfig profile in catalog.Profiles)
        {
            ValidateProfile(profile, ids);
        }

        catalog.DefaultProfileId = catalog.DefaultProfileId?.Trim();
        if (string.IsNullOrWhiteSpace(catalog.DefaultProfileId) || !ids.Contains(catalog.DefaultProfileId))
            throw new InvalidDataException($"玩家创建 JSON defaultProfileId 不存在：{catalog.DefaultProfileId}");
    }

    private static void ValidateProfile(PlayerCreationTemplateConfig profile, ISet<string> ids)
    {
        if (profile == null)
            throw new InvalidDataException("玩家创建 JSON 包含空 profile");

        profile.Id = profile.Id?.Trim();
        if (string.IsNullOrWhiteSpace(profile.Id))
            throw new InvalidDataException("玩家创建 profile 缺少 id");
        if (!ids.Add(profile.Id))
            throw new InvalidDataException($"玩家创建 JSON 包含重复 profile ID：{profile.Id}");
        if (profile.Core == null || profile.Movement == null || profile.Food == null ||
            profile.Food.Nutrition == null || profile.Stamina == null)
            throw new InvalidDataException($"玩家创建 profile 配置不完整：{profile.Id}");

        ValidateFiniteNonNegative(profile.Core.DataSpeed, $"{profile.Id}.core.dataSpeed");
        ValidateFiniteNonNegative(profile.Core.PlayerPov, $"{profile.Id}.core.playerPov");
        ValidateFiniteNonNegative(profile.Core.PerceptionRadiusMultiplier, $"{profile.Id}.core.perceptionRadiusMultiplier");
        ValidateFiniteNonNegative(profile.Core.InitialStamina, $"{profile.Id}.core.initialStamina");
        ValidateFiniteNonNegative(profile.Core.MaxStamina, $"{profile.Id}.core.maxStamina");
        ValidateFiniteNonNegative(profile.Core.StaminaRecoverySpeed, $"{profile.Id}.core.staminaRecoverySpeed");

        ValidateFiniteNonNegative(profile.Movement.Speed, $"{profile.Id}.movement.speed");
        ValidateFiniteNonNegative(profile.Movement.SlowDownSpeed, $"{profile.Id}.movement.slowDownSpeed");
        ValidateFiniteNonNegative(profile.Movement.EndSpeed, $"{profile.Id}.movement.endSpeed");
        ValidateFiniteNonNegative(profile.Movement.MoveStaminaConsume, $"{profile.Id}.movement.moveStaminaConsume");
        ValidateFiniteNonNegative(profile.Movement.RunStaminaConsume, $"{profile.Id}.movement.runStaminaConsume");
        ValidatePositive(profile.Movement.RunSpeedRate, $"{profile.Id}.movement.runSpeedRate");
        ValidateFiniteNonNegative(profile.Movement.RunStaminaThreshold, $"{profile.Id}.movement.runStaminaThreshold");
        ValidatePositive(profile.Movement.SpeedTransitionDuration, $"{profile.Id}.movement.speedTransitionDuration");
        ValidatePositive(profile.Movement.StopTransitionDuration, $"{profile.Id}.movement.stopTransitionDuration");
        ValidateFiniteNonNegative(profile.Movement.MoveNutritionConsumeMultiplier, $"{profile.Id}.movement.moveNutritionConsumeMultiplier");
        ValidateFiniteNonNegative(profile.Movement.RunNutritionConsumeMultiplier, $"{profile.Id}.movement.runNutritionConsumeMultiplier");
        ValidateFiniteNonNegative(profile.Movement.RunWaterConsumeMultiplier, $"{profile.Id}.movement.runWaterConsumeMultiplier");

        ValidateFiniteNonNegative(profile.Food.NutritionConsumeSpeed, $"{profile.Id}.food.nutritionConsumeSpeed");
        ValidateFiniteNonNegative(profile.Food.WaterConsumeSpeedRate, $"{profile.Id}.food.waterConsumeSpeedRate");
        ValidateFiniteNonNegative(profile.Food.NutritionConsumeRate, $"{profile.Id}.food.nutritionConsumeRate");
        ValidateFiniteNonNegative(profile.Food.StaminaRecoverSpeed, $"{profile.Id}.food.staminaRecoverSpeed");
        ValidateFiniteNonNegative(profile.Food.StaminaConsumeSpeed, $"{profile.Id}.food.staminaConsumeSpeed");
        ValidateFiniteNonNegative(profile.Food.HealSpeed, $"{profile.Id}.food.healSpeed");
        ValidateFiniteNonNegative(profile.Food.WaterSelfHurt, $"{profile.Id}.food.waterSelfHurt");
        ValidateFiniteNonNegative(profile.Food.ProteinSelfHurt, $"{profile.Id}.food.proteinSelfHurt");
        ValidateFiniteNonNegative(profile.Food.VitaminSelfHurt, $"{profile.Id}.food.vitaminSelfHurt");
        if (!IsFinite(profile.Food.HealNeedRatio) || profile.Food.HealNeedRatio < 0f || profile.Food.HealNeedRatio > 1f)
            throw new InvalidDataException($"{profile.Id}.food.healNeedRatio 必须在 0 到 1 之间");
        ValidateFiniteNonNegative(profile.Food.ProteinHealThreshold, $"{profile.Id}.food.proteinHealThreshold");

        ValidateNutrition(profile.Id, profile.Food.Nutrition);
        ValidateFiniteNonNegative(profile.Stamina.CurrentStamina, $"{profile.Id}.stamina.currentStamina");
        ValidateFiniteNonNegative(profile.Stamina.MaxStamina, $"{profile.Id}.stamina.maxStamina");
    }

    private static void ValidateNutrition(string profileId, PlayerCreationTemplateConfig.NutritionSettings nutrition)
    {
        ValidateFiniteNonNegative(nutrition.Carbohydrates, $"{profileId}.food.nutrition.carbohydrates");
        ValidateFiniteNonNegative(nutrition.MaxCarbohydrates, $"{profileId}.food.nutrition.maxCarbohydrates");
        ValidateFiniteNonNegative(nutrition.Fat, $"{profileId}.food.nutrition.fat");
        ValidateFiniteNonNegative(nutrition.MaxFat, $"{profileId}.food.nutrition.maxFat");
        ValidateFiniteNonNegative(nutrition.Protein, $"{profileId}.food.nutrition.protein");
        ValidateFiniteNonNegative(nutrition.MaxProtein, $"{profileId}.food.nutrition.maxProtein");
        ValidateFiniteNonNegative(nutrition.Water, $"{profileId}.food.nutrition.water");
        ValidateFiniteNonNegative(nutrition.MaxWater, $"{profileId}.food.nutrition.maxWater");
        ValidateFiniteNonNegative(nutrition.Vitamins, $"{profileId}.food.nutrition.vitamins");
        ValidateFiniteNonNegative(nutrition.MaxVitamins, $"{profileId}.food.nutrition.maxVitamins");
    }

    private static void ValidateFiniteNonNegative(float value, string name)
    {
        if (!IsFinite(value) || value < 0f)
            throw new InvalidDataException($"玩家创建配置 {name} 无效：{value}");
    }

    private static void ValidatePositive(float value, string name)
    {
        if (!IsFinite(value) || value <= 0f)
            throw new InvalidDataException($"玩家创建配置 {name} 必须大于 0：{value}");
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}

#endregion

#region 玩家创建模板注册表

/// <summary>合并内建 JSON 与 MOD 玩家模板，并在玩家创建时提供最终配置。</summary>
public static class PlayerCreationTemplateCatalogService
{
    public const string PatchTargetPrefix = "playerTemplate:";
    public const string CatalogPatchTarget = "playerTemplateCatalog";

    private static readonly Dictionary<string, JObject> Sources = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, JObject> ResolvedSources = new(StringComparer.OrdinalIgnoreCase);
    private static string defaultProfileId = "default";
    private static string builtInDefaultProfileId = "default";
    private static bool dirty = true;

    public static string DefaultProfileId => defaultProfileId;

    /// <summary>替换内建目录并清除旧 MOD 配置。</summary>
    public static void ReplaceBuiltIn(PlayerCreationTemplateCatalogConfig catalog)
    {
        PlayerCreationTemplateJsonLoader.Validate(catalog);
        Sources.Clear();
        ResolvedSources.Clear();
        defaultProfileId = catalog.DefaultProfileId;
        builtInDefaultProfileId = catalog.DefaultProfileId;
        foreach (PlayerCreationTemplateConfig profile in catalog.Profiles)
            Sources.Add(profile.Id, JObject.FromObject(profile));
        dirty = true;
    }

    /// <summary>清理 MOD 注册内容，保留本体 JSON 模板。</summary>
    public static void ClearExternal()
    {
        string[] builtInIds = Sources.Keys.Where(id => !id.Contains(":", StringComparison.Ordinal)).ToArray();
        Dictionary<string, JObject> builtIns = new(StringComparer.OrdinalIgnoreCase);
        foreach (string id in builtInIds)
            builtIns[id] = Sources[id];

        Sources.Clear();
        foreach (KeyValuePair<string, JObject> pair in builtIns)
            Sources.Add(pair.Key, pair.Value);
        ResolvedSources.Clear();
        defaultProfileId = builtInDefaultProfileId;
        dirty = true;
    }

    /// <summary>注册 MOD 定义文件中的玩家模板；裸 ID 自动归属当前 MOD 命名空间。</summary>
    public static void RegisterModTemplate(string modId, JObject document, string sourceFile, int sourceIndex)
    {
        if (document == null)
            throw new InvalidDataException($"MOD {modId} 玩家模板为空：{sourceFile}#{sourceIndex}");

        string rawId = document.Value<string>("id")?.Trim();
        if (string.IsNullOrWhiteSpace(rawId))
            throw new InvalidDataException($"MOD {modId} 玩家模板缺少 id：{sourceFile}#{sourceIndex}");

        string id = rawId.Contains(":", StringComparison.Ordinal) ? rawId : $"{modId}:{rawId}";
        if (!id.StartsWith(modId + ":", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"MOD {modId} 玩家模板 ID 必须使用 {modId}: 命名空间：{id}");
        JObject normalizedDocument = (JObject)document.DeepClone();
        string parent = normalizedDocument.Value<string>("parent")?.Trim();
        if (!string.IsNullOrWhiteSpace(parent) &&
            !parent.Contains(":", StringComparison.Ordinal) &&
            !Sources.ContainsKey(parent))
        {
            normalizedDocument["parent"] = $"{modId}:{parent}";
        }

        if (!Sources.TryAdd(id, normalizedDocument))
            throw new InvalidDataException($"重复玩家模板 ID：{id}");

        Sources[id]["id"] = id;
        ResolvedSources.Clear();
        dirty = true;
    }

    /// <summary>应用 target 为 playerTemplate:ID 的 MOD Patch。</summary>
    public static void ApplyModPatch(ModPatchOperation operation, string sourceFile, int sourceIndex)
    {
        if (!IsPatchTarget(operation?.Target))
            return;

        if (string.Equals(operation.Target, CatalogPatchTarget, StringComparison.OrdinalIgnoreCase))
        {
            JObject catalog = new() { ["defaultProfileId"] = defaultProfileId };
            ModRuntimeManager.ApplyPatchOperation(catalog, operation, sourceFile, sourceIndex);
            string patchedDefaultProfileId = catalog.Value<string>("defaultProfileId")?.Trim();
            if (string.IsNullOrWhiteSpace(patchedDefaultProfileId))
                throw new InvalidDataException($"玩家模板默认 profile ID 不能为空：{sourceFile}#{sourceIndex}");
            defaultProfileId = patchedDefaultProfileId;
            return;
        }

        string id = operation.Target.Substring(PatchTargetPrefix.Length).Trim();
        if (string.IsNullOrWhiteSpace(id) || !Sources.TryGetValue(id, out JObject target))
        {
            if (operation.Optional)
                return;
            throw new InvalidDataException($"玩家模板 Patch 找不到目标：{operation.Target}（{sourceFile}#{sourceIndex}）");
        }

        ModRuntimeManager.ApplyPatchOperation(target, operation, sourceFile, sourceIndex);
        ResolvedSources.Clear();
        dirty = true;
    }

    /// <summary>完成 MOD 注册并验证继承、Patch 后的最终模板。</summary>
    public static void FinalizeExternal()
    {
        EnsureResolved();
        if (!ResolvedSources.ContainsKey(defaultProfileId))
            throw new InvalidDataException($"玩家创建 JSON 默认 profile 不存在：{defaultProfileId}");

        foreach (KeyValuePair<string, JObject> pair in ResolvedSources)
        {
            PlayerCreationTemplateConfig config = pair.Value.ToObject<PlayerCreationTemplateConfig>();
            PlayerCreationTemplateJsonLoader.Validate(new PlayerCreationTemplateCatalogConfig
            {
                SchemaVersion = PlayerCreationTemplateJsonLoader.SupportedSchemaVersion,
                DefaultProfileId = config.Id,
                Profiles = new List<PlayerCreationTemplateConfig> { config }
            });
        }
    }

    /// <summary>按 ID 取得玩家创建模板。</summary>
    public static bool TryGet(string id, out PlayerCreationTemplateConfig config)
    {
        EnsureResolved();
        config = null;
        string normalizedId = string.IsNullOrWhiteSpace(id) ? defaultProfileId : id.Trim();
        if (!ResolvedSources.TryGetValue(normalizedId, out JObject source))
            return false;

        config = source.ToObject<PlayerCreationTemplateConfig>();
        PlayerCreationTemplateJsonLoader.Validate(new PlayerCreationTemplateCatalogConfig
        {
            SchemaVersion = PlayerCreationTemplateJsonLoader.SupportedSchemaVersion,
            DefaultProfileId = config.Id,
            Profiles = new List<PlayerCreationTemplateConfig> { config }
        });
        return true;
    }

    /// <summary>按 ID 取得模板，找不到时抛出配置错误。</summary>
    public static PlayerCreationTemplateConfig GetRequired(string id)
    {
        if (TryGet(id, out PlayerCreationTemplateConfig config))
            return config;

        throw new InvalidDataException($"找不到玩家创建模板：{(string.IsNullOrWhiteSpace(id) ? defaultProfileId : id)}");
    }

    public static bool IsPatchTarget(string target)
    {
        return !string.IsNullOrWhiteSpace(target) &&
               (target.StartsWith(PatchTargetPrefix, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(target, CatalogPatchTarget, StringComparison.OrdinalIgnoreCase));
    }

    private static void EnsureResolved()
    {
        if (!dirty)
            return;

        ResolvedSources.Clear();
        foreach (string id in Sources.Keys)
            Resolve(id, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        dirty = false;
    }

    private static JObject Resolve(string id, ISet<string> resolving)
    {
        if (ResolvedSources.TryGetValue(id, out JObject resolved))
            return resolved;
        if (!Sources.TryGetValue(id, out JObject source))
            throw new InvalidDataException($"玩家创建模板继承找不到父模板：{id}");
        if (!resolving.Add(id))
            throw new InvalidDataException($"玩家创建模板继承存在循环：{id}");

        JObject result = new();
        string parentId = source.Value<string>("parent")?.Trim();
        if (!string.IsNullOrWhiteSpace(parentId))
        {
            JObject parent = Resolve(parentId, resolving);
            result = (JObject)parent.DeepClone();
        }

        JObject overrides = (JObject)source.DeepClone();
        overrides.Remove("id");
        overrides.Remove("parent");
        result.Merge(overrides, new JsonMergeSettings
        {
            MergeArrayHandling = MergeArrayHandling.Replace,
            MergeNullValueHandling = MergeNullValueHandling.Ignore
        });

        result["id"] = id;
        result.Remove("parent");
        resolving.Remove(id);
        ResolvedSources[id] = result;
        return result;
    }
}

#endregion
