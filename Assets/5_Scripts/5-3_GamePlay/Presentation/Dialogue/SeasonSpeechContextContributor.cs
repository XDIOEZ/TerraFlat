using UnityEngine;

namespace FlatWorld.Dialogue
{
    /// <summary>读取统一季节日历，向配置台词提供临近换季事实；后六分之一季节进入预告，随自定义季长调整。</summary>
    public sealed class SeasonSpeechContextContributor : MonoBehaviour, ICharacterSpeechContextContributor,
        ICharacterSpeechStateChangeSource
    {
        private DayTimeSystem observedTimeSystem;
        private CharacterSoliloquyController observingController;
        private string observedPreparation;

        public int ContextOrder => 160;

        public void BeginObserving(CharacterSoliloquyController controller)
        {
            EndObserving();
            observingController = controller;
            observedPreparation = ReadSeasonPreparation();
            observedTimeSystem = DayTimeSystem.Instance;
            if (observedTimeSystem != null)
                observedTimeSystem.TimeAdvanced += HandleTimeAdvanced;
        }

        public void EndObserving()
        {
            if (observedTimeSystem != null)
                observedTimeSystem.TimeAdvanced -= HandleTimeAdvanced;
            observedTimeSystem = null;
            observingController = null;
        }

        private void HandleTimeAdvanced(string sceneName, float previousTime, float currentTime)
        {
            string preparation = ReadSeasonPreparation();
            if (preparation == observedPreparation)
                return;
            observedPreparation = preparation;
            observingController?.NotifySpeechStateChanged();
        }

        /// <summary>只贡献事实，不修改世界时间或直接播放台词。</summary>
        public void Contribute(CharacterSpeechContext context)
            => context.SetFact(CharacterSpeechFacts.SeasonPreparation, ReadSeasonPreparation());

        private static string ReadSeasonPreparation()
        {
            if (DimensionManager.ExistingInstance?.ActiveDefinition?.SuppressWeather != true &&
                DayTimeSystem.Instance.TryGetCurrentSeason(out SeasonSnapshot season) && season.Progress >= 5f / 6f)
                return season.Season.ToString();
            return string.Empty;
        }
    }
}
