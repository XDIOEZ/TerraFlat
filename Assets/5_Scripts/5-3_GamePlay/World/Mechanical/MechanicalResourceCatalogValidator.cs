using System.Collections.Generic;

/// <summary>机械加工目录在资源 Ready 前检查输入/产物及正式面板，避免运行到加工时才发现悬空引用。</summary>
public sealed class MechanicalResourceCatalogValidator : IResourceCatalogValidator
{
    #region 目录校验
    public string Id => "mechanical";
    public void Validate(GameRes resources, List<string> errors)
    {
        foreach (var process in MechanicalCatalog.Processes)
        {
            if (!resources.ItemDefinitions.ContainsKey(process.Input)) errors.Add("机械加工输入未注册：" + process.Input);
            foreach (var output in process.Outputs)
                if (!resources.ItemDefinitions.ContainsKey(output.ItemName)) errors.Add("机械加工产物未注册：" + output.ItemName);
        }
        foreach (string id in new[] { "Module_HandDrill", "Module_MechanicalNode", "UI_HandDrill", "UI_Mechanical" })
            if (resources.GetPrefab(id, false) == null) errors.Add("机械资源未注册：" + id);
    }
    #endregion
}
