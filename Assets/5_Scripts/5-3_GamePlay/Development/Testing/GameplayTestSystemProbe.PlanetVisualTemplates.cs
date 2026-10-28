using FlatWorld.Spaceflight;
using MemoryPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 星球 JSON 模板独立采样
    // 只构建独立目录和外观参数，不注册当前会话或修改玩家世界。
    private static JObject WorldPlanetVisualTemplates(JObject arguments)
    {
        string path = StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, SpaceCatalog.DefaultConfigPath);
        string json = StreamingAssetsTextLoader.ReadAllText(path);
        var catalog = new SpaceCatalog(json);
        var samples = new JArray();
        foreach (SpaceSectorId sector in catalog.ConfiguredSectors)
        {
            UniverseState universe = catalog.GenerateSector(17413, sector);
            foreach (BodyState body in universe.Bodies)
            {
                BodyState definition = catalog.GetBodyDefinition(body.BodyId);
                var properties = new MaterialPropertyBlock();
                Planet.ApplySurfaceProperties(properties, body.Visual);
                samples.Add(new JObject
                {
                    ["bodyId"] = body.BodyId,
                    ["definition"] = CapturePlanetVisualConfiguration(definition.Visual),
                    ["generated"] = CapturePlanetVisualConfiguration(body.Visual),
                    ["independentConfiguration"] = !ReferenceEquals(definition.Visual, body.Visual) &&
                        !ReferenceEquals(definition.Visual.BaseColor, body.Visual.BaseColor) &&
                        !ReferenceEquals(definition.Visual.AccentColor, body.Visual.AccentColor),
                    ["properties"] = CapturePlanetSurfaceProperties(properties)
                });
            }
        }

        BodyState dynamicDefinition = JsonConvert.DeserializeObject<BodyState>(JsonConvert.SerializeObject(catalog.Bodies[0]));
        dynamicDefinition.BodyId = "probe-random-planet";
        dynamicDefinition.PlanetId = "probe-random-world";
        dynamicDefinition.DisplayName = "测试随机星球";
        dynamicDefinition.ParentBodyId = string.Empty;
        dynamicDefinition.Visual = new PlanetVisualConfiguration
        {
            Style = PlanetSurfaceStyle.Ocean,
            BaseColor = new[] { .12f, .34f, .56f }, AccentColor = new[] { .65f, .43f, .21f },
            DetailStrength = .37f, DetailScale = 19f
        };
        JObject document = JObject.Parse(json);
        document["templateId"] = "probe:json-planet";
        document["sectorSystems"] = new JArray();
        document["bodies"] = new JArray(JObject.FromObject(dynamicDefinition));
        var dynamicCatalog = new SpaceCatalog(document.ToString());
        var invalidConfigurations = new JArray();
        void CaptureInvalidVisual(string label, JToken visual)
        {
            var candidate = (JObject)document.DeepClone();
            candidate["bodies"][0]["Visual"] = visual;
            string exception = null;
            try { _ = new SpaceCatalog(candidate.ToString()); }
            catch (System.Exception error) { exception = error.GetType().Name; }
            invalidConfigurations.Add(new JObject { ["label"] = label, ["exception"] = exception });
        }
        CaptureInvalidVisual("missing-visual", JValue.CreateNull());
        JObject invalidColor = JObject.FromObject(dynamicDefinition.Visual);
        invalidColor["BaseColor"] = new JArray(.1f, .2f);
        CaptureInvalidVisual("incomplete-rgb", invalidColor);
        UniverseState first = dynamicCatalog.Generate(17413);
        UniverseState repeated = dynamicCatalog.Generate(17413);
        UniverseState alternate = dynamicCatalog.Generate(17414);
        BodyState firstBody = first.Bodies[0], secondBody = repeated.Bodies[0];
        BodyState binary = MemoryPackSerializer.Deserialize<BodyState>(MemoryPackSerializer.Serialize(firstBody));
        JObject expected = CapturePlanetVisualConfiguration(firstBody.Visual);
        PlanetVisualConfiguration cloned = firstBody.Visual.Clone();
        cloned.BaseColor[0] = .98f;
        cloned.AccentColor[1] = .97f;
        JObject sourceAfterCloneMutation = CapturePlanetVisualConfiguration(firstBody.Visual);
        firstBody.Visual.BaseColor[0] = .91f;
        firstBody.Visual.AccentColor[1] = .92f;
        dynamicCatalog.RebindUniverse(first);
        JObject rebound = CapturePlanetVisualConfiguration(firstBody.Visual);
        firstBody.Visual.BaseColor[0] = .93f;
        firstBody.Visual.AccentColor[1] = .94f;
        UniverseState unknown = UniverseSectorFactory.Generate(new UniverseState
            { TemplateId = "probe:random-sector", Bodies = new() { dynamicCatalog.Bodies[0] } },
            17413, new SpaceSectorId(41, 42), true);
        JObject unknownBefore = JObject.FromObject(unknown.Bodies[0]);
        var savedSession = new SpaceSessionState();
        savedSession.Sectors.Add(unknown);
        SpaceSessionState restoredSession = SaveDataMgr.DeserializeSpaceState(JsonConvert.SerializeObject(savedSession));
        catalog.RebindUniverse(restoredSession.Sectors[0]);
        var result = new JObject
        {
            ["scope"] = "isolated-json-planet-templates", ["commonPrefabId"] = Planet.CommonPrefabId,
            ["invalidConfigurations"] = invalidConfigurations,
            ["templates"] = samples, ["dynamicBodyId"] = secondBody.TemplateBodyId,
            ["stableIdentity"] = firstBody.BodyId == secondBody.BodyId && firstBody.Seed == secondBody.Seed,
            ["alternateSeed"] = alternate.Bodies[0].Seed != secondBody.Seed,
            ["expected"] = expected, ["repeated"] = CapturePlanetVisualConfiguration(secondBody.Visual),
            ["binary"] = CapturePlanetVisualConfiguration(binary.Visual), ["rebound"] = rebound,
            ["definitionAfterMutation"] = CapturePlanetVisualConfiguration(dynamicCatalog.Bodies[0].Visual),
            ["sourceAfterCloneMutation"] = sourceAfterCloneMutation, ["cloned"] = CapturePlanetVisualConfiguration(cloned),
            ["unknownBody"] = new JObject
            {
                ["version"] = restoredSession.Version, ["count"] = restoredSession.Sectors[0].Bodies.Count,
                ["before"] = unknownBefore, ["restored"] = JObject.FromObject(restoredSession.Sectors[0].Bodies[0])
            }
        };
