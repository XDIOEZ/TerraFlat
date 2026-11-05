using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MemoryPack;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static partial class GameplayTestSystemProbe
{
    #region 玩家快照与快捷栏保存缓存采样

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
#endif
    private static void RegisterNetworkCaptureCacheRules()
    {
        Register("networking", "player_capture_cache", ProbeNetworkPlayerCaptureCache);
        Register("networking", "public_network_save_route", ProbeNetworkPublicSaveRoute);
        Register("networking", "hotbar_save_cache", ProbeNetworkHotbarSaveCache);
    }

    public static Dictionary<string, Inventory_Data> GetTestActorInventories(ItemData data)
    {
        if (!data.ModuleDataDic.TryGetValue("test:inventory", out var module))
        {
            module = new Inventory_ModuleData { StableName = "test:inventory", ModuleId = "Inventory" };
            data.ModuleDataDic.Add(module.StableName, module);
        }
        return ((Inventory_ModuleData)module).Data;
    }

    private static ItemData NetworkCapturePlayerData()
    {
        var data = ActorProfileMetadataStore.CreateActorData(new Data_GeneralItem
        { IDName = "probe:actor", Guid = 91001, Stack = new ItemStack { Amount = 1 }, MaxDurability = 10, Durability = 10 },
            "probe:profile", "probe:world");
        foreach (string id in new[] { "health", "movement", "stamina", ModText.Camera, Mod_TargetPerceptionSignature.ModuleId })
        {
            var module = new Ex_ModData { StableName = id, ModuleId = id };
            module.BitData = new JObject { ["value"] = 10f }.ToString();
            data.ModuleDataDic.Add(id, module);
        }
        ActorModuleState.SetCameraPov(data, 10f);
        ActorModuleState.SetTargetPerceptionMultiplier(data, 1f);
        return data;
    }
    private static void ChangeCaptureModule(ItemData data, string id, string field)
    {
        var module = (Ex_ModData)data.ModuleDataDic[id];
        var payload = JObject.Parse(module.BitData);
        payload[field] = (payload.Value<float?>(field) ?? 0f) + 1f;
        module.BitData = payload.ToString();
    }

    private static JObject ProbeNetworkPlayerCaptureCache(JObject args)
    {
        var root = new GameObject("network-player-capture-probe") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        Item player = ActorProfileRegistry.CreateProfileActorForFixture(root);
        var visiting = new HashSet<ItemData>();
        ulong Signature(ItemData value, bool publicState)
        {
            visiting.Clear();
            if (!ItemRuntimeStateFingerprint.TryCalculate(value, false, publicState, visiting, out ulong signature))
                throw new InvalidOperationException("标准玩家状态未能生成指纹。");
            return signature;
        }
        byte[] PrivatePayload(ItemData value)
            => ItemNetworkStateSerialization.SerializeSnapshot(ItemInstanceSnapshot.Capture(value, false));
        try
        {
            var cases = new (string Name, Action<ItemData> Change)[]
            {
                ("hp", value => ChangeCaptureModule(value, "health", "Hp")),
                ("max_hp", value => ChangeCaptureModule(value, "health", "MaxHp")),
                ("body_health", value => ChangeCaptureModule(value, "health", "BodyPartHp")),
                ("speed_base", value => ChangeCaptureModule(value, "movement", "BaseValue")),
                ("speed_base_add", value => ChangeCaptureModule(value, "movement", "BaseAdditive")),
                ("speed_add", value => ChangeCaptureModule(value, "movement", "AdditiveModifier")),
                ("speed_multiply", value => ChangeCaptureModule(value, "movement", "MultiplicativeModifier")),
                ("speed_final_add", value => ChangeCaptureModule(value, "movement", "FinalAdditive")),
                ("run", value => ChangeCaptureModule(value, "movement", "Run")),
                ("stamina", value => ChangeCaptureModule(value, "stamina", "CurrentStamina")),
                ("stamina_max", value => ChangeCaptureModule(value, "stamina", "MaxStamina")),
                ("pov", value => ActorModuleState.SetCameraPov(value, 11f)),
                ("perception", value => ActorModuleState.SetTargetPerceptionMultiplier(value, 2f)),
                ("special", value => value.ItemSpecialData = "changed"),
                ("durability", value => value.Durability -= 1),
                ("faction", value => value.FactionId = "changed"),
                ("tags", value => value.Tags.Add("changed")),
                ("amount", value => value.Stack.Amount += 1),
                ("matter", value => value.MatterState.TemperatureCelsius += 1)
            };
            var samples = new JArray();
            foreach (var change in cases)
            {
                ItemData data = NetworkCapturePlayerData();
                player.BindData(data);
                ulong before = Signature(data, true);
                var first = ItemNetworkStateSerialization.CaptureVersioned(player, true);
                byte[] frozen = (byte[])first.Payload.Clone();
                var repeated = ItemNetworkStateSerialization.CaptureVersioned(player, true);
                change.Change(data);
                var changed = ItemNetworkStateSerialization.CaptureVersioned(player, true);
                ChangeCaptureModule(data, "stamina", "CurrentStamina");
                var changedAgain = ItemNetworkStateSerialization.CaptureVersioned(player, true);
                samples.Add(new JObject
                {
                    ["field"] = change.Name,
                    ["reused"] = first.Reusable && repeated.Reusable && first.Version == repeated.Version &&
                        ReferenceEquals(first.Payload, repeated.Payload),
                    ["invalidated"] = before != Signature(data, true) && changed.Version > first.Version &&
                        !ReferenceEquals(first.Payload, changed.Payload) && !first.Payload.SequenceEqual(changed.Payload),
                    ["sameFrameChanged"] = changedAgain.Version > changed.Version && !changedAgain.Payload.SequenceEqual(changed.Payload),
                    ["oldPayloadUnchanged"] = first.Payload.SequenceEqual(frozen)
                });
            }

            ItemData privateData = NetworkCapturePlayerData();
            var inventory = new Inventory_Data(new List<ItemSlot>
            {
                new(0) { itemData = new Data_GeneralItem { IDName = "probe:private", Guid = 91002, Stack = new ItemStack { Amount = 5 } } }
            }, "private");
            GetTestActorInventories(privateData)["private"] = inventory;
            player.BindData(privateData);
            ulong publicBefore = Signature(privateData, true), privateBefore = Signature(privateData, false);
            byte[] privateBytes = PrivatePayload(privateData);
            var publicCapture = ItemNetworkStateSerialization.CaptureVersioned(player, true);
            inventory.itemSlots[0].itemData.Stack.Amount = 4;
            var publicAfter = ItemNetworkStateSerialization.CaptureVersioned(player, true);
            var result = new JObject
            {
                ["fields"] = samples,
                ["publicPrivateInventoryIgnored"] = Signature(privateData, true) == publicBefore &&
                    ReferenceEquals(publicCapture.Payload, publicAfter.Payload),
                ["privateInventoryChanged"] = Signature(privateData, false) != privateBefore &&
                    !PrivatePayload(privateData).SequenceEqual(privateBytes)
            };

            ItemData normalized = NetworkCapturePlayerData();
            GetTestActorInventories(normalized);
            var normalizedInventory = (Inventory_ModuleData)normalized.ModuleDataDic["test:inventory"];
            normalizedInventory.Data = null;
            ulong nullSignature = Signature(normalized, false);
            byte[] nullPayload = PrivatePayload(normalized);
            normalizedInventory.Data = new Dictionary<string, Inventory_Data>();
            result["nullInventoryNormalized"] = Signature(normalized, false) == nullSignature &&
                PrivatePayload(normalized).SequenceEqual(nullPayload);
            visiting.Clear();
            bool derivedPlayer = ItemRuntimeStateFingerprint.TryCalculate(new GameplayTestCaptureExtendedPlayer(), false, true, visiting, out _);
            normalized.ModuleDataDic["unknown"] = new GameplayTestCaptureUnknownModuleData
            { StableName = "unknown", ModuleId = "probe:unknown" };
            using (ModuleInstanceStateCodecs.Register(new GameplayTestCaptureUnknownCodec()))
            {
                visiting.Clear();
                bool unknownCodec = ItemRuntimeStateFingerprint.TryCalculate(normalized, false, true, visiting, out _);
                player.BindData(normalized);
                result["unsupportedFallsBack"] = !derivedPlayer && !unknownCodec &&
                    !ItemNetworkStateSerialization.CaptureVersioned(player, true).Reusable;
            }
            return result;
        }
        finally
        {
            ItemNetworkCaptureCache.Invalidate(player);
            player.PrepareForDespawn(false);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static JObject ProbeNetworkPublicSaveRoute(JObject args)
    {
        var root = new GameObject("network-public-save-probe") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        Item player = ActorProfileRegistry.CreateProfileActorForFixture(root);
        player.BindData(NetworkCapturePlayerData());
        var optimized = root.AddComponent<GameplayTestNetworkPublicSaveCounter>();
        optimized.ModuleInit(player, new Ex_ModData_MemoryPackable
        { StableName = "public-save", ModuleId = "probe:public-save" });
        player.itemMods.AddMod(optimized);
        player.itemData.ModuleDataDic["public-save"] = optimized._Data;
        var fallback = root.AddComponent<GameplayTestNetworkCaptureSaveCounter>();
        fallback.ModuleInit(player, new Ex_ModData_MemoryPackable
        { StableName = "full-save", ModuleId = "probe:full-save" });
        player.itemMods.AddMod(fallback);
        player.itemData.ModuleDataDic["full-save"] = fallback._Data;
        typeof(Item).GetField("modulesLoaded", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(player, true);
        optimized.LoadRuntime();
        fallback.LoadRuntime();
        try
        {
            var first = ItemNetworkStateSerialization.CaptureVersioned(player, true);
            byte[] frozen = (byte[])first.Payload.Clone();
            var repeated = ItemNetworkStateSerialization.CaptureVersioned(player, true);
            bool publicOnly = optimized.PublicSaves == 2 && optimized.FullSaves == 0;
            bool fallbackSaved = fallback.SaveCalls == 2;
            bool payloadReused = first.Reusable && repeated.Reusable && first.Version == repeated.Version &&
                                 ReferenceEquals(first.Payload, repeated.Payload);

            player.ModuleSave();
            bool fullSaveUnchanged = optimized.PublicSaves == 2 && optimized.FullSaves == 1 &&
                                     fallback.SaveCalls == 3;
            optimized.Value++;
            var changed = ItemNetworkStateSerialization.CaptureVersioned(player, true);
            return new JObject
            {
                ["publicOnly"] = publicOnly,
                ["fallbackSaved"] = fallbackSaved,
                ["payloadReused"] = payloadReused,
                ["fullSaveUnchanged"] = fullSaveUnchanged,
                ["newValueCaptured"] = changed.Version > repeated.Version &&
                                       !ReferenceEquals(changed.Payload, repeated.Payload) &&
                                       !changed.Payload.SequenceEqual(repeated.Payload),
                ["oldPayloadUnchanged"] = first.Payload.SequenceEqual(frozen)
            };
        }
        finally
        {
            ItemNetworkCaptureCache.Invalidate(player);
            player.PrepareForDespawn(false);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static JObject ProbeNetworkHotbarSaveCache(JObject args)
    {
        var root = new GameObject("network-hotbar-save-probe") { hideFlags = HideFlags.HideAndDontSave };
        root.SetActive(false);
        var owner = root.AddComponent<GameplayTestNetworkCaptureItem>();
        owner.BindData(new Data_GeneralItem { IDName = "probe:hotbar-owner", Guid = 91100, Stack = new ItemStack() });
        var heldRoot = new GameObject("held-save-probe");
        heldRoot.transform.SetParent(root.transform, false);
        var held = heldRoot.AddComponent<GameplayTestNetworkCaptureItem>();
        var heldData = new Data_GeneralItem { IDName = "probe:held", Guid = 91101, Stack = new ItemStack { Amount = 3 } };
        held.BindData(heldData);
        var counter = heldRoot.AddComponent<GameplayTestNetworkCaptureSaveCounter>();
        counter.ModuleInit(held, new Ex_ModData_MemoryPackable { StableName = "counter", ModuleId = "probe:counter" });
        held.itemMods.AddMod(counter);
        heldData.ModuleDataDic["counter"] = counter._Data;
        typeof(Item).GetField("modulesLoaded", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(held, true);
        counter.LoadRuntime();
        var hotbar = root.AddComponent<Mod_HotBar>();
        hotbar.ModuleInit(owner, new Ex_ModData_MemoryPackable { StableName = "hotbar", ModuleId = ModText.Hotbar });
        hotbar.RuntimeInventory.Data = new Inventory_Data(new List<ItemSlot>
        { new(0) { itemData = heldData }, new(1) }, "hotbar");
        hotbar.CurentSelectItem = held;
        MethodInfo save = typeof(Mod_HotBar).GetMethod("OnSave", BindingFlags.Instance | BindingFlags.NonPublic);
        int writes = 0;
        void Watch(ModuleData value) => writes++;
        hotbar.ModSaveData.PayloadChanged += Watch;
        int SaveWrites()
        {
            int before = writes;
            save.Invoke(hotbar, null);
            return writes - before;
        }
        try
        {
            int first = SaveWrites();
            byte[] firstPayload = hotbar.ModSaveData.BitData;
            int repeated = SaveWrites();
            var result = new JObject
            {
                ["firstWrites"] = first, ["repeatedWrites"] = repeated,
                ["payloadReused"] = ReferenceEquals(firstPayload, hotbar.ModSaveData.BitData),
                ["heldSavedEveryTime"] = counter.SaveCalls == 2
            };
            counter.Value = 12;
            result["heldChangeWrites"] = SaveWrites();
            InventoryHotBarSaveState state = hotbar.ModSaveData.GetData<InventoryHotBarSaveState>();
            var restored = state.Inventory.CreateColdData();
            result["heldChangeCaptured"] = ((Ex_ModData_MemoryPackable)restored.itemSlots[0].itemData.ModuleDataDic["counter"]).BitData[0] == 12;
            hotbar.RuntimeInventory.Data.Index = 1;
            result["selectionWrites"] = SaveWrites();
            result["selectionCaptured"] = hotbar.ModSaveData.GetData<InventoryHotBarSaveState>().Inventory.CreateColdData().Index == 1;
            var inventoryChanges = new (string Name, Action<Inventory_Data> Change)[]
            {
                ("name", value => value.Name += "2"),
                ("initialized", value => value.IsInjected = !value.IsInjected),
                ("panel_position", value => value.PanelPosition += Vector3.one),
                ("panel_open", value => value.PanelIsOpen = !value.PanelIsOpen),
                ("deposit_blocked", value => value.IsDepositBlocked = !value.IsDepositBlocked),
                ("slot_count", value => value.itemSlots.Add(new ItemSlot(2))),
                ("item_amount", value => value.itemSlots[0].itemData.Stack.Amount -= 1),
                ("item_durability", value => value.itemSlots[0].itemData.Durability = .5f),
                ("item_position", value => value.itemSlots[0].itemData.transform.position += Vector3.right),
                ("item_tags", value => value.itemSlots[0].itemData.Tags.Add("changed")),
                ("item_matter", value => value.itemSlots[0].itemData.MatterState.TemperatureCelsius += 1)
            };
            var fieldWrites = new JArray();
            foreach (var change in inventoryChanges)
            {
                change.Change(hotbar.RuntimeInventory.Data);
                fieldWrites.Add(new JObject { ["field"] = change.Name, ["writes"] = SaveWrites() });
            }
            result["inventoryFields"] = fieldWrites;
            hotbar.RuntimeInventory.Data.itemSlots[0].SlotMaxVolume += 1;
            hotbar.RuntimeInventory.Data.itemSlots[0].CanAcceptTags = new List<string> { "layout-only" };
            result["layoutNotPersistedWrites"] = SaveWrites();
            hotbar.ModSaveData.BitData[0] ^= 1;
            result["externalRewriteWrites"] = SaveWrites();
            result["externalRewriteRepaired"] = hotbar.ModSaveData.GetData<InventoryHotBarSaveState>().Version == InventoryHotBarSaveState.CurrentVersion;
            hotbar.ModSaveData.PayloadChanged -= Watch;
            hotbar.ModSaveData = new Ex_ModData_MemoryPackable
            { StableName = "hotbar", ModuleId = ModText.Hotbar, BitData = (byte[])hotbar.ModSaveData.BitData.Clone() };
            hotbar.ModSaveData.PayloadChanged += Watch;
            result["moduleRebindWrites"] = SaveWrites();
            hotbar.RuntimeInventory.Data = InventoryRuntimeDataFactory.CloneRuntime(hotbar.RuntimeInventory.Data);
            result["inventoryRebindWrites"] = SaveWrites();
            PropertyInfo generation = typeof(Module).GetProperty(nameof(Module.RuntimeGeneration));
            generation.SetValue(hotbar, hotbar.RuntimeGeneration + 1);
            result["generationWrites"] = SaveWrites();
            using (ModuleInstanceStateCodecs.Register(new GameplayTestCaptureUnknownCodec()))
                result["codecRevisionWrites"] = SaveWrites();
            hotbar.RuntimeInventory.Data = new GameplayTestCaptureExtendedInventory();
            result["fallbackFirstWrites"] = SaveWrites();
            result["fallbackRepeatWrites"] = SaveWrites();
            hotbar.RuntimeInventory.Data = new Inventory_Data(new List<ItemSlot> { new(0) }, "hotbar");
            SaveWrites();
            hotbar.CurentSelectItem = null;
            typeof(Mod_HotBar).GetMethod("OnUnload", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hotbar, null);
            result["unloadInvalidates"] = SaveWrites() == 1;
            return result;
        }
        finally
        {
            hotbar.CurentSelectItem = null;
            hotbar.ModSaveData.PayloadChanged -= Watch;
            held.PrepareForDespawn(false);
            owner.PrepareForDespawn(false);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    #endregion
}

#region 快照缓存测试替身

internal sealed class GameplayTestCaptureExtendedPlayer : Data_GeneralItem { }
internal sealed class GameplayTestCaptureExtendedSpeed : GameValue_float { }
internal sealed class GameplayTestCaptureExtendedInventory : Inventory_Data
{
    public GameplayTestCaptureExtendedInventory() : base(new List<ItemSlot> { new(0) }, "extended") { }
}
internal sealed class GameplayTestCaptureUnknownModuleData : ModuleData { }
internal sealed class GameplayTestCaptureUnknownCodec : IModuleInstanceStateCodec
{
    public string Id => "probe.network-capture.unknown";
    public Type DataType => typeof(GameplayTestCaptureUnknownModuleData);
    public ModuleData CreateData() => new GameplayTestCaptureUnknownModuleData();
    public byte[] Capture(ModuleData data, bool publicState) => new byte[] { 7 };
    public void Restore(ModuleData data, byte[] payload) { }
}
internal sealed class GameplayTestNetworkCaptureItem : Item
{
    private ItemData data;
    public override ItemData itemData => data;
    protected override void SetItemData(ItemData value) => data = value;
}
internal sealed class GameplayTestNetworkCaptureSaveCounter : Module
{
    private Ex_ModData_MemoryPackable data = new();
    public override ModuleData _Data { get => data; set => data = (Ex_ModData_MemoryPackable)value; }
    public int SaveCalls;
    public byte Value = 11;
    protected override void OnLoad() { }
    protected override void OnSave() { SaveCalls++; data.BitData = new[] { Value }; }
}
internal sealed class GameplayTestNetworkPublicSaveCounter : Module, IPublicNetworkStateSaver
{
    private Ex_ModData_MemoryPackable data = new();
    public override ModuleData _Data { get => data; set => data = (Ex_ModData_MemoryPackable)value; }
    public int PublicSaves, FullSaves;
    public byte Value = 17;
    protected override void OnLoad() { }
    protected override void OnSave() { FullSaves++; data.BitData = new[] { Value }; }
    public void SavePublicNetworkState() { PublicSaves++; data.BitData = new[] { Value }; }
}

#endregion
