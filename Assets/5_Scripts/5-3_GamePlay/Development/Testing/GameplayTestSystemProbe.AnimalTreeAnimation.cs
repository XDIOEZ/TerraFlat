using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 动物行为树动画回退
    // 真实单图模块接收树节点动画，验证缺失状态按动作角色回退。
    private static JObject AiAnimalBehaviorTreeAnimation(JObject args)
    {
        var root = new GameObject("[LuaAnimalTreeAnimation]") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        var texture = new Texture2D(2, 2);
        Sprite stand = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * .5f);
        Sprite sleep = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * .5f);
        Sprite move = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * .5f);
        AnimalBehaviorTreeContext context = null;
        AnimalMateAction mate = null;
        Mod_AnimatorController animation = null;
        try
        {
            GameItem actor = root.AddComponent<GameItem>();
            actor.BindData(EntityProbeData("test:animal_tree_animation", 1f));
            var mover = root.AddComponent<Mod_Mover_AI>();
            mover.ModuleInit(actor, new Ex_ModData_MemoryPackable { Enabled = true });
            actor.itemMods.AddMod(mover);
            var visual = new GameObject("visual");
            visual.transform.SetParent(root.transform, false);
            SpriteRenderer renderer = visual.AddComponent<SpriteRenderer>();
            renderer.sprite = stand;
            animation = root.AddComponent<Mod_AnimatorController>();
            animation.ModuleInit(actor, new Ex_ModData_MemoryPackable { Enabled = true });
            actor.itemMods.AddMod(animation);
            animation.visualMode = Mod_AnimatorController.VisualMode.SingleSprite;
            animation.targetSpriteRenderer = renderer;
            animation.spriteVisualRoot = visual.transform;
            animation.singleSpriteStates = new[]
            {
                new Mod_AnimatorController.SingleSpriteState { stateName = "Stand", sprite = stand },
                new Mod_AnimatorController.SingleSpriteState { stateName = "Sleep", sprite = sleep },
                new Mod_AnimatorController.SingleSpriteState { stateName = "Move", sprite = move }
            };
            root.SetActive(true);
            animation.SetNetworkPresentation(true, false);
            context = new AnimalBehaviorTreeContext(actor, mover, null, null, null, animation, 5f, .8f, null, 0f, 0f, default);
            context.BeginAction("sleep", "Sleep", AIStateAnimationRole.Stopped);
            bool explicitState = renderer.sprite == sleep;
            context.BeginAction("missingSit", "Sit", AIStateAnimationRole.Stopped);
            bool stoppedFallback = renderer.sprite == stand;
            context.BeginAction("missingMove", "MissingMovement", AIStateAnimationRole.Moving);
            bool movingFallback = renderer.sprite == move;
            mate = new AnimalMateAction(context, new JObject());
            AnimalBehaviorTreeStatus status = mate.Tick(0f);
            return new JObject { ["explicitState"] = explicitState, ["stoppedFallback"] = stoppedFallback,
                ["movingFallback"] = movingFallback,
                ["actualNode"] = status == AnimalBehaviorTreeStatus.Running && renderer.sprite == stand &&
                    context.CurrentAction == "mate" && context.AnimationRole == AIStateAnimationRole.Stopped };
        }
        finally
        {
            mate?.Abort();
            context?.Dispose();
            animation?.SetNetworkPresentation(false, false);
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(stand);
            UnityEngine.Object.DestroyImmediate(sleep);
            UnityEngine.Object.DestroyImmediate(move);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
    #endregion
}
