using System;
using System.Collections.Generic;
using UnityEngine;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 本地格建造预览
        public bool TryGetPlacementWorldShip(Player actor, out ShipState ship)
        {
            string key = actor != null ? GameplayWorldRegistry.GetItemWorldKey(actor) : GameplayWorldRegistry.ExecutionWorldKey;
            if (key?.StartsWith("ship:", StringComparison.Ordinal) == true)
            {
                string shipId = key.Substring(5);
                if (TryGetShipInteriorWorld(shipId, out _) && (ship = GetShip(shipId)) != null && !ship.IsWreck) return true;
            }
            ship = null;
            return false;
        }
        public bool TrySnapPlacement(ItemData source, Vector2 point, int quarterTurns, out Vector2 snapped, out float angle,
            Player actor = null)
        {
            snapped = point; angle = 0f;
            ShipPartConfiguration config = ResolvePartConfiguration(source);
            bool localWorld = TryGetPlacementWorldShip(actor, out ShipState ship);
            Vector2 local = point;
            if (!localWorld) TryGetSupport(point, out ship, out _, out local);
            if (ship == null && config?.Kind == ShipPieceKind.Floor)
                ship = FindAdjacentShip(point, config.FootprintWidth, config.FootprintHeight, quarterTurns, out local);
            if (ship == null)
            {
                if (!IsSpaceView) return false;
                if (config?.Kind != ShipPieceKind.Floor) return false;
                snapped = new Vector2(Mathf.Floor(point.x) + .5f, Mathf.Floor(point.y) + .5f);
                angle = quarterTurns * 90f; return true;
            }
            double x = (Math.Floor(local.x / ship.CellSizeMeters) + .5d) * ship.CellSizeMeters;
            double y = (Math.Floor(local.y / ship.CellSizeMeters) + .5d) * ship.CellSizeMeters;
            if (localWorld)
            {
                snapped = new Vector2((float)x, (float)y);
                angle = (float)(GetShipPresentationPose(ship).AngleRadians * 180d / Math.PI) + quarterTurns * 90f;
                return true;
            }
            snapped = DisplayPresentationPoint(ship, x, y);
            if (!IsSpaceView) snapped.y += (float)ShipSurfaceState.VisualRise(GetFlight(ship.ShipId));
            if (!IsSpaceView) snapped = WorldLocalPresentation.ToLogical(snapped);
            angle = (float)(GetShipPresentationPose(ship).AngleRadians * 180d / Math.PI) + quarterTurns * 90f;
            return true;
        }
        public bool TryValidatePlacement(ItemData source, Vector2 point, int quarterTurns, out bool valid, out string reason,
            Player actor = null)
        {
            reason = null; valid = false;
            ShipPartConfiguration config = ResolvePartConfiguration(source);
            bool localWorld = TryGetPlacementWorldShip(actor, out ShipState ship);
            Vector2 local = point;
            if (!localWorld) TryGetSupport(point, out ship, out _, out local);
            if (ship == null && config?.Kind == ShipPieceKind.Floor)
                ship = FindAdjacentShip(point, config.FootprintWidth, config.FootprintHeight, quarterTurns, out local);
            if (ship == null)
            {
                if (!IsSpaceView) return false;
                if (config?.Kind == ShipPieceKind.Floor) { valid = true; return true; }
                if (config?.Kind == ShipPieceKind.Engine)
                { reason = "引擎需要安装在飞船边缘的地板上。"; return true; }
                if (!IsSpaceView) return false;
                reason = "太空中的设备需要飞船地板支撑。"; return true;
            }
            config ??= InferPart(source);
            Mod_Building.TryReadBuildingData(source, out _, out Mod_Building.Building_Data building);
            var proposal = new ShipPieceState { Kind = config.Kind, Layer = config.Layer,
                CellX = (int)Math.Floor(local.x / ship.CellSizeMeters), CellY = (int)Math.Floor(local.y / ship.CellSizeMeters),
                Width = Math.Max(config.FootprintWidth, building?.FootprintWidth ?? 1), Height = Math.Max(config.FootprintHeight, building?.FootprintHeight ?? 1),
                QuarterTurns = quarterTurns & 3 };
            if (!ShipEnginePlacement.TryValidateAddition(ship, proposal, out reason)) return true;
            foreach (ShipCell cell in ShipGeometry.Footprint(proposal))
            {
                if (proposal.Kind != ShipPieceKind.Floor && !ShipAtmosphereService.HasFloor(ship, cell.X, cell.Y))
                { reason = "设备全部占地都需要飞船地板支撑。"; return true; }
                foreach (ShipPieceState occupied in ship.Pieces)
                    if (occupied.IsAlive && SameOccupancyLayer(occupied, proposal) && ContainsCell(occupied, cell.X, cell.Y))
                    { reason = "船上这一层格子已经被占用。"; return true; }
            }
            valid = true; return true;
        }
        #endregion
    }
}
