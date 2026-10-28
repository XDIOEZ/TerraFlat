using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 基础物品分包与公共模板采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterBasicItemCatalogPartitionRules()
        => Register("item_runtime", "basic_item_catalog", CaptureBasicItemCatalogPartitionAndInheritedConfiguration);

    // 通过正式目录读取跨分包继承结果，Lua 校验外观、营养和加工规则。
    private static JObject CaptureBasicItemCatalogPartitionAndInheritedConfiguration(JObject args)
    {
        var definitions = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions().ToDictionary(value => value.Id);
        ItemDefinitionManifestDto manifest = ItemDefinitionCatalogLoader.DeserializeManifest(
            StreamingAssetsTextLoader.ReadAllText(ItemDefinitionCatalogLoader.BuiltInManifestPath));
        var items = new JObject();
        foreach (JToken idToken in args["ids"] as JArray ?? new JArray())
        {
            string id = idToken.Value<string>();
            ItemDefinitionDto definition = definitions[id];
            definition.Modules.TryGetValue("food", out ItemModuleDefinitionDto food);
            items[id] = new JObject
            {
                ["id"] = definition.Id,
                ["abstract"] = definition.Abstract,
                ["shell"] = definition.ShellPrefab,
                ["weight"] = definition.Weight,
                ["volume"] = definition.Volume,
                ["positionDeclared"] = definition.Visual?.RendererLocalPosition.HasValue == true,
                ["scaleX"] = definition.Visual?.RendererLocalScale?.x,
                ["scaleY"] = definition.Visual?.RendererLocalScale?.y,
                ["scaleZ"] = definition.Visual?.RendererLocalScale?.z,
                ["nutrition"] = food?.Data?["FoodData"]?["nutrition"]?.DeepClone(),
                ["processing"] = JObject.FromObject(definition.Processing),
                ["tags"] = new JArray(definition.Tags),
                ["modules"] = new JArray(definition.Modules.Keys)
            };
        }
        return new JObject
        {
            ["packages"] = new JArray(manifest.Packages.Where(package => package.Enabled).Select(package => package.Id)),
            ["items"] = items,
            ["visualTemplateAbstract"] = definitions["BasicItemVisual_Base"].Abstract,
            ["foodTemplateAbstract"] = definitions["BasicFood_Base"].Abstract
        };
    }

    #endregion
}
