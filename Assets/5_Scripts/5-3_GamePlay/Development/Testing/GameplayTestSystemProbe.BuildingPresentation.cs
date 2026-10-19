using System;
using FlatWorld.Spaceflight;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
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
        var root = new GameObject("[LuaDoorSlideVisual]") { hideFlags = HideFlags.HideAndDontSave };
        var renderer = root.AddComponent<SpriteRenderer>();
        Sprite sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, Texture2D.whiteTexture.width,
            Texture2D.whiteTexture.height), new Vector2(.5f, .5f), Texture2D.whiteTexture.width);
        DoorSlideVisual door = null;
        try
        {
            renderer.sprite = sprite;
            root.transform.rotation = Quaternion.Euler(0, 0, 90);
            root.transform.localScale = new Vector3(2, 3, 1);
            var samples = new JArray();
            void Capture(string name)
            {
                SpriteRenderer left = root.transform.GetChild(root.transform.childCount - 2).GetComponent<SpriteRenderer>();
                SpriteRenderer right = root.transform.GetChild(root.transform.childCount - 1).GetComponent<SpriteRenderer>();
                samples.Add(new JObject { ["name"] = name, ["progress"] = door.Progress,
                    ["sourceVisible"] = renderer.enabled, ["leftX"] = left.transform.localPosition.x,
                    ["rightX"] = right.transform.localPosition.x,
                    ["order"] = left.sortingOrder, ["green"] = left.color.g,
                    ["sourceScaleX"] = root.transform.localScale.x,
                    ["sourceRotation"] = root.transform.eulerAngles.z });
            }
            // 只推进正式共用表现，采样交由 Lua 判断闭合、反转、恢复和清理。
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
            return new JObject { ["samples"] = samples, ["sourceRestored"] = restored,
                ["activeLeavesAfterRelease"] = activeLeaves, ["invalidRejected"] = invalid,
                ["defaultBodySizes"] = defaults,
                ["engineSize"] = MachineCatalog.Get("ShipChemicalEngine").Fluid.ResolveBodySizeCells(0),
                ["gmEngineSize"] = MachineCatalog.Get("ShipChemicalEngine_GM").Fluid.ResolveBodySizeCells(0) };
        }
        finally
        {
            door?.Dispose();
            if (Application.isPlaying) { UnityEngine.Object.Destroy(root); UnityEngine.Object.Destroy(sprite); }
            else { UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(sprite); }
        }
    }
    #endregion
}
