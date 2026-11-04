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
                Vector2 rolling = settled;
                float rollingDistance = 0f;
                for (int i = 0; i < 50; i++)
                {
                    rolling = carrier.SmoothDrivenVelocity(rolling, Vector2.zero, false, false, .02f, true);
                    rollingDistance += rolling.magnitude * .02f;
                }
                samples[id] = new JObject
                {
                    ["land"] = carrier.ResolveSurfaceSpeedLimit(false), ["water"] = carrier.ResolveSurfaceSpeedLimit(true),
                    ["boostLand"] = carrier.ResolveSurfaceSpeedLimit(false, true), ["boostWater"] = carrier.ResolveSurfaceSpeedLimit(true, true),
                    ["start"] = carrier.SmoothDrivenVelocity(Vector2.zero, Vector2.right, false, false, .02f).magnitude,
                    ["diagonal"] = settled.magnitude, ["coast"] = coast.magnitude, ["stopped"] = stopped.magnitude,
                    ["freeRollingDeceleration"] = carrier.FreeRollingDeceleration,
                    ["freeRollingSpeedAfterOneSecond"] = rolling.magnitude, ["freeRollingDistanceOneSecond"] = rollingDistance,
                    ["settledWater"] = water.magnitude,
                    ["zeroTime"] = carrier.SmoothDrivenVelocity(settled, Vector2.zero, false, false, 0f).magnitude,
                    ["landPush"] = WorldMotionSystem.CalculatePushVelocity(Vector2.right * 6f, false, carrier.LandPushSpeedMultiplier, carrier.WaterPushSpeedMultiplier).x,
                    ["waterPush"] = WorldMotionSystem.CalculatePushVelocity(Vector2.right * 6f, true, carrier.LandPushSpeedMultiplier, carrier.WaterPushSpeedMultiplier).x,
                    ["wake"] = carrier.EmitWaterWake, ["rotateFrame"] = carrier.RotateVisualWithHeading,
                    ["riderExposed"] = carrier.AllowsRiderTerrainContact,
                    ["seatX"] = carrier.SeatOffset.x, ["seatY"] = carrier.SeatOffset.y
                };
            }
            Vector2 configuredSeat = new((float)samples["Bicycle_Building"]["seatX"],
                (float)samples["Bicycle_Building"]["seatY"]);
            SpriteRenderer frame = new GameObject("Frame").AddComponent<SpriteRenderer>();
            frame.transform.SetParent(root.transform, false);
            var wheels = root.AddComponent<CarrierWheelPresentation>();
            wheels.FrameRenderer = frame;
            wheels.RearWheel = new GameObject("RearWheel").transform;
            wheels.FrontWheel = new GameObject("FrontWheel").transform;
            wheels.PedalCrank = new GameObject("PedalCrank").transform;
            wheels.RearWheel.SetParent(frame.transform, false);
            wheels.FrontWheel.SetParent(frame.transform, false);
            wheels.PedalCrank.SetParent(frame.transform, false);
            wheels.RearWheel.localPosition = new Vector3(-.7f, -.3f);
            wheels.FrontWheel.localPosition = new Vector3(.7f, -.3f);
            wheels.PedalCrank.localPosition = new Vector3(-.09f, -.09f);
            wheels.ResetWheelPresentationMotionBaseline();
            Vector2 rightGroundAnchor = wheels.FacingGroundAnchorWorldPosition;
            wheels.AdvanceWheelRotationByDisplacement(Vector2.right * .1f);
            float rightAngle = wheels.WheelAngle;
            float rightPedal = wheels.PedalAngle;
            Quaternion rightPedalRotation = wheels.PedalCrank.localRotation;
            wheels.AdvanceWheelRotationByDisplacement(Vector2.zero);
            bool still = wheels.WheelAngle == rightAngle;
            bool pedalStill = wheels.PedalAngle == rightPedal && wheels.PedalCrank.localRotation == rightPedalRotation;
            wheels.SetFacingFromPointerDirection(Vector2.left);
            Vector2 leftGroundAnchor = wheels.FacingGroundAnchorWorldPosition;
            bool pointerLeftWhileStill = frame.flipX && wheels.WheelAngle == rightAngle;
            wheels.AdvanceWheelRotationByDisplacement(Vector2.right * .1f);
            bool movingRightKeepsLeft = frame.flipX;
            wheels.SetFacingFromPointerDirection(Vector2.right);
            bool pointerRightWhileStill = !frame.flipX;
            wheels.AdvanceWheelRotationByDisplacement(Vector2.left * .1f);
            bool movingLeftKeepsRight = !frame.flipX;
            wheels.SetFacingFromPointerDirection(Vector2.left);
            wheels.SetFacingFromPointerDirection(Vector2.zero);
            bool centeredPointerKeepsFacing = frame.flipX;
            float deadZone = wheels.FacingDeadZonePixels;
            samples["wheels"] = new JObject
            {
                ["rightAngle"] = rightAngle, ["still"] = still, ["mirrored"] = frame.flipX,
                ["pointerLeftWhileStill"] = pointerLeftWhileStill, ["pointerRightWhileStill"] = pointerRightWhileStill,
                ["movingRightKeepsLeft"] = movingRightKeepsLeft, ["movingLeftKeepsRight"] = movingLeftKeepsRight,
                ["rearMirrored"] = wheels.RearWheel.localPosition.x > 0f,
                ["pedalTurned"] = Mathf.Abs(Mathf.DeltaAngle(0f, rightPedal)) > .1f,
                ["pedalStill"] = pedalStill, ["pedalMirrored"] = wheels.PedalCrank.localPosition.x > 0f,
                ["seatRightX"] = configuredSeat.x, ["seatLeftX"] = wheels.ResolveSeatOffset(configuredSeat).x,
                ["seatLeftY"] = wheels.ResolveSeatOffset(configuredSeat).y,
                ["groundAnchorCentered"] = Mathf.Abs(rightGroundAnchor.x - root.transform.position.x) < .0001f &&
                    Mathf.Abs(leftGroundAnchor.x - rightGroundAnchor.x) < .0001f,
                ["groundAnchorAtWheelBottom"] = rightGroundAnchor.y < wheels.RearWheel.position.y &&
                    Mathf.Abs(leftGroundAnchor.y - rightGroundAnchor.y) < .0001f,
                ["centeredPointerKeepsFacing"] = centeredPointerKeepsFacing,
                ["deadZonePixels"] = deadZone,
                ["centerDeadZoneHolds"] = CarrierWheelPresentation.IsPointerInsideFacingDeadZone(500f, 600f, 500f, deadZone),
                ["anchorDeadZoneHolds"] = CarrierWheelPresentation.IsPointerInsideFacingDeadZone(620f, 600f, 500f, deadZone),
                ["outsideDeadZoneTurns"] = !CarrierWheelPresentation.IsPointerInsideFacingDeadZone(700f, 600f, 500f, deadZone),
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
            ItemShadowVisualDefinitionDto shadow = definitions["Bicycle_Building"].Visual?.Shadows;
            samples["groundShadow"] = new JObject
            {
                ["width"] = shadow?.ContactWidth ?? 0f,
                ["footX"] = shadow?.FootLocalPosition?.x ?? 999f,
                ["footY"] = shadow?.FootLocalPosition?.y ?? 999f,
                ["overlap"] = shadow?.FootOverlap ?? -1f,
                ["enabled"] = shadow?.Enabled != false
            };
#if UNITY_EDITOR
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/Gameplay/Items/Common/BicycleBodyShell.prefab");
            CarrierWheelPresentation presentation = prefab != null ? prefab.GetComponent<CarrierWheelPresentation>() : null;
            ItemPhysicsTemplate2D physics = prefab != null ? prefab.GetComponent<ItemPhysicsTemplate2D>() : null;
            samples["prefabReady"] = presentation != null && presentation.FrameSprite != null &&
                presentation.FrameRenderer != null && presentation.RearWheel != null && presentation.FrontWheel != null &&
                presentation.PedalCrank != null && presentation.PedalCrank.GetComponent<SpriteRenderer>()?.sprite != null &&
                prefab.GetComponent<UnityEngine.Rendering.SortingGroup>() != null && physics != null &&
                physics.hasBody && physics.bodyType == RigidbodyType2D.Kinematic &&
                prefab.GetComponent<Rigidbody2D>() == null;
#endif
            return samples;
        }
        finally { Object.DestroyImmediate(root); }
    }
    #endregion
}
