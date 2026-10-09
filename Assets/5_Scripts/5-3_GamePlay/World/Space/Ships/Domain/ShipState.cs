using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace FlatWorld.Spaceflight
{
    #region 船体保存模型
    public enum ShipPieceKind
    {
        Floor, Wall, Door, DockingPort, Console, Engine, Equipment,
        GravityGenerator, Cultivator, Navigation, Wire, Pipe, Fiber
    }

    public enum ShipMassKind { Cargo, Occupant, Gas, Other }
    public enum ShipPlatformKind { Space, Land, Ocean }

    [Serializable, JsonObject(MemberSerialization.Fields)]
    public sealed class ShipCell : IEquatable<ShipCell>
    {
        public int X;
        public int Y;
        public ShipCell() { }
        public ShipCell(int x, int y) { X = x; Y = y; }
        public bool Equals(ShipCell other) => other != null && X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is ShipCell other && Equals(other);
        public override int GetHashCode() => unchecked((X * 397) ^ Y);
    }

    [Serializable, JsonObject(MemberSerialization.Fields)]
    public sealed class ShipPieceState
    {
        public string PieceId = Guid.NewGuid().ToString("N");
        public string ItemId;
        public ShipPieceKind Kind;
        public int CellX;
        public int CellY;
        public int Layer;
        public int Width = 1;
        public int Height = 1;
        public int QuarterTurns;
        // 部件质量与额外载荷分开登记，同一份真实内容只选择其中一个入口计重。
        public double MassKg;
        public double Health = 100d;
        public double MaxHealth = 100d;
        public bool Destroyed;
        public bool SealsAtmosphere;
        public bool BlocksMovement;
        public bool RequiresFloorSupport = true;
        public double MinimumPressureKPa;
        public double MaximumPressureKPa = 1000000d;
        public double PressureDamagePerKPaSecond;
        public double MinimumTemperatureCelsius = -273.15d;
        public double MaximumTemperatureCelsius = 1000000d;
        public double TemperatureDamagePerCelsiusSecond;
        public string MaterialId;
        public string SnapshotJson;
        [JsonIgnore] public bool IsAlive => !Destroyed && Health > 0d;
    }

    [Serializable, JsonObject(MemberSerialization.Fields)]
    public sealed class ShipMassContribution
    {
        public string SourceId;
        public string SupportPieceId;
        public ShipMassKind Kind;
        public double LocalX;
        public double LocalY;
        public double MassKg;
        public double IntrinsicInertiaKgM2;
    }

    [Serializable, JsonObject(MemberSerialization.Fields)]
    public sealed class ShipCompartmentState
    {
        public string CompartmentId = Guid.NewGuid().ToString("N");
        public List<ShipCell> Cells = new List<ShipCell>();
        public double VolumeLiters;
        public FluidInventoryState Gas = new FluidInventoryState();
    }

    [Serializable, JsonObject(MemberSerialization.Fields)]
    public sealed class ShipDockingPortState
    {
        public string PortId;
        public string PieceId;
        public int CellX;
        public int CellY;
        public int Width = 1, Height = 1;
        // 朝向零为本地右侧，之后每次逆时针转九十度。
        public int QuarterTurns;
        public string PairedShipId;
        public string PairedPortId;
        public bool Confirmed;
        public bool Connected;
    }

    [Serializable, JsonObject(MemberSerialization.Fields)]
    public sealed class ShipState
    {
        public string ShipId = Guid.NewGuid().ToString("N");
        public string IdentityAnchorPieceId;
        public string ReferenceBodyId;
        public string WorldAddress;
        public ShipPlatformKind PlatformKind;
        public bool IsWreck;
        public double CellSizeMeters = 1d;
        public double CabinHeightMeters = 2.5d;
        public double NormalCabinTemperatureCelsius = 20d;
        public double PositionX;
        public double PositionY;
        public double VelocityX;
        public double VelocityY;
        public double AngleRadians;
        public double AngularVelocityRadiansPerSecond;
        public double MassKg;
        public double CenterOfMassLocalX;
        public double CenterOfMassLocalY;
        public double InertiaKgM2;
        public List<ShipPieceState> Pieces = new List<ShipPieceState>();
        public List<ShipMassContribution> MassContributions = new List<ShipMassContribution>();
        public List<ShipCompartmentState> Compartments = new List<ShipCompartmentState>();
        [JsonIgnore] public bool AtmosphereStructureDirty = true;
        public List<ShipDockingPortState> DockingPorts = new List<ShipDockingPortState>();
        // 曾经形成密闭区的格子不会在修补或读档时再次免费抽取外界气体。
        public List<ShipCell> EverEnclosedCells = new List<ShipCell>();
    }

    public readonly struct ShipMassProperties
    {
        public readonly double MassKg;
        public readonly double CenterX;
        public readonly double CenterY;
        public readonly double InertiaKgM2;
        public ShipMassProperties(double mass, double centerX, double centerY, double inertia)
        { MassKg = mass; CenterX = centerX; CenterY = centerY; InertiaKgM2 = inertia; }
    }

    [Serializable, JsonObject(MemberSerialization.Fields)]
    public sealed class ShipAssemblyState
    {
        public string AssemblyId;
        public List<string> MemberShipIds = new List<string>();
        public double MassKg;
        public double PositionX;
        public double PositionY;
        public double VelocityX;
        public double VelocityY;
        public double AngleRadians;
        public double AngularVelocityRadiansPerSecond;
        public double InertiaKgM2;
        public List<ShipAssemblyMemberState> MemberTransforms = new List<ShipAssemblyMemberState>();
    }

    [Serializable, JsonObject(MemberSerialization.Fields)]
    public sealed class ShipAssemblyMemberState
    {
        public string ShipId;
        public double OffsetX;
        public double OffsetY;
        public double RelativeAngleRadians;
    }

    [Serializable, JsonObject(MemberSerialization.Fields)]
    public sealed class ShipDockingPairState
    {
        public string PairId = Guid.NewGuid().ToString("N");
        public string ShipAId;
        public string PortAId;
        public string ShipBId;
        public string PortBId;
        public bool ConfirmedA;
        public bool ConfirmedB;
        public bool Connected;
        public double PositionToleranceMeters = 0.1d;
        public double AngleToleranceRadians = 0.1d;
        public double MaximumRelativeSpeedMetersPerSecond = 0.75d;
    }
    #endregion

    #region 船体坐标与速度
    public static class ShipGeometry
    {
        public static void Rotate(double x, double y, double angle, out double resultX, out double resultY)
        {
            double cos = Math.Cos(angle), sin = Math.Sin(angle);
            resultX = cos * x - sin * y;
            resultY = sin * x + cos * y;
        }

        public static void LocalToWorld(ShipState ship, double localX, double localY, out double x, out double y)
        {
            Rotate(localX - ship.CenterOfMassLocalX, localY - ship.CenterOfMassLocalY,
                ship.AngleRadians, out x, out y);
            x += ship.PositionX; y += ship.PositionY;
        }

        public static void WorldToLocal(ShipState ship, double worldX, double worldY, out double x, out double y)
        {
            Rotate(worldX - ship.PositionX, worldY - ship.PositionY, -ship.AngleRadians, out x, out y);
            x += ship.CenterOfMassLocalX; y += ship.CenterOfMassLocalY;
        }

        public static void PointVelocity(ShipState ship, double worldX, double worldY, out double x, out double y)
        {
            x = ship.VelocityX - ship.AngularVelocityRadiansPerSecond * (worldY - ship.PositionY);
            y = ship.VelocityY + ship.AngularVelocityRadiansPerSecond * (worldX - ship.PositionX);
        }

        public static IEnumerable<ShipCell> Footprint(ShipPieceState piece)
        {
            int width = Math.Max(1, piece.Width), height = Math.Max(1, piece.Height);
            if ((piece.QuarterTurns & 1) != 0) { int swap = width; width = height; height = swap; }
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    yield return new ShipCell(piece.CellX + x, piece.CellY + y);
        }

        public static IEnumerable<ShipCell> Neighbors(ShipCell cell)
        {
            yield return new ShipCell(cell.X + 1, cell.Y);
            yield return new ShipCell(cell.X - 1, cell.Y);
            yield return new ShipCell(cell.X, cell.Y + 1);
            yield return new ShipCell(cell.X, cell.Y - 1);
        }
    }
    #endregion
}
