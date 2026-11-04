using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class GameplayTestSystemProbe
{
    #region 视觉插值规则采样

    private static JObject CaptureVisualMotionInterpolation(JObject args)
    {
        var history = new WorldVisualMotionHistory();
        history.Reset(Vector3.zero, 0d);
        history.Capture(Vector3.right, 0.05d);

        JArray CaptureDisplayFrames(int fps)
        {
            var samples = new JArray();
            for (int i = 0; i <= fps / 20; i++)
                samples.Add(history.Evaluate(0.05d + (double)i / fps).x);
            return samples;
        }

        JArray CaptureTierDisplayFrames(int tps, int fps)
        {
            var motion = new WorldVisualMotionHistory();
            motion.Reset(Vector3.zero, 0d);
            motion.Capture(Vector3.right, 1d / tps);
            var samples = new JArray();
            for (int i = 0; i <= fps / tps; i++)
                samples.Add(motion.Evaluate(1d / tps + (double)i / fps).x);
            return samples;
        }

        JArray at60Fps = CaptureDisplayFrames(60);
        JArray at120Fps = CaptureDisplayFrames(120);
        JArray at240Fps = CaptureDisplayFrames(240);
        float frozen = history.Evaluate(0.075d).x;
        bool pausedUnchanged = history.Evaluate(0.075d).x == frozen;

        history.Capture(new Vector3(2f, 0f, 0f), 0.1d);
        float nextTickMid = history.Evaluate(0.125d).x;
        history.Capture(new Vector3(20f, 0f, 0f), 0.15d);
        float teleported = history.Evaluate(0.15d).x;
        history.Reset(new Vector3(-3f, 0f, 0f), 0.2d);
        float reused = history.Evaluate(0.225d).x;
        var idleResume = new WorldVisualMotionHistory();
        idleResume.Reset(Vector3.zero, 0d);
        idleResume.Capture(Vector3.zero, 0.05d);
        idleResume.Capture(Vector3.zero, 0.1d);
        idleResume.Capture(Vector3.right, 0.15d);
        float idleResumeMid = idleResume.Evaluate(0.175d).x;

        var wrapped = new WorldVisualMotionHistory();
        var domain = new WorldTopologyDomain(new int2(0, 0), new int2(100, 100), true);
        wrapped.Reset(new Vector3(99f, 5f, 0f), 1d);
        wrapped.CaptureWrapped(new Vector2(0f, 5f), domain, 1.05d);
        float seamMid = wrapped.Evaluate(1.075d).x;

        bool sourceUnchanged = false, projectionTranslated = false, projectionRestored = false;
        var poses = new WorldVisualPoseHistory();
        poses.Reset(new WorldVisualPose(Vector3.zero, Quaternion.identity, Vector3.one), 0d);
        poses.Capture(new WorldVisualPose(Vector3.right, Quaternion.Euler(0f, 0f, 90f), Vector3.one * 2f), .1d);
        WorldVisualPose middlePose = poses.Evaluate(.15d);
        for (int i = 0; i < 100; i++) poses.Evaluate(.15d);
        bool pureReads = poses.SampledAt == .1d && poses.Current.Position == Vector3.right &&
            poses.Evaluate(.15d).Matches(middlePose);
        WorldVisualPose external = new(new Vector3(12f, 0f, 0f), Quaternion.identity, Vector3.one);
        bool externalPosePreserved = poses.Evaluate(external, .15d).Matches(external);
        poses.Capture(new WorldVisualPose(new Vector3(2f, 0f, 0f), Quaternion.identity, Vector3.one), .2d);
        poses.Capture(new WorldVisualPose(new Vector3(3f, 0f, 0f), Quaternion.identity, Vector3.one), .3d);
        float catchUpPosition = poses.Evaluate(.35d).Position.x;
        poses.Reset(external, .4d);
        bool snapPosePreserved = poses.Evaluate(.45d).Matches(external);
        var clock = new GameplaySimulationFrameClock();
        int sampleTicks = 0;
        clock.AdvanceFrame(.02f, 1f, 30, _ => sampleTicks++);
        double presentationBeforeRateChange = clock.PresentationTime;
        clock.AdvanceFrame(0f, 1f, 120, _ => sampleTicks++);
        bool rateChangeKeepsRemainder = clock.PresentationTime == presentationBeforeRateChange && sampleTicks == 0;
        clock.AdvanceFrame(.001f, 1f, 120, _ => sampleTicks++);
        bool rateChangeAdvances = sampleTicks == 2 && clock.PresentationTime >= presentationBeforeRateChange;
        bool childPoseInterpolated = false, childSourceUnchanged = false, rootRotationApplied = false;
        bool heldPositionFollowsParent = false, heldRotationStillInterpolated = false, heldSourceUnchanged = false;
        bool emptyAttachmentOmitted = false, lateHeldItemProjected = false, replacedHeldItemProjected = false,
            removedHeldItemRestored = false;
        double oldSimulationTime = FlatWorld.Simulation.GameplaySimulationTime.CurrentTime;
        double oldPresentationTime = FlatWorld.Simulation.GameplaySimulationTime.PresentationTime;
        Scene scene = default;
        WorldPresentationProjection projection = null;
        try
        {
            scene = CreateGameplayTestProbeScene("[LuaVisualInterpolation]");
            var source = new GameObject("SourceVisual") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(source, scene);
            source.transform.position = new Vector3(7f, 3f, 0f);
            source.AddComponent<SpriteRenderer>();
            GameItem owner = source.AddComponent<GameItem>();
            owner.BindData(EntityProbeData("test:visual_owner", 1f));
            var child = new GameObject("LogicalActionPose");
            child.transform.SetParent(source.transform, false);
            child.AddComponent<SpriteRenderer>();
            var attachment = new GameObject("LateHandAnchor").transform;
            attachment.SetParent(source.transform, false);
            projection = WorldPresentationProjection.Create(source.transform, visualHierarchyOnly: true);
            projection.Synchronize(Matrix4x4.identity, true);
            emptyAttachmentOmitted = !projection.TryGetProjectedTransform(attachment, out _);

            // 首次投影后才在空挂点装入手持物，复现未被层级监听覆盖的子树。
            var held = new GameObject("HeldFramePose");
            held.transform.SetParent(attachment, false);
            GameItem heldItem = held.AddComponent<GameItem>();
            heldItem.BindData(EntityProbeData("test:visual_held", 1f));
            heldItem.Owner = owner;
            heldItem.SetInHand(true);
            SpriteRenderer heldRenderer = held.AddComponent<SpriteRenderer>();
            heldItem.NotifyRuntimeStructureChanged();
            projection.Synchronize(Matrix4x4.identity, true);
            lateHeldItemProjected = projection.TryGetProjectedTransform(held.transform, out _) &&
                WorldPresentationProjection.IsSourceProjected(heldRenderer) && heldRenderer.forceRenderingOff;
            projection.CaptureSimulationPoses(0d);
            child.transform.localPosition = Vector3.right;
            child.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            held.transform.localPosition = Vector3.right;
            held.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            source.transform.position += Vector3.right * .6f;
            projection.CaptureSimulationPoses(.1d);
            FlatWorld.Simulation.GameplaySimulationTime.Publish(.1d, .15d);
            projection.Synchronize(Matrix4x4.identity, true);
            Transform childView = projection.transform.Find("LogicalActionPose");
            projection.TryGetProjectedTransform(held.transform, out Transform heldView);
            childPoseInterpolated = childView != null && Mathf.Abs(childView.localPosition.x - .5f) < .0001f &&
                Quaternion.Angle(childView.localRotation, Quaternion.Euler(0f, 0f, 45f)) < .001f;
            // 乘员根随车平移时，手持物的逐帧挂点位置不能再落回前一逻辑步。
            heldPositionFollowsParent = heldView != null && Mathf.Abs(heldView.localPosition.x - 1f) < .0001f &&
                Mathf.Abs(heldView.position.x - source.transform.position.x - 1f) < .0001f;
            heldRotationStillInterpolated = heldView != null &&
                Quaternion.Angle(heldView.localRotation, Quaternion.Euler(0f, 0f, 45f)) < .001f;
            heldSourceUnchanged = held.transform.localPosition == Vector3.right &&
                Quaternion.Angle(held.transform.localRotation, Quaternion.Euler(0f, 0f, 90f)) < .001f;
            childSourceUnchanged = child.transform.localPosition == Vector3.right &&
                Quaternion.Angle(child.transform.localRotation, Quaternion.Euler(0f, 0f, 90f)) < .001f;
            Vector3 original = projection.transform.position;
            projection.ApplyVisualMotionTranslation(new Vector3(-0.5f, 0.25f, 0f));
            sourceUnchanged = Vector3.Distance(source.transform.position, new Vector3(7.6f, 3f, 0f)) < .0001f;
            projectionTranslated = projection.transform.position == original + new Vector3(-0.5f, 0.25f, 0f);
            projection.Synchronize(Matrix4x4.identity, true);
            projectionRestored = projection.transform.position == original;
            projection.ApplyVisualMotionTransform(Matrix4x4.Translate(original) *
                Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, 45f)) * Matrix4x4.Translate(-original));
            rootRotationApplied = Quaternion.Angle(projection.transform.rotation, Quaternion.Euler(0f, 0f, 45f)) < .001f &&
                source.transform.rotation == Quaternion.identity;

            held.transform.SetParent(null, true);
            var replacement = new GameObject("ReplacementHeldItem");
            replacement.transform.SetParent(attachment, false);
            GameItem replacementItem = replacement.AddComponent<GameItem>();
            replacementItem.BindData(EntityProbeData("test:replacement_held", 1f));
            replacementItem.Owner = owner;
            replacementItem.SetInHand(true);
            SpriteRenderer replacementRenderer = replacement.AddComponent<SpriteRenderer>();
            replacementItem.NotifyRuntimeStructureChanged();
            projection.Synchronize(Matrix4x4.identity, true);
            replacedHeldItemProjected = projection.TryGetProjectedTransform(replacement.transform, out _) &&
                replacementRenderer.forceRenderingOff && !heldRenderer.forceRenderingOff &&
                !WorldPresentationProjection.IsSourceProjected(heldRenderer);
            replacement.transform.SetParent(null, true);
            projection.Synchronize(Matrix4x4.identity, true);
            removedHeldItemRestored = !projection.TryGetProjectedTransform(attachment, out _) &&
                !replacementRenderer.forceRenderingOff && !WorldPresentationProjection.IsSourceProjected(replacementRenderer);
        }
        finally
        {
            projection?.Release();
            FlatWorld.Simulation.GameplaySimulationTime.Publish(oldSimulationTime, oldPresentationTime);
            if (scene.IsValid()) CloseGameplayTestProbeScene(scene);
        }

        return new JObject
        {
            ["fps60"] = at60Fps,
            ["fps120"] = at120Fps,
            ["fps240"] = at240Fps,
            ["tps30Fps60"] = CaptureTierDisplayFrames(30, 60),
            ["tps30Fps120"] = CaptureTierDisplayFrames(30, 120),
            ["tps120Fps240"] = CaptureTierDisplayFrames(120, 240),
            ["nextTickMid"] = nextTickMid,
            ["pausedUnchanged"] = pausedUnchanged,
            ["teleported"] = teleported,
            ["reused"] = reused,
            ["idleResumeMid"] = idleResumeMid,
            ["seamMid"] = seamMid,
            ["pixelAligned"] = WorldVisualMotionHistory.PixelAlignTranslation(new Vector3(0.12f, 0f), 32f).x,
            ["sourceUnchanged"] = sourceUnchanged,
            ["projectionTranslated"] = projectionTranslated,
            ["projectionRestored"] = projectionRestored
            , ["poseMidPosition"] = middlePose.Position.x
            , ["poseMidRotation"] = middlePose.Rotation.eulerAngles.z
            , ["poseMidScale"] = middlePose.Scale.x
            , ["pureReads"] = pureReads
            , ["externalPosePreserved"] = externalPosePreserved
            , ["catchUpPosition"] = catchUpPosition
            , ["snapPosePreserved"] = snapPosePreserved
            , ["rateChangeKeepsRemainder"] = rateChangeKeepsRemainder
            , ["rateChangeAdvances"] = rateChangeAdvances
            , ["childPoseInterpolated"] = childPoseInterpolated
            , ["childSourceUnchanged"] = childSourceUnchanged
            , ["rootRotationApplied"] = rootRotationApplied
            , ["heldPositionFollowsParent"] = heldPositionFollowsParent
            , ["heldRotationStillInterpolated"] = heldRotationStillInterpolated
            , ["heldSourceUnchanged"] = heldSourceUnchanged
            , ["emptyAttachmentOmitted"] = emptyAttachmentOmitted
            , ["lateHeldItemProjected"] = lateHeldItemProjected
            , ["replacedHeldItemProjected"] = replacedHeldItemProjected
            , ["removedHeldItemRestored"] = removedHeldItemRestored
        };
    }

    #endregion
}
