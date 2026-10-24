using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 散物共用运动内核采样
    private const string MotionTestWorld = "test:loose-item-motion";

    private static void RegisterLooseItemMotionOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("mechanical/loose_item_motion", LooseItemMotionValues);
        operations.Add("mechanical/loose_item_sources", LooseItemSourceValues);
    }

    // 夹具只提供地形、墙、环绕与带形，时间积分与力合成直接调用正式生产内核。
    private struct MotionField : LooseItemMotion.IField
    {
        internal bool Belt, Water, External, MissingTerrain;
        internal ConveyorPath Route;
        internal float Speed, Shore, Wall, Wrap;
        internal int Moves;
        public bool WaterOnly => Water;
        public Vector2 CarrierVelocity(Vector2 position, float seconds)
        {
            if (!Belt) return Vector2.right * Speed;
            return Route.TryMove(position, Speed * seconds, .4f, out Vector2 destination)
                ? (destination - position) / seconds : Vector2.zero;
        }
        public Vector2 ExternalVelocity(Vector2 position) => External
            ? LooseItemMotion.SampleAdditionalVelocity(new LooseItemMotionContext(MotionTestWorld, null, position, Water))
            : Vector2.zero;
        public bool IsWater(Vector2 position) => position.x < Shore;
        public Vector2 Move(Vector2 position, Vector2 displacement)
        {
            Moves++;
            if (MissingTerrain) return position;
            Vector2 destination = position + displacement;
            if (destination.x >= Wall) return position;
            if (Wrap > 0f) destination.x = Mathf.Repeat(destination.x, Wrap);
            return destination;
        }
        public Vector2 Delta(Vector2 from, Vector2 to)
        {
            Vector2 delta = to - from;
            if (Wrap > 0f) delta.x = Mathf.Repeat(delta.x + Wrap * .5f, Wrap) - Wrap * .5f;
            return delta;
        }
    }

    private static JObject LooseItemMotionValues(JObject args)
    {
        Vector2 force = new((float)Number(args, "forceX", 0f), (float)Number(args, "forceY", 0f));
        if (Flag(args, "invalidForce", false)) force.x = float.NaN;
        using var source = LooseItemMotion.RegisterVelocitySource("test:" + Guid.NewGuid().ToString("N"),
            context => context.WorldKey == MotionTestWorld ? force : Vector2.zero);
        var field = new MotionField
        {
            Belt = Flag(args, "belt", false), Water = Flag(args, "water", false),
            External = !Flag(args, "submerged", false), MissingTerrain = Flag(args, "missing", false),
            Route = Flag(args, "curve", false) ? new ConveyorPath(2, 1) : ConveyorPath.Straight(0),
            Speed = (float)Number(args, "speed", 1f), Shore = (float)Number(args, "shore", 100f),
            Wall = (float)Number(args, "wall", 100f), Wrap = (float)Number(args, "wrap", 0f)
        };
        Vector2 start = new((float)Number(args, "x", 0f), (float)Number(args, "y", 0f));
        float seconds = (float)Number(args, "seconds", .2f);
        if (Flag(args, "invalidTime", false)) seconds = float.PositiveInfinity;
        Vector2 whole = LooseItemMotion.Integrate(start, seconds, ref field);
        int moves = field.Moves;
        Vector2 split = start;
        for (int i = 0; i < 20; i++) split = LooseItemMotion.Integrate(split, seconds / 20f, ref field);
        return new JObject
        {
            ["x"] = whole.x, ["y"] = whole.y, ["splitX"] = split.x, ["splitY"] = split.y, ["moves"] = moves
        };
    }

    private static JObject LooseItemSourceValues(JObject args)
    {
        var context = new LooseItemMotionContext(MotionTestWorld, null, Vector2.zero, false);
        Vector2 baseline = LooseItemMotion.SampleAdditionalVelocity(context);
        string id = "test:" + Guid.NewGuid().ToString("N");
        using var first = LooseItemMotion.RegisterVelocitySource(id,
            value => value.WorldKey == MotionTestWorld ? Vector2.right : Vector2.zero);
        using var second = LooseItemMotion.RegisterVelocitySource(id + ":second",
            value => value.WorldKey == MotionTestWorld ? Vector2.up * 2f : Vector2.zero);
        Vector2 combined = LooseItemMotion.SampleAdditionalVelocity(context) - baseline;
        string duplicate = string.Empty;
        try { using var rejected = LooseItemMotion.RegisterVelocitySource(id, _ => Vector2.zero); }
        catch (InvalidOperationException exception) { duplicate = exception.GetType().Name; }
        second.Dispose(); second.Dispose();
        Vector2 removed = LooseItemMotion.SampleAdditionalVelocity(context) - baseline;
        first.Dispose();
        Vector2 cleared = LooseItemMotion.SampleAdditionalVelocity(context) - baseline;
        IDisposable self = null;
        try
        {
            self = LooseItemMotion.RegisterVelocitySource(id + ":self", value =>
            { self.Dispose(); return Vector2.up; });
            Vector2 once = LooseItemMotion.SampleAdditionalVelocity(context) - baseline;
            Vector2 twice = LooseItemMotion.SampleAdditionalVelocity(context) - baseline;
            return new JObject
            {
                ["combinedX"] = combined.x, ["combinedY"] = combined.y, ["removedX"] = removed.x,
                ["removedY"] = removed.y, ["cleared"] = cleared.sqrMagnitude, ["duplicate"] = duplicate,
                ["selfOnceY"] = once.y, ["selfTwice"] = twice.sqrMagnitude
            };
        }
        finally { self?.Dispose(); }
    }
    #endregion
}
