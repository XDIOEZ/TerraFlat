using System;
using System.Collections.Generic;

namespace FlatWorld.Spaceflight
{
    public sealed class ShipThrustAllocation
    {
        #region 每步推力请求与工作区
        private const double Epsilon = 1e-9d;
        private readonly List<EngineRequest> engines = new(16);
        private readonly List<DirectionGroup> groups = new(4);
        private SpaceVector2 requestedVelocityChange;
        private double maximumVelocityChange;
        private double requestedTurn;
        private double turnCapacity;
        private double actualTurnCapacity;

        private struct EngineRequest
        {
            public double Throttle;
            public double Capacity;
            public double ActualFraction;
            public int Group;
            public bool Turns;
        }

        private struct DirectionGroup
        {
            public SpaceVector2 Direction;
            public double VelocityChange;
            public double Capacity;
            public double ActualCapacity;
        }

        public int Count => engines.Count;

        public void Reset(SpaceVector2 velocityChange, double maximumChange, double turn)
        {
            engines.Clear();
            groups.Clear();
            requestedVelocityChange = velocityChange.IsFinite ? velocityChange : default;
            maximumVelocityChange = SpaceVector2.Finite(maximumChange) ? Math.Max(0d, maximumChange) : 0d;
            requestedTurn = SpaceVector2.Finite(turn) ? Math.Clamp(turn, -1d, 1d) : 0d;
            turnCapacity = 0d;
            actualTurnCapacity = 0d;
        }

        public void Add(SpaceVector2 thrustDirection, SpaceVector2 worldLever, double thrustNewtons)
        {
            var request = new EngineRequest { Group = -1 };
            if (!thrustDirection.IsFinite || thrustDirection.SqrMagnitude <= Epsilon * Epsilon ||
                !SpaceVector2.Finite(thrustNewtons) || thrustNewtons <= 0d)
            {
                engines.Add(request);
                return;
            }

            SpaceVector2 direction = thrustDirection.Normalized;
            double translationThrottle = maximumVelocityChange > Epsilon
                ? Math.Clamp(SpaceVector2.Dot(direction, requestedVelocityChange) / maximumVelocityChange, 0d, 1d)
                : 0d;
            request.Turns = worldLever.IsFinite && SpaceVector2.Cross(worldLever, direction) * requestedTurn > Epsilon;
            request.Throttle = Math.Max(translationThrottle, request.Turns ? Math.Abs(requestedTurn) : 0d);
            request.Capacity = thrustNewtons;

            // 同方向引擎共享一份速度变化，缺油引擎仍计入计划容量。
            if (translationThrottle > Epsilon)
            {
                int groupIndex = 0;
                while (groupIndex < groups.Count && SpaceVector2.Dot(groups[groupIndex].Direction, direction) <= .999999d)
                    groupIndex++;
                if (groupIndex == groups.Count)
                    groups.Add(new DirectionGroup { Direction = direction, VelocityChange = maximumVelocityChange * translationThrottle });
                DirectionGroup group = groups[groupIndex];
                group.Capacity += thrustNewtons;
                groups[groupIndex] = group;
                request.Group = groupIndex;
            }
            if (request.Turns) turnCapacity += thrustNewtons;
            engines.Add(request);
        }

        public double GetThrottle(int index) => engines[index].Throttle;

        public void SetActualOutput(int index, double actualThrottle)
        {
            EngineRequest request = engines[index];
            double fraction = request.Throttle > Epsilon && SpaceVector2.Finite(actualThrottle)
                ? Math.Clamp(actualThrottle / request.Throttle, 0d, 1d) : 0d;
            double capacityChange = request.Capacity * (fraction - request.ActualFraction);
            request.ActualFraction = fraction;
            engines[index] = request;
            if (request.Group >= 0)
            {
                DirectionGroup group = groups[request.Group];
                group.ActualCapacity += capacityChange;
                groups[request.Group] = group;
            }
            if (request.Turns) actualTurnCapacity += capacityChange;
        }
        #endregion

        #region 实际出力与安装方向
        public SpaceVector2 ResolveVelocityChange()
        {
            SpaceVector2 result = default;
            for (int i = 0; i < groups.Count; i++)
            {
                DirectionGroup group = groups[i];
                result += group.Direction * (group.VelocityChange * Math.Clamp(group.ActualCapacity / group.Capacity, 0d, 1d));
            }
            double maximum = requestedVelocityChange.Magnitude;
            double magnitude = result.Magnitude;
            return magnitude > maximum && magnitude > Epsilon ? result * (maximum / magnitude) : result;
        }

        // Q/E 保留直接角速度控制，纯转向的引擎出力不会附加平移。
        public double ResolveTurnAuthority() => turnCapacity > 0d
            ? Math.Clamp(actualTurnCapacity / turnCapacity, 0d, 1d) : 0d;

        public static SpaceVector2 GetThrustDirection(double shipAngleRadians, int quarterTurns)
        {
            SpaceVector2 local = (quarterTurns & 3) switch
            {
                0 => new SpaceVector2(0d, 1d),
                1 => new SpaceVector2(-1d, 0d),
                2 => new SpaceVector2(0d, -1d),
                _ => new SpaceVector2(1d, 0d)
            };
            double sin = Math.Sin(shipAngleRadians), cos = Math.Cos(shipAngleRadians);
            return new SpaceVector2(local.X * cos - local.Y * sin, local.X * sin + local.Y * cos);
        }
        #endregion
    }
}
