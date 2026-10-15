using FlatWorld.Spaceflight;

internal static partial class Program
{
    #region 统一船体表现插值回归
    private static void PresentationEndpoints()
    {
        var previous = new SpacePresentationPose(new SpaceSectorPosition(new(2, -3), new(10d, 20d)), .2d);
        var current = new SpacePresentationPose(new SpaceSectorPosition(new(2, -3), new(30d, 60d)), .6d);
        AssertPresentationPose(previous, SpacePresentationPose.Interpolate(previous, current, 0d), "zero alpha retains previous pose");
        AssertPresentationPose(current, SpacePresentationPose.Interpolate(previous, current, 1d), "one alpha retains current pose");
        AssertPresentationPose(previous, SpacePresentationPose.Interpolate(previous, current, -5d), "negative alpha cannot predict past previous pose");
        AssertPresentationPose(current, SpacePresentationPose.Interpolate(previous, current, 7d), "large alpha cannot predict future motion");
        // 非法进度不能把 NaN 传播到船体、乘员或星区坐标，退回当前权威姿态。
        AssertPresentationPose(current, SpacePresentationPose.Interpolate(previous, current, double.NaN),
            "NaN alpha returns current authoritative pose");
        AssertPresentationPose(previous, SpacePresentationPose.Interpolate(previous, current, double.NegativeInfinity),
            "negative infinite alpha clamps to previous pose");
        AssertPresentationPose(current, SpacePresentationPose.Interpolate(previous, current, double.PositiveInfinity),
            "positive infinite alpha clamps to current pose");
        var midpoint = SpacePresentationPose.Interpolate(previous, current, .5d);
        Near(20d, midpoint.Position.LocalMeters.X, "midpoint x");
        Near(40d, midpoint.Position.LocalMeters.Y, "midpoint y");
        Near(.4d, midpoint.AngleRadians, "midpoint angle");

        var track = new SpacePresentationTrack();
        track.Advance(previous);
        AssertPresentationPose(previous, track.Previous, "first advance initializes previous");
        AssertPresentationPose(previous, track.Current, "first advance initializes current");
        track.Advance(current);
        AssertPresentationPose(previous, track.Previous, "next advance keeps one fixed-step history");
        AssertPresentationPose(midpoint, track.Evaluate(.5d), "track uses production interpolation");
        track.Reset(current);
        AssertPresentationPose(current, track.Evaluate(.25d), "reset discards old-world interpolation");
        AssertPresentationPose(current, track.Previous, "reset replaces previous pose");
    }

    private static void PresentationShortestTurn()
    {
        const double degree = Math.PI / 180d;
        var positive = new SpacePresentationPose(default, 170d * degree);
        var negative = new SpacePresentationPose(default, -170d * degree);
        Near(180d * degree, SpacePresentationPose.Interpolate(positive, negative, .5d).AngleRadians,
            "positive seam crossing follows twenty-degree arc");
        Near(-180d * degree, SpacePresentationPose.Interpolate(negative, positive, .5d).AngleRadians,
            "negative seam crossing follows twenty-degree arc");
        var wrapped = new SpacePresentationPose(default, (170d + 720d) * degree);
        Near((180d + 720d) * degree, SpacePresentationPose.Interpolate(wrapped, negative, .5d).AngleRadians,
            "accumulated full turns still use shortest arc");
        AssertPresentationPose(negative, SpacePresentationPose.Interpolate(positive, negative, 1d),
            "angle endpoint preserves exact authoritative representation");
    }

    private static void PresentationSectorCrossing()
    {
        var sector = new SpaceSectorId(long.MaxValue - 2, long.MinValue + 2);
        var previous = new SpacePresentationPose(new SpaceSectorPosition(sector, new(127999.875d, -127999.875d)), 0d);
        var current = new SpacePresentationPose(new SpaceSectorPosition(new(sector.X + 1, sector.Y - 1),
            new(-127999.875d, 127999.875d)), .2d);
        var midpoint = SpacePresentationPose.Interpolate(previous, current, .5d);
        SpaceVector2 displacement = SpaceSectorCoordinates.Difference(midpoint.Position, previous.Position);
        Near(.125d, displacement.X, "huge adjacent sector retains eighth-meter x midpoint");
        Near(-.125d, displacement.Y, "huge adjacent sector retains eighth-meter y midpoint");
        Check(midpoint.Position.Sector == new SpaceSectorId(sector.X + 1, sector.Y),
            "midpoint normalizes each half-open sector axis independently");
        SpaceSectorPosition piece = midpoint.TransformPoint(.25d, 0d);
        SpaceVector2 pieceOffset = SpaceSectorCoordinates.Difference(piece, midpoint.Position);
        Near(Math.Cos(.1d) * .25d, pieceOffset.X, "local piece x survives giant sector identity");
        Near(Math.Sin(.1d) * .25d, pieceOffset.Y, "local piece y survives giant sector identity");
    }

