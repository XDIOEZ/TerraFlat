using FlatWorld.Networking;
using RuntimeWorldAddress = FlatWorld.WorldModel.WorldAddress;

public partial class ChunkMgr
{
    /// <summary>把资源自行声明的来年恢复资格记入同一个生态差量。</summary>
    public void ScheduleNaturalRenewal(RuntimeWorldAddress address, int guid, int year)
    {
        if (!GameNetwork.HasStateAuthority || !SaveDataMgr.Instance.TryGetActivePlanetData(out PlanetData planet)) return;
        planet.Ecology.GetOrCreateChunk(address.ChunkOrigin.X, address.ChunkOrigin.Y).RenewalYears[guid] = year;
    }
    /// <summary>以稳定 GUID 将补位分散到前半个春季，读取不会提前清除删除记录。</summary>
    public bool IsNaturalRenewalDue(RuntimeWorldAddress address, int guid)
    {
        return GameNetwork.HasStateAuthority && DayTimeSystem.Instance.TryGetCurrentSeason(out SeasonSnapshot season) &&
            season.Season == WorldSeason.Spring && SaveDataMgr.Instance.TryGetActivePlanetData(out PlanetData planet) &&
            planet.Ecology.TryGetChunk(address.ChunkOrigin.X, address.ChunkOrigin.Y, out EcologyChunkSaveData chunk) &&
            chunk.RenewalYears.TryGetValue(guid, out int year) && season.Year >= year &&
            season.Progress >= (unchecked((uint)guid) % 1000u) / 2000f;
    }
    /// <summary>成功创建后才清除墓碑和资格，失败或被建筑占用时可稍后重试。</summary>
    public void CompleteNaturalRenewal(RuntimeWorldAddress address, int guid)
    {
        if (!GameNetwork.HasStateAuthority || !SaveDataMgr.Instance.TryGetActivePlanetData(out PlanetData planet) ||
            !planet.Ecology.TryGetChunk(address.ChunkOrigin.X, address.ChunkOrigin.Y, out EcologyChunkSaveData chunk)) return;
        chunk.RemovedGuids.Remove(guid);
        chunk.RenewalYears.Remove(guid);
    }
}
