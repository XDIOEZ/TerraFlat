using System;
using System.Runtime.CompilerServices;
using FlatWorld.Networking;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>物品声明关节与零件，地面批绘和船上表现只消费同一套运动参数。</summary>
internal sealed class MachineArticulatedVisual
{
    #region 数据驱动零件装配
    internal const int SliderMode = 8, ConnectingRodMode = 9, NeedleMode = 10;
    internal sealed class PartDefinition
    {
        public string SpriteState, Motion;
        public Vector2 PivotPixels;
        public float Scale = 1f;
        public float SourceAngleRadians;
        public float SliderAxisPixelsX;
        public int Order;
    }
    private sealed class Declaration { public MachineArticulatedVisual Articulation; }
    public Vector2 CanvasPivotPixels;
    public Vector2 CrankCenterPixels;
    public float CrankRadiusPixels, ConnectingRodLengthPixels;
    public string WorkingFlowPhase;
    public float NeedleIdleDegrees = -130f, NeedleWorkingDegrees = 45f, NeedlePulseDegrees = 3f;
    public PartDefinition[] Parts;
    private static readonly ConditionalWeakTable<RuntimeItemDefinition, Declaration> definitions = new();
    private static readonly ConditionalWeakTable<MachineEntity, MachineArticulatedMotionClock> clocks = new();

    internal static MachineArticulatedVisual Resolve(RuntimeItemDefinition definition)
    {
        if (definition == null) return null;
        if (definitions.TryGetValue(definition, out Declaration cached)) return cached.Articulation;
        Declaration value = definition.TryGetModuleParameters(Mod_MechanicalNode.ModuleId, out string json)
            ? JsonConvert.DeserializeObject<Declaration>(json) ?? new Declaration() : new Declaration();
        MachineArticulatedVisual rig = value.Articulation;
        if (rig != null)
        {
            if (rig.Parts == null || rig.Parts.Length == 0 || rig.Parts.Length > 8 ||
                !float.IsFinite(rig.CrankRadiusPixels) || rig.CrankRadiusPixels <= 0f ||
                !float.IsFinite(rig.ConnectingRodLengthPixels) || rig.ConnectingRodLengthPixels <= 0f)
                throw new InvalidOperationException("机器关节装配定义无效：" + definition.Id);
            foreach (PartDefinition part in rig.Parts)
            {
                if (part == null || !float.IsFinite(part.Scale) || part.Scale <= 0f ||
                    !definition.TryGetVisualStateSprite(part.SpriteState, out _))
                    throw new InvalidOperationException("机器关节零件缺失：" + definition.Id + "/" + part?.SpriteState);
                if ((part.Motion is "slider" or "rod") &&
                    Math.Abs(part.SliderAxisPixelsX - rig.CrankCenterPixels.x) + rig.CrankRadiusPixels >= rig.ConnectingRodLengthPixels)
                    throw new InvalidOperationException("机器刚性连杆不能完成整圈转动：" + definition.Id);
            }
        }
        definitions.Add(definition, value);
        return rig;
    }

    internal static MachineArticulatedMotionClock SynchronizeClock(MachineEntity node, MachineArticulatedVisual rig)
    {
        var clock = clocks.GetOrCreateValue(node);
        FluidMachineState state = MachineWorld.GetFluidState(node);
        bool authority = GameNetwork.HasStateAuthority;
        // 客户端不运行机械求解，只以服务端成功流量和同步转速决定表现。
        bool powered = !authority ? node.SpeedRpm > 0f : node.Definition.Electrical?.IsConsumer == true
            ? node.ElectricalSuppliedWatts > 0f : node.LoadSatisfied && node.SpeedRpm > 0f;
        bool transferring = string.IsNullOrEmpty(rig.WorkingFlowPhase) || state.LastFlowPhase == rig.WorkingFlowPhase;
        double rpm = MachineArticulatedKinematics.ResolveWorkingSignedRpm((!authority || node.Active) && node.State != null,
            powered, transferring, state.Ruptured, node.VisualRpm);
        clock.Synchronize(Time.time, rpm);
        return clock;
    }

