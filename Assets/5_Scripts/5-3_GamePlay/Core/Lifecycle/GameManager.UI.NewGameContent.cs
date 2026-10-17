using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class GameManager
{
    #region 新世界页面按需绑定

    private static bool HasNewGameWorldContent(BasePanel panel)
    {
        return panel != null && panel.GetComponent<NewGameDeferredContent>()?.HasWorldContent == true;
    }

    private void EnsureNewGameWorldContent(BasePanel panel)
    {
        NewGameDeferredContent content = panel.GetComponent<NewGameDeferredContent>();
        if (content == null || !content.EnsureWorldContent(panel))
            return;

        TMP_InputField radiusInput = panel.GetInputField(NewGameRadiusInputKey);
        TMP_InputField spatialInput = panel.GetInputField(NewGameSpatialDistanceScaleInputKey);
        TMP_InputField chunkWidthInput = panel.GetInputField(NewGameChunkWidthInputKey);
        TMP_InputField chunkHeightInput = panel.GetInputField(NewGameChunkHeightInputKey);
        Toggle topologyToggle = panel.GetToggle(NewGameTopologyToggleKey);
        radiusInput?.SetTextWithoutNotify(ReadyPlanetData.Radius.ToString(CultureInfo.InvariantCulture));
        spatialInput?.SetTextWithoutNotify(ReadyPlanetData.SpatialDistanceScale.ToString("0.##", CultureInfo.InvariantCulture));
        chunkWidthInput?.SetTextWithoutNotify(ReadyPlanetData.ChunkSize.x.ToString(CultureInfo.InvariantCulture));
        chunkHeightInput?.SetTextWithoutNotify(ReadyPlanetData.ChunkSize.y.ToString(CultureInfo.InvariantCulture));
        topologyToggle?.SetIsOnWithoutNotify(pendingNewWorldTopology == WorldTopologyMode.Wrapped);
        if (radiusInput != null)
            radiusInput.interactable = pendingNewWorldTopology == WorldTopologyMode.Wrapped;
        radiusInput?.onValueChanged.AddListener(OnPlanetRadiusChanged);
        spatialInput?.onValueChanged.AddListener(OnPlanetSpatialDistanceScaleChanged);
        chunkWidthInput?.onValueChanged.AddListener(OnPlanetChunkWidthChanged);
        chunkHeightInput?.onValueChanged.AddListener(OnPlanetChunkHeightChanged);
        topologyToggle?.onValueChanged.AddListener(isOn => OnWorldTopologyChanged(panel, isOn));
    }

    private void EnsureNewGameDifficultyContent(BasePanel panel)
    {
        NewGameDeferredContent content = panel.GetComponent<NewGameDeferredContent>();
        if (content == null || !content.EnsureDifficultyContent(panel))
            return;

        panel.SetButtonOnClick(NewGameDifficultyCloseButtonKey, () => CloseNewGameDifficultyPanel(panel));
        panel.SetButtonOnClick(NewGameDifficultyConfirmButtonKey, () => ConfirmNewGameDifficulty(panel));
        for (int i = 0; i < GameDifficultyCatalog.All.Count; i++)
        {
            GameDifficultyId difficulty = GameDifficultyCatalog.All[i].Id;
            panel.SetButtonOnClick(GetNewGameDifficultyPresetButtonKey(difficulty),
                () => SelectNewGameDifficulty(panel, difficulty));
        }
    }

    #endregion
}
