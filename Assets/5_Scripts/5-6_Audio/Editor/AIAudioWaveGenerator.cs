using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FlatWorld.Audio;
using UnityEditor;
using UnityEngine;

namespace FlatWorld.Audio.Editor
{
    /// <summary>
    /// 生成项目内可直接使用的原创基础音效。文件名遵循“事件ID__变体.wav”，
    /// 并在生成后自动交给 AIAudioCatalogBuilder 注册。
    /// </summary>
    [InitializeOnLoad]
    public static class AIAudioWaveGenerator
    {
        private const int SampleRate = 44100;
        private static bool queued;

        private enum SoundKind
        {
            UiHover,
            UiClick,
            UiConfirm,
            UiCancel,
            ItemPickup,
            ItemDrop,
            DoorOpen,
            DoorClose,
            CombatHit,
            WeaponGenericAttack,
            WeaponKnifeAttack,
            WeaponAxeAttack,
            WeaponPickaxeAttack,
            WeaponSpearAttack,
            WeaponBluntAttack,
            ImpactDefault,
            ImpactFoliage,
            ImpactWood,
            ImpactStone,
            ImpactMetal,
            ImpactFlesh,
            ImpactKnifeFoliage,
            ImpactKnifeStone,
            ImpactAxeWood,
            ImpactPickaxeStone,
            FoodEat,
            FoodCrunch,
            FoodDrink,
            WeatherRainLoop
        }

        private static readonly KeyValuePair<string, SoundKind>[] DefaultSounds =
        {
            new KeyValuePair<string, SoundKind>("ui.hover__01.wav", SoundKind.UiHover),
            new KeyValuePair<string, SoundKind>("ui.click__01.wav", SoundKind.UiClick),
            new KeyValuePair<string, SoundKind>("ui.confirm__01.wav", SoundKind.UiConfirm),
            new KeyValuePair<string, SoundKind>("ui.cancel__01.wav", SoundKind.UiCancel),
            new KeyValuePair<string, SoundKind>("item.pickup__01.wav", SoundKind.ItemPickup),
            new KeyValuePair<string, SoundKind>("item.drop__01.wav", SoundKind.ItemDrop),
            new KeyValuePair<string, SoundKind>("door.open__01.wav", SoundKind.DoorOpen),
            new KeyValuePair<string, SoundKind>("door.close__01.wav", SoundKind.DoorClose),
            new KeyValuePair<string, SoundKind>("combat.hit__01.wav", SoundKind.CombatHit),
            new KeyValuePair<string, SoundKind>("combat.weapon.generic.attack__01.wav", SoundKind.WeaponGenericAttack),
            new KeyValuePair<string, SoundKind>("combat.weapon.knife.attack__01.wav", SoundKind.WeaponKnifeAttack),
            new KeyValuePair<string, SoundKind>("combat.weapon.axe.attack__01.wav", SoundKind.WeaponAxeAttack),
            new KeyValuePair<string, SoundKind>("combat.weapon.pickaxe.attack__01.wav", SoundKind.WeaponPickaxeAttack),
            new KeyValuePair<string, SoundKind>("combat.weapon.spear.attack__01.wav", SoundKind.WeaponSpearAttack),
            new KeyValuePair<string, SoundKind>("combat.weapon.blunt.attack__01.wav", SoundKind.WeaponBluntAttack),
            new KeyValuePair<string, SoundKind>("combat.impact.default__01.wav", SoundKind.ImpactDefault),
            new KeyValuePair<string, SoundKind>("combat.impact.foliage__01.wav", SoundKind.ImpactFoliage),
            new KeyValuePair<string, SoundKind>("combat.impact.wood__01.wav", SoundKind.ImpactWood),
            new KeyValuePair<string, SoundKind>("combat.impact.stone__01.wav", SoundKind.ImpactStone),
            new KeyValuePair<string, SoundKind>("combat.impact.metal__01.wav", SoundKind.ImpactMetal),
            new KeyValuePair<string, SoundKind>("combat.impact.flesh__01.wav", SoundKind.ImpactFlesh),
            new KeyValuePair<string, SoundKind>("combat.impact.knife.foliage__01.wav", SoundKind.ImpactKnifeFoliage),
            new KeyValuePair<string, SoundKind>("combat.impact.knife.stone__01.wav", SoundKind.ImpactKnifeStone),
            new KeyValuePair<string, SoundKind>("combat.impact.axe.wood__01.wav", SoundKind.ImpactAxeWood),
            new KeyValuePair<string, SoundKind>("combat.impact.pickaxe.stone__01.wav", SoundKind.ImpactPickaxeStone),
            new KeyValuePair<string, SoundKind>("food.eat__01.wav", SoundKind.FoodEat),
            new KeyValuePair<string, SoundKind>("food.crunch__01.wav", SoundKind.FoodCrunch),
            new KeyValuePair<string, SoundKind>("food.drink__01.wav", SoundKind.FoodDrink),
            new KeyValuePair<string, SoundKind>("weather.rain.loop__01.wav", SoundKind.WeatherRainLoop)
        };

