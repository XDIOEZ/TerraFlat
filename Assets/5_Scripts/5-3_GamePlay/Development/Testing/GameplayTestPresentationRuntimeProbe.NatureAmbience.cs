using System;
using System.Linq;
using FlatWorld.Audio;
using FlatWorld.NaturalEntities;
using FlatWorld.WorldModel;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestPresentationRuntimeProbe
{
    #region 自然环境音现场与树木索引
    // 播放正式自然循环并短暂注册原生树木，采样完成后立即释放所有自有资源。
    private static JObject CaptureNatureAmbience(AudioFixture fixture, Player player)
    {
        AudioService service = fixture.Service;
        var cues = new JArray
        {
            CaptureNatureAmbientCue(fixture, player, NaturalAmbientAudioPresenter.FoliageCueId),
            CaptureNatureAmbientCue(fixture, player, NaturalAmbientAudioPresenter.RiverCueId)
        };
        if (!service.TryGetCue(NaturalAmbientAudioPresenter.FoliageCueId, out AudioCue foliageCue))
            throw new InvalidOperationException("正式树叶循环 Cue 未加载。");

        return new JObject
        {
            ["cues"] = cues,
            ["foliageSources"] = CaptureNatureFoliageSources(player, foliageCue.MaxDistance)
        };
    }

    private static JObject CaptureNatureAmbientCue(AudioFixture fixture, Player player, string cueId)
    {
        AudioService service = fixture.Service;
        AudioCue cue = fixture.CloneCue(cueId);
        var occupied = new System.Collections.Generic.HashSet<AudioSource>(
            service.GetComponentsInChildren<AudioSource>(true).Where(value => value.isPlaying));
        AudioHandle handle = default;
        AudioSource source = null;
        JObject result;
        try
        {
            handle = fixture.Play(cue, AudioPlayOptions.At(player.transform.position));
            source = service.GetComponentsInChildren<AudioSource>(true).SingleOrDefault(value =>
                value.isPlaying && cue.Clips.Contains(value.clip) && !occupied.Contains(value));
            AudioClip clip = source != null ? source.clip : cue.Clips.FirstOrDefault();
            var energy = CaptureNatureAmbientClipEnergy(clip);
            result = new JObject
            {
                ["id"] = cueId,
                ["accepted"] = handle.IsValid,
                ["sourceFound"] = source != null,
                ["sourcePlaying"] = source != null && source.isPlaying,
                ["bus"] = cue.Bus.ToString(), ["loop"] = cue.Loop,
                ["minDistance"] = cue.MinDistance, ["maxDistance"] = cue.MaxDistance,
                ["spatialBlend"] = cue.SpatialBlend, ["rolloff"] = cue.RolloffMode.ToString(),
                ["clipCount"] = cue.Clips.Count,
                ["clipChannels"] = clip != null ? clip.channels : 0,
                ["clipFrequency"] = clip != null ? clip.frequency : 0,
                ["clipSamples"] = clip != null ? clip.samples : 0,
                ["clipDuration"] = clip != null ? clip.length : 0f,
                ["readable"] = energy.Readable, ["rms"] = energy.Rms,
                ["minimumWindowRms"] = energy.MinimumWindowRms,
                ["sourceSpatialBlend"] = source != null ? source.spatialBlend : -1f,
                ["sourceRolloff"] = source != null ? source.rolloffMode.ToString() : string.Empty,
                ["sourceMinDistance"] = source != null ? source.minDistance : -1f,
                ["sourceMaxDistance"] = source != null ? source.maxDistance : -1f
            };
        }
        finally
        {
            if (handle.IsValid) service.Stop(handle);
        }
        result["stopped"] = !handle.IsPlaying && (source == null || (!source.isPlaying && source.clip == null));
        return result;
    }

    // 直接读取正式导入录音的百毫秒能量，循环交叠不能在人为接缝处突然静音。
    private static (bool Readable, double Rms, double MinimumWindowRms) CaptureNatureAmbientClipEnergy(AudioClip clip)
    {
        if (clip == null || clip.samples == 0) return default;
        var samples = new float[clip.samples * clip.channels];
        if (!clip.GetData(samples, 0)) return default;
        int windowSize = clip.frequency * clip.channels / 10;
        double total = 0d, minimum = double.MaxValue;
        for (int start = 0; start < samples.Length; start += windowSize)
        {
            int end = Math.Min(samples.Length, start + windowSize);
            double energy = 0d;
            for (int i = start; i < end; i++) energy += (double)samples[i] * samples[i];
            total += energy;
            minimum = Math.Min(minimum, Math.Sqrt(energy / (end - start)));
        }
        return (true, Math.Sqrt(total / samples.Length), minimum);
    }

    private static JObject CaptureNatureFoliageSources(Player player, float radius)
    {
        GameRes resources = GameRes.ExistingInstance ??
            throw new InvalidOperationException("树叶来源采样需要正式 GameRes。");
        if (!resources.TryGetItemDefinition("Tree_Pine", out RuntimeItemDefinition leafDefinition) ||
            !resources.TryGetItemDefinition("Tree_Dead", out RuntimeItemDefinition deadDefinition))
            throw new InvalidOperationException("正式目录缺少松树或枯树定义。");
        string worldKey = GameplayWorldRegistry.GetItemWorldKey(player);
        using var worldScope = GameplayWorldRegistry.UseWorld(worldKey);
        ChunkMgr chunks = ChunkMgr.ExistingInstance;
        Vector2 logicalPlayer = WorldLocalPresentation.ToLogical((Vector2)player.transform.position);
        string dimensionId = chunks.ResolveWorldAddress(logicalPlayer).DimensionId;
        Vector2 leafPosition = WorldTopologyRuntime.NormalizePosition(logicalPlayer + Vector2.right * 3f);
        Vector2 deadPosition = WorldTopologyRuntime.NormalizePosition(logicalPlayer + Vector2.right * 6f);
        ItemMgr items = ItemMgr.Instance;
        int leafGuid = items.GenerateGuid();
        int deadGuid;
        do { deadGuid = items.GenerateGuid(); } while (deadGuid == leafGuid);
        Vector3 leafWorldPosition = new(leafPosition.x, leafPosition.y, player.transform.position.z);
        Vector3 deadWorldPosition = new(deadPosition.x, deadPosition.y, player.transform.position.z);
        ItemData leafData = CreateNatureAmbienceTreeData(resources, leafDefinition, leafGuid, leafWorldPosition);
        ItemData deadData = CreateNatureAmbienceTreeData(resources, deadDefinition, deadGuid, deadWorldPosition);
        NaturalEntityHandle leafHandle = default;
        NaturalEntityHandle deadHandle = default;
        bool leafOwned = false, deadOwned = false;
        JObject result;

        try
        {
            int Count(out Vector2 nearest) => NaturalEntityEcsService.FindNearestTaggedAmbientSource(
                logicalPlayer, radius, NaturalAmbientAudioPresenter.FoliageSourceTag, out nearest);
            int beforeCount = Count(out _);
            ulong beforeRevision = NaturalEntityEcsService.SpatialRevision;
            if (!NaturalEntityEcsService.TryRegister(leafDefinition, leafGuid, leafWorldPosition, 26f, 0f,
                    dimensionId, leafData, out leafHandle))
                throw new InvalidOperationException("正式自然实体入口未能注册测试松树。");
            leafOwned = true;
            int afterLeafCount = Count(out _);
            ulong afterLeafRevision = NaturalEntityEcsService.SpatialRevision;
            if (!NaturalEntityEcsService.TryRegister(deadDefinition, deadGuid, deadWorldPosition, 26f, 0f,
                    dimensionId, deadData, out deadHandle))
                throw new InvalidOperationException("正式自然实体入口未能注册测试枯树。");
            deadOwned = true;
            int afterDeadCount = Count(out _);
            ulong afterDeadRevision = NaturalEntityEcsService.SpatialRevision;
            NaturalEntityEcsService.Remove(leafHandle);
            leafOwned = false;
            int afterRemoveLeafCount = Count(out _);
            ulong afterRemoveLeafRevision = NaturalEntityEcsService.SpatialRevision;
            result = new JObject
            {
                ["worldKey"] = worldKey, ["dimensionId"] = dimensionId,
                ["leafDefinitionTagged"] = leafDefinition.HasTag(NaturalAmbientAudioPresenter.FoliageSourceTag),
                ["deadDefinitionTagged"] = deadDefinition.HasTag(NaturalAmbientAudioPresenter.FoliageSourceTag),
                ["beforeCount"] = beforeCount, ["beforeRevision"] = beforeRevision,
                ["afterLeafCount"] = afterLeafCount, ["afterLeafRevision"] = afterLeafRevision,
                ["afterDeadCount"] = afterDeadCount, ["afterDeadRevision"] = afterDeadRevision,
                ["afterRemoveLeafCount"] = afterRemoveLeafCount,
                ["afterRemoveLeafRevision"] = afterRemoveLeafRevision
            };
        }
        finally
        {
            try
            {
                if (deadOwned) NaturalEntityEcsService.Remove(deadHandle);
            }
            finally
            {
                if (leafOwned) NaturalEntityEcsService.Remove(leafHandle);
            }
        }

        result["afterCleanupCount"] = NaturalEntityEcsService.FindNearestTaggedAmbientSource(
            logicalPlayer, radius, NaturalAmbientAudioPresenter.FoliageSourceTag, out _);
        result["afterCleanupRevision"] = NaturalEntityEcsService.SpatialRevision;
        return result;
    }

    private static ItemData CreateNatureAmbienceTreeData(GameRes resources, RuntimeItemDefinition definition,
        int guid, Vector3 position)
    {
        ItemData data = resources.CreateItemData(definition.Id);
        data.Guid = guid;
        data.transform ??= new ItemTransform();
        data.transform.position = position;
        return data;
    }
    #endregion
}
