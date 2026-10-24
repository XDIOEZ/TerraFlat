using System.Runtime.CompilerServices;
using Newtonsoft.Json.Linq;

/// <summary>攻击力信息读取最终定义；模板默认值只读且按定义缓存，不触发攻击或实例化武器。</summary>
public sealed class DamageItemInfoProvider : IItemInfoProvider
{
    #region 攻击力信息
    private sealed class Settings { public float Physical; }
    private readonly ConditionalWeakTable<RuntimeItemModuleDefinition, Settings> settings = new();

    public void CollectItemInfo(ItemInfoContext context, ItemInfoBuffer results)
    {
        RuntimeItemModuleDefinition definition = context.ModuleDefinition;
        if (definition?.Enabled != true || context.ModuleData?.Enabled != true) return;
        if (!settings.TryGetValue(definition, out Settings value))
        {
            CombatDamage damage = string.IsNullOrWhiteSpace(definition.ParametersJson) ? null :
                JObject.Parse(definition.ParametersJson)["DamageValues"]?.ToObject<CombatDamage>();
            if (damage == null)
            {
                GameRes resources = GameRes.ExistingInstance;
                string prefabId = string.IsNullOrWhiteSpace(definition.PrefabId) ? definition.ModuleId : definition.PrefabId;
                var prefab = resources?.GetPrefab(prefabId, false);
                if (prefab == null) return;
                damage = prefab.GetComponentInChildren<Mod_Damage>(true)?.DamageValues;
                if (damage == null) return;
            }
            value = new Settings { Physical = damage.Physical };
            settings.Add(definition, value);
        }
        if (value.Physical > 0f && ItemNumericInfo.TryCreate(ItemInfoKeys.PhysicalAttack, definition.StableName,
                value.Physical, out var info)) results.NumericValues.Add(info);
    }
    #endregion
}
