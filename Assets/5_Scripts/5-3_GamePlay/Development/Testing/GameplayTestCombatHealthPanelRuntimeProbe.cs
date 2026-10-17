using System;
using System.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>世界血条回归只操作自有动物，采样正式伤害、显隐和模块卸载链。</summary>
public static class GameplayTestCombatHealthPanelRuntimeProbe
{
    #region 正式操作登记

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void RegisterRuntime() => Register();
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditor() => Register();
#endif
    private static void Register() => GameplayTestRuntimeProbe.RegisterAsync(
        "combat", "world_health_panel_lifecycle", CaptureLifecycle);

    #endregion

    #region 世界血条实例与延时采样

    private static IEnumerator CaptureLifecycle(Player player, JObject args, Action<JObject> completed)
    {
        Item actor = null;
        var result = new JObject();
        try
        {
            actor = ItemMgr.Instance.InstantiateItem("Chicken", player.transform.position + Vector3.right * 2f);
            actor.Load();
            // 自有夹具关闭自主移动和营养消耗，受伤仍走正式接收器。
            foreach (Module module in actor.itemMods.Mods.Values)
                if (module is Mod_Food || module.ResolvedModuleId == ModText.AI ||
                    module.ResolvedModuleId == ModText.Mod_Mover_AI)
                    module.SetEnabled(false);
            Mod_DamageReceiver receiver = actor.itemMods.GetMod_ByID<Mod_DamageReceiver>(ModText.Hp);
            if (receiver == null || receiver.PanelPrefab == null)
                throw new InvalidOperationException("正式鸡定义缺少世界血条接收器或 Prefab。");

            receiver.ShowPanel();
            GameObject first = receiver.PanleInstance;
            Transform actorRoot = actor.transform;
            result["initiallyVisible"] = receiver.IsPanelVisible();
            bool reused = first != null;
            bool hiddenInactive = true;
            for (int i = 0; i < 20; i++)
            {
                receiver.HidePanel();
                hiddenInactive &= !receiver.IsPanelVisible() && first != null && !first.activeSelf;
                receiver.ShowPanel();
                reused &= receiver.PanleInstance == first && receiver.IsPanelVisible();
            }
            result["hiddenInactive"] = hiddenInactive;
            result["reusedAcrossTwentyCycles"] = reused;

            receiver.HideUiDelayAfterLastDamage = 0.15f;
            float hpBefore = receiver.Hp;
            receiver.ForceHurt(0.01f, "dehydration");
            actor.gameObject.SetActive(false);
            result["ownerDisableHidPanel"] = !receiver.IsPanelVisible();
            actor.gameObject.SetActive(true);
            yield return null;
            receiver.ForceHurt(0.01f, "dehydration");
            float lastDamageTime = Time.time;
            result["ownerReactivationReusedInstance"] = receiver.IsPanelVisible() && receiver.PanleInstance == first;
            yield return null;
            receiver.ForceHurt(0.01f, "dehydration");
            lastDamageTime = Time.time;
            bool hiddenEarly = false;
            float deadline = Time.realtimeSinceStartup + 8f;
            while (receiver.IsPanelVisible() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                if (!receiver.IsPanelVisible() && Time.time - lastDamageTime < receiver.HideUiDelayAfterLastDamage)
                    hiddenEarly = true;
            }
            result["damageApplied"] = receiver.Hp < hpBefore;
            result["didNotHideBeforeLastDamageDelay"] = !hiddenEarly;
            result["automaticallyHidden"] = !receiver.IsPanelVisible();
            result["automaticHideRetainedInstance"] = receiver.PanleInstance == first && first != null && !first.activeSelf;
            receiver.ForceHurt(0.01f, "dehydration");
            result["damageReusedInstance"] = receiver.IsPanelVisible() && receiver.PanleInstance == first;

            receiver.Save();
            receiver.Unload();
            result["unloadReleasedReferences"] = receiver.PanleInstance == null && receiver.UIValues == null;
            result["unloadImmediatelyDisabledPanel"] = first == null || !first.activeSelf;
            result["unloadImmediatelyRestoredHierarchy"] = first == null || !first.transform.IsChildOf(actorRoot);
            receiver.Load();
            result["reloadStartsWithoutPanel"] = receiver.PanleInstance == null && !receiver.IsPanelVisible();
            receiver.ShowPanel();
            GameObject second = receiver.PanleInstance;
            result["reloadCreatedNewInstance"] = second != null && second != first;
            ItemMgr.Instance.DespawnItem(actor, saveData: false);
            result["despawnReleasedReferences"] = receiver == null || receiver.PanleInstance == null && receiver.UIValues == null;
            result["despawnImmediatelyDisabledPanel"] = second == null || !second.activeSelf;
            result["despawnImmediatelyRestoredHierarchy"] = second == null || !second.transform.IsChildOf(actorRoot);
            actor = null;
        }
        finally
        {
            if (actor != null && !actor.DestructionHandled)
                ItemMgr.Instance.DespawnItem(actor, saveData: false);
        }
        completed(result);
    }

    #endregion
}
