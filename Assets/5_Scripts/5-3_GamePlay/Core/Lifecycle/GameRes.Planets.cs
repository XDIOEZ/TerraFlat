using System.IO;
using FlatWorld.Spaceflight;
using UnityEngine;

public partial class GameRes
{
    #region JSON 星球模板构建与实例化
    public Planet GetPlanetRuntimeTemplate(string bodyId)
        => AllPrefabs.TryGetValue("planet-template:" + bodyId, out GameObject template) ? template.GetComponent<Planet>() : null;

    private void BuildPlanetRuntimeTemplates(SpaceCatalog catalog)
    {
        GameObject common = GetPrefab(Planet.CommonPrefabId, false);
        if (common == null || common.GetComponent<Planet>() == null || common.GetComponentInChildren<SpriteRenderer>(true)?.sprite == null)
            throw new InvalidDataException("通用星体公模缺少 Planet 或圆形 SpriteRenderer。");
        var templateRoot = resourceAssets.OwnRuntimeTemplate(new GameObject("JSON 星球模板"));
        templateRoot.SetActive(false);
        DontDestroyOnLoad(templateRoot);
        foreach (SpaceSectorId sector in catalog.ConfiguredSectors)
            foreach (BodyState body in catalog.GetBodyTemplates(sector))
            {
                // 模板归资源会话所有，重载失败和正常卸载均沿既有所有权释放。
                GameObject template = Instantiate(common, templateRoot.transform);
                string bodyId = SpaceSectorCoordinates.BodyId(sector, body.BodyId);
                template.name = "星球模板/" + bodyId;
                template.GetComponent<Planet>().ConfigurePlanetVisualTemplate(body.Visual.Clone());
                RegisterPrefabAlias("planet-template:" + bodyId, template);
            }
    }

    public Planet InstantiatePlanetFromBodyConfiguration(BodyState body, PlanetData planet, Transform parent = null)
    {
        Planet template = GetPlanetRuntimeTemplate(body.BodyId);
        GameObject source = template != null ? template.gameObject : GetPrefab(Planet.CommonPrefabId);
        GameObject instance = Instantiate(source, parent);
        Planet runtime = instance.GetComponent<Planet>();
        runtime.ConfigurePlanetVisualTemplate(body.Visual);
        runtime.planetData = planet;
        runtime.name = body.DisplayName;
        instance.SetActive(true);
        return runtime;
    }
    #endregion
}
