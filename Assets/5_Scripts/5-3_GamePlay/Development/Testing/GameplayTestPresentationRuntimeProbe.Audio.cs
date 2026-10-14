using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FlatWorld.Audio;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestPresentationRuntimeProbe
{
    #region 正式音频服务与自有播放租约
    private sealed class AudioFixture : IDisposable
    {
        public readonly AudioService Service;
        public readonly AudioUserSettings Original;
        public readonly List<AudioHandle> Handles = new();
        public readonly List<AudioCue> Cues = new();
        private bool disposed;

        public AudioFixture(Player player)
        {
            GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
            Service = UnityEngine.Object.FindObjectOfType<AudioService>() ??
                throw new InvalidOperationException("真实音频服务未建立，不能使用独立假服务替代。");
            Original = JsonUtility.FromJson<AudioUserSettings>(JsonUtility.ToJson(Service.UserSettings));
        }

        public AudioCue CloneCue(string id)
        {
            if (!Service.TryGetCue(id, out AudioCue original) || original.Clips.Count == 0 || original.Clips.Any(value => value == null))
                throw new InvalidOperationException("正式音频目录缺少可播放的测试 Cue：" + id);
            AudioCue cue = UnityEngine.Object.Instantiate(original);
            cue.hideFlags = HideFlags.HideAndDontSave;
            // 只隔离 Cue 身份，正式录音、总线、空间范围和池策略均保留资源原值。
            JsonUtility.FromJsonOverwrite("{\"id\":\"lua.presentation." + Guid.NewGuid().ToString("N") + "\"}", cue);
            Cues.Add(cue);
            return cue;
        }

        public AudioHandle Play(AudioCue cue, AudioPlayOptions options)
        {
            if (Service.ActiveVoiceCount >= Service.MaxVoices)
                throw new InvalidOperationException("真实声源池已满，拒绝抢占其他来源的声音。");
            AudioHandle handle = Service.Play(cue, options);
            Handles.Add(handle);
            return handle;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (AudioHandle handle in Handles) Service.Stop(handle);
            try
            {
                Service.SetMasterVolume(Original.Master, false);
                foreach (AudioBus bus in Enum.GetValues(typeof(AudioBus)))
                    Service.SetBusVolume(bus, Original.GetBusVolume(bus), false);
                Service.SetMuted(Original.Muted, false);
            }
            finally
            {
                foreach (AudioCue cue in Cues) if (cue != null) UnityEngine.Object.Destroy(cue);
            }
        }
    }

    private static IEnumerator CaptureAudio(Player player, JObject args, Action<JObject> complete)
    {
        var fixture = new AudioFixture(player);
        GameplayTestRuntimeProbe.RegisterCleanup(fixture);
        JObject result = null;
        try
        {
            AudioService service = fixture.Service;
            AudioCue loop = fixture.CloneCue(AudioEventIds.PlayerWetClothesMoveLoop);
            AudioCue shortCue = fixture.CloneCue(AudioEventIds.DoorOpen);
            AudioListener[] listeners = UnityEngine.Object.FindObjectsOfType<AudioListener>()
                .Where(value => value.isActiveAndEnabled).ToArray();
            if (listeners.Length != 1) throw new InvalidOperationException("真实现场需要唯一启用的 AudioListener。");
            AudioListener listener = listeners[0];
            service.SetMasterVolume(.8f, false);
            service.SetBusVolume(loop.Bus, .75f, false);
            service.SetMuted(false, false);
            yield return null;
            var alreadyPlaying = new HashSet<AudioSource>(service.GetComponentsInChildren<AudioSource>(true).Where(value => value.isPlaying));
            int nearPlayFrame = Time.frameCount;
            AudioHandle handle = fixture.Play(loop, AudioPlayOptions.Attached(player.transform));
            if (!handle.IsValid) throw new InvalidOperationException("正式角色循环声音没有取得播放句柄。");
            AudioSource source = service.GetComponentsInChildren<AudioSource>(true)
                .SingleOrDefault(value => value.loop && loop.Clips.Contains(value.clip) && value.isPlaying && !alreadyPlaying.Contains(value));
            if (source == null) throw new InvalidOperationException("播放句柄没有对应真正播放录音的 AudioSource。");
            // Play 已同步定位声源；同帧比较听者，避免协程先于下一次 LateUpdate 读取移动中的旧坐标。
            Vector3 nearOffset = source.transform.position - listener.transform.position;
            int nearSampleFrame = Time.frameCount;
            float originalVolume = source.volume;
            service.SetVolumeScale(handle, .5f);
            float scaledVolume = source.volume;
            service.SetMasterVolume(.4f, false);
            float masterVolume = source.volume;
            service.SetBusVolume(loop.Bus, .25f, false);
            float busVolume = source.volume;
            service.SetMuted(true, false);
            float mutedVolume = source.volume;
            bool retainedWhenMuted = handle.IsPlaying;
            service.SetMuted(false, false);
            float unmutedVolume = source.volume;
            int beforeFarShort = service.ActiveVoiceCount;
            AudioHandle farShort = fixture.Play(shortCue,
                AudioPlayOptions.At(player.transform.position + Vector3.right * (shortCue.MaxDistance + 2f)));
            int afterFarShort = service.ActiveVoiceCount;
            Vector3 farPosition = player.transform.position + Vector3.right * (loop.MaxDistance + 2f);
            var beforeFarLoop = new HashSet<AudioSource>(service.GetComponentsInChildren<AudioSource>(true).Where(value => value.isPlaying));
            int farPlayFrame = Time.frameCount;
            AudioHandle farLoop = fixture.Play(loop, AudioPlayOptions.At(farPosition));
            AudioSource[] loopSources = service.GetComponentsInChildren<AudioSource>(true)
                .Where(value => value.loop && loop.Clips.Contains(value.clip) && value.isPlaying && !beforeFarLoop.Contains(value)).ToArray();
            if (loopSources.Length != 1) throw new InvalidOperationException("远处循环没有对应唯一的正式空间音源。");
            AudioSource farSource = loopSources[0];
            Vector3 farOffset = farSource.transform.position - listener.transform.position;
            int farSampleFrame = Time.frameCount;
            bool nearPlaying = source.isPlaying && handle.IsPlaying;
            bool nearSpatial = source.spatialBlend == 1f && source.rolloffMode == AudioRolloffMode.Linear;
            bool distancePreserved = source.minDistance == loop.MinDistance && source.maxDistance == loop.MaxDistance;
            service.Stop(farLoop);
            service.Stop(handle);
            bool released = !handle.IsPlaying && !source.isPlaying && source.clip == null;
            AudioHandle replacement = fixture.Play(loop, AudioPlayOptions.Attached(player.transform));
            bool sourceReused = source.isPlaying && source.clip != null;
            service.Stop(handle);
            bool replacementSurvivedOldStop = replacement.IsPlaying && source.isPlaying;
            result = new JObject
            {
                ["formalCatalog"] = service.Catalog != null, ["clipCount"] = loop.Clips.Count,
                ["clipSamples"] = source.clip != null ? source.clip.samples : 0,
                ["playing"] = nearPlaying, ["spatialLinear"] = nearSpatial, ["cueDistancesPreserved"] = distancePreserved,
                ["sourceUnmuted"] = !source.mute,
                ["nearPlayFrame"] = nearPlayFrame, ["nearSampleFrame"] = nearSampleFrame,
                ["farPlayFrame"] = farPlayFrame, ["farSampleFrame"] = farSampleFrame,
                ["nearOffsetX"] = nearOffset.x, ["nearOffsetY"] = nearOffset.y, ["nearOffsetZ"] = nearOffset.z,
                ["initialVolume"] = originalVolume, ["scaledVolume"] = scaledVolume,
                ["masterVolume"] = masterVolume, ["busVolume"] = busVolume,
                ["mutedVolume"] = mutedVolume, ["retainedWhenMuted"] = retainedWhenMuted, ["unmutedVolume"] = unmutedVolume,
                ["farShortAccepted"] = farShort.IsValid, ["beforeFarShort"] = beforeFarShort, ["afterFarShort"] = afterFarShort,
                ["farLoopAccepted"] = farLoop.IsValid,
                ["farOffsetX"] = farOffset.x, ["expectedFarOffsetX"] = loop.MaxDistance + 2f,
                ["farOffsetY"] = farOffset.y, ["farOffsetZ"] = farOffset.z,
                ["released"] = released, ["sourceReused"] = sourceReused,
                ["newHandleDistinct"] = replacement != handle, ["replacementSurvivedOldStop"] = replacementSurvivedOldStop
            };
        }
        finally { fixture.Dispose(); }
        result["handlesReleased"] = fixture.Handles.All(value => !value.IsPlaying);
        result["settingsRestored"] = JsonUtility.ToJson(fixture.Service.UserSettings) == JsonUtility.ToJson(fixture.Original);
        complete(result);
    }
    #endregion
}