        static AIAudioWaveGenerator()
        {
            QueueGenerateMissingDefaults();
        }

        [MenuItem("Tools/FlatWorld/Audio/Generate Default AI SFX")]
        public static void GenerateDefaultSfx()
        {
            GenerateDefaults(overwriteExisting: true);
        }

        private static void QueueGenerateMissingDefaults()
        {
            if (queued)
                return;

            queued = true;
            EditorApplication.delayCall += GenerateMissingDefaultsWhenReady;
        }

        private static void GenerateMissingDefaultsWhenReady()
        {
            queued = false;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                QueueGenerateMissingDefaults();
                return;
            }

            GenerateDefaults(overwriteExisting: false);
        }

        private static void GenerateDefaults(bool overwriteExisting)
        {
            string folderPath = Path.Combine(Application.dataPath, "Audio", "Generated");
            Directory.CreateDirectory(folderPath);

            int writtenCount = 0;
            for (int i = 0; i < DefaultSounds.Length; i++)
            {
                KeyValuePair<string, SoundKind> sound = DefaultSounds[i];
                string outputPath = Path.Combine(folderPath, sound.Key);
                if (!overwriteExisting && File.Exists(outputPath))
                    continue;

                File.WriteAllBytes(outputPath, EncodeWave(CreateSamples(sound.Value)));
                writtenCount++;
            }

            bool catalogMissing = AssetDatabase.LoadAssetAtPath<AudioCatalog>(
                AIAudioCatalogBuilder.CatalogPath) == null;
            if (writtenCount == 0 && !catalogMissing)
                return;

            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AIAudioCatalogBuilder.Rebuild();
            Debug.Log($"[AIAudioWaveGenerator] 已生成/更新 {writtenCount} 个基础音效，并完成 Catalog 注册。");
        }

        private static float[] CreateSamples(SoundKind kind)
        {
            switch (kind)
            {
                case SoundKind.UiHover: return CreateChirp(0.055f, 720f, 1140f, 0.20f, 0.01f, 11);
                case SoundKind.UiClick: return CreateChirp(0.085f, 820f, 470f, 0.27f, 0.025f, 13);
                case SoundKind.UiConfirm: return CreateConfirm();
                case SoundKind.UiCancel: return CreateChirp(0.17f, 530f, 250f, 0.20f, 0.015f, 17);
                case SoundKind.ItemPickup: return CreatePickup();
                case SoundKind.ItemDrop: return CreateImpact(0.16f, 145f, 0.25f, 0.20f, 23);
                case SoundKind.DoorOpen: return CreateDoorOpen();
                case SoundKind.DoorClose: return CreateDoorClose();
                case SoundKind.CombatHit: return CreateImpact(0.13f, 92f, 0.34f, 0.37f, 31);
                case SoundKind.WeaponGenericAttack: return CreateWeaponSwing(0.15f, 420f, 0.48f, 73);
                case SoundKind.WeaponKnifeAttack: return CreateWeaponSwing(0.11f, 980f, 0.74f, 79);
                case SoundKind.WeaponAxeAttack: return CreateWeaponSwing(0.19f, 245f, 0.34f, 83);
                case SoundKind.WeaponPickaxeAttack: return CreateWeaponSwing(0.17f, 360f, 0.46f, 89);
                case SoundKind.WeaponSpearAttack: return CreateWeaponSwing(0.14f, 720f, 0.64f, 97);
                case SoundKind.WeaponBluntAttack: return CreateWeaponSwing(0.20f, 185f, 0.25f, 101);
                case SoundKind.ImpactDefault: return CreateImpact(0.14f, 105f, 0.30f, 0.33f, 103);
                case SoundKind.ImpactFoliage: return CreateFoliageImpact(false);
                case SoundKind.ImpactWood: return CreateWoodImpact(false);
                case SoundKind.ImpactStone: return CreateStoneImpact(false);
                case SoundKind.ImpactMetal: return CreateMetalImpact(0.32f, 760f, false);
                case SoundKind.ImpactFlesh: return CreateFleshImpact();
                case SoundKind.ImpactKnifeFoliage: return CreateFoliageImpact(true);
                case SoundKind.ImpactKnifeStone: return CreateMetalImpact(0.24f, 1040f, true);
                case SoundKind.ImpactAxeWood: return CreateWoodImpact(true);
                case SoundKind.ImpactPickaxeStone: return CreateStoneImpact(true);
                case SoundKind.FoodEat: return CreateFoodEat();
                case SoundKind.FoodCrunch: return CreateFoodCrunch();
                case SoundKind.FoodDrink: return CreateFoodDrink();
                case SoundKind.WeatherRainLoop: return CreateRainLoop();
                default: return CreateChirp(0.06f, 440f, 440f, 0.2f, 0f, 1);
            }
        }

