using System;
using System.Collections.Generic;
using FlatWorld.Audio;
using FlatWorld.Gameplay.InventoryFeedback;
using UnityEngine;

namespace FlatWorld.Gameplay.Presentation.Audio
{
    /// <summary>把成功库存事务映射到 LMMS UI 音效，选择规则不依赖 AudioService。</summary>
    internal static class InventoryInteractionAudio
    {
        #region 生命周期和播放
        private const float MinimumFeedbackInterval = 0.045f;
        private static readonly string[] MaterialPriority = { "metal", "stone", "wood", "soft" };
        private static readonly string[] Metals = { "metal", "iron", "steel", "copper", "silver",
            "gold", "bronze", "aluminum", "aluminium", "tin", "lead" };
        private static readonly string[] Stones = { "stone", "rock", "ore", "mineral",
            "granite", "flint", "sandstone", "salt" };
        private static readonly string[] Woods = { "wood", "log", "plank", "stick", "bamboo", "timber" };
        private static float lastFeedbackTime = -100f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Register()
        {
            InventoryAudioFeedbackEvents.Committed -= OnInventoryChanged;
            InventoryAudioFeedbackEvents.Committed += OnInventoryChanged;
            EquipmentAudioFeedbackEvents.Committed -= OnEquipmentChanged;
            EquipmentAudioFeedbackEvents.Committed += OnEquipmentChanged;
            lastFeedbackTime = -100f;
        }

        private static void OnInventoryChanged(InventoryAudioSignal signal)
        {
            if (!HasLocalPlayer() || signal.Item?.Stack == null)
                return;

            string cue = ResolveCue(signal.Action, signal.Item.IDName, signal.Item.Tags,
                signal.Item.Stack.Weight, signal.Item.Stack.Volume, signal.Amount);
            Play(cue);
        }

        private static void OnEquipmentChanged(Player actor, bool equipped)
        {
            if (actor != null && actor.IsLocalProfile)
                Play(equipped ? AudioEventIds.UiEquipmentEquip : AudioEventIds.UiEquipmentUnequip);
        }

        private static bool HasLocalPlayer()
            => Inventory_Hand.PlayerHand?.item is Player player &&
               player != null && player.IsLocalProfile && player.gameObject.activeInHierarchy;

        private static void Play(string cueId)
        {
            if (cueId == null || Time.unscaledTime - lastFeedbackTime < MinimumFeedbackInterval)
                return;
            if (AudioService.Instance.Play(cueId).IsValid)
                lastFeedbackTime = Time.unscaledTime;
        }
        #endregion

        #region 音色选择
        internal static string ResolveCue(InventoryAudioAction action, string itemId,
            IReadOnlyList<string> tags, float unitWeightKg, float unitVolumeL, float amount)
        {
            if (action == InventoryAudioAction.Swap)
                return AudioEventIds.UiInventorySwap;
            if (!float.IsFinite(unitWeightKg) || !float.IsFinite(unitVolumeL) ||
                !float.IsFinite(amount) || unitWeightKg < 0f || unitVolumeL < 0f || amount <= 0f)
                return null;

            string operation = action switch
            {
                InventoryAudioAction.Pickup => "pickup",
                InventoryAudioAction.Backpack => "backpack",
                InventoryAudioAction.Chest => "chest",
                _ => null
            };
            if (operation == null)
                return null;

            string material = ResolveMaterial(itemId, tags);
            float weight = unitWeightKg * amount;
            float volume = unitVolumeL * amount;
            if (!float.IsFinite(weight) || !float.IsFinite(volume))
                return null;
            string weightBand = weight < 1f ? "light" : weight < 8f ? "medium" : "heavy";
            string volumeBand = volume < 4f ? "compact" : "bulky";
            return $"ui.inventory.{operation}.{material}.{weightBand}.{volumeBand}";
        }

        private static string ResolveMaterial(string itemId, IReadOnlyList<string> tags)
        {
            foreach (string material in MaterialPriority)
                if (HasMaterialKey(tags, "AudioMaterial." + material))
                    return material;

            if (ContainsAnyKey(tags, itemId, Metals))
                return "metal";
            if (ContainsAnyKey(tags, itemId, Stones))
                return "stone";
            if (ContainsAnyKey(tags, itemId, Woods))
                return "wood";
            return "soft";
        }

        private static bool HasMaterialKey(IReadOnlyList<string> tags, string key)
        {
            if (tags == null)
                return false;
            for (int i = 0; i < tags.Count; i++)
                if (string.Equals(tags[i], key, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static bool ContainsAnyKey(IReadOnlyList<string> tags, string itemId, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                if (!string.IsNullOrEmpty(itemId) &&
                    itemId.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (tags == null)
                    continue;
                for (int j = 0; j < tags.Count; j++)
                    if (!string.IsNullOrEmpty(tags[j]) &&
                        (tags[j].Equals(key, StringComparison.OrdinalIgnoreCase) ||
                         tags[j].EndsWith("." + key, StringComparison.OrdinalIgnoreCase)))
                        return true;
            }
            return false;
        }
        #endregion
    }
}
