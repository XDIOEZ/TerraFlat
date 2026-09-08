using UnityEngine;

namespace FlatWorld.Dialogue
{
    /// <summary>读取统一季节日历，向配置台词提供临近换季事实；后六分之一季节进入预告，随自定义季长调整。</summary>
    public sealed class SeasonSpeechContextContributor : MonoBehaviour, ICharacterSpeechContextContributor
    {
        public int ContextOrder => 160;

        /// <summary>只贡献事实，不修改世界时间或直接播放台词。</summary>
        public void Contribute(CharacterSpeechContext context)
        {
            string preparation = string.Empty;
            if (DimensionManager.ExistingInstance?.ActiveDefinition?.SuppressWeather != true &&
                DayTimeSystem.Instance.TryGetCurrentSeason(out SeasonSnapshot season) && season.Progress >= 5f / 6f)
                preparation = season.Season.ToString();
            context.SetFact(CharacterSpeechFacts.SeasonPreparation, preparation);
        }
    }
}