        private static float[] CreateRainLoop()
        {
            const float duration = 6f;
            const float crossFadeDuration = 0.4f;
            int count = Mathf.CeilToInt(duration * SampleRate);
            int crossFadeSamples = Mathf.CeilToInt(crossFadeDuration * SampleRate);
            float[] samples = new float[count];
            float lowBand = 0f;
            float highBand = 0f;
            uint seed = 20260730;

            for (int i = 0; i < count; i++)
            {
                float white = NextNoise(ref seed);
                lowBand = Mathf.Lerp(lowBand, white, 0.018f);
                highBand = Mathf.Lerp(highBand, white, 0.18f);
                float rainBed = lowBand * 0.22f + (white - highBand) * 0.12f;

                float drop = 0f;
                if ((i % 997) == 0 || (i % 1553) == 0)
                    drop = Mathf.Abs(white) * 0.14f;
                samples[i] = Mathf.Clamp(rainBed + drop, -0.8f, 0.8f);
            }

            for (int i = 0; i < crossFadeSamples; i++)
            {
                int tailIndex = count - crossFadeSamples + i;
                float blend = i / (float)Mathf.Max(1, crossFadeSamples - 1);
                samples[tailIndex] = Mathf.Lerp(samples[tailIndex], samples[i], blend);
            }

            return samples;
        }

        private static float[] CreateChirp(
            float duration,
            float startFrequency,
            float endFrequency,
            float gain,
            float noiseGain,
            uint seed)
        {
            int count = Mathf.CeilToInt(duration * SampleRate);
            float[] samples = new float[count];
            float phase = 0f;
            for (int i = 0; i < count; i++)
            {
                float time = i / (float)SampleRate;
                float progress = i / (float)(count - 1);
                float frequency = Mathf.Lerp(startFrequency, endFrequency, progress);
                phase += 2f * Mathf.PI * frequency / SampleRate;
                float tone = Mathf.Sin(phase) + 0.24f * Mathf.Sin(phase * 2f);
                samples[i] = (tone * gain + NextNoise(ref seed) * noiseGain) * Envelope(time, duration, 0.004f, 0.035f);
            }

            return samples;
        }

        private static float[] CreateConfirm()
        {
            const float duration = 0.19f;
            int count = Mathf.CeilToInt(duration * SampleRate);
            float[] samples = new float[count];
            float lowPhase = 0f;
            float highPhase = 0f;
            for (int i = 0; i < count; i++)
            {
                float time = i / (float)SampleRate;
                lowPhase += 2f * Mathf.PI * 660f / SampleRate;
                highPhase += 2f * Mathf.PI * 990f / SampleRate;
                float highDelay = time > 0.045f ? Mathf.Sin(highPhase) * 0.16f : 0f;
                samples[i] = (Mathf.Sin(lowPhase) * 0.22f + highDelay) * Envelope(time, duration, 0.005f, 0.065f);
            }

            return samples;
        }

        private static float[] CreatePickup()
        {
            const float duration = 0.22f;
            int count = Mathf.CeilToInt(duration * SampleRate);
            float[] samples = new float[count];
            float phase = 0f;
            float overtonePhase = 0f;
            for (int i = 0; i < count; i++)
            {
                float time = i / (float)SampleRate;
                float progress = i / (float)(count - 1);
                float frequency = Mathf.Lerp(470f, 1080f, progress * progress);
                phase += 2f * Mathf.PI * frequency / SampleRate;
                overtonePhase += 2f * Mathf.PI * frequency * 2.01f / SampleRate;
                samples[i] = (Mathf.Sin(phase) * 0.23f + Mathf.Sin(overtonePhase) * 0.05f) *
                    Envelope(time, duration, 0.004f, 0.08f);
            }

            return samples;
        }

