using System;
using System.Globalization;
using Cinemachine;
using FlatWorld.Spaceflight;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>独立姿态样本只调用正式表现插值，断言由 Lua 负责。</summary>
public static class GameplayTestSpacePresentationProbe
{
    #region 飞船表现插值采样

    public static JObject Capture()
    {
        var previous = Pose(0, 0, 10d, 20d, 0d);
        var current = Pose(0, 0, 14d, 28d, Math.PI * .5d);
        var interpolation = new JArray
        {
            Sample("previous", SpacePresentationPose.Interpolate(previous, current, 0d)),
            Sample("current", SpacePresentationPose.Interpolate(previous, current, 1d)),
            Sample("middle", SpacePresentationPose.Interpolate(previous, current, .5d)),
            Sample("clamp-low", SpacePresentationPose.Interpolate(previous, current, -1d)),
            Sample("clamp-high", SpacePresentationPose.Interpolate(previous, current, 2d)),
            Sample("nan", SpacePresentationPose.Interpolate(previous, current, double.NaN)),
            Sample("negative-infinity", SpacePresentationPose.Interpolate(previous, current, double.NegativeInfinity)),
            Sample("positive-infinity", SpacePresentationPose.Interpolate(previous, current, double.PositiveInfinity))
        };
        var wrappedPrevious = Pose(0, 0, 0d, 0d, Math.PI - .1d);
        var wrappedCurrent = Pose(0, 0, 0d, 0d, -Math.PI + .1d);
        var wrapped = SpacePresentationPose.Interpolate(wrappedPrevious, wrappedCurrent, .5d);
        var reverseWrapped = SpacePresentationPose.Interpolate(wrappedCurrent, wrappedPrevious, .5d);
        var largePrevious = Pose(long.MaxValue - 2, long.MinValue + 2, 127999.75d, -10d, 0d);
        var largeCurrent = Pose(long.MaxValue - 1, long.MinValue + 2, -127999.75d, -6d, 0d);
        var sectorCrossing = new JArray
        {
            Sample("quarter", SpacePresentationPose.Interpolate(largePrevious, largeCurrent, .25d)),
            Sample("middle", SpacePresentationPose.Interpolate(largePrevious, largeCurrent, .5d)),
            Sample("three-quarters", SpacePresentationPose.Interpolate(largePrevious, largeCurrent, .75d))
        };

        // 两份权威状态只移动质量中心，船格原点保持在同一个真实位置。
        var ship = new ShipState { PositionX = 10d, PositionY = 20d, AngleRadians = Math.PI * .5d,
            CenterOfMassLocalX = 2d, CenterOfMassLocalY = 3d };
        var beforeMassShift = SpacePresentationPose.FromShip(ship);
        ship.CenterOfMassLocalX = 4d;
        ship.CenterOfMassLocalY = 5d;
        ship.PositionX = 8d;
        ship.PositionY = 22d;
        var afterMassShift = SpacePresentationPose.FromShip(ship);

        var shared = SpacePresentationPose.Interpolate(Pose(0, 0, 10d, 20d, Math.PI * .5d),
            Pose(0, 0, 14d, 28d, Math.PI * .5d), .5d);
        var track = new SpacePresentationTrack();
        track.Reset(previous);
        var initialTrack = Sample("initial", track.Evaluate(.5d));
        track.Advance(current);
        var advancedPrevious = Sample("previous", track.Previous);
        var advancedCurrent = Sample("current", track.Current);
        var advancedMiddle = Sample("middle", track.Evaluate(.5d));
        track.Advance(Pose(0, 0, 18d, 36d, Math.PI));
        var secondMiddle = Sample("second-middle", track.Evaluate(.5d));
        track.Reset(Pose(0, 0, 1000d, -500d, .3d));

        return new JObject
        {
            ["operation"] = "presentation_interpolation", ["scope"] = "production-domain",
            ["interpolation"] = interpolation,
            ["wrappedRotation"] = new JObject
            {
                ["forward"] = Sample("forward", wrapped), ["reverse"] = Sample("reverse", reverseWrapped),
                ["forwardPoint"] = Position(wrapped.TransformPoint(1d, 0d)),
                ["reversePoint"] = Position(reverseWrapped.TransformPoint(1d, 0d))
            },
            ["sectorCrossing"] = sectorCrossing,
            ["massShift"] = new JObject
            {
                ["before"] = Sample("before", beforeMassShift), ["after"] = Sample("after", afterMassShift),
                ["pieceBefore"] = Position(beforeMassShift.TransformPoint(6d, 7d)),
                ["pieceAfter"] = Position(afterMassShift.TransformPoint(6d, 7d))
            },
            ["sharedTransform"] = new JObject
            {
                ["pose"] = Sample("shared", shared), ["piece"] = Position(shared.TransformPoint(6d, 7d)),
                ["passenger"] = Position(shared.TransformPoint(2d, 4d)),
                ["cameraCenter"] = Position(shared.TransformPoint(3d, 5d))
            },
            ["track"] = new JObject
            {
                ["initial"] = initialTrack, ["advancedPrevious"] = advancedPrevious,
                ["advancedCurrent"] = advancedCurrent, ["advancedMiddle"] = advancedMiddle,
                ["secondMiddle"] = secondMiddle,
                ["resetPrevious"] = Sample("reset-previous", track.Previous),
                ["resetCurrent"] = Sample("reset-current", track.Current),
                ["resetLow"] = Sample("reset-low", track.Evaluate(0d)),
                ["resetMiddle"] = Sample("reset-middle", track.Evaluate(.5d)),
                ["resetHigh"] = Sample("reset-high", track.Evaluate(1d))
            },
            ["limitations"] = "只采样正式双精度插值与共同坐标变换；不证明实际船件、玩家和镜头已接入，也不代表运行画面的平滑度。"
        };
    }

