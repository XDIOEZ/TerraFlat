using System;
using FlatWorld.Spaceflight;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 建造拒绝消息采样
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterBuildingFeedbackRules()
    {
        Register("building", "placement_feedback", ServiceBuildingPlacementFeedback);
        Register("building", "door_definitions", ServiceDoorDefinitions);
    }

    private static JObject ServiceBuildingPlacementFeedback(JObject args)
    {
        var host = new GameObject("probe:inactive-building-feedback");
        host.SetActive(false);
        Player actor = host.AddComponent<Player>();
        var details = new JArray();
        int semanticCount = 0;
        bool actorPreserved = true;
        void Detailed(Player value, FlatWorld.Gameplay.Building.BuildingPlacementFailureReason reason, string detail)
        { actorPreserved &= ReferenceEquals(value, actor); details.Add(detail); }
        void Semantic(Player value, FlatWorld.Gameplay.Building.BuildingPlacementFailureReason reason) => semanticCount++;
        FlatWorld.Gameplay.Building.BuildingPlacementFeedbackEvents.PlacementRejectedDetailed += Detailed;
        FlatWorld.Gameplay.Building.BuildingPlacementFeedbackEvents.PlacementRejected += Semantic;
        try
        {
            FlatWorld.Gameplay.Building.BuildingPlacementFeedbackEvents.PublishPlacementRejected(actor, detail: "召唤器数量不足");
            FlatWorld.Gameplay.Building.BuildingPlacementFeedbackEvents.PublishPlacementRejected(actor, detail: "地块支撑能力不足");
            FlatWorld.Gameplay.Building.BuildingPlacementFeedbackEvents.PublishPlacementRejected(null, detail: "不应发布");
            return new JObject { ["details"] = details, ["semanticCount"] = semanticCount, ["actorPreserved"] = actorPreserved };
        }
        finally
        {
            FlatWorld.Gameplay.Building.BuildingPlacementFeedbackEvents.PlacementRejectedDetailed -= Detailed;
            FlatWorld.Gameplay.Building.BuildingPlacementFeedbackEvents.PlacementRejected -= Semantic;
            UnityEngine.Object.DestroyImmediate(host);
        }
    }
    #endregion

    #region 普通滑动门正式目录采样
    private static JObject ServiceDoorDefinitions(JObject args)
    {
        var definitions = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions();
        var doors = new JArray();
        // 读取正式继承后的载体与本体，防止只有船门或测试夹具启用了新能力。
        foreach (string id in new[] { "Door_Wood", "Door_Stone", "Door_Wood_Summoner", "Door_Stone_Summoner" })
        {
            ItemDefinitionDto definition = definitions.Find(value => value.Id == id)
                ?? throw new InvalidOperationException("缺少门定义：" + id);
            ItemModuleDefinitionDto module = definition.Modules[ModText.Building];
            Mod_Building.Building_Data state = new();
            module.Data.ToObject<Ex_ModData>().ReadData(ref state);
            definition.Modules.TryGetValue("Door", out ItemModuleDefinitionDto door);
            doors.Add(new JObject { ["id"] = id, ["buildingEnabled"] = module.Enabled != false,
                ["quarterTurnAllowed"] = state.AllowQuarterTurnPlacement, ["role"] = (int)state.Role,
                ["stackable"] = definition.Stackable == true, ["sharedModuleCount"] = state.SharedModuleIds?.Length ?? 0,
                ["bodyId"] = state.BuildingPrefabId, ["summonerId"] = state.SummonerPrefabId,
                ["spriteAddress"] = definition.Visual?.SpriteAddress,
                ["spriteStateCount"] = definition.Visual?.SpriteStates?.Count ?? 0,
                ["hasDoorModule"] = door != null && door.Enabled != false,
                ["useItemSprite"] = door?.Parameters?.Value<bool?>("UseItemSprite") == true });
        }
        return new JObject { ["doors"] = doors };
    }
    #endregion

    #region 船件建筑状态的正式能力采样

    // 禁用独立物品只装配必要模块，分层与通行判断全部交给正式船件接口。
    private static JObject ServiceShipPartPlacementState(JObject args) => WithEntityProbeItem(item =>
    {
        var stored = new Ex_ModData { ID = ModText.Building, StableName = "building" };
        stored.WriteData(new Mod_Building.Building_Data { BuildingPrefabId = "Shaft_Wood" });
        item.BindData(new Data_GeneralItem { IDName = "probe:ship-part-placement" });
        item.itemData.ModuleDataDic[stored.StableName] = stored;
        T Create<T>(string name) where T : Module
        {
            var child = new GameObject(name);
            child.transform.SetParent(item.transform, false);
            T module = child.AddComponent<T>();
            module.item = item;
            return module;
        }
        Mod_Building building = Create<Mod_Building>("building");
        building.BuildingData = stored;
        building.Data.BuildingPrefabId = "probe:not-mechanical";
        Mod_MechanicalNode mechanical = Create<Mod_MechanicalNode>("mechanical");
        mechanical.Data.StableName = "mechanical";
        mechanical.DefinitionId = "Shaft_Wood";
        Mod_ShipPart part = Create<Mod_ShipPart>("ship-part");
        part.ModData.StableName = "ship-part";
        part.Configuration.Kind = ShipPieceKind.Wall;
        part.Configuration.Layer = 9;
        part.Configuration.BlocksMovement = true;
        item.itemMods.AddMod(building);
        item.itemMods.AddMod(mechanical);
        item.itemMods.AddMod(part);
        JObject Capture() => new()
        {
            ["layer"] = part.OccupancyLayer, ["blocks"] = part.BlocksMovement,
            ["speed"] = part.PlayerMoveSpeedMultiplier
        };
        var result = new JObject { ["liveOrdinary"] = Capture() };
        building.Data.BuildingPrefabId = "Shaft_Wood";
        result["liveMechanical"] = Capture();
        long before = GC.GetAllocatedBytesForCurrentThread();
        int layerSum = 0;
        for (int i = 0; i < 256; i++)
        {
            layerSum += part.OccupancyLayer;
            _ = part.BlocksMovement;
            _ = part.PlayerMoveSpeedMultiplier;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        result["allocated"] = allocated;
        result["layerSum"] = layerSum;
        building.Data.BuildingPrefabId = "probe:not-mechanical";
        result["liveChanged"] = Capture();
        building.Data.BuildingPrefabId = null;
        result["beforeBuildingLoad"] = Capture();
        item.itemMods.RemoveMod(building);
        result["storedFallback"] = Capture();
        item.itemMods.RemoveMod(mechanical);
        result["mechanicalRemoved"] = Capture();
        stored.BitData = "not-json";
        part.Configuration.Kind = ShipPieceKind.Floor;
        result["floor"] = Capture();
        return result;
    });

    #endregion

    #region 方块机身与双扇门表现采样
    private static JObject ServiceShipBlockVisual(JObject args)
    {
        Sprite sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, Texture2D.whiteTexture.width,
            Texture2D.whiteTexture.height), new Vector2(.5f, .5f), Texture2D.whiteTexture.width);
        try
        {
            var orientations = new JArray();
            for (int turns = 0; turns < 4; turns++)
            {
                var root = new GameObject("[LuaDoorSlideVisual]") { hideFlags = HideFlags.HideAndDontSave };
                var renderer = root.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                root.transform.position = new Vector3(3f, -4f, 0f);
                root.transform.rotation = Quaternion.Euler(0, 0, turns * 90);
                root.transform.localScale = new Vector3(2, 3, 1);
                DoorSlideVisual door = null;
                try
                {
                    var samples = new JArray();
                    void Capture(string name)
                    {
                        SpriteRenderer left = root.transform.GetChild(root.transform.childCount - 2).GetComponent<SpriteRenderer>();
                        SpriteRenderer right = root.transform.GetChild(root.transform.childCount - 1).GetComponent<SpriteRenderer>();
                        samples.Add(new JObject { ["name"] = name, ["progress"] = door.Progress,
                            ["sourceVisible"] = renderer.enabled, ["leftX"] = left.transform.localPosition.x,
                            ["rightX"] = right.transform.localPosition.x,
                            ["leftWorldX"] = left.transform.position.x, ["leftWorldY"] = left.transform.position.y,
                            ["rightWorldX"] = right.transform.position.x, ["rightWorldY"] = right.transform.position.y,
                            ["order"] = left.sortingOrder, ["green"] = left.color.g,
                            ["sourceScaleX"] = root.transform.localScale.x, ["sourceScaleY"] = root.transform.localScale.y,
                            ["sourceRotation"] = root.transform.eulerAngles.z });
                    }
                    // 四向都推进正式门动画，世界坐标交由 Lua 检查滑动轴和反转。
                    door = new DoorSlideVisual(renderer, false);
                    Capture("closed");
                    door.SetOpen(true); door.Advance(DoorSlideVisual.SlideSeconds * .5f); Capture("opening");
                    door.SetOpen(false); door.Advance(DoorSlideVisual.SlideSeconds * .25f); Capture("reversing");
                    door.Advance(DoorSlideVisual.SlideSeconds); Capture("closed_again");
                    door.SetOpen(true); door.Advance(DoorSlideVisual.SlideSeconds); Capture("open");
                    renderer.sortingOrder = 37; renderer.color = new Color(.2f, .4f, .6f, 1f);
                    renderer.enabled = true; door.Advance(0); Capture("appearance");
                    door.Dispose(); door = null;
                    bool restored = renderer.enabled;
                    door = new DoorSlideVisual(renderer, true); Capture("restored_open");
                    door.Dispose(); door = null;
                    int activeLeaves = 0;
                    foreach (Transform child in root.transform) if (child.gameObject.activeSelf) activeLeaves++;
                    orientations.Add(new JObject { ["quarterTurns"] = turns, ["samples"] = samples,
                        ["sourceRestored"] = restored && renderer.enabled,
                        ["activeLeavesAfterRelease"] = activeLeaves });
                }
                finally
                {
                    door?.Dispose();
                    if (Application.isPlaying) UnityEngine.Object.Destroy(root);
                    else UnityEngine.Object.DestroyImmediate(root);
                }
            }
            var fluid = new FluidMachineDefinition { Kind = "receiver", BodySizeCells = 1f };
            var invalid = new JArray();
            foreach (float value in new[] { 0f, -1f, float.NaN, float.PositiveInfinity })
            {
                fluid.BodySizeCells = value;
                try { fluid.Validate("probe:body-size"); invalid.Add(false); }
                catch (ArgumentException) { invalid.Add(true); }
            }
            var defaults = new JArray();
            foreach (var sample in new[] { ("receiver", 0), ("receiver", 2), ("mechanical-probe", 4) })
                defaults.Add(new FluidMachineDefinition { Kind = sample.Item1 }.ResolveBodySizeCells(sample.Item2));
            return new JObject { ["orientations"] = orientations, ["invalidRejected"] = invalid,
                ["defaultBodySizes"] = defaults,
                ["engineSize"] = MachineCatalog.Get("ShipChemicalEngine").Fluid.ResolveBodySizeCells(0),
                ["gmEngineSize"] = MachineCatalog.Get("ShipChemicalEngine_GM").Fluid.ResolveBodySizeCells(0) };
        }
        finally
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(sprite);
            else UnityEngine.Object.DestroyImmediate(sprite);
        }
    }
    #endregion
}