        private static float[] CreateImpact(float duration, float baseFrequency, float toneGain, float noiseGain, uint seed)
        {
            int count = Mathf.CeilToInt(duration * SampleRate);
            float[] samples = new float[count];
            float phase = 0f;
            for (int i = 0; i < count; i++)
            {
                float time = i / (float)SampleRate;
                float frequency = Mathf.Lerp(baseFrequency * 1.9f, baseFrequency, time / duration);
                phase += 2f * Mathf.PI * frequency / SampleRate;
                float decay = Mathf.Exp(-time * 20f);
                float noise = NextNoise(ref seed) * noiseGain * Mathf.Exp(-time * 32f);
                samples[i] = (Mathf.Sin(phase) * toneGain * decay + noise) * Envelope(time, duration, 0.001f, 0.06f);
            }

            return samples;
        }

        private static float[] CreateDoorOpen()
        {
            const float duration = 0.46f;
            int count = Mathf.CeilToInt(duration * SampleRate);
            float[] samples = new float[count];
            float phase = 0f;
            uint seed = 41;
            for (int i = 0; i < count; i++)
            {
                float time = i / (float)SampleRate;
                float progress = time / duration;
                float frequency = Mathf.Lerp(105f, 195f, progress);
                phase += 2f * Mathf.PI * frequency / SampleRate;
                float creak = Mathf.Sin(phase + Mathf.Sin(phase * 0.1f) * 1.8f) * 0.14f;
                float grain = NextNoise(ref seed) * 0.08f * (0.4f + 0.6f * Mathf.Sin(progress * Mathf.PI));
                samples[i] = (creak + grain) * Envelope(time, duration, 0.012f, 0.09f);
            }

            return samples;
        }

        private static float[] CreateDoorClose()
        {
            const float duration = 0.29f;
            int count = Mathf.CeilToInt(duration * SampleRate);
            float[] samples = CreateImpact(duration, 88f, 0.33f, 0.18f, 47);
            float phase = 0f;
            for (int i = 0; i < count; i++)
            {
                float time = i / (float)SampleRate;
                phase += 2f * Mathf.PI * 175f / SampleRate;
                samples[i] += Mathf.Sin(phase) * 0.11f * Mathf.Exp(-time * 13f);
            }

            return samples;
        }

        private static float[] CreateFoodEat()
        {
            const float duration = 0.27f;
            int count = Mathf.CeilToInt(duration * SampleRate);
            float[] samples = new float[count];
            float phase = 0f;
            uint seed = 61;
            for (int i = 0; i < count; i++)
            {
                float time = i / (float)SampleRate;
                float bitePulse = 0f;
                for (int bite = 0; bite < 3; bite++)
                {
                    float elapsed = time - (0.018f + bite * 0.075f);
                    if (elapsed >= 0f)
                        bitePulse += Mathf.Exp(-elapsed * 34f);
                }

                phase += 2f * Mathf.PI * (118f + bitePulse * 28f) / SampleRate;
                float chew = Mathf.Sin(phase) * 0.12f;
                float grain = NextNoise(ref seed) * 0.11f;
                samples[i] = (chew + grain) * bitePulse * Envelope(time, duration, 0.004f, 0.05f);
            }

            return samples;
        }

        private static float[] CreateFoodCrunch()
        {
            const float duration = 0.20f;
            int count = Mathf.CeilToInt(duration * SampleRate);
            float[] samples = new float[count];
            float phase = 0f;
            uint seed = 67;
            for (int i = 0; i < count; i++)
            {
                float time = i / (float)SampleRate;
                float fragments = 0f;
                for (int fragment = 0; fragment < 4; fragment++)
                {
                    float elapsed = time - (0.008f + fragment * 0.038f);
                    if (elapsed >= 0f)
                        fragments += Mathf.Exp(-elapsed * 55f);
                }

                phase += 2f * Mathf.PI * 690f / SampleRate;
                float crackle = NextNoise(ref seed) * 0.24f + Mathf.Sin(phase) * 0.07f;
                samples[i] = crackle * fragments * Envelope(time, duration, 0.001f, 0.045f);
            }

            return samples;
        }

