using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 权威结构伤害反馈
        private readonly Dictionary<string, ShipDamageFeedback> pendingShipDamageFeedback = new(StringComparer.Ordinal);
        private readonly List<string> shipDamageFeedbackKeys = new();
        private sealed class ShipDamageFeedback
        {
            public bool InSpace;
            public SpaceSectorId Sector;
            public string WorldKey, Reason;
            public SpaceSessionState Session;
            public double X, Y, Amount;
            public float ShowAt;
        }

        private void RegisterShipDamageFeedback() => ShipStructureService.DamageApplied += OnShipDamageApplied;
        private void UnregisterShipDamageFeedback()
        {
            ShipStructureService.DamageApplied -= OnShipDamageApplied;
            pendingShipDamageFeedback.Clear(); shipDamageFeedbackKeys.Clear();
        }

        private void OnShipDamageApplied(ShipState ship, ShipPieceState piece, double actualLoss, string reason)
        {
            if (actualLoss <= 0d || State == null || !State.Ships.Contains(ship)) return;
            SynchronizePieceHealth(ship, piece);
            if (reason == "攻击") return;
            QueueShipDamageFeedback(ship, piece, actualLoss, reason);
        }

        private void QueueShipDamageFeedback(ShipState ship, ShipPieceState piece, double actualLoss, string reason)
        {
            if (ship == null || actualLoss <= 0d || State == null || !IsVisible(ship)) return;
            ShipFlightState flight = GetFlight(ship.ShipId);
            if (flight == null) return;
            string key = (piece?.PieceId ?? ship.ShipId) + ":" + reason;
            if (!pendingShipDamageFeedback.TryGetValue(key, out ShipDamageFeedback feedback))
            {
                feedback = new ShipDamageFeedback { ShowAt = Time.time + .5f, Session = State };
                pendingShipDamageFeedback.Add(key, feedback);
            }
            if (piece == null) { feedback.X = ship.PositionX; feedback.Y = ship.PositionY; }
            else
            {
                int width = (piece.QuarterTurns & 1) == 0 ? piece.Width : piece.Height;
                int height = (piece.QuarterTurns & 1) == 0 ? piece.Height : piece.Width;
                ShipGeometry.LocalToWorld(ship, (piece.CellX + width * .5d) * ship.CellSizeMeters,
                    (piece.CellY + height * .5d) * ship.CellSizeMeters, out feedback.X, out feedback.Y);
            }
            feedback.InSpace = flight.Phase == ShipFlightPhase.Cruising;
            feedback.Sector = ship.Sector;
            feedback.WorldKey = feedback.InSpace ? SpaceSectorCoordinates.SceneKey(ship.Sector) : flight.SurfaceWorldKey;
            feedback.Amount += actualLoss; feedback.Reason = reason;
            if (piece?.Health <= 0d) feedback.ShowAt = Time.time;
        }

        private void FlushShipDamageFeedback()
        {
            if (pendingShipDamageFeedback.Count == 0) return;
            shipDamageFeedbackKeys.Clear();
            GameObject prefab = null;
            GameRes.ExistingInstance?.AllPrefabs.TryGetValue("DamageText", out prefab);
            DamageTextEffect template = prefab != null ? prefab.GetComponent<DamageTextEffect>() : null;
            int presented = 0;
            foreach (var pair in pendingShipDamageFeedback)
            {
                ShipDamageFeedback feedback = pair.Value;
                bool visible = feedback.InSpace ? IsSpaceView &&
                    (SpaceSectorCoordinates.Relative(feedback.Sector, new SpaceVector2(feedback.X, feedback.Y), ViewSector) - ViewOrigin).Magnitude < 1200d :
                    feedback.WorldKey == SceneManager.GetActiveScene().name;
                if (!running || !ReferenceEquals(State, feedback.Session) || !visible)
                { shipDamageFeedbackKeys.Add(pair.Key); continue; }
                if (Time.time < feedback.ShowAt || presented >= 24) continue;
                shipDamageFeedbackKeys.Add(pair.Key);
                if (template == null) continue;
                Vector2 point = feedback.InSpace ? ProjectPosition(new SpaceSectorPosition(feedback.Sector, new SpaceVector2(feedback.X, feedback.Y)))
                    : WorldLocalPresentation.ProjectPosition(new Vector2((float)feedback.X, (float)feedback.Y));
                var data = new DamageTextEffectData((float)Math.Min(float.MaxValue, feedback.Amount))
                {
                    UseColorOverride = true, ColorOverride = new Color(1f, .18f, .14f, 1f),
                    UseTextOverride = true,
                    TextOverride = DamageReasonText(feedback.Reason) + " -" + feedback.Amount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                    ScaleMultiplier = .7f, TextWidthMultiplier = 5f
                };
                GameEffect effect = VisualEffectManager.Instance.GetGameEffectFromPool(template);
                if (effect == null) continue;
                effect.transform.position = point;
                effect.Effect(null, data); presented++;
            }
            foreach (string key in shipDamageFeedbackKeys) pendingShipDamageFeedback.Remove(key);
        }

        private static string DamageReasonText(string reason) => reason switch
        {
            "outside-temperature" => "温度损伤",
            "outside-pressure" => "压力损伤",
            "outside-temperature-pressure" => "温压损伤",
            "燃料与压力爆炸" => "爆炸损伤",
            _ => string.IsNullOrWhiteSpace(reason) ? "结构损伤" : reason
        };
        #endregion

        #region 沿线冲击预算
        // 一次冲击只把原有损伤预算交给一条断裂线，静止接触没有冲击预算。
        private void ApplyLineImpact(ShipState ship, double damageBudget, string reason)
        {
            if (!SpaceVector2.Finite(damageBudget) || damageBudget <= 0d) return;
            if (!ShipDamagePattern.TryCreateLine(ship, UnityEngine.Random.value, UnityEngine.Random.value,
                    out ShipDamageLine line)) return;
            ShipDamagePattern.ApplyLineDamage(ship, line, damageBudget, reason, DestroyPiece);
        }
        #endregion
    }
}
