using System;
using System.Collections.Generic;

/// <summary>完整生态快照按序号组装，只有所有分片就绪才替换本机呈现。</summary>
public sealed class AiEcologySnapshotBuffer
{
    #region 分片所有权与边界
    private ulong receiving, applied;
    private string world;
    private uint realm;
    private int received;
    private AiEcologyNetworkActor[][] parts;

    public void Reset()
    { receiving = applied = 0; world = null; realm = 0; received = 0; parts = null; }

    public bool Accept(string worldKey, uint realmRevision, ulong sequence, int index, int count,
        AiEcologyNetworkActor[] actors, out AiEcologyNetworkActor[] completed)
    {
        completed = null;
        if (string.IsNullOrEmpty(worldKey) || worldKey.Length > 128 || sequence == 0 || sequence <= applied ||
            sequence < receiving || count < 1 || count > 64 || index < 0 || index >= count || actors == null || actors.Length > 64) return false;
        foreach (AiEcologyNetworkActor actor in actors)
            if (!IsValid(actor)) return false;
        if (sequence != receiving)
        {
            receiving = sequence; world = worldKey; realm = realmRevision;
            parts = new AiEcologyNetworkActor[count][]; received = 0;
        }
        if (parts == null || parts.Length != count || world != worldKey || realm != realmRevision || parts[index] != null) return false;
        parts[index] = actors;
        if (++received != count) return false;
        int length = 0;
        foreach (AiEcologyNetworkActor[] part in parts) length += part.Length;
        var result = new AiEcologyNetworkActor[length];
        var guids = new HashSet<int>(); int offset = 0;
        foreach (AiEcologyNetworkActor[] part in parts)
        {
            foreach (AiEcologyNetworkActor actor in part)
                if (!guids.Add(actor.Guid)) { parts = null; return false; }
            Array.Copy(part, 0, result, offset, part.Length); offset += part.Length;
        }
        applied = sequence; parts = null; completed = result;
        return true;
    }

    public static bool IsValid(AiEcologyNetworkActor actor)
        => actor.Guid != 0 && !string.IsNullOrEmpty(actor.Species) && actor.Species.Length <= 128 &&
           float.IsFinite(actor.Position.x) && float.IsFinite(actor.Position.y) &&
           float.IsFinite(actor.Facing.x) && float.IsFinite(actor.Facing.y) &&
           float.IsFinite(actor.Hp) && float.IsFinite(actor.MaxHp) && float.IsFinite(actor.ActionElapsed) &&
           float.IsFinite(actor.LiquidDepth) && float.IsFinite(actor.WaterBlend) &&
           float.IsFinite(actor.FlightHeight) && float.IsFinite(actor.FlightCruiseHeight);
    #endregion
}
