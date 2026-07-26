using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace FlatWorld.Audio
{
    [Serializable]
    public sealed class AudioBusRoute
    {
        public AudioBus Bus;
        public AudioMixerGroup Output;
    }

    [CreateAssetMenu(fileName = "AudioRuntimeConfig", menuName = "FlatWorld/Audio/Runtime Config")]
    public sealed class AudioRuntimeConfig : ScriptableObject
    {
        [SerializeField] private AudioCatalog catalog;
        [SerializeField, Min(0)] private int initialVoiceCount = 12;
        [SerializeField, Min(1)] private int maxVoices = 48;
        [SerializeField] private bool persistUserSettings = true;
        [SerializeField] private List<AudioBusRoute> busRoutes = new List<AudioBusRoute>();

        public AudioCatalog Catalog => catalog;
        public int InitialVoiceCount => Mathf.Clamp(initialVoiceCount, 0, MaxVoices);
        public int MaxVoices => Mathf.Max(1, maxVoices);
        public bool PersistUserSettings => persistUserSettings;

        public AudioMixerGroup GetOutput(AudioBus bus)
        {
            for (int i = 0; i < busRoutes.Count; i++)
            {
                AudioBusRoute route = busRoutes[i];
                if (route != null && route.Bus == bus)
                    return route.Output;
            }

            return null;
        }

        private void OnValidate()
        {
            maxVoices = Mathf.Max(1, maxVoices);
            initialVoiceCount = Mathf.Clamp(initialVoiceCount, 0, maxVoices);
        }
    }
}
