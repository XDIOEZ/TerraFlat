using System;
using System.Collections.Generic;
using System.Globalization;

namespace FlatWorld.Spaceflight
{
    public sealed partial class SpaceSession
    {
        #region 船上耗尽工作站的掉落交付
        public bool TryReleaseWornMachine(MachineEntity node, IReadOnlyList<ItemData> drops, out string reason)
        {
            reason = null;
            if (drops == null || drops.Count == 0 || node == null ||
                !FindPiece(node.Id.ToString(CultureInfo.InvariantCulture), out ShipState ship, out ShipPieceState piece))
            { reason = "耗尽工作站缺少船部件或材料"; return false; }
            var queued = new List<SpaceLooseItemState>();
            try
            {
                // 复用正式船部件拆除事务；其余材料先交付，失败时统一撤销。
                for (int i = 1; i < drops.Count; i++) queued.Add(QueueDrop(drops[i], ship, piece));
                if (TryDismantleMachinePiece(node, drops[0], out reason)) return true;
            }
            catch (Exception exception) { reason = exception.Message; }
            if (MachineWorld.Contains(node))
                foreach (SpaceLooseItemState drop in queued) State.LooseItems.Remove(drop);
            return !MachineWorld.Contains(node);
        }
        #endregion
    }
}