#if UNITY_EDITOR
        var prefabs = new JArray();
        foreach (string guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/2_Prefabs/World/Space" }))
        {
            string assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab != null && prefab.GetComponent<Planet>() != null)
                prefabs.Add(new JObject { ["name"] = prefab.name, ["path"] = assetPath,
                    ["spriteReady"] = prefab.GetComponentInChildren<SpriteRenderer>(true)?.sprite != null });
        }
        result["planetPrefabs"] = prefabs;
#endif
        return result;
    }

    internal static JObject CapturePlanetVisualConfiguration(PlanetVisualConfiguration visual) => new()
    {
        ["style"] = visual.Style.ToString(), ["styleIndex"] = (int)visual.Style,
        ["baseColor"] = JArray.FromObject(visual.BaseColor), ["accentColor"] = JArray.FromObject(visual.AccentColor),
        ["detailStrength"] = visual.DetailStrength, ["detailScale"] = visual.DetailScale
    };

    internal static JObject CapturePlanetSurfaceProperties(MaterialPropertyBlock properties)
    {
        Color color = properties.GetColor("_Color"), accent = properties.GetColor("_SurfaceAccentColor");
        return new JObject
        {
            ["styleIndex"] = properties.GetFloat("_SurfaceStyle"),
            ["baseColor"] = new JArray(color.r, color.g, color.b),
            ["accentColor"] = new JArray(accent.r, accent.g, accent.b),
            ["detailStrength"] = properties.GetFloat("_SurfaceDetailStrength"),
            ["detailScale"] = properties.GetFloat("_SurfaceDetailScale"),
            ["extent"] = properties.GetFloat("_SurfaceExtent")
        };
    }
    #endregion
}
