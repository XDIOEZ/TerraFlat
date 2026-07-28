using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FlatWorld.Dialogue
{
    /// <summary>
    /// 角色自言自语的唯一调度入口。
    /// 不关心饥饿规则或气泡实现，只组合上下文、内容提供者与显示器。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterSoliloquyController : MonoBehaviour
    {
        [Header("调度")]
        [SerializeField, Min(0f)] private float initialDelay = 5f;
        [SerializeField, Min(1f)] private float minIdleInterval = 18f;
        [SerializeField, Min(1f)] private float maxIdleInterval = 32f;
        [SerializeField, Min(0.1f)] private float statePollInterval = 0.5f;
        [SerializeField, Min(0.5f)] private float providerTimeout = 8f;
        [SerializeField] private bool suppressLowerPriorityWhileVisible = true;

        private readonly List<ICharacterSpeechContextContributor> contributors =
            new List<ICharacterSpeechContextContributor>();
        private readonly List<ICharacterSpeechProvider> providers =
            new List<ICharacterSpeechProvider>();
        private readonly List<ICharacterSpeechTriggerSource> triggerSources =
            new List<ICharacterSpeechTriggerSource>();

        private ICharacterSpeechPresenter presenter;
        private Item actorItem;
        private Coroutine startupRoutine;
        private Coroutine stateRoutine;
        private Coroutine idleRoutine;
        private Coroutine providerRoutine;
        private bool started;
        private int requestGeneration;

        public event Action<CharacterSpeechRequest> SpeechRequested;
        public event Action<CharacterSpeechRequest> SpeechShown;

        private void Start()
        {
            started = true;
            StartController();
        }

        private void OnEnable()
        {
            if (started)
                StartController();
        }

        private void OnDisable()
        {
            StopController();
        }

        /// <summary>
        /// 新增或移除 Provider 后可调用，无需修改控制器本身。
        /// </summary>
        public void RebuildExtensions()
        {
            contributors.Clear();
            providers.Clear();
            triggerSources.Clear();
            presenter = null;

            MonoBehaviour[] behaviours = GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                    continue;

                if (behaviour is ICharacterSpeechContextContributor contributor)
                    contributors.Add(contributor);
                if (behaviour is ICharacterSpeechProvider provider)
                    providers.Add(provider);
                if (behaviour is ICharacterSpeechTriggerSource triggerSource)
                    triggerSources.Add(triggerSource);
                if (presenter == null && behaviour is ICharacterSpeechPresenter speechPresenter)
                    presenter = speechPresenter;
            }

            contributors.Sort((left, right) => left.ContextOrder.CompareTo(right.ContextOrder));
            providers.Sort((left, right) => right.ProviderOrder.CompareTo(left.ProviderOrder));
        }

        /// <summary>
        /// 供任务、剧情、网络消息和未来大模型直接发言。
        /// </summary>
        public bool Say(
            string text,
            CharacterSpeechPriority priority = CharacterSpeechPriority.Ambient,
            float duration = 0f,
            string topic = "external")
        {
            return TryPresent(new CharacterSpeechRequest(text, topic, priority, duration));
        }

        public bool Present(CharacterSpeechRequest request)
        {
            return TryPresent(request);
        }

        private void StartController()
        {
            StopController();
            RebuildExtensions();
            startupRoutine = StartCoroutine(StartWhenActorReady());
        }

        private void StopController()
        {
            requestGeneration++;

            if (startupRoutine != null)
                StopCoroutine(startupRoutine);
            if (stateRoutine != null)
                StopCoroutine(stateRoutine);
            if (idleRoutine != null)
                StopCoroutine(idleRoutine);
            if (providerRoutine != null)
                StopCoroutine(providerRoutine);

            startupRoutine = null;
            stateRoutine = null;
            idleRoutine = null;
            providerRoutine = null;
            presenter?.HideImmediate();
        }

        private IEnumerator StartWhenActorReady()
        {
            actorItem = GetComponentInParent<Item>();
            while (isActiveAndEnabled && actorItem != null && !actorItem.IsInitialized)
                yield return null;

            if (!isActiveAndEnabled)
                yield break;

            stateRoutine = StartCoroutine(StateTriggerLoop());

            if (initialDelay > 0f)
                yield return new WaitForSecondsRealtime(initialDelay);

            if (isActiveAndEnabled)
                idleRoutine = StartCoroutine(IdleSpeechLoop());
        }

        private IEnumerator StateTriggerLoop()
        {
            WaitForSecondsRealtime wait =
                new WaitForSecondsRealtime(Mathf.Max(0.1f, statePollInterval));

            while (isActiveAndEnabled)
            {
                EvaluateStateTriggersOnce();
                yield return wait;
            }
        }

        private void EvaluateStateTriggersOnce()
        {
            if (triggerSources.Count == 0)
                return;

            CharacterSpeechContext context =
                BuildContext(CharacterSpeechTrigger.StateChanged);
            CharacterSpeechRequest best = null;

            for (int i = 0; i < triggerSources.Count; i++)
            {
                try
                {
                    CharacterSpeechRequest candidate =
                        triggerSources[i].PollTriggeredSpeech(context);
                    if (candidate != null &&
                        candidate.IsValid &&
                        (best == null || candidate.Priority > best.Priority))
                    {
                        best = candidate;
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }

            if (best != null)
                TryPresent(best);
        }

        private IEnumerator IdleSpeechLoop()
        {
            while (isActiveAndEnabled)
            {
                float minimum = Mathf.Max(1f, minIdleInterval);
                float maximum = Mathf.Max(minimum, maxIdleInterval);
                yield return new WaitForSecondsRealtime(UnityEngine.Random.Range(minimum, maximum));

                if (!isActiveAndEnabled || providerRoutine != null || providers.Count == 0)
                    continue;

                int generation = ++requestGeneration;
                providerRoutine = StartCoroutine(RequestFromProviders(generation));
            }
        }

        private IEnumerator RequestFromProviders(int generation)
        {
            CharacterSpeechContext context = BuildContext(CharacterSpeechTrigger.Idle);

            for (int i = 0; i < providers.Count; i++)
            {
                if (generation != requestGeneration || !isActiveAndEnabled)
                    break;

                ICharacterSpeechProvider provider = providers[i];
                bool canProvide;
                try
                {
                    canProvide = provider.CanProvide(context);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                    continue;
                }

                if (!canProvide)
                    continue;

                bool completed = false;
                CharacterSpeechRequest response = null;
                try
                {
                    provider.RequestSpeech(
                        context,
                        request =>
                        {
                            response = request;
                            completed = true;
                        });
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                    completed = true;
                }

                float deadline = Time.realtimeSinceStartup + Mathf.Max(0.5f, providerTimeout);
                while (!completed &&
                       generation == requestGeneration &&
                       Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                if (generation != requestGeneration)
                    break;

                if (completed && response != null && response.IsValid)
                {
                    TryPresent(response);
                    break;
                }
            }

            providerRoutine = null;
        }

        private CharacterSpeechContext BuildContext(CharacterSpeechTrigger trigger)
        {
            CharacterSpeechContext context =
                new CharacterSpeechContext(transform, trigger, Time.unscaledTime);

            for (int i = 0; i < contributors.Count; i++)
            {
                try
                {
                    contributors[i].Contribute(context);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, this);
                }
            }

            return context;
        }

        private bool TryPresent(CharacterSpeechRequest request)
        {
            if (request == null || !request.IsValid)
                return false;

            if (presenter == null)
                RebuildExtensions();
            if (presenter == null)
                return false;

            if (suppressLowerPriorityWhileVisible &&
                presenter.IsVisible &&
                request.Priority < presenter.VisiblePriority)
            {
                return false;
            }

            if (request.Duration <= 0f)
            {
                request.Duration = Mathf.Clamp(
                    2.2f + request.Text.Trim().Length * 0.12f,
                    2.8f,
                    6f);
            }

            SpeechRequested?.Invoke(request);
            if (!presenter.Show(request))
                return false;

            SpeechShown?.Invoke(request);
            return true;
        }
    }
}
