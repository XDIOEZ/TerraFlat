using System;
using System.Collections.Generic;

namespace FlatWorld.Spaceflight
{
    /// <summary>落地预检失败后，只在占地、成员、结构或落点变化时重新规划。</summary>
    public sealed class ShipGroundRestoreGate
    {
        #region 落地失败的输入失效
        private readonly List<Member> blockedMembers = new();
        private string blockedWorld;
        private uint blockedOccupancyRevision;
        private bool blocked;

        private readonly struct Member
        {
            private readonly ShipState ship;
            private readonly ulong structureRevision;
            private readonly int pieceCount;
            private readonly double x, y, angle, centerX, centerY, cellSize;

            public Member(ShipState value)
            {
                ship = value; structureRevision = value.StructureRevision; pieceCount = value.Pieces.Count;
                x = value.PositionX; y = value.PositionY; angle = value.AngleRadians;
                centerX = value.CenterOfMassLocalX; centerY = value.CenterOfMassLocalY; cellSize = value.CellSizeMeters;
            }

            public bool Matches(ShipState value) => ReferenceEquals(ship, value) &&
                structureRevision == value.StructureRevision && pieceCount == value.Pieces.Count &&
                x == value.PositionX && y == value.PositionY && angle == value.AngleRadians &&
                centerX == value.CenterOfMassLocalX && centerY == value.CenterOfMassLocalY && cellSize == value.CellSizeMeters;
        }

        public bool ShouldAttempt(string world, uint occupancyRevision, IReadOnlyList<ShipState> members)
        {
            if (!blocked || !string.Equals(world, blockedWorld, StringComparison.Ordinal) ||
                occupancyRevision != blockedOccupancyRevision || members.Count != blockedMembers.Count) return true;
            for (int i = 0; i < members.Count; i++)
                if (!blockedMembers[i].Matches(members[i])) return true;
            return false;
        }

        public void Block(string world, uint occupancyRevision, IReadOnlyList<ShipState> members)
        {
            blockedWorld = world; blockedOccupancyRevision = occupancyRevision; blocked = true;
            blockedMembers.Clear();
            foreach (ShipState ship in members) blockedMembers.Add(new Member(ship));
        }
        #endregion
    }
}
