using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 正式生物单图动画配置采样

    private static void RegisterAiAnimationOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("ai/single_sprite_catalog", EntityAiSingleSpriteCatalog);
        operations.Add("ai/single_sprite_aquatic", EntityAiSingleSpriteAquatic);
    }

    // 正式加载器负责 Manifest 和继承，候选上下文保证规则不替换正在使用的目录缓存。
    private static JObject EntityAiSingleSpriteCatalog(JObject args)
    {
        List<ItemDefinitionDto> definitions = null;
        var candidate = new ResourceReloadContext();
        ActorDefinitionCatalogLoader.ConfigureResourceReload(candidate);
        candidate.InCandidate(() => definitions = ActorDefinitionCatalogLoader.LoadBuiltInDefinitions());
        var actors = new JArray();
        foreach (ItemDefinitionDto definition in definitions)
        {
            if (definition.Abstract) continue;
            var sample = new JObject { ["id"] = definition.Id };
            actors.Add(sample);
            bool hasModule = definition.Modules.TryGetValue("animator", out ItemModuleDefinitionDto configured);
            sample["hasAnimatorModule"] = hasModule;
            if (!hasModule) continue;

            GameObject shell;
            GameObject controllerPrefab;
#if UNITY_EDITOR
            shell = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(definition.SourcePrefab);
            controllerPrefab = configured.Prefab == "Module_SingleSpriteController"
                ? UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/2_Prefabs/Gameplay/Modules/Animation/Module_SingleSpriteController.prefab") : null;
#else
            shell = GameRes.ExistingInstance?.GetPrefab(definition.ShellPrefab, false);
            controllerPrefab = GameRes.ExistingInstance?.GetPrefab(configured.Prefab, false);
#endif
            Module prototype = ItemDefinitionCatalogLoader.FindModulePrototype(shell, controllerPrefab,
                string.IsNullOrWhiteSpace(configured.Id) ? configured.Prefab : configured.Id);
            sample["hasControllerPrototype"] = prototype is Mod_AnimatorController;
            sample["enabled"] = configured.Enabled.HasValue ? new JValue(configured.Enabled.Value) : JValue.CreateNull();
            sample["bodyRendererCount"] = shell != null ? shell.GetComponentsInChildren<SpriteRenderer>(true).Length : -1;
            sample["moduleRendererCount"] = controllerPrefab != null ? controllerPrefab.GetComponentsInChildren<Renderer>(true).Length : -1;
            sample["moduleColliderCount"] = controllerPrefab != null ? controllerPrefab.GetComponentsInChildren<Collider2D>(true).Length : -1;
            sample["moduleBodyCount"] = controllerPrefab != null ? controllerPrefab.GetComponentsInChildren<Rigidbody2D>(true).Length : -1;
            if (prototype is not Mod_AnimatorController) continue;

            var root = new GameObject("[LuaActorAnimationConfiguration]") { hideFlags = HideFlags.HideAndDontSave };
            root.SetActive(false);
            try
            {
                Module instance = (Module)root.AddComponent(prototype.GetType());
                ModuleJsonConfigurator.Apply(instance, definition.Id, "animator", configured.Id,
                    configured.Parameters?.ToString());
                sample["visualMode"] = (int)((Mod_AnimatorController)instance).visualMode;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        return new JObject { ["actors"] = actors };
    }

    #endregion

    #region 水生表现的单图接管采样

    private static JObject EntityAiSingleSpriteAquatic(JObject args)
    {
#if UNITY_EDITOR
        GameObject shell = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/2_Prefabs/Gameplay/AI/SmallFish.prefab");
#else
        GameObject shell = GameRes.ExistingInstance?.GetPrefab("CoreActor.SmallFish.Shell", false);
#endif
        Material material = shell != null ? shell.GetComponentInChildren<SpriteRenderer>(true)?.sharedMaterial : null;
        if (material == null) throw new InvalidOperationException("水生单图采样缺少正式小鱼主体材质。");
        var root = new GameObject("[LuaAquaticAnimation]") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        AquaticActorPresentation presentation = null;
        try
        {
            GameItem item = root.AddComponent<GameItem>();
            item.BindData(EntityProbeData("test:aquatic_animation", 1f));
            var visual = new GameObject("FishVisual");
            visual.transform.SetParent(root.transform, false);
            visual.transform.localPosition = new Vector3(.13f, .17f, 0f);
            visual.transform.localRotation = Quaternion.Euler(0f, 0f, 7f);
            Vector3 originalPosition = visual.transform.localPosition;
            Quaternion originalRotation = visual.transform.localRotation;
            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sharedMaterial = material;
            Mod_AnimatorController animation = root.AddComponent<Mod_AnimatorController>();
            animation.ModuleInit(item, new Ex_ModData_MemoryPackable { Enabled = true });
            animation.visualMode = Mod_AnimatorController.VisualMode.SingleSprite;
            item.itemMods.AddMod(animation);
            presentation = new AquaticActorPresentation(item, renderer);
            Vector3 commandedPosition = Vector3.right;
            root.transform.position = commandedPosition;
            JObject Capture()
            {
                Vector3 offset = presentation.VisualWorldOffset;
                return new JObject
                {
                    ["singleSprite"] = presentation.UseSingleSpriteAnimation,
                    ["swimming"] = presentation.IsSwimming,
                    ["height"] = offset.y, ["roll"] = presentation.VisualRollDegrees,
                    ["positionError"] = Vector3.Distance(originalPosition, visual.transform.localPosition),
                    ["rotationError"] = Quaternion.Angle(originalRotation, visual.transform.localRotation),
                    ["rootPositionError"] = Vector3.Distance(commandedPosition, root.transform.position)
                };
            }
            presentation.Tick(true, .08f);
            var result = new JObject { ["swimming"] = Capture() };
            presentation.SetStrandedHop(.24f, 18f);
            presentation.Tick(false, .08f);
            result["stranded"] = Capture();
            presentation.SetCarried(true, .4f);
            presentation.Tick(false, .08f);
            result["carried"] = Capture();
            presentation.SetCarried(false, 0f);
            presentation.SetStrandedHop(0f, 0f);
            animation.visualMode = Mod_AnimatorController.VisualMode.Animator;
            commandedPosition = Vector3.right * 2f;
            root.transform.position = commandedPosition;
            presentation.Tick(true, .08f);
            result["animatorSwimming"] = Capture();
            return result;
        }
        finally
        {
            try { presentation?.Dispose(); }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
    }

    #endregion
}
