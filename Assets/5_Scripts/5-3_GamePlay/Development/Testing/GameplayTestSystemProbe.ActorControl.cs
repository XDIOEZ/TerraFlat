using System;
using System.Collections.Generic;
using System.Linq;
using MemoryPack;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 角色控制与旧库存规则
    private static void RegisterActorControlOperations(Dictionary<string, Func<JObject, JObject>> operations)
    {
        operations.Add("ai/actor_control_lease", CaptureActorControlLease);
        operations.Add("save/legacy_actor_inventory", CaptureLegacyActorInventory);
    }

    private static JObject CaptureActorControlLease(JObject args)
    {
        var scene = CreateGameplayTestProbeScene("ActorControlLease");
        var root = new GameObject("ActorControlLease");
        root.SetActive(false);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        var actor = root.AddComponent<GameItem>();
        var data = new Data_GeneralItem { IDName = "Human" };
        actor.BindData(data);
        var control = root.AddComponent<Mod_ActorControl>();
        control.ModuleInit(actor, new Ex_ModData { StableName = "control", ModuleId = Mod_ActorControl.ModuleId });
        actor.itemMods.AddMod(control);
        try
        {
            ActorProfileRegistry.BindProfile(actor, false, false, "fixture:actor-control");
            control.ReleaseManualControl();
            object first = new(), second = new();
            bool acquired = control.TryAcquireManualControl(first, false, out var lease);
            bool exclusive = !control.TryAcquireManualControl(second, false, out _);
            control.TryAcquireManualControl(first, false, out var renewed);
            lease.Dispose();
            bool staleLeaseIgnored = control.HasManualControl;
            renewed.Dispose();
            return new JObject
            {
                ["acquired"] = acquired, ["exclusive"] = exclusive, ["staleLeaseIgnored"] = staleLeaseIgnored,
                ["released"] = !control.HasManualControl,
                ["profileRetained"] = ActorProfileRegistry.GetProfileId(actor) == "fixture:actor-control" &&
                    ActorProfileRegistry.ProfileActors["fixture:actor-control"] == actor,
                ["sameData"] = ReferenceEquals(data, actor.itemData)
            };
        }
        finally
        {
            ActorProfileRegistry.UnbindProfile(actor);
            UnityEngine.Object.DestroyImmediate(root);
            CloseGameplayTestProbeScene(scene);
        }
    }

    private static JObject CaptureLegacyActorInventory(JObject args)
    {
        Inventory_Data Inventory(string name, params int[] ids) => new(ids.Select((id, index) => new ItemSlot(index)
        { itemData = new Data_GeneralItem { IDName = "probe:cargo", Guid = id, Stack = new ItemStack { Amount = 1 } } }).ToList(), name);
        var actor = new Data_GeneralItem { IDName = "Human" };
        var bag = new Inventory_ModuleData { StableName = "bag", ModuleId = ModText.Bag };
        Inventory_Data actual = Inventory("bag", 701);
        bag.Data["bag"] = actual;
        bag.Data["empty"] = Inventory("empty");
        var hotbar = new Ex_ModData_MemoryPackable { StableName = "hotbar", ModuleId = ModText.Hotbar,
            BitData = MemoryPackSerializer.Serialize(new InventoryHotBarSaveState
            { Version = InventoryHotBarSaveState.CurrentVersion, Inventory = InventoryInstanceSnapshot.Capture(Inventory("hotbar", 703)) }) };
        var legacy = new Inventory_ModuleData { StableName = LegacyActorModuleState.InventoriesKey,
            ModuleId = LegacyActorModuleState.InventoriesKey };
        legacy.Data["bag"] = Inventory("bag", 701, 702, 703);
        legacy.Data["empty"] = Inventory("empty", 701, 704);
        actor.ModuleDataDic[bag.StableName] = bag;
        actor.ModuleDataDic[hotbar.StableName] = hotbar;
        actor.ModuleDataDic[legacy.StableName] = legacy;
        ActorModuleState.ApplyLegacyModuleState(actor);
        int[] ids = bag.Data.Values.SelectMany(value => value.itemSlots).Where(slot => slot?.itemData != null)
            .Select(slot => slot.itemData.Guid).ToArray();
        return new JObject
        {
            ["modulePriority"] = ReferenceEquals(actual, bag.Data["bag"]),
            ["remainingIds"] = new JArray(ids), ["compatibilityKey"] = bag.Data.ContainsKey("legacy:bag"),
            ["keyMatched"] = bag.Data["empty"].itemSlots[1].itemData?.Guid == 704,
            ["legacyRemoved"] = LegacyActorModuleState.FindPendingModule(actor, LegacyActorModuleState.InventoriesKey) == null
        };
    }
    #endregion
}