    internal readonly struct PartPose
    {
        public readonly Sprite Sprite;
        public readonly Vector3 Offset, Scale;
        public readonly Quaternion Rotation;
        public readonly Vector4 Animation, Parameters;
        public readonly int Mode, Order;
        public PartPose(Sprite sprite, Vector3 offset, Vector3 scale, Quaternion rotation,
            Vector4 animation, Vector4 parameters, int mode, int order)
        { Sprite = sprite; Offset = offset; Scale = scale; Rotation = rotation; Animation = animation; Parameters = parameters; Mode = mode; Order = order; }
    }

    internal PartPose GetPartPose(RuntimeItemDefinition item, int index, float fit,
        MachineArticulatedMotionClock clock)
    {
        PartDefinition part = Parts[index];
        item.TryGetVisualStateSprite(part.SpriteState, out Sprite sprite);
        float pixelsPerUnit = item.Sprite.pixelsPerUnit;
        // 每个零件只作固定尺寸校准，连杆运动时长度不变。
        float sourceScale = part.Scale * sprite.pixelsPerUnit / pixelsPerUnit;
        Vector2 anchor = part.PivotPixels;
        int mode = 0;
        float phaseOffset = 0f;
        Vector4 payload = default;
        float stroke = 0f;
        Quaternion rotation = Quaternion.identity;
        if (part.Motion is "slider" or "rod")
        {
            mode = part.Motion == "slider" ? SliderMode : ConnectingRodMode;
            anchor = part.Motion == "slider" ? new Vector2(part.SliderAxisPixelsX, CrankCenterPixels.y) : CrankCenterPixels;
            float unit = pixelsPerUnit * sourceScale;
            stroke = CrankRadiusPixels / unit;
            payload = new Vector4((part.SliderAxisPixelsX - CrankCenterPixels.x) / unit,
                ConnectingRodLengthPixels / unit, part.SourceAngleRadians, 0f);
        }
        else if (part.Motion == "crank")
        { mode = 1; anchor = CrankCenterPixels; phaseOffset = -part.SourceAngleRadians; }
        else if (part.Motion == "needle")
        {
            bool working = clock.SignedRpm != 0d;
            float needleAngle = working ? NeedleWorkingDegrees : NeedleIdleDegrees;
            rotation = Quaternion.Euler(0f, 0f, needleAngle - part.SourceAngleRadians * Mathf.Rad2Deg);
            mode = NeedleMode;
            stroke = working ? NeedlePulseDegrees * Mathf.Deg2Rad : 0f;
        }
        Vector3 offset = new((anchor.x - CanvasPivotPixels.x) / pixelsPerUnit * fit,
            (CanvasPivotPixels.y - anchor.y) / pixelsPerUnit * fit, 0f);
        float radians = (float)(clock.SignedRpm * Math.PI / 30d);
        Vector4 animation = new(mode, radians,
            (float)(clock.PhaseRadians - clock.SampleTimeSeconds * radians) + phaseOffset, stroke);
        return new PartPose(sprite, offset, Vector3.one * (sourceScale * fit), rotation,
            animation, payload, mode, part.Order);
    }

    internal static Matrix4x4 ResolveMotion(int mode, Vector4 animation, Vector4 parameters, float now)
    {
        float phase = animation.z + now * animation.y;
        if (mode == 1) return Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, phase * Mathf.Rad2Deg));
        if (mode == NeedleMode) return Matrix4x4.Rotate(Quaternion.Euler(0f, 0f,
            Mathf.Sin(phase * 2f) * animation.w * Mathf.Rad2Deg));
        if (mode != SliderMode && mode != ConnectingRodMode) return Matrix4x4.identity;
        if (!MachineArticulatedKinematics.TrySolveVerticalSliderCrank(phase, 0d, 0d, animation.w,
            parameters.x, parameters.y, 1, out var pose)) return Matrix4x4.identity;
        if (mode == SliderMode) return Matrix4x4.Translate(new Vector3(0f, (float)pose.SliderPinY, 0f));
        return Matrix4x4.TRS(new Vector3((float)pose.CrankPinX, (float)pose.CrankPinY, 0f),
            Quaternion.Euler(0f, 0f, ((float)pose.ConnectingRodAngleRadians - parameters.z) * Mathf.Rad2Deg), Vector3.one);
    }
    #endregion
}
