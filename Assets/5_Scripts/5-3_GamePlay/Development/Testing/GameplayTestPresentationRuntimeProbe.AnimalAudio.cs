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
}
