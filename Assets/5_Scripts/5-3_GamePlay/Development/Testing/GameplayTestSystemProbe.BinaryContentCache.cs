using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using FlatWorld.WorldModel;
using MemoryPack;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 启动二进制缓存规则采样

    private static JObject ServiceBinaryContentCache(JObject args)
    {
        // 只使用自有临时目录，不读写玩家缓存、JSON 或存档。
        string root = Path.Combine(Path.GetTempPath(), "FlatWorldBinaryContent", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string manifest = Path.Combine(root, "manifest.json");
        string parent = Path.Combine(root, "parent.json");
        string child = Path.Combine(root, "child.json");
        string cachePath = Path.Combine(root, "cache.fwbin");
        File.WriteAllText(manifest, "{\"packages\":[\"parent.json\",\"child.json\"]}", new UTF8Encoding(true));
        File.WriteAllText(parent, "{\"schemaVersion\":1,\"items\":[{\"id\":\"base\",\"abstract\":true,\"weight\":3}]}");
        File.WriteAllText(child, "{\"schemaVersion\":1,\"items\":[{\"id\":\"child\",\"parent\":\"base\",\"gameName\":\"缓存样本\"}]}");
        BinaryContentCache Session(string version = "fixture-v1") => new(cachePath, version, root, manifest);
        List<ItemDefinitionDto> Parse(BinaryContentCache session)
        {
            session.ReadText(manifest);
            return ItemDefinitionCatalogLoader.ResolveDefinitions(new[] { session.ReadText(parent), session.ReadText(child) });
        }

        try
        {
            var result = new JObject();
            BinaryContentCache cold = Session();
            result["coldMiss"] = !cold.TryRead<List<ItemDefinitionDto>>(out _);
            List<ItemDefinitionDto> expected = Parse(cold);
            ItemDefinitionDto definition = expected.Single(value => value.Id == "child");
            definition.Health = new ItemHealthDefinitionDto
            { Defense = new ItemDefenseDefinitionDto { Cutting = 1, Piercing = 2, Chopping = 3, Blunt = 4 } };
            definition.Visual = new ItemVisualDefinitionDto
            { RendererLocalScale = new Vector3(2, 3, 4), LiquidSurface = new LiquidSurfaceDefinition { Bounds = new Rect(.1f, .2f, .3f, .4f) } };
            definition.WorldGridOccupancy = new WorldGridOccupancyData
            { Cells = new List<WorldGridOccupancyCellData> { new() { X = -2, Y = 3 }, null } };
            definition.Modules["sample"] = new ItemModuleDefinitionDto
            {
                Prefab = "sample-module",
                Data = new JObject { ["integer"] = 9007199254740993L, ["decimal"] = new JValue(1.234567890123456789m),
                    ["array"] = new JArray(true, JValue.CreateNull(), new JObject { ["value"] = "参数" }) },
                Parameters = new JObject { ["$transform"] = new JObject { ["x"] = 1.25 }, ["empty"] = new JObject() }
            };
            definition.Ecs = new JObject { ["move"] = new JObject { ["speed"] = 3.5 } };
            definition.Matter = new ItemMatterDefinitionDto
            { Transitions = new List<ItemMatterTransitionDto> { new() { Id = "heat", MinTemperature = 100, OutputItemId = "heated" } } };
            result["stored"] = cold.Store(expected);
            result["warmHit"] = Session().TryRead(out List<ItemDefinitionDto> restored);
            ItemDefinitionDto actual = restored?.Single(value => value.Id == "child");
            result["count"] = restored?.Count;
            result["inheritedWeight"] = actual?.Weight;
            result["defense"] = actual == null ? null : new JArray(actual.Health.Defense.Cutting,
                actual.Health.Defense.Piercing, actual.Health.Defense.Chopping, actual.Health.Defense.Blunt);
            result["parametersEqual"] = actual != null && JToken.DeepEquals(definition.Modules["sample"].Data, actual.Modules["sample"].Data) &&
                JToken.DeepEquals(definition.Modules["sample"].Parameters, actual.Modules["sample"].Parameters) &&
                JToken.DeepEquals(definition.Ecs, actual.Ecs);
            result["gridX"] = actual?.WorldGridOccupancy?.Cells[0].X;
            result["nullGridCell"] = actual?.WorldGridOccupancy?.Cells[1] == null;
            result["scaleZ"] = actual?.Visual?.RendererLocalScale?.z;
            result["surfaceWidth"] = actual?.Visual?.LiquidSurface?.Bounds.width;
            result["transitionTemperature"] = actual?.Matter?.Transitions[0].MinTemperature;
            if (actual != null) actual.Modules["sample"].Data["integer"] = 0;
            Session().TryRead(out List<ItemDefinitionDto> independent);
            result["independentInteger"] = independent?.Single(value => value.Id == "child").Modules["sample"].Data["integer"]?.ToString();

            var actor = new ActorDefinitionInputs { Definitions = expected,
                Sources = new List<JObject> { new() { ["id"] = "child", ["custom"] = new JObject { ["enabled"] = true } } } };
            ActorDefinitionInputs actorRestored = MemoryPackSerializer.Deserialize<ActorDefinitionInputs>(MemoryPackSerializer.Serialize(actor));
            result["actorSourcesEqual"] = JToken.DeepEquals(actor.Sources[0], actorRestored.Sources[0]);
            result["actorDefinitions"] = actorRestored.Definitions.Count;
            result["contractMiss"] = !Session("fixture-v2").TryRead<List<ItemDefinitionDto>>(out _);

            string originalParent = File.ReadAllText(parent);
            DateTime originalTime = File.GetLastWriteTimeUtc(parent);
            File.WriteAllText(parent, originalParent.Replace("\"weight\":3", "\"weight\":4"));
            File.SetLastWriteTimeUtc(parent, originalTime);
            result["sameLengthChangedMiss"] = !Session().TryRead<List<ItemDefinitionDto>>(out _);
            BinaryContentCache changed = Session();
            List<ItemDefinitionDto> updated = Parse(changed);
            result["rebuilt"] = changed.Store(updated);
            Session().TryRead(out List<ItemDefinitionDto> updatedRead);
            result["updatedWeight"] = updatedRead?.Single(value => value.Id == "child").Weight;

            string originalManifest = File.ReadAllText(manifest);
            File.WriteAllText(manifest, "{\"packages\":[]}");
            result["manifestMiss"] = !Session().TryRead<List<ItemDefinitionDto>>(out _);
            File.WriteAllText(manifest, originalManifest, new UTF8Encoding(true));
            File.Delete(child);
            result["missingSourceMiss"] = !Session().TryRead<List<ItemDefinitionDto>>(out _);
            File.WriteAllText(child, "{\"schemaVersion\":1,\"items\":[{\"id\":\"child\",\"parent\":\"base\",\"gameName\":\"缓存样本\"}]}");
            byte[] validCache = File.ReadAllBytes(cachePath);
            byte[] corrupt = (byte[])validCache.Clone();
            corrupt[corrupt.Length - 1] ^= 1;
            File.WriteAllBytes(cachePath, corrupt);
            result["corruptMiss"] = !Session().TryRead<List<ItemDefinitionDto>>(out _);
            File.WriteAllBytes(cachePath, validCache.Take(17).ToArray());
            result["truncatedMiss"] = !Session().TryRead<List<ItemDefinitionDto>>(out _);
            BinaryContentCache repair = Session();
            result["repaired"] = repair.Store(Parse(repair)) && Session().TryRead<List<ItemDefinitionDto>>(out _);

            BinaryContentCache raced = Session();
            List<ItemDefinitionDto> racedData = Parse(raced);
            File.AppendAllText(parent, " ");
            result["changedDuringParseRejected"] = !raced.Store(racedData);
            string blocked = Path.Combine(root, "blocked");
            File.WriteAllText(blocked, "占用缓存父目录");
            var unwritable = new BinaryContentCache(Path.Combine(blocked, "cache.fwbin"), "fixture-v1", root, manifest);
            List<ItemDefinitionDto> fallback = Parse(unwritable);
            result["writeFailureFallback"] = !unwritable.Store(fallback) && fallback.Count == 2;
            result["noTemporaryFiles"] = Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories).Length == 0;
            return result;
        }
        finally { Directory.Delete(root, true); }
    }

    #endregion

    #region 全部启动配置共用缓存边界

    private static JObject ServiceJsonConfigurationCache(JObject args)
    {
        string root = Path.Combine(Path.GetTempPath(), "FlatWorldJsonConfiguration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        byte[] Digest(string text) { using var hash = SHA256.Create(); return hash.ComputeHash(Encoding.UTF8.GetBytes(text)); }
        BinaryContentCache Cache(string name, string source = "first")
            => BinaryContentCache.ForDocument(Path.Combine(root, name + ".fwbin"), "configuration-fixture", Digest(source));
        T Load<T>(string name, T source) where T : class
            => JsonConfigurationCache.ReadOrBuild(Cache(name), () => source);
        T Warm<T>(string name) where T : class
            => JsonConfigurationCache.ReadOrBuild<T>(Cache(name), () => throw new InvalidOperationException("缓存命中不应再次调用 JSON 解析器"));
        try
        {
            int before = BinaryContentCache.BuildRevision;
            var texts = new TextLibraryCatalogDto { SchemaVersion = 1 };
            texts.Libraries["Names"] = new List<string> { "甲", "乙" };
            texts.Generators["FullName"] = new TextLibraryGeneratorDto { Parts = new List<string> { "Names" }, Separator = "-" };
            Load("texts", texts);
            int afterCold = BinaryContentCache.BuildRevision;
            TextLibraryCatalogDto warm = Warm<TextLibraryCatalogDto>("texts");
            int afterWarm = BinaryContentCache.BuildRevision;
            texts.Libraries["Names"][0] = "已修改";
            var result = new JObject
            {
                ["coldBuildsHint"] = afterCold > before,
                ["warmDoesNotBuildHint"] = afterWarm == afterCold,
                ["libraryIgnoreCase"] = warm.Libraries.ContainsKey("names"),
                ["generatorIgnoreCase"] = warm.Generators.ContainsKey("fullname"),
                ["independentValue"] = warm.Libraries["Names"][0]
            };
            Load("time", new TimeSystemConfigCatalog { SchemaVersion = 3,
                Profiles = new List<TimeSystemProfileConfig> { new() { Id = "test", TimeScale = 2.5f } } });
            result["timeScale"] = Warm<TimeSystemConfigCatalog>("time").Profiles[0].TimeScale;
            Load("player", new PlayerCreationTemplateCatalogConfig
            { Profiles = new List<PlayerCreationTemplateConfig> { new() { Id = "test" } } });
            result["playerStamina"] = Warm<PlayerCreationTemplateCatalogConfig>("player").Profiles[0].Core.InitialStamina;
            Load("theme", new FlatWorldUIThemeConfig
            { schemaVersion = 1, colors = new[] { new FlatWorldUIThemeSwatch { id = "test", hex = "#FFFFFF" } } });
            result["themeHex"] = Warm<FlatWorldUIThemeConfig>("theme").colors[0].hex;
            Load("rendering", new WorldRenderingConfig { schemaVersion = 2,
                preferences = new WorldRenderingConfig.RenderingPreferences { graphicsPreset = 3 },
                denseFog = new WorldRenderingConfig.RenderingDenseFog() });
            result["renderingPreset"] = Warm<WorldRenderingConfig>("rendering").preferences.graphicsPreset;
            Load("weather", new RegionalWeatherSettings { Associations = new[]
                { new WeatherAssociationRule { SourceId = "rain", TargetId = "wind", ProbabilityBonus = .2f } } });
            result["weatherAssociation"] = Warm<RegionalWeatherSettings>("weather").Associations[0].SourceId;
            var atmosphere = new FlatWorld.Spaceflight.SurfaceProfile();
            atmosphere.AtmosphereComposition["Nitrogen"] = .75m;
            Load("surface", atmosphere);
            result["atmosphereIgnoreCase"] = Warm<FlatWorld.Spaceflight.SurfaceProfile>("surface").AtmosphereComposition.ContainsKey("nitrogen");
            Load("tile", new TileDefinitionCatalogDto { Tiles = new List<TileDefinitionDto> { new()
            { Id = "test", Data = new TileComponentDefinitionDto { Type = "ground", Parameters = new JObject { ["amount"] = 5 } } } } });
            result["tileParameter"] = Warm<TileDefinitionCatalogDto>("tile").Tiles[0].Data.Parameters.Value<int>("amount");
            var fixedDefinition = new FlatWorld.Structures.FixedStructureDefinition { Id = "test", Items = new() };
            fixedDefinition.Members.Add(new FlatWorld.Structures.FixedStructureMember { MemberId = "runtime-only" });
            Load("structure", fixedDefinition);
            result["derivedMembersExcluded"] = Warm<FlatWorld.Structures.FixedStructureDefinition>("structure").Members.Count == 0;
            Load("mod", new ModPatchDocument { Patches = new List<ModPatchOperation>
                { new() { Value = new JObject { ["nested"] = new JArray(1, true, JValue.CreateNull()) } } } });
            result["modPayload"] = JToken.DeepEquals(Warm<ModPatchDocument>("mod").Patches[0].Value,
                new JObject { ["nested"] = new JArray(1, true, JValue.CreateNull()) });
            int changedParses = 0;
            TextLibraryCatalogDto updated = JsonConfigurationCache.ReadOrBuild(Cache("texts", "changed"), () =>
            { changedParses++; return new TextLibraryCatalogDto { SchemaVersion = 9 }; });
            result["changedParses"] = changedParses;
            result["updatedVersion"] = updated.SchemaVersion;
            int invalidParses = 0;
            for (int i = 0; i < 2; i++)
            {
                try { JsonConfigurationCache.ReadOrBuild<TextLibraryCatalogDto>(Cache("invalid"), () =>
                    { invalidParses++; throw new InvalidDataException("配置校验失败"); }); }
                catch (InvalidDataException) { }
            }
            result["invalidParses"] = invalidParses;
            result["invalidNotCached"] = !File.Exists(Path.Combine(root, "invalid.fwbin"));
            return result;
        }
        finally { Directory.Delete(root, true); }
    }

    #endregion
}
