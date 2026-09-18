using System.Collections;
using DG.Tweening;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace FlatWorld.GameTest.UI
{
    /// <summary>测试探针只观察基类实际生成的 Tween，不改变生产代码接口。</summary>
    public sealed class UIAnimationTestProbe : BaseUIAnimation
    {
        public Tween LastTween { get; private set; }

        protected override Tween CreateTween(bool opening, float duration)
        {
            LastTween = base.CreateTween(opening, duration);
            return LastTween;
        }
    }

    /// <summary>验证 BasePanel 直接调用动画组件、配置解析、重入和真实 Tween 播放。</summary>
    [Category("UI.Animation")]
    public sealed class UIAnimationTests
    {
        #region 测试环境
        private GameObject parent;
        private GameObject owner;
        private BasePanel panel;
        private CanvasGroup group;
        private UIAnimationTestProbe animation;
        private UIAnimationManager manager;
        private Tween unrelatedTween;
        private float originalTimeScale;
        private int initialRegisteredCount;

        [SetUp]
        public void SetUp()
        {
            originalTimeScale = Time.timeScale;
            manager = UIAnimationManager.Instance;
            manager.SetAnimationsEnabled(true);
            manager.SetPlaybackSpeed(1f);
            Assert.That(manager.ReloadDefaultConfiguration(), Is.True);
            initialRegisteredCount = manager.RegisteredCount;

            parent = new GameObject("UIAnimationTests", typeof(RectTransform));
            new GameObject("Sibling", typeof(RectTransform)).transform.SetParent(parent.transform, false);

            owner = new GameObject("Panel", typeof(RectTransform), typeof(CanvasGroup));
            owner.SetActive(false);
            owner.transform.SetParent(parent.transform, false);
            group = owner.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            // BasePanel 缓存同级 BUA，因此测试也按正式 Prefab 的组件顺序先准备动画组件。
            animation = owner.AddComponent<UIAnimationTestProbe>();
            panel = owner.AddComponent<BasePanel>();
            panel.Init();
            owner.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = originalTimeScale;
            unrelatedTween?.Kill();
            Object.DestroyImmediate(parent);
            manager.SetAnimationsEnabled(true);
            manager.SetPlaybackSpeed(1f);
            manager.ReloadDefaultConfiguration();
        }

        private static string Config(string data)
        {
            return "{\"version\":1,\"defaultId\":\"panel.default\",\"profiles\":[{\"id\":\"panel.default\",\"data\":{" + data + "}}]}";
        }
        #endregion

        #region 生命周期与直接调用
        [Test]
        public void NoAnimationKeepsImmediateLegacyBehavior()
        {
            Object.DestroyImmediate(animation);
            panel.Open();
            Assert.That(group.alpha, Is.EqualTo(1f));
            panel.Close();
            Assert.That(group.alpha, Is.Zero);
            Assert.That(panel.IsVisualTransitioning, Is.False);
        }

        [Test]
        public void ClosingReleasesInputImmediatelyAndHidesAfterAnimation()
        {
            panel.Open();
            animation.CompleteAnimation();

            panel.Close();
            Assert.That(panel.IsOpen(), Is.False);
            Assert.That(group.interactable || group.blocksRaycasts, Is.False);
            Assert.That(group.alpha, Is.EqualTo(1f));

            animation.CompleteAnimation();
            Assert.That(group.alpha, Is.Zero);
            Assert.That(owner.transform.GetSiblingIndex(), Is.Zero);
        }

        [Test]
        public void ReversalKeepsCurrentVisualAndInvalidatesOldTween()
        {
            panel.Open();
            Tween old = animation.LastTween;
            old.Goto(0.07f, false);
            float alpha = group.alpha;
            Assert.That(alpha, Is.InRange(0.01f, 0.99f));

            panel.Close();
            Assert.That(group.alpha, Is.EqualTo(alpha).Within(0.0001f));
            Assert.That(old.IsActive(), Is.False);

            panel.Open();
            Assert.That(group.alpha, Is.EqualTo(alpha).Within(0.0001f));
            animation.CompleteAnimation();
            Assert.That(group.alpha, Is.EqualTo(1f));
        }

        [Test]
        public void RepeatedOpenDoesNotRestartAnimation()
        {
            panel.Open();
            Tween first = animation.LastTween;
            panel.Init();
            panel.Open();
            Assert.That(animation.LastTween, Is.SameAs(first));
            Assert.That(panel.IsOpen(), Is.True);
        }

        [Test]
        public void DisablingAnimationCompletesCurrentTransitionAndFallsBackToImmediateBehavior()
        {
            panel.Open();
            animation.LastTween.Goto(0.05f, false);
            animation.enabled = false;
            Assert.That(group.alpha, Is.EqualTo(1f));
            Assert.That(manager.RegisteredCount, Is.EqualTo(initialRegisteredCount));

            panel.Close();
            Assert.That(group.alpha, Is.Zero);
            Assert.That(panel.IsVisualTransitioning, Is.False);
        }

        [Test]
        public void DeactivatingParentDuringCloseDoesNotReorderOrLogError()
        {
            panel.Open();
            animation.CompleteAnimation();
            panel.Close();
            int siblingIndex = owner.transform.GetSiblingIndex();

            parent.SetActive(false);
            Assert.That(group.alpha, Is.Zero);
            Assert.That(owner.transform.GetSiblingIndex(), Is.EqualTo(siblingIndex));

            parent.SetActive(true);
        }

        [Test]
        public void ExternalKillCompletesVisualWithoutRepeatingBusinessEvent()
        {
            int opens = 0;
            panel.Opened += () => opens++;
            panel.Open();
            animation.LastTween.Kill(false);
            Assert.That(group.alpha, Is.EqualTo(1f));
            Assert.That(panel.IsVisualTransitioning, Is.False);
            Assert.That(opens, Is.EqualTo(1));
        }

        [Test]
        public void TransparentOpenPreferenceSurvivesTransition()
        {
            panel.SetOpenVisualAlpha(0.4f);
            panel.Open();
            animation.LastTween.Goto(0.1f, false);
            Assert.That(group.alpha, Is.InRange(0.001f, 0.4f));
            animation.CompleteAnimation();
            Assert.That(group.alpha, Is.EqualTo(0.4f).Within(0.0001f));
        }
        #endregion

        #region 配置与统一倍率
        [TestCase("\"openDuration\":-1")]
        [TestCase("\"offsetX\":\"NaN\"")]
        [TestCase("\"closeEase\":\"NotAnEase\"")]
        [TestCase("\"closedScale\":\"Infinity\"")]
        [TestCase("\"moveSpeed\":100")]
        [TestCase("\"typoDuration\":1")]
        public void InvalidConfigurationDoesNotReplaceValidSnapshot(string data)
        {
            UIAnimationProfile previous = manager.GetProfile("panel.default");
            Assert.That(manager.TryReloadJson(Config(data), out string error), Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(manager.GetProfile("panel.default"), Is.SameAs(previous));
        }

        [Test]
        public void RuntimeGlobalPlaybackSpeedDoesNotAffectOtherTweens()
        {
            float value = 0f;
            float originalGlobal = DOTween.timeScale;
            unrelatedTween = DOTween.To(() => value, x => value = x, 1f, 10f);
            panel.Open();

            manager.SetPlaybackSpeed(2f);
            Assert.That(animation.LastTween.timeScale, Is.EqualTo(2f));
            Assert.That(unrelatedTween.timeScale, Is.EqualTo(1f));
            Assert.That(DOTween.timeScale, Is.EqualTo(originalGlobal));
        }

        [Test]
        public void ReloadChangesNextDurationButDoesNotStorePlaybackSpeed()
        {
            manager.SetPlaybackSpeed(1.75f);
            panel.Open();
            Assert.That(animation.LastTween.Duration(), Is.EqualTo(0.2f).Within(0.001f));

            Assert.That(manager.TryReloadJson(Config("\"openDuration\":0.7,\"closeDuration\":0.6"), out _), Is.True);
            Assert.That(manager.PlaybackSpeed, Is.EqualTo(1.75f));
            Assert.That(animation.LastTween.Duration(), Is.EqualTo(0.2f).Within(0.001f));

            animation.CompleteAnimation();
            panel.Close();
            Assert.That(animation.LastTween.Duration(), Is.EqualTo(0.6f).Within(0.001f));
        }

        [UnityTest]
        public IEnumerator AnimationStillFinishesWhenGameTimeIsPaused()
        {
            Time.timeScale = 0f;
            panel.Open();
            yield return new WaitForSecondsRealtime(0.35f);
            Assert.That(group.alpha, Is.EqualTo(1f).Within(0.001f));
            Assert.That(panel.IsVisualTransitioning, Is.False);
        }
        #endregion

        #region 测试滑入动画
        [UnityTest]
        public IEnumerator TestSlideAnimationMovesFromJsonOffsetAndReturnsToRestPosition()
        {
            GameObject slideOwner = new GameObject("TestSlidePanel", typeof(RectTransform), typeof(CanvasGroup));
            slideOwner.SetActive(false);
            slideOwner.transform.SetParent(parent.transform, false);
            CanvasGroup slideGroup = slideOwner.GetComponent<CanvasGroup>();
            slideGroup.alpha = 0f;
            slideGroup.interactable = false;
            slideGroup.blocksRaycasts = false;

            RectTransform motionRoot = new GameObject("MotionRoot", typeof(RectTransform)).GetComponent<RectTransform>();
            motionRoot.SetParent(slideOwner.transform, false);
            Vector2 rest = new Vector2(30f, 20f);
            motionRoot.anchoredPosition = rest;

            SlideUIAnimation slide = slideOwner.AddComponent<SlideUIAnimation>();
            slide.SetAnimationId("panel.test.slide");
            slide.SetMotionRoot(motionRoot);
            BasePanel slidePanel = slideOwner.AddComponent<BasePanel>();
            slidePanel.Init();
            slideOwner.SetActive(true);

            slidePanel.Open();
            Assert.That(motionRoot.anchoredPosition, Is.EqualTo(rest + new Vector2(-120f, 0f)));
            yield return new WaitForSecondsRealtime(0.12f);
            Assert.That(motionRoot.anchoredPosition.x, Is.GreaterThan(rest.x - 120f));
            Assert.That(motionRoot.anchoredPosition.x, Is.LessThan(rest.x));

            yield return new WaitForSecondsRealtime(0.35f);
            Assert.That(motionRoot.anchoredPosition, Is.EqualTo(rest));
            Assert.That(slideGroup.alpha, Is.EqualTo(1f).Within(0.001f));
            Assert.That(slidePanel.IsOpen(), Is.True);
        }

#if UNITY_EDITOR
        [Test]
        public void OfficialPrefabsContainDirectAnimationComponentWithoutMissingScripts()
        {
            string[] paths =
            {
                "Assets/2_Prefabs/2-1_UI/Common/Templates/UI_BasePanel.prefab",
                "Assets/2_Prefabs/2-1_UI/Settings/Panels/UI_MainMenuSettings.prefab"
            };

            foreach (string path in paths)
            {
                GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(prefab, Is.Not.Null, path);
                BaseUIAnimation component = prefab.GetComponent<BaseUIAnimation>();
                Assert.That(component, Is.Not.Null, path);
                Assert.That(prefab.GetComponent<BasePanel>(), Is.Not.Null, path);
                Assert.That(prefab.GetComponent<CanvasGroup>(), Is.Not.Null, path);
                Assert.That(manager.HasProfile(component.AnimationId), Is.True, path);
                Assert.That(UnityEditor.GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(prefab), Is.Zero, path);
            }
        }
#endif
        #endregion
    }
}
