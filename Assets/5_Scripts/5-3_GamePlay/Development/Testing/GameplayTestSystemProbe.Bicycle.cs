using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 自行车与木筏的共用运动规则
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterBicycleCarrierRules()
        => Register("player", "bicycle_carrier", CaptureBicycleCarrierRules);

    // 采样当前定义和正式移动入口，不在夹具中复制限速或惯性算法。
    private static JObject CaptureBicycleCarrierRules(JObject args)
    {
        var definitions = ItemDefinitionCatalogLoader.LoadBuiltInDefinitions().ToDictionary(value => value.Id);
        var root = new GameObject("[BicycleCarrierRules]") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        try
        {
            var samples = new JObject();
            foreach (string id in new[] { "Bicycle_Building", "Boat_Building" })
            {
                Mod_Carrier carrier = root.AddComponent<Mod_Carrier>();
                var definition = definitions[id].Modules["carrier"];
                ModuleJsonConfigurator.Apply(carrier, id, "carrier", definition.Id, definition.Parameters.ToString());
                Vector2 settled = Vector2.zero;
                for (int i = 0; i < 100; i++)
                    settled = carrier.SmoothDrivenVelocity(settled, Vector2.one, false, false, .02f);
                Vector2 coast = carrier.SmoothDrivenVelocity(settled, Vector2.zero, false, false, .02f);
                Vector2 water = settled;
                for (int i = 0; i < 100; i++)
                    water = carrier.SmoothDrivenVelocity(water, Vector2.right, true, false, .02f);
                Vector2 stopped = coast;
                for (int i = 0; i < 100; i++)
                    stopped = carrier.SmoothDrivenVelocity(stopped, Vector2.zero, false, false, .02f);
                samples[id] = new JObject
                {
                    ["land"] = carrier.ResolveSurfaceSpeedLimit(false), ["water"] = carrier.ResolveSurfaceSpeedLimit(true),
                    ["boostLand"] = carrier.ResolveSurfaceSpeedLimit(false, true), ["boostWater"] = carrier.ResolveSurfaceSpeedLimit(true, true),
                    ["start"] = carrier.SmoothDrivenVelocity(Vector2.zero, Vector2.right, false, false, .02f).magnitude,
                    ["diagonal"] = settled.magnitude, ["coast"] = coast.magnitude, ["stopped"] = stopped.magnitude,
                    ["settledWater"] = water.magnitude,
                    ["zeroTime"] = carrier.SmoothDrivenVelocity(settled, Vector2.zero, false, false, 0f).magnitude,
                    ["landPush"] = WorldMotionSystem.CalculatePushVelocity(Vector2.right * 6f, false, carrier.LandPushSpeedMultiplier, carrier.WaterPushSpeedMultiplier).x,
                    ["waterPush"] = WorldMotionSystem.CalculatePushVelocity(Vector2.right * 6f, true, carrier.LandPushSpeedMultiplier, carrier.WaterPushSpeedMultiplier).x,
                    ["wake"] = carrier.EmitWaterWake, ["rotateFrame"] = carrier.RotateVisualWithHeading
                };
            }
            SpriteRenderer frame = new GameObject("Frame").AddComponent<SpriteRenderer>();
            frame.transform.SetParent(root.transform, false);
            var wheels = root.AddComponent<CarrierWheelPresentation>();
            wheels.FrameRenderer = frame;
            wheels.RearWheel = new GameObject("RearWheel").transform;
            wheels.FrontWheel = new GameObject("FrontWheel").transform;
            wheels.RearWheel.SetParent(frame.transform, false);
            wheels.FrontWheel.SetParent(frame.transform, false);
            wheels.RearWheel.localPosition = new Vector3(-.7f, -.3f);
            wheels.FrontWheel.localPosition = new Vector3(.7f, -.3f);
            wheels.ResetWheelPresentationMotionBaseline();
            wheels.AdvanceWheelRotationByDisplacement(Vector2.right * .1f);
            float rightAngle = wheels.WheelAngle;
            wheels.AdvanceWheelRotationByDisplacement(Vector2.zero);
            bool still = wheels.WheelAngle == rightAngle;
            wheels.AdvanceWheelRotationByDisplacement(Vector2.left * .1f);
            samples["wheels"] = new JObject
            {
                ["rightAngle"] = rightAngle, ["still"] = still, ["mirrored"] = frame.flipX,
                ["rearMirrored"] = wheels.RearWheel.localPosition.x > 0f,
                ["sameRotation"] = wheels.RearWheel.localRotation == wheels.FrontWheel.localRotation,
                ["frameAngle"] = frame.transform.localEulerAngles.z,
                ["fullTurn"] = CarrierWheelPresentation.CalculateWheelRotationDegrees(Mathf.PI * 2f * wheels.WheelRadius, wheels.WheelRadius, false)
            };
            string path = StreamingAssetsTextLoader.CombinePath(Application.streamingAssetsPath, "GameConfig/Recipes/crafting/vehicles.json");
            JObject recipe = JObject.Parse(StreamingAssetsTextLoader.ReadAllText(path))["recipes"]
                .OfType<JObject>().Single(value => value.Value<string>("id") == "core:bicycle");
            samples["recipe"] = recipe;
            Mod_Building.Building_Data state = new();
            definitions["Bicycle"].Modules[ModText.Building].Data.ToObject<Ex_ModData>().ReadData(ref state);
            samples["summonerBody"] = state.BuildingPrefabId;
#if UNITY_EDITOR
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Gameplay/Items/Common/BicycleBodyShell.prefab");
            CarrierWheelPresentation presentation = prefab != null ? prefab.GetComponent<CarrierWheelPresentation>() : null;
            samples["prefabReady"] = presentation != null && presentation.FrameSprite != null &&
                presentation.FrameRenderer != null && presentation.RearWheel != null && presentation.FrontWheel != null &&
                prefab.GetComponent<UnityEngine.Rendering.SortingGroup>() != null && prefab.GetComponent<Rigidbody2D>() != null;
#endif
            return samples;
        }
        finally { Object.DestroyImmediate(root); }
    }
    #endregion
}
