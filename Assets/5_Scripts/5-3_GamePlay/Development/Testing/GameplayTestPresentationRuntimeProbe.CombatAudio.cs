using System;
using System.Collections;
using System.Collections.Generic;
using FlatWorld.Audio;
using FlatWorld.Combat;
using FlatWorld.NaturalEntities;
using Newtonsoft.Json.Linq;
using Unity.Mathematics;
using UnityEngine;

public static partial class GameplayTestPresentationRuntimeProbe
{
    #region 正式伤害提交与短音现场采样
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterCombatAudioRuntime() => RegisterCombatAudioSettlement();
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterCombatAudioEditor() => RegisterCombatAudioSettlement();
#endif
    private static void RegisterCombatAudioSettlement() => GameplayTestRuntimeProbe.RegisterAsync(
        "audio", "combat_settlement", CaptureCombatAudioSettlement);

    private static IEnumerator CaptureCombatAudioSettlement(Player player, JObject args, Action<JObject> completed)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        string world = GameplayWorldRegistry.GetItemWorldKey(player);
        using var scope = GameplayWorldRegistry.UseWorld(world);
        using var audio = new AudioFixture(player);
        Item axe = null, chicken = null;
        NaturalEntityHandle mine = default;
        bool ownsMine = false;
        var events = new List<string>();
        var samples = new JArray();
        void Capture(string cue, CombatAudioLocation location) { if (location.WorldKey == world) events.Add(cue); }
        CombatAudioFeedback.Published += Capture;
        try
        {
            Vector3 position = player.transform.position + Vector3.right * 2f;
            axe = ItemMgr.Instance.InstantiateItem("Axe_Stone", position);
            chicken = ItemMgr.Instance.InstantiateItem("Chicken", position);
            foreach (Item item in new[] { axe, chicken })
            {
                item.transform.SetParent(null, true);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(item.gameObject, player.gameObject.scene);
                GameplayWorldRegistry.AssignItemToWorld(item, world);
                item.Load();
            }
            axe.Owner = player;
            foreach (Module module in chicken.itemMods.Mods.Values)
                if (module is not Mod_DamageReceiver) module.SetEnabled(false);
            Mod_Damage weapon = axe.itemMods.GetMod_ByID<Mod_Damage>("Mod_Damage");
            Mod_DamageReceiver receiver = chicken.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
            receiver.Data.DamageInterval = 0f;
            receiver.Data.UseBodyPartHealth = false;
            receiver.Data.LootTable.Clear();
            receiver.Data.Hp = receiver.Data.MaxHp = 100f;
            receiver.Data.DefenseValues = new CombatDefense { Physical = 10000f };
            weapon.SetDamageValues(new CombatDamage(1f, 0f, 0f, 0f));

            void Sample(string label, Func<float> hit)
            {
                int before = events.Count;
                float loss = hit();
                string cueId = events.Count > before ? events[events.Count - 1] : string.Empty;
                bool playing = false;
                if (audio.Service.TryGetCue(cueId, out AudioCue cue))
                    foreach (AudioSource source in audio.Service.GetComponentsInChildren<AudioSource>(true))
                        if (source.isPlaying)
                            foreach (AudioClip clip in cue.Clips)
                                if (source.clip == clip) playing = true;
                samples.Add(new JObject { ["label"] = label, ["loss"] = loss,
                    ["eventCount"] = events.Count - before, ["cue"] = cueId, ["playing"] = playing });
            }

            Sample("go_blocked", () => receiver.Hurt(weapon));
            receiver.Data.DefenseValues = new CombatDefense();
            Sample("go_damaged", () => receiver.Hurt(weapon));
            using (receiver.AcquireTemporaryInvulnerability()) Sample("go_rejected", () => receiver.Hurt(weapon));
            Sample("go_environment", () => receiver.HurtEnvironmentalDamage(1, position, .1f));
            receiver.Data.Hp = .01f;
            Sample("go_fatal", () => receiver.Hurt(weapon));

            RuntimeItemDefinition definition = GameRes.Instance.ItemDefinitions["Mine_Stone"];
            string dimension = ChunkMgr.ExistingInstance.ResolveWorldAddress(WorldLocalPresentation.ToLogical(position)).DimensionId;
            if (!NaturalEntityEcsService.TryRegister(definition, ItemMgr.Instance.GenerateGuid(), position,
                26f, 0f, dimension, null, out mine)) throw new InvalidOperationException("战斗声音现场无法建立正式石矿。");
            ownsMine = true;
            CombatDamageContext context = GameplayCombatBridge.Context(weapon, new CombatClock
                { Time = GameplayFixedStepSimulation.CurrentTime, Tick = GameplayFixedStepSimulation.TickIndex });
            context.HitPoint = (Vector2)position;
            context.Clock.Time += 100d;
            context.ResourceToolKind = (int)ResourceToolKind.Pickaxe;
            context.ResourceToolTier = 100;
            context.ResourceToolEfficiency = 1f;
            context.Damage = float4.zero;
            Sample("native_blocked", () => NaturalEntityEcsService.ApplyDamage(mine, context, weapon));
            context.Clock.Time += 100d;
            context.Damage = new float4(100000f, 0f, 0f, 0f);
            Sample("native_fatal", () => NaturalEntityEcsService.ApplyDamage(mine, context, weapon));
            completed(new JObject { ["samples"] = samples });
            yield break;
        }
        finally
        {
            CombatAudioFeedback.Published -= Capture;
            if (ownsMine) NaturalEntityEcsService.Remove(mine);
            if (chicken != null && !chicken.DestructionHandled) ItemMgr.Instance.DespawnItem(chicken, saveData: false);
            if (axe != null && !axe.DestructionHandled) ItemMgr.Instance.DespawnItem(axe, saveData: false);
        }
    }
    #endregion
}
