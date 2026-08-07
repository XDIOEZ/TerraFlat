using System.IO;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FlatWorld.GameTest.UI
{
    public sealed class WorldTopologyUISmokeTests
    {


        [Test]
        [Category("UI.Layout")]
        public void WrappedToggleFitsBetweenGenerationInputsAndProfileCard()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/2-1_UI/Menu_UI/UI_NewGame.prefab");
            RectTransform topology = prefab.GetComponentsInChildren<Toggle>(true)
                .Single(toggle => toggle.name == GameManager.NewGameTopologyToggleKey)
                .GetComponent<RectTransform>();
            RectTransform radius = prefab.GetComponentsInChildren<TMP_InputField>(true)
                .Single(input => input.name == GameManager.NewGameRadiusInputKey)
                .GetComponent<RectTransform>();
            RectTransform profile = prefab.GetComponentsInChildren<RectTransform>(true)
                .Single(rect => rect.name == "世界生成概览");
            RectTransform settings = topology.parent as RectTransform;

            float radiusBottom = radius.anchoredPosition.y - radius.sizeDelta.y;
            float toggleTop = topology.anchoredPosition.y;
            float toggleBottom = toggleTop - topology.sizeDelta.y;
            float profileTop = profile.anchoredPosition.y;
            float profileBottom = profileTop - profile.sizeDelta.y;
            float panelBottom = -settings.sizeDelta.y;

            Assert.That(toggleTop, Is.LessThan(radiusBottom));
            Assert.That(profileTop, Is.LessThan(toggleBottom));
            Assert.That(profileBottom, Is.GreaterThan(panelBottom));
            Assert.That(topology.sizeDelta.x, Is.LessThanOrEqualTo(settings.sizeDelta.x - 48f));
        }
    }
}
