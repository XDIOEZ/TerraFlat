using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// 动物战斗技能的通用契约。技能本体作为 Item Module 存在，AI 只负责选择和调用，
/// 因此同一个技能模块可以通过 Actor JSON 挂载到不同动物上。
/// </summary>
public interface IAnimalCombatSkill
{
    string SkillId { get; }
    float TriggerDistance { get; }
    bool IsReady { get; }
    bool IsActive { get; }
    bool IsOnCooldown { get; }
    bool CanStart(Item target);
    bool TryStart(Item target);
    void Cancel();
    void ResetRuntime();
}

/// <summary>动物技能组合器：按模块顺序寻找可用技能，不包含具体技能玩法。</summary>
public sealed class AI_AnimalSkillController
{
    private readonly List<IAnimalCombatSkill> _skills = new List<IAnimalCombatSkill>();

    public bool HasSkills => _skills.Count > 0;
    public bool IsAnyActive
    {
        get
        {
            for (int i = 0; i < _skills.Count; i++)
            {
                if (_skills[i] != null && _skills[i].IsActive)
                    return true;
            }

            return false;
        }
    }

    /// <summary>从动物 Item 的子模块收集技能组件。</summary>
    public void Bind(Item owner)
    {
        _skills.Clear();
        if (owner == null)
            return;

        Module[] modules = owner.GetComponentsInChildren<Module>(true);
        for (int i = 0; i < modules.Length; i++)
        {
            // AI 初始化可能早于某个技能模块的 Load；先收集契约，CanStart 时再解析依赖。
            if (modules[i] is IAnimalCombatSkill skill)
                _skills.Add(skill);
        }
    }

    /// <summary>判断当前目标是否存在可用技能。</summary>
    public bool HasUsableSkill(Item target)
    {
        for (int i = 0; i < _skills.Count; i++)
        {
            IAnimalCombatSkill skill = _skills[i];
            if (skill != null && skill.CanStart(target))
                return true;
        }

        return false;
    }

    /// <summary>启动第一个可用技能；后续可扩展为优先级或权重选择。</summary>
    public bool TryStart(Item target)
    {
        for (int i = 0; i < _skills.Count; i++)
        {
            IAnimalCombatSkill skill = _skills[i];
            if (skill != null && skill.CanStart(target) && skill.TryStart(target))
                return true;
        }

        return false;
    }

    /// <summary>离开技能状态时取消所有正在执行的技能。</summary>
    public void CancelAll()
    {
        for (int i = 0; i < _skills.Count; i++)
            _skills[i]?.Cancel();
    }

    /// <summary>重新初始化 AI 时清除技能运行时状态，不产生一次新的冷却。</summary>
    public void ResetRuntime()
    {
        for (int i = 0; i < _skills.Count; i++)
            _skills[i]?.ResetRuntime();
    }
}

/// <summary>动物技能目录根节点。</summary>
[Serializable]
public sealed class AnimalSkillCatalog
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion;

    [JsonProperty("skills")]
    public List<AnimalSkillDefinition> Skills = new List<AnimalSkillDefinition>();
}

/// <summary>
/// 可由 JSON 配置的动物技能模板。数值不写入技能模块 Prefab，避免不同动物复制出多套逻辑。
/// </summary>
[Serializable]
public sealed class AnimalSkillDefinition
{
    public const string ChargeAttackType = "chargeAttack";

    [JsonProperty("id")]
    public string Id;

    [JsonProperty("type")]
    public string Type;

    [JsonProperty("chargeDurationSeconds")]
    public float ChargeDurationSeconds;

    [JsonProperty("rushSpeedMultiplier")]
    public float RushSpeedMultiplier;

    [JsonProperty("damageMultiplier")]
    public float DamageMultiplier;

    [JsonProperty("cooldownSeconds")]
    public float CooldownSeconds;

    [JsonProperty("rushDurationSeconds")]
    public float RushDurationSeconds;

    [JsonProperty("rushDistance")]
    public float RushDistance = 6f;

    [JsonProperty("triggerDistance")]
    public float TriggerDistance;

    [JsonProperty("arrivalDistance")]
    public float ArrivalDistance;

    [JsonProperty("hitboxForwardOffset")]
    public float HitboxForwardOffset;

    [JsonProperty("hitboxSize")]
    public Vector2 HitboxSize;

    [JsonProperty("hitboxOffset")]
    public Vector2 HitboxOffset;

    [JsonProperty("chargeAnimation")]
    public string ChargeAnimation;

    [JsonProperty("rushAnimation")]
    public string RushAnimation;
}

/// <summary>动物技能 JSON 目录加载器，兼容桌面、Android 和 WebGL 的 StreamingAssets 读取方式。</summary>
public static class AnimalSkillCatalogLoader
{
    public const int SupportedSchemaVersion = 1;
    public const string RelativeConfigPath = "GameConfig/Skills/animal-skills.json";

    private static readonly JsonSerializerSettings StrictJsonSettings = new JsonSerializerSettings
    {
        MissingMemberHandling = MissingMemberHandling.Error,
        DateParseHandling = DateParseHandling.None
    };

