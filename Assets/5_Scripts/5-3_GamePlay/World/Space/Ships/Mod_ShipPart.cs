using System;
using Newtonsoft.Json;
using UnityEngine;

public sealed class Mod_ShipPart : Module
{
    #region 显式船体能力
    public const string ModuleId = "船体部件模块";
    public override string CanonicalModuleId => ModuleId;
    public override ModuleTickMode TickMode => ModuleTickMode.Disabled;
    public Ex_ModData_MemoryPackable ModData = new() { ID = ModuleId };
    public FlatWorld.Spaceflight.ShipPartConfiguration Configuration = new();
    public override ModuleData _Data
    {
        get => ModData;
        set => ModData = value as Ex_ModData_MemoryPackable ?? throw new ArgumentException("船体部件数据类型错误。");
    }
    protected override void OnLoad() { }
    protected override void OnSave() { }
    protected override void OnUnload() { }

    public static FlatWorld.Spaceflight.ShipPartConfiguration Read(ItemData data)
    {
        if (data == null || GameRes.ExistingInstance == null || !GameRes.ExistingInstance.TryGetItemDefinition(data.IDName, out RuntimeItemDefinition definition)) return null;
        foreach (RuntimeItemModuleDefinition entry in definition.ModuleDefinitions)
        {
            if (entry.ModuleId != ModuleId || !entry.Enabled) continue;
            GameObject prefab = GameRes.ExistingInstance.GetPrefab(entry.PrefabId, false);
            Mod_ShipPart authoring = prefab != null ? prefab.GetComponentInChildren<Mod_ShipPart>(true) : null;
            if (authoring == null) throw new InvalidOperationException("船体部件模板缺失：" + entry.PrefabId);
            var configuration = JsonConvert.DeserializeObject<FlatWorld.Spaceflight.ShipPartConfiguration>(JsonConvert.SerializeObject(authoring.Configuration));
            if (!string.IsNullOrWhiteSpace(entry.ParametersJson))
            {
                var parameters = Newtonsoft.Json.Linq.JObject.Parse(entry.ParametersJson);
                if (parameters["Configuration"] is Newtonsoft.Json.Linq.JObject values)
                    JsonConvert.PopulateObject(values.ToString(), configuration);
            }
            return configuration;
        }
        return null;
    }
    #endregion
}
