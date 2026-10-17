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
            AudioCatalog startupCatalog = service.Catalog;
            string startupSettings = JsonUtility.ToJson(service.UserSettings);
            yield return AudioService.PreloadAsync();
            yield return AudioService.PreloadAsync();
            // 重复预热不能替换正式服务、目录或覆盖用户设置。
            bool startupServiceStable = ReferenceEquals(service, AudioService.Instance);
            bool startupCatalogStable = ReferenceEquals(startupCatalog, service.Catalog);
            bool startupSettingsStable = startupSettings == JsonUtility.ToJson(service.UserSettings);
            bool startupUiCueReady = service.TryGetCue("ui.click", out AudioCue click) &&
                click.Clips.Count > 0 && click.Clips.All(clip => clip != null &&
                    clip.loadState == AudioDataLoadState.Loaded);
            AudioCue loop = fixture.CloneCue(AudioEventIds.PlayerWetClothesMoveLoop);
            AudioCue shortCue = fixture.CloneCue(AudioEventIds.DoorOpen);
            JArray feedbackCues = new JArray();
            foreach (string cueId in new[]
                     {
                         AudioEventIds.BuildingPlace, AudioEventIds.BuildingReject,
                         AudioEventIds.CraftComplete, AudioEventIds.SmeltComplete,
                         AudioEventIds.FireSeed, AudioEventIds.FurnaceIgnite
                     })
            {
                // 读取 Unity 实际导入的音频，避免只检查磁盘有文件却遗漏可播放的 Cue。
                bool exists = service.TryGetCue(cueId, out AudioCue feedback);
                feedbackCues.Add(new JObject
                {
                    ["id"] = cueId,
                    ["loaded"] = exists,
                    ["variants"] = exists ? feedback.Clips.Count : 0,
                    ["validClips"] = exists && feedback.Clips.All(
                        clip => clip != null && clip.channels == 1 &&
                                clip.frequency == 48000 && clip.samples > 0),
                    ["spatial"] = exists && feedback.SpatialBlend == 1f &&
                                   feedback.Bus == AudioBus.Sfx && feedback.Cooldown > 0f
                });
            }
            JArray movementCues = new JArray();
            foreach (string cueId in new[]
                     {
                         AudioEventIds.FootstepDirt, AudioEventIds.FootstepGrass,
                         AudioEventIds.FootstepSand, AudioEventIds.FootstepStone,
                         AudioEventIds.FootstepSnow, AudioEventIds.FootstepWater,
                         AudioEventIds.FishingCast, AudioEventIds.FishingHook,
                         AudioEventIds.FishingReel
                     })
            {
                bool loaded = service.TryGetCue(cueId, out AudioCue cue);
                bool isFootstep = cueId.StartsWith("player.footstep.", StringComparison.Ordinal);
                movementCues.Add(new JObject
                {
                    ["id"] = cueId,
                    ["loaded"] = loaded,
                    ["expectedVariants"] = isFootstep ? 3 : 2,
                    ["variants"] = loaded ? cue.Clips.Count : 0,
                    ["validClips"] = loaded && cue.Clips.All(
                        clip => clip != null && clip.channels == 1 &&
                                clip.frequency == 48000 && clip.samples > 0),
                    ["spatial"] = loaded && cue.SpatialBlend == 1f &&
                                   cue.Bus == AudioBus.Sfx && cue.Cooldown > 0f &&
                                   cue.MaxInstances == (isFootstep ? 2 : 3)
                });
            }
            JArray swimmingCues = new JArray();
            foreach (string cueId in new[] { "player.swim.enter", "player.swim.stroke", "player.swim.exit" })
            {
                // 只读取正式目录和导入结果，变体、格式与限流要求交给 Lua 断言。
                bool loaded = service.TryGetCue(cueId, out AudioCue cue);
                JArray clips = new JArray();
                if (loaded)
                    foreach (AudioClip clip in cue.Clips)
                        clips.Add(new JObject
                        {
                            ["loaded"] = clip != null,
                            ["channels"] = clip != null ? clip.channels : 0,
                            ["frequency"] = clip != null ? clip.frequency : 0,
                            ["samples"] = clip != null ? clip.samples : 0
                        });
                swimmingCues.Add(new JObject
                {
                    ["id"] = cueId, ["loaded"] = loaded, ["clips"] = clips,
                    ["bus"] = loaded ? cue.Bus.ToString() : string.Empty,
                    ["spatialBlend"] = loaded ? cue.SpatialBlend : 0f,
                    ["loop"] = loaded && cue.Loop,
                    ["cooldown"] = loaded ? cue.Cooldown : 0f,
                    ["maxInstances"] = loaded ? cue.MaxInstances : 0,
                    ["minDistance"] = loaded ? cue.MinDistance : 0f,
                    ["maxDistance"] = loaded ? cue.MaxDistance : 0f
                });
            }
            int inventoryUiCueCount = 0;
            bool inventoryUiCuesValid = true;
            foreach (string action in new[] { "pickup", "backpack", "chest" })
            foreach (string material in new[] { "soft", "wood", "stone", "metal" })
            foreach (string weight in new[] { "light", "medium", "heavy" })
            foreach (string volume in new[] { "compact", "bulky" })
            {
                string id = $"ui.inventory.{action}.{material}.{weight}.{volume}";
                bool loaded = service.TryGetCue(id, out AudioCue cue);
                inventoryUiCueCount++;
                inventoryUiCuesValid &= loaded && cue.Bus == AudioBus.UI &&
                    cue.SpatialBlend == 0f && !cue.Loop && cue.Clips.Count == 1 &&
                    cue.Clips[0] != null && cue.Clips[0].channels == 1 &&
                    cue.Clips[0].frequency == 48000 && cue.Clips[0].samples > 0;
            }
            foreach (string id in new[] { "ui.inventory.swap", "ui.equipment.equip", "ui.equipment.unequip" })
            {
                bool loaded = service.TryGetCue(id, out AudioCue cue);
                inventoryUiCueCount++;
                inventoryUiCuesValid &= loaded && cue.Bus == AudioBus.UI &&
                    cue.SpatialBlend == 0f && cue.Clips.Count == 2 &&
                    cue.Clips.All(clip => clip != null && clip.channels == 1 &&
                                          clip.frequency == 48000 && clip.samples > 0);
            }
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
                ["startupServiceStable"] = startupServiceStable, ["startupCatalogStable"] = startupCatalogStable,
                ["startupSettingsStable"] = startupSettingsStable, ["startupUiCueReady"] = startupUiCueReady,
                ["feedbackCues"] = feedbackCues,
                ["movementCues"] = movementCues,
                ["swimmingCues"] = swimmingCues,
                ["inventoryUiCueCount"] = inventoryUiCueCount,
                ["inventoryUiCuesValid"] = inventoryUiCuesValid,
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