    public static string BuiltInConfigPath =>
        StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, RelativeConfigPath);

    /// <summary>同步读取目录，供编辑器迁移和静态检查使用。</summary>
    public static AnimalSkillCatalog LoadBuiltIn()
    {
        return Deserialize(StreamingAssetsTextLoader.ReadAllText(BuiltInConfigPath));
    }

    /// <summary>异步读取目录，供 GameRes 启动阶段加载。</summary>
    public static IEnumerator LoadBuiltInAsync(
        Action<AnimalSkillCatalog> completed,
        Action<Exception> failed)
    {
        string json = null;
        Exception readError = null;
        yield return StreamingAssetsTextLoader.ReadAllTextAsync(
            BuiltInConfigPath,
            text => json = text,
            exception => readError = exception);

        if (readError != null)
        {
            failed?.Invoke(readError);
            yield break;
        }

        try
        {
            completed?.Invoke(Deserialize(json));
        }
        catch (Exception exception)
        {
            failed?.Invoke(exception);
        }
    }

    /// <summary>严格解析并校验技能模板，错误在加载阶段尽早暴露。</summary>
    public static AnimalSkillCatalog Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("动物技能 JSON 为空");

        AnimalSkillCatalog catalog = JsonConvert.DeserializeObject<AnimalSkillCatalog>(
            json,
            StrictJsonSettings);
        Validate(catalog);
        return catalog;
    }

    private static void Validate(AnimalSkillCatalog catalog)
    {
        if (catalog == null)
            throw new InvalidDataException("动物技能目录为空");
        if (catalog.SchemaVersion != SupportedSchemaVersion)
            throw new InvalidDataException(
                $"动物技能目录 schemaVersion 不支持：{catalog.SchemaVersion}");
        if (catalog.Skills == null || catalog.Skills.Count == 0)
            throw new InvalidDataException("动物技能目录至少需要一个技能模板");

        HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < catalog.Skills.Count; i++)
        {
            AnimalSkillDefinition skill = catalog.Skills[i];
            if (skill == null || string.IsNullOrWhiteSpace(skill.Id))
                throw new InvalidDataException($"动物技能模板 {i} 缺少 id");
            if (!ids.Add(skill.Id.Trim()))
                throw new InvalidDataException($"动物技能 ID 重复：{skill.Id}");
            if (!string.Equals(skill.Type, AnimalSkillDefinition.ChargeAttackType,
                StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"动物技能 {skill.Id} 的 type 不支持：{skill.Type}");
            }

            RequirePositive(skill.ChargeDurationSeconds, skill.Id, "chargeDurationSeconds");
            RequirePositive(skill.RushSpeedMultiplier, skill.Id, "rushSpeedMultiplier");
            RequirePositive(skill.DamageMultiplier, skill.Id, "damageMultiplier");
            RequireNonNegative(skill.CooldownSeconds, skill.Id, "cooldownSeconds");
            RequirePositive(skill.RushDurationSeconds, skill.Id, "rushDurationSeconds");
            RequirePositive(skill.RushDistance, skill.Id, "rushDistance");
            RequirePositive(skill.TriggerDistance, skill.Id, "triggerDistance");
            RequirePositive(skill.ArrivalDistance, skill.Id, "arrivalDistance");
            RequireNonNegative(skill.HitboxForwardOffset, skill.Id, "hitboxForwardOffset");
            RequirePositive(skill.HitboxSize.x, skill.Id, "hitboxSize.x");
            RequirePositive(skill.HitboxSize.y, skill.Id, "hitboxSize.y");
            RequireFinite(skill.HitboxOffset.x, skill.Id, "hitboxOffset.x");
            RequireFinite(skill.HitboxOffset.y, skill.Id, "hitboxOffset.y");
        }
    }

    private static void RequirePositive(float value, string id, string field)
    {
        RequireFinite(value, id, field);
        if (value <= 0f)
            throw new InvalidDataException($"动物技能 {id} 的 {field} 必须大于 0");
    }

    private static void RequireNonNegative(float value, string id, string field)
    {
        RequireFinite(value, id, field);
        if (value < 0f)
            throw new InvalidDataException($"动物技能 {id} 的 {field} 不能小于 0");
    }

    private static void RequireFinite(float value, string id, string field)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            throw new InvalidDataException($"动物技能 {id} 的 {field} 不是有限数值");
    }
}

/// <summary>已加载动物技能目录的运行时访问入口。</summary>
public static class AnimalSkillCatalogService
{
    private static readonly Dictionary<string, AnimalSkillDefinition> Definitions =
        new Dictionary<string, AnimalSkillDefinition>(StringComparer.OrdinalIgnoreCase);

    public static void Replace(AnimalSkillCatalog catalog)
    {
        if (catalog == null)
            throw new ArgumentNullException(nameof(catalog));

        Definitions.Clear();
        for (int i = 0; i < catalog.Skills.Count; i++)
        {
            AnimalSkillDefinition skill = catalog.Skills[i];
            Definitions.Add(skill.Id.Trim(), skill);
        }
    }

    public static bool TryGet(string id, out AnimalSkillDefinition definition)
    {
        return Definitions.TryGetValue(id?.Trim() ?? string.Empty, out definition);
    }

    public static void Reset()
    {
        Definitions.Clear();
    }
}