    private static SpacePresentationPose Pose(long sectorX, long sectorY, double x, double y, double angle) =>
        new(new SpaceSectorPosition(new SpaceSectorId(sectorX, sectorY), new SpaceVector2(x, y)), angle);

    private static JObject Position(SpaceSectorPosition value) => new()
    {
        ["sector"] = new JObject
        {
            ["x"] = value.Sector.X.ToString(CultureInfo.InvariantCulture),
            ["y"] = value.Sector.Y.ToString(CultureInfo.InvariantCulture)
        },
        ["local"] = new JObject { ["x"] = value.LocalMeters.X, ["y"] = value.LocalMeters.Y }
    };

    private static JObject Sample(string name, SpacePresentationPose value) => new()
    {
        ["name"] = name, ["position"] = Position(value.Position), ["angle"] = value.AngleRadians
    };

    #endregion

    #region 乘员整体高度与碰撞恢复采样

    public static JObject CapturePassengerPose(Player player)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        var root = new GameObject("乘员高度独立夹具");
        try
        {
            var body = root.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            var solid = root.AddComponent<CircleCollider2D>();
            var hand = new GameObject("手持物");
            hand.transform.SetParent(root.transform, false);
            hand.transform.localPosition = new Vector3(1f, 2f);
            var childSolid = hand.AddComponent<BoxCollider2D>();
            var sensor = hand.AddComponent<CircleCollider2D>();
            sensor.isTrigger = true;
            var independent = new GameObject("独立刚体");
            independent.transform.SetParent(root.transform, false);
            independent.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            var independentCollider = independent.AddComponent<CircleCollider2D>();
            Collider2D late = null;
            var samples = new JArray();
            Pose("ground", 0f);
            Pose("lift", 4f);
            Pose("repeat", 4f);
            Pose("descending", 1f);
            Pose("landed", 0f);
            Pose("lift-again", 4f);
            ShipSurfacePresentation.ClearPassenger(root);
            Sample("clear");
            ShipSurfacePresentation.ClearPassenger(root);
            Sample("repeat-clear");
            ShipSurfacePresentation.SetPassengerPose(root, body, new Vector2(10f, 20f), 4f);
            late = hand.AddComponent<CapsuleCollider2D>();
            Pose("late-collider", 4f);
            root.SetActive(false);
            Sample("disabled");
            return new JObject { ["scope"] = "production-components", ["samples"] = samples,
                ["limitations"] = "同步调用正式乘员位姿组件，检查根节点、刚体、手持物与碰撞恢复；不推进物理场景，不代表树木接触、完整起降或双客户端验收。" };

            void Pose(string name, float rise)
            {
                ShipSurfacePresentation.SetPassengerPose(root, body, new Vector2(10f, 20f), rise);
                Sample(name);
            }
            void Sample(string name)
            {
                Vector3 ground = ShipSurfacePresentation.GroundPosition(root, root.transform.position);
                samples.Add(new JObject { ["name"] = name,
                    ["rootX"] = root.transform.position.x, ["rootY"] = root.transform.position.y,
                    ["bodyY"] = body.position.y, ["groundY"] = ground.y,
                    ["handY"] = hand.transform.position.y, ["handLocalY"] = hand.transform.localPosition.y,
                    ["solidTrigger"] = solid.isTrigger, ["childTrigger"] = childSolid.isTrigger,
                    ["sensorTrigger"] = sensor.isTrigger, ["independentTrigger"] = independentCollider.isTrigger,
                    ["solidEnabled"] = solid.enabled, ["lateTrigger"] = late != null && late.isTrigger });
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    #endregion

    #region 太空驾驶镜头租约采样

    public static JObject CaptureCameraOverride(Player player)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        string[] keys = { "FlatWorld.Camera.Lookahead", "FlatWorld.Camera.LookaheadSmoothing", "FlatWorld.Camera.LookaheadZoomInfluence" };
        bool[] existed = { PlayerPrefs.HasKey(keys[0]), PlayerPrefs.HasKey(keys[1]), PlayerPrefs.HasKey(keys[2]) };
        float[] preferences = { CameraUserSettings.Lookahead, CameraUserSettings.LookaheadSmoothing, CameraUserSettings.LookaheadZoomInfluence };
        float[] storedPreferences = { PlayerPrefs.GetFloat(keys[0]), PlayerPrefs.GetFloat(keys[1]), PlayerPrefs.GetFloat(keys[2]) };
        var root = new GameObject("太空驾驶镜头租约测试夹具");
        root.SetActive(false);
        Mod_Cam module = null;
        Transform first = null, second = null;
        try
        {
            // 独立且禁用输出的组件只采正式设置契约，不接管隔离角色的实际镜头。
            Camera camera = root.AddComponent<Camera>();
            camera.enabled = false;
            CinemachineBrain brain = root.AddComponent<CinemachineBrain>();
            brain.enabled = false;
            brain.m_UpdateMethod = CinemachineBrain.UpdateMethod.SmartUpdate;
            brain.m_BlendUpdateMethod = CinemachineBrain.BrainUpdateMethod.FixedUpdate;
            var virtualRoot = new GameObject("独立虚拟镜头");
            virtualRoot.transform.SetParent(root.transform, false);
            CinemachineVirtualCamera virtualCamera = virtualRoot.AddComponent<CinemachineVirtualCamera>();
            virtualCamera.enabled = false;
            CinemachineFramingTransposer framing = virtualCamera.AddCinemachineComponent<CinemachineFramingTransposer>();
            framing.m_XDamping = .12f;
            framing.m_YDamping = .24f;
            module = root.AddComponent<Mod_Cam>();
            module.vcam = virtualCamera;
            module.ControllerCamera = camera;
            first = new GameObject("驾驶目标一").transform;
            first.SetParent(root.transform, false);
            second = new GameObject("驾驶目标二").transform;
            second.SetParent(root.transform, false);
            root.SetActive(true);

            CameraUserSettings.SetLookahead(-.4f);
            CameraUserSettings.SetLookaheadSmoothing(2f);
            CameraUserSettings.SetLookaheadZoomInfluence(0f);
            module.SetOrthographicSize(10f);
            JObject baseline = CameraOverrideSample(module, framing, brain, null);
            module.SetFollowOverride(first, 120000f, 1.25f, Mod_Cam.FollowMode.PresentationLocked);
            module.SetOrthographicSize(30f);
            JObject locked = CameraOverrideSample(module, framing, brain, first);
            for (int index = 0; index < 3; index++)
                module.SetFollowOverride(first, 120000f, 1.25f, Mod_Cam.FollowMode.PresentationLocked);
            JObject repeated = CameraOverrideSample(module, framing, brain, first);

            CameraUserSettings.SetLookahead(.6f);
            CameraUserSettings.SetLookaheadSmoothing(3.4f);
            CameraUserSettings.SetLookaheadZoomInfluence(.5f);
            JObject changedSettings = CameraOverrideSample(module, framing, brain, first);
            module.SetOrthographicSize(240f);
            module.ChangeCameraView(-2f);
            JObject zoomed = CameraOverrideSample(module, framing, brain, first);
            module.ClearFollowOverride(second);
            JObject wrongRelease = CameraOverrideSample(module, framing, brain, first);

            module.SetFollowOverride(first, 120000f, 1.25f);
            JObject playerMode = CameraOverrideSample(module, framing, brain, first);
            module.SetFollowOverride(first, 120000f, 1.25f, Mod_Cam.FollowMode.PresentationLocked);
            JObject relocked = CameraOverrideSample(module, framing, brain, first);
            module.SetFollowOverride(second, 120000f, 1.25f, Mod_Cam.FollowMode.PresentationLocked);
            module.ClearFollowOverride(first);
            JObject staleRelease = CameraOverrideSample(module, framing, brain, second);
            module.ClearFollowOverride(second);
            JObject released = CameraOverrideSample(module, framing, brain, null);
            module.ClearFollowOverride(second);
            JObject repeatedRelease = CameraOverrideSample(module, framing, brain, null);

            return new JObject
            {
                ["scope"] = "isolated-camera-components", ["baseline"] = baseline, ["locked"] = locked,
                ["repeated"] = repeated, ["changedSettings"] = changedSettings, ["zoomed"] = zoomed,
                ["wrongRelease"] = wrongRelease, ["playerMode"] = playerMode, ["relocked"] = relocked,
                ["staleRelease"] = staleRelease, ["released"] = released, ["repeatedRelease"] = repeatedRelease,
                ["limitations"] = "仅检查正式镜头租约、设置通知、缩放与恢复；独立组件禁用输出，不证明实际高速航行画面的平滑度。"
            };
        }
        finally
        {
            if (module != null)
            {
                module.ClearFollowOverride(first);
                module.ClearFollowOverride(second);
            }
            root.SetActive(false);
            UnityEngine.Object.Destroy(root);
            // 正式 setter 恢复缓存与事件，原本不存在的偏好键同时恢复为不存在。
            CameraUserSettings.SetLookahead(preferences[0]);
            CameraUserSettings.SetLookaheadSmoothing(preferences[1]);
            CameraUserSettings.SetLookaheadZoomInfluence(preferences[2]);
            for (int index = 0; index < keys.Length; index++)
            {
                if (existed[index]) PlayerPrefs.SetFloat(keys[index], storedPreferences[index]);
                else PlayerPrefs.DeleteKey(keys[index]);
            }
            PlayerPrefs.Save();
        }
    }

    private static JObject CameraOverrideSample(Mod_Cam module, CinemachineFramingTransposer framing,
        CinemachineBrain brain, Transform expectedTarget) => new()
    {
        ["hasOverride"] = module.HasFollowOverride, ["followsExpected"] = module.Vcam.Follow == expectedTarget,
        ["xDamping"] = framing.m_XDamping, ["yDamping"] = framing.m_YDamping,
        ["lookahead"] = framing.m_LookaheadTime, ["smoothing"] = framing.m_LookaheadSmoothing,
        ["updateMethod"] = brain.m_UpdateMethod.ToString(), ["blendUpdateMethod"] = brain.m_BlendUpdateMethod.ToString(),
        ["size"] = module.CurrentOrthographicSize, ["maximumSize"] = module.EffectiveMaxPovValue,
        ["proportionalZoom"] = module.UsesProportionalZoom,
        ["userLookahead"] = CameraUserSettings.Lookahead,
        ["userSmoothing"] = CameraUserSettings.LookaheadSmoothing,
        ["userZoomInfluence"] = CameraUserSettings.LookaheadZoomInfluence
    };

    #endregion
}
