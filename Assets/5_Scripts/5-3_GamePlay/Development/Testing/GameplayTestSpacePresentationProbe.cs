using System;
using System.Globalization;
using FlatWorld.Spaceflight;
using Newtonsoft.Json.Linq;

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
            Sample("clamp-high", SpacePresentationPose.Interpolate(previous, current, 2d))
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
}
