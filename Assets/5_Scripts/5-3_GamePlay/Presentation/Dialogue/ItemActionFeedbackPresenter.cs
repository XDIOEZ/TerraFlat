using FlatWorld.Dialogue;
using FlatWorld.Localization;
using UnityEngine;

/// <summary>玩法反馈的气泡适配器；保持表现依赖玩法的单向关系。</summary>
public static class ItemActionFeedbackPresenter
{
    /// <summary>每次进入游戏重新登记适配，支持关闭域重载的编辑器配置。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    {
        ItemActionFeedback.Requested -= Show;
        ItemActionFeedback.Requested += Show;
    }

    /// <summary>复用角色气泡和紧急台词优先级，不添加输入锁。</summary>
    private static void Show(Item actor, string message)
    {
        if (actor is not Player player || !player.IsLocalProfile) return;
        actor.GetComponentInChildren<CharacterSoliloquyController>(true)?.Say(
            FlatWorldLocalizationService.GetUiText(message), CharacterSpeechPriority.Need, 3f, "item.action");
    }
}