public static partial class GameplayTestActorWorldRuntimeProbe
{
    #region 同一身体的接管与释放现场
    private static JObject CaptureActorControl(Item original, JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(original);
        string id = args.Value<string>("actor") ?? "Wolf";
        GameRes.Instance.TryGetItemDefinition(id, out RuntimeItemDefinition definition);
        bool rawShellAssembled = id != "Human" || CanAssembleRawHumanShell(definition);
        Item actor = ItemMgr.Instance.InstantiateItem(definition.CreateItemData(), original.transform.position + Vector3.right * 4f,
            Quaternion.identity, Vector3.one);
        string fixtureProfile = "fixture:control:" + actor.itemData.Guid;
        var originalBinding = ActorProfileRegistry.GetBinding(original);
        string originalProfile = originalBinding.ProfileId;
        IDisposable lease = null;
        try
        {
            actor.Load();
            // 记录正式首帧和角色外观合成后的身体图片。
            var animation = actor.itemMods.GetMod_ByID<Mod_AnimatorController>(ModText.AnimatorReceiver);
            var appearance = actor.itemMods.GetMod_ByID<Mod_CharacterAppearance>("CharacterAppearance");
            var discard = actor.itemMods.GetMod_ByID<Mod_DiscardItem>(ModText.ItemDorper);
            var equipment = actor.itemMods.GetMod_ByID<Mod_Equipment>(ModText.Equipment_Module);
            var crafting = actor.GetComponentInChildren<Mod_HandCraftTable>(true);
            bool bodyAbilitiesLoaded = discard != null && discard.IsRuntimeLoaded &&
                equipment != null && equipment.IsRuntimeLoaded && crafting != null && crafting.IsRuntimeLoaded;
            Sprite bodySprite = actor.Sprite != null ? actor.Sprite.sprite : null;
            var population = MonsterManager.GetForWorld(actor.gameObject.scene.name, false);
            int residentsBefore = population?.GetResidentSpeciesCount(id) ?? 0;
            ActorProfileRegistry.BindProfile(actor, false, false, fixtureProfile);
            int residentsAfter = population?.GetResidentSpeciesCount(id) ?? 0;
            population?.Register(actor);
            bool excludedFromPopulation = (population?.GetResidentSpeciesCount(id) ?? 0) == residentsAfter &&
                (id == "Human" || residentsAfter == residentsBefore - 1);
            var control = actor.itemMods.GetMod_ByID<Mod_ActorControl>(Mod_ActorControl.ModuleId);
            var brain = actor.itemMods.GetMod_ByID<Mod_AnimalBehaviorTree>(Mod_AnimalBehaviorTree.ModuleId);
            control.ReleaseManualControl();
            brain.ModUpdate(.1f);
            var context = brain.Context;
            ItemData data = actor.itemData;
            int modules = actor.Mods.Count;
            control.TryAcquireManualControl(new object(), true, out lease);
            var controller = actor.itemMods.GetMod_ByID<Mod_GameController>(ModText.Controller);
            object inputLock = new();
            controller.AcquireGameplayInputLock(inputLock);
            brain.ModUpdate(.2f);
            bool idleRemainsManual = control.HasManualControl && brain.CurrentAction == null;
            controller.ReleaseGameplayInputLock(inputLock);
            var stamina = actor.itemMods.GetMod_ByID<Mod_Stamina>(ModText.Stamina);
            var food = actor.itemMods.GetMod_ByID<Mod_Food>(ModText.Food);
            var staminaHud = stamina?.slider;
            var nutritionHud = food?.PanleInstance;
            bool bodyHudsLoaded = id != "Human" || (staminaHud != null && nutritionHud != null);
            int localModules = actor.Mods.Values.Count(module => module is IActorLocalControlModule && module.IsRuntimeLoaded);
            bool nativeAttack = control.RequestConfiguredBodyAttack(actor.transform.position + Vector3.right * 3f);
            bool coolingDown = !control.RequestConfiguredBodyAttack(actor.transform.position + Vector3.right * 3f);
            lease.Dispose();
            lease = null;
            brain.ModUpdate(.1f);
            bool bodyAbilitiesAfterRelease = discard != null && discard.IsRuntimeLoaded &&
                equipment != null && equipment.IsRuntimeLoaded && crafting != null && crafting.IsRuntimeLoaded;
            bool bodyHudsReleased = (stamina == null || stamina.slider == null) && (food == null || food.PanleInstance == null) &&
                (staminaHud == null || !staminaHud.gameObject.activeInHierarchy) && (nutritionHud == null || !nutritionHud.activeInHierarchy);
            control.TryAcquireManualControl(new object(), true, out lease);
            bool bodyHudsRecreated = id != "Human" || (stamina.slider != null && food.PanleInstance != null &&
                !ReferenceEquals(staminaHud, stamina.slider) && !ReferenceEquals(nutritionHud, food.PanleInstance));
            lease.Dispose();
            lease = null;
            brain.ModUpdate(.1f);
            return new JObject
            {
                ["rawShellAssembled"] = rawShellAssembled,
                ["idleRemainsManual"] = idleRemainsManual, ["nativeAttack"] = nativeAttack,
                ["coolingDown"] = coolingDown, ["resumed"] = brain.CurrentAction != null && !control.HasManualControl,
                ["sameBodyAndContext"] = ReferenceEquals(data, actor.itemData) && ReferenceEquals(context, brain.Context),
                ["sameModules"] = modules == actor.Mods.Count, ["genuineAI"] = RuntimeAiEntityUtility.IsAiEntity(actor),
                ["excludedFromPopulation"] = excludedFromPopulation,
                ["bodyHudsLoaded"] = bodyHudsLoaded, ["bodyHudsReleased"] = bodyHudsReleased,
                ["bodyHudsRecreated"] = bodyHudsRecreated,
                ["profileRetained"] = ActorProfileRegistry.GetProfileId(actor) == fixtureProfile,
                ["localModulesLoaded"] = localModules,
                ["localModulesAfterRelease"] = actor.Mods.Values.Count(module => module is IActorLocalControlModule && module.IsRuntimeLoaded),
                ["singleSpriteMode"] = animation != null && animation.IsSingleSpriteMode,
                ["hasDefinitionSprite"] = definition.Sprite != null,
                ["hasBodySprite"] = bodySprite != null,
                ["bodyUsesDefinitionSprite"] = bodySprite != null && ReferenceEquals(bodySprite, definition.Sprite),
                ["appearanceLoaded"] = appearance != null && appearance.IsRuntimeLoaded,
                ["hasEquip"] = equipment is Mod_Equipment_Player,
                ["bodyAbilitiesLoaded"] = bodyAbilitiesLoaded,
                ["bodyAbilitiesAfterRelease"] = bodyAbilitiesAfterRelease,
                ["hasBag"] = actor.itemMods.GetMod_ByID<Mod_Inventory>(ModText.Bag) != null,
                ["hasRoll"] = actor.itemMods.GetMod_ByID<Mod_Roll>(Mod_Roll.ModuleId) != null
            };
        }
        finally
        {
            lease?.Dispose();
            ItemMgr.Instance.DespawnItem(actor, saveData: false);
            ActorProfileRegistry.BindProfile(original, true, originalBinding.WasCreated, originalProfile);
        }
    }

    // 对原始外壳预先生成模块名，再按当前定义装配，覆盖旧变体与正式能力并存。
    private static bool CanAssembleRawHumanShell(RuntimeItemDefinition definition)
    {
        var root = new GameObject("[LuaRawHumanAssembly]");
        root.SetActive(false);
        try
        {
            GameObject template = UnityEngine.Object.Instantiate(GameRes.Instance.GetPrefab("Human"), root.transform, false);
            foreach (Module module in template.GetComponentsInChildren<Module>(true)) module.EnsureRuntimeIdentity();
            GameObject shell = UnityEngine.Object.Instantiate(template, root.transform, false);
            Item item = shell.GetComponent<Item>();
            ItemDefinitionRuntime.ConfigureInstance(GameRes.Instance, definition, item, definition.CreateItemData());
            return shell.GetComponentsInChildren<Module>(true).Count(module =>
                definition.TryGetModuleAssembly(module.StableName, out var plan) && plan.MatchesBoundModule(module)) ==
                definition.ModuleAssemblies.Count(plan => !plan.IsDataOnly);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }
    #endregion
}