        private static float[] CreateFoodDrink()
        {
            const float duration = 0.32f;
            int count = Mathf.CeilToInt(duration * SampleRate);
            float[] samples = new float[count];
            float lowPhase = 0f;
            float bubblePhase = 0f;
            uint seed = 71;
            for (int i = 0; i < count; i++)
            {
                float time = i / (float)SampleRate;
                float progress = time / duration;
                lowPhase += 2f * Mathf.PI * Mathf.Lerp(165f, 120f, progress) / SampleRate;
                bubblePhase += 2f * Mathf.PI * 470f / SampleRate;
                float gulp = Mathf.Sin(lowPhase) * 0.10f;
                float bubbles = Mathf.Max(0f, Mathf.Sin(bubblePhase)) * NextNoise(ref seed) * 0.10f;
                samples[i] = (gulp + bubbles) * Envelope(time, duration, 0.008f, 0.08f);
            }

            return samples;
        }

        private static float[] CreateWeaponSwing(
            float duration,
            float toneFrequency,
            float brightness,
            uint seed)
        {
            int count = Mathf.CeilToInt(duration * SampleRate);
            float[] samples = new float[count];
            float phase = 0f;
            float smoothNoise = 0f;
            for (int i = 0; i < count; i++)
            {
                float progress = i / (float)(count - 1);
                float time = i / (float)SampleRate;
                float rawNoise = NextNoise(ref seed);
                smoothNoise = Mathf.Lerp(smoothNoise, rawNoise, 0.12f);
                float highNoise = rawNoise - smoothNoise;
                float swish = Mathf.Sin(progress * Mathf.PI);
                swish *= swish;

                float frequency = Mathf.Lerp(toneFrequency * 0.72f, toneFrequency * 1.35f, progress);
                phase += 2f * Mathf.PI * frequency / SampleRate;
                float air = Mathf.Lerp(smoothNoise, highNoise, brightness) * 0.34f;
                float edge = Mathf.Sin(phase) * 0.055f;
                samples[i] = (air + edge) * swish * Envelope(time, duration, 0.008f, 0.018f);
            }

            return samples;
        }

        private static float[] CreateFoliageImpact(bool sharpCut)
        {
            const float duration = 0.29f;
            int count = Mathf.CeilToInt(duration * SampleRate);
            float[] samples = new float[count];
            float smoothNoise = 0f;
            float cutPhase = 0f;
            uint seed = sharpCut ? 107u : 109u;
            for (int i = 0; i < count; i++)
            {
                float time = i / (float)SampleRate;
                float rawNoise = NextNoise(ref seed);
                smoothNoise = Mathf.Lerp(smoothNoise, rawNoise, 0.18f);
                float dryLeaves = rawNoise - smoothNoise;
                float bursts = 0f;
                for (int burst = 0; burst < 4; burst++)
                {
                    float elapsed = time - burst * 0.052f;
                    if (elapsed >= 0f)
                        bursts += Mathf.Exp(-elapsed * (sharpCut ? 34f : 25f));
                }

                cutPhase += 2f * Mathf.PI * Mathf.Lerp(1580f, 760f, time / duration) / SampleRate;
                float cut = sharpCut
                    ? Mathf.Sin(cutPhase) * 0.13f * Mathf.Exp(-time * 30f)
                    : 0f;
                samples[i] = (dryLeaves * 0.16f * bursts + cut) *
                    Envelope(time, duration, 0.001f, 0.055f);
            }

            return samples;
        }

        private static float[] CreateWoodImpact(bool heavyChop)
        {
            float duration = heavyChop ? 0.25f : 0.19f;
            int count = Mathf.CeilToInt(duration * SampleRate);
            float[] samples = CreateImpact(
                duration,
                heavyChop ? 105f : 142f,
                heavyChop ? 0.46f : 0.34f,
                heavyChop ? 0.28f : 0.20f,
                heavyChop ? 113u : 127u);

            uint seed = heavyChop ? 131u : 137u;
            for (int i = 0; i < count; i++)
            {
                float time = i / (float)SampleRate;
                float splinter = 0f;
                for (int crack = 0; crack < 3; crack++)
                {
                    float elapsed = time - (0.012f + crack * 0.027f);
                    if (elapsed >= 0f)
                        splinter += NextNoise(ref seed) * Mathf.Exp(-elapsed * 72f);
                }

                samples[i] += splinter * (heavyChop ? 0.12f : 0.075f);
            }

            return samples;
        }

