using UnityEngine;

public partial class GameRes
{
    #region 工业模块模板
    public bool IsRuntimeModuleTemplate(GameObject template) => template != null &&
        (resourceAssets.ContainsRuntimeTemplate(template) ||
         ModRuntimeManager.Instance != null && ModRuntimeManager.Instance.IsRuntimeTemplate(template));

    private void EnsureIndustrialRuntimeTemplates()
    {
        RegisterIndustrialModule<Mod_Pressure>("Module_Pressure");
        RegisterIndustrialModule<Mod_FluidTank>("Module_FluidTank");
        RegisterIndustrialModule<Mod_Spacesuit>("Module_Spacesuit");
        RegisterIndustrialModule<Mod_AtmosphericOxygenSupply>("Module_AtmosphericOxygenSupply");
    }
    private void RegisterIndustrialModule<T>(string key) where T : Module
    {
        if (AllPrefabs.ContainsKey(key)) return;
        // 无场景业务的模块模板只归当前资源目录所有，候选加载失败会一起释放。
        var template = new GameObject(key);
        template.SetActive(false);
        Object.DontDestroyOnLoad(template);
        T module = template.AddComponent<T>();
        resourceAssets.OwnRuntimeTemplate(template);
        RegisterPrefabAlias(key, template);
        RegisterPrefabAlias(module.CanonicalModuleId, template);
    }
    #endregion
}
