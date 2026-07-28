using FlatWorld.Dialogue;
using UnityEngine;

namespace FlatWorld.GameTest.Dialogue
{
    /// <summary>
    /// 对话冒烟测试探针：提供固定饥饿上下文并记录最终展示的台词。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DialogueSmokeTestProbe :
        MonoBehaviour,
        ICharacterSpeechContextContributor,
        ICharacterSpeechPresenter
    {
        #region 测试状态

        public int ContextOrder => 0;
        public bool IsVisible { get; private set; }
        public CharacterSpeechPriority VisiblePriority { get; private set; }
        public CharacterSpeechRequest LastRequest { get; private set; }

        #endregion

        #region 测试接口

        public void Contribute(CharacterSpeechContext context)
        {
            context.SetFact(CharacterSpeechFacts.HungerRate, "0.1");
            context.SetFact(CharacterSpeechFacts.HungerTier, "Critical");
            context.SetFact(CharacterSpeechFacts.HungerIsTakingDamage, "false");
        }

        public bool Show(CharacterSpeechRequest request)
        {
            if (request == null || !request.IsValid)
                return false;

            LastRequest = request;
            VisiblePriority = request.Priority;
            IsVisible = true;
            Debug.Log(
                $"[GameTest][Dialogue] 已展示台词：{request.SourceId} / {request.Text}",
                this);
            return true;
        }

        public void HideImmediate()
        {
            IsVisible = false;
            VisiblePriority = CharacterSpeechPriority.Ambient;
        }

        #endregion
    }
}