    private static void PresentationRigidPoints()
    {
        var previous = new SpacePresentationPose(new SpaceSectorPosition(default, new(50d, 60d)), 0d);
        var current = new SpacePresentationPose(new SpaceSectorPosition(default, new(70d, 80d)), Math.PI * .5d);
        var pose = SpacePresentationPose.Interpolate(previous, current, .5d);
        SpaceSectorPosition root = pose.TransformPoint(0d, 0d);
        SpaceSectorPosition piece = pose.TransformPoint(.5d, 1.5d);
        SpaceSectorPosition passenger = pose.TransformPoint(2.25d, 3.75d);
        Near(60d, root.LocalMeters.X, "ship root shares interpolated x");
        Near(70d, root.LocalMeters.Y, "ship root shares interpolated y");
        SpaceVector2 separation = SpaceSectorCoordinates.Difference(passenger, piece);
        double diagonal = Math.Sqrt(.5d);
        Near((1.75d - 2.25d) * diagonal, separation.X, "passenger and piece share interpolated rotation x");
        Near((1.75d + 2.25d) * diagonal, separation.Y, "passenger and piece share interpolated rotation y");
        Near(Math.Sqrt(1.75d * 1.75d + 2.25d * 2.25d), separation.Magnitude,
            "interpolation preserves rigid passenger-to-piece distance");

        var ship = new ShipState { Sector = new(3, -2), PositionX = 300d, PositionY = 400d,
            CenterOfMassLocalX = 2d, CenterOfMassLocalY = 3d, AngleRadians = .7d };
        ShipGeometry.LocalToWorld(ship, .5d, 1.5d, out double x, out double y);
        SpaceSectorPosition authoritative = SpacePresentationPose.FromShip(ship).TransformPoint(.5d, 1.5d);
        Near(x, authoritative.LocalMeters.X, "captured origin reproduces authoritative piece x");
        Near(y, authoritative.LocalMeters.Y, "captured origin reproduces authoritative piece y");
    }

    private static void PresentationMassOrigin()
    {
        // 使用正式载荷重算改变重心，验证表现原点和船上点不会被重心修正拉动。
        ShipState ship = MassShip();
        ship.Sector = new(5, -4); ship.PositionX = 1234d; ship.PositionY = -2345d; ship.AngleRadians = .7d;
        SpacePresentationPose before = SpacePresentationPose.FromShip(ship);
        double centerX = ship.PositionX, centerY = ship.PositionY;
        ShipStructureService.RecalculateMass(ship);
        Check(Math.Abs(ship.PositionX - centerX) + Math.Abs(ship.PositionY - centerY) > .1d,
            "fixture actually moves authoritative center of mass");
        AssertPresentationPose(before, SpacePresentationPose.FromShip(ship), "initial mass recalculation preserves local grid origin");
        ship.MassContributions[0].MassKg = 30d;
        ship.MassContributions[1].LocalX = -2d;
        ShipStructureService.RecalculateMass(ship);
        SpacePresentationPose after = SpacePresentationPose.FromShip(ship);
        AssertPresentationPose(before, after, "live cargo changes preserve local grid origin");
        var track = new SpacePresentationTrack(); track.Reset(before); track.Advance(after);
        SpaceSectorPosition point = before.TransformPoint(2.25d, 3.75d);
        foreach (double alpha in new[] { 0d, .25d, .5d, .75d, 1d })
        {
            SpaceVector2 error = SpaceSectorCoordinates.Difference(track.Evaluate(alpha).TransformPoint(2.25d, 3.75d), point);
            Near(0d, error.Magnitude, "mass-only change cannot animate passenger or piece position");
        }
    }

    private static void AssertPresentationPose(SpacePresentationPose expected, SpacePresentationPose actual, string message)
    {
        SpaceVector2 delta = SpaceSectorCoordinates.Difference(actual.Position, expected.Position);
        Near(0d, delta.X, message + " x");
        Near(0d, delta.Y, message + " y");
        Near(expected.AngleRadians, actual.AngleRadians, message + " angle");
    }
    #endregion
}
