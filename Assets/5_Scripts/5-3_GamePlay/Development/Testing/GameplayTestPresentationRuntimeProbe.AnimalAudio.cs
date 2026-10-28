using System;
using System.Linq;
using FlatWorld.Audio;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestPresentationRuntimeProbe
{
    #region 正式动物待机声音资源采样

    private static JArray CaptureAnimalIdleCues(AudioService service)
    {
        GameRes resources = GameRes.ExistingInstance;
        if (resources == null)
            throw new InvalidOperationException("动物声音资源采样需要正式已加载的 GameRes。");

        var result = new JArray();
        foreach (string actorId in new[]
                 { "Chicken", "WildBoar", "Wolf", "Bird", "Seagull", "Bee", "Sheep", "Rabbit", "SnowLeopard", "SmallFish" })
        {
            bool actorLoaded = resources.TryGetItemDefinition(actorId, out RuntimeItemDefinition definition);
            RuntimeItemModuleDefinition[] modules = actorLoaded
                ? definition.ModuleDefinitions.Where(entry => entry.ModuleId == Mod_AnimalAmbientAudio.ModuleId).ToArray()
                : Array.Empty<RuntimeItemModuleDefinition>();
            RuntimeItemModuleDefinition module = modules.Length == 1 ? modules[0] : null;
            JObject parameters = string.IsNullOrWhiteSpace(module?.ParametersJson)
                ? new JObject() : JObject.Parse(module.ParametersJson);
            RuntimeItemModuleAssemblyPlan assembly = null;
            if (module != null) definition.TryGetModuleAssembly(module.StableName, out assembly);
            string cueId = parameters.Value<string>("cueId") ?? string.Empty;
            bool loaded = service.TryGetCue(cueId, out AudioCue cue);
            var clips = new JArray();
            // 读取最终合并的 Actor 模块与 Unity 导入资源，业务要求统一交给 Lua 断言。
            if (loaded)
                foreach (AudioClip clip in cue.Clips)
                    clips.Add(new JObject
                    {
                        ["loaded"] = clip != null,
                        ["channels"] = clip != null ? clip.channels : 0,
                        ["frequency"] = clip != null ? clip.frequency : 0,
                        ["samples"] = clip != null ? clip.samples : 0
                    });
            result.Add(new JObject
            {
                ["actorId"] = actorId, ["actorLoaded"] = actorLoaded,
                ["isActor"] = actorLoaded && definition.IsActor,
                ["moduleCount"] = modules.Length,
                ["moduleEnabled"] = module?.Enabled == true,
                ["stableName"] = module?.StableName ?? string.Empty,
                ["implementation"] = assembly?.ModuleType?.Name ?? string.Empty,
                ["parameters"] = parameters,
                ["loaded"] = loaded, ["cueId"] = loaded ? cue.Id : string.Empty,
                ["clips"] = clips,
                ["bus"] = loaded ? cue.Bus.ToString() : string.Empty,
                ["loop"] = loaded && cue.Loop,
                ["spatialBlend"] = loaded ? cue.SpatialBlend : 0f,
                ["rolloff"] = loaded ? cue.RolloffMode.ToString() : string.Empty,
                ["cooldown"] = loaded ? cue.Cooldown : -1f,
                ["maxInstances"] = loaded ? cue.MaxInstances : 0,
                ["minDistance"] = loaded ? cue.MinDistance : 0f,
                ["maxDistance"] = loaded ? cue.MaxDistance : 0f
            });
        }
        return result;
    }

    #endregion

    #region 动物声音传播边界现场采样

    private static JArray CaptureAnimalPropagationRanges(AudioFixture fixture, Player player)
    {
        var result = new JArray();
        foreach (string id in new[] { "animal.wolf.idle", "animal.bird.idle", "animal.bee.idle", "animal.wildboar.idle" })
        {
            AudioCue cue = fixture.CloneCue(id);
            foreach (float distance in new[] { cue.MaxDistance * 0.75f, cue.MaxDistance - 0.5f, cue.MaxDistance, cue.MaxDistance + 1f })
            {
                // 每个距离使用独立身份，不受上一次成功播放的冷却影响。
                cue = fixture.CloneCue(id);
                AudioSource[] occupied = fixture.Service.GetComponentsInChildren<AudioSource>(true)
                    .Where(source => source.isPlaying).ToArray();
                int voicesBefore = fixture.Service.ActiveVoiceCount;
                // 真实播放器决定是否分配声源，Lua 检查格数边界与 Unity 实际衰减参数。
                AudioHandle handle = fixture.Play(cue, AudioPlayOptions.At(player.transform.position + Vector3.right * distance));
                AudioSource source = fixture.Service.GetComponentsInChildren<AudioSource>(true)
                    .SingleOrDefault(value => value.isPlaying && !occupied.Contains(value) && cue.Clips.Contains(value.clip));
                result.Add(new JObject
                {
                    ["id"] = id, ["distance"] = distance, ["accepted"] = handle.IsValid,
                    ["voicesBefore"] = voicesBefore, ["voicesAfter"] = fixture.Service.ActiveVoiceCount,
                    ["sourceFound"] = source != null,
                    ["minDistance"] = source != null ? source.minDistance : 0f,
                    ["maxDistance"] = source != null ? source.maxDistance : 0f,
                    ["spatialBlend"] = source != null ? source.spatialBlend : 0f,
                    ["rolloff"] = source != null ? source.rolloffMode.ToString() : string.Empty
                });
                fixture.Service.Stop(handle);
            }
        }
        return result;
    }

    #endregion
}