        private static float[] CreateStoneImpact(bool sharpChip)
        {
            float duration = sharpChip ? 0.26f : 0.20f;
            int count = Mathf.CeilToInt(duration * SampleRate);
            float[] samples = new float[count];
            float lowPhase = 0f;
            float highPhase = 0f;
            uint seed = sharpChip ? 139u : 149u;
            for (int i = 0; i < count; i++)
            {
                float time = i / (float)SampleRate;
                lowPhase += 2f * Mathf.PI * (sharpChip ? 510f : 355f) / SampleRate;
                highPhase += 2f * Mathf.PI * (sharpChip ? 1320f : 870f) / SampleRate;
                float ring =
                    Mathf.Sin(lowPhase) * 0.25f * Mathf.Exp(-time * 18f) +
                    Mathf.Sin(highPhase) * 0.14f * Mathf.Exp(-time * 27f);
                float chip = NextNoise(ref seed) * 0.16f * Mathf.Exp(-time * 58f);
                samples[i] = (ring + chip) * Envelope(time, duration, 0.001f, 0.045f);
            }

            return samples;
        }

        private static float[] CreateMetalImpact(float duration, float baseFrequency, bool shortPing)
        {
            int count = Mathf.CeilToInt(duration * SampleRate);
            float[] samples = new float[count];
            float phaseA = 0f;
            float phaseB = 0f;
            float phaseC = 0f;
            uint seed = shortPing ? 151u : 157u;
            for (int i = 0; i < count; i++)
            {
                float time = i / (float)SampleRate;
                phaseA += 2f * Mathf.PI * baseFrequency / SampleRate;
                phaseB += 2f * Mathf.PI * baseFrequency * 1.47f / SampleRate;
                phaseC += 2f * Mathf.PI * baseFrequency * 2.13f / SampleRate;
                float decay = Mathf.Exp(-time * (shortPing ? 15f : 9f));
                float ring =
                    Mathf.Sin(phaseA) * 0.21f +
                    Mathf.Sin(phaseB) * 0.13f +
                    Mathf.Sin(phaseC) * 0.075f;
                float strike = NextNoise(ref seed) * 0.10f * Mathf.Exp(-time * 62f);
                samples[i] = (ring * decay + strike) * Envelope(time, duration, 0.001f, 0.06f);
            }

            return samples;
        }

        private static float[] CreateFleshImpact()
        {
            const float duration = 0.16f;
            int count = Mathf.CeilToInt(duration * SampleRate);
            float[] samples = new float[count];
            float phase = 0f;
            float smoothNoise = 0f;
            uint seed = 163;
            for (int i = 0; i < count; i++)
            {
                float time = i / (float)SampleRate;
                float progress = time / duration;
                phase += 2f * Mathf.PI * Mathf.Lerp(145f, 72f, progress) / SampleRate;
                float rawNoise = NextNoise(ref seed);
                smoothNoise = Mathf.Lerp(smoothNoise, rawNoise, 0.08f);
                float thump = Mathf.Sin(phase) * 0.33f * Mathf.Exp(-time * 24f);
                float softImpact = smoothNoise * 0.20f * Mathf.Exp(-time * 35f);
                samples[i] = (thump + softImpact) * Envelope(time, duration, 0.001f, 0.04f);
            }

            return samples;
        }

        private static float Envelope(float time, float duration, float attack, float release)
        {
            float attackGain = attack <= 0f ? 1f : Mathf.Clamp01(time / attack);
            float releaseGain = release <= 0f ? 1f : Mathf.Clamp01((duration - time) / release);
            return attackGain * releaseGain;
        }

        private static float NextNoise(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0x00FFFFFF) / 8388607.5f - 1f;
        }

        private static byte[] EncodeWave(float[] samples)
        {
            const short channelCount = 1;
            const short bitsPerSample = 16;
            int byteRate = SampleRate * channelCount * bitsPerSample / 8;
            short blockAlign = (short)(channelCount * bitsPerSample / 8);
            int dataLength = samples.Length * blockAlign;

            using (MemoryStream stream = new MemoryStream(44 + dataLength))
            using (BinaryWriter writer = new BinaryWriter(stream, Encoding.ASCII))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + dataLength);
                writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write(channelCount);
                writer.Write(SampleRate);
                writer.Write(byteRate);
                writer.Write(blockAlign);
                writer.Write(bitsPerSample);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(dataLength);

                for (int i = 0; i < samples.Length; i++)
                {
                    short value = (short)Mathf.RoundToInt(Mathf.Clamp(samples[i], -1f, 1f) * short.MaxValue);
                    writer.Write(value);
                }

                writer.Flush();
                return stream.ToArray();
            }
        }
    }
}
