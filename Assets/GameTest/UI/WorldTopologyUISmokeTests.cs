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
                "Assets/2_Prefabs/2-1_UI/MainMenu/WorldSetup/UI_NewGame.prefab");
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

        [Test]
        [Category("UI.Smoke")]
        [Category("Smoke")]
        public void WorldSeedInputIsOptionalAndFitsInsideGenerationProfile()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/2_Prefabs/2-1_UI/MainMenu/WorldSetup/UI_NewGame.prefab");
            TMP_InputField seedInput = prefab.GetComponentsInChildren<TMP_InputField>(true)
                .Single(input => input.name == GameManager.NewGameSeedInputKey);
            RectTransform seedRect = seedInput.GetComponent<RectTransform>();
            RectTransform profile = seedRect.parent as RectTransform;

            Assert.That(seedInput.contentType, Is.EqualTo(TMP_InputField.ContentType.Standard));
            Assert.That(seedInput.text, Is.Empty);
            Assert.That(seedInput.placeholder.GetComponent<TMP_Text>().text, Does.Contain("留空"));
            Assert.That(profile.name, Is.EqualTo("世界生成概览"));
            Assert.That(seedRect.anchoredPosition.x, Is.GreaterThanOrEqualTo(0f));
            Assert.That(seedRect.anchoredPosition.x + seedRect.sizeDelta.x, Is.LessThanOrEqualTo(profile.sizeDelta.x));
            Assert.That(seedRect.anchoredPosition.y - seedRect.sizeDelta.y, Is.GreaterThanOrEqualTo(-profile.sizeDelta.y));
        }
    }
}
