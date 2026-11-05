#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>采样取火工具的真实模块、库存和面板生命周期。</summary>
public static class GameplayTestFireMakingToolRuntimeProbe
{
    #region 测试入口

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterRuntime() => Register();
#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void RegisterEditor() => Register();
#endif
    private static void Register()
    {
        GameplayTestSystemProbe.Register("processing", "fire_tool_lifecycle", CaptureLifecycle);
        GameplayTestRuntimeProbe.Register("processing", "fire_tool_panel", CapturePanel);
    }

    #endregion

    #region 模块配置与保存

    private static JObject CaptureLifecycle(JObject args)
    {
        bool drill = (string)args["kind"] == "drill";
        using var scope = new Fixture(drill, 2, configure: true);
        bool loaded = scope.Tool.IsRuntimeLoaded;
        bool bound = scope.EventsBound;
        bool configuredRaw = scope.Drill != null ? scope.Drill.RawData.Contains("configured") : scope.Flint.RawData.Contains("configured");
        bool configuredToolValue = scope.Drill != null ? scope.Drill.HeatSourceTemperature == 175f : scope.Flint.SuccessChancePerClick == .75f;
        int inputSlots = scope.Input.Data.itemSlots.Count;
        int outputSlots = scope.Output.Data.itemSlots.Count;
        scope.Tool.Save();
        bool saved;
        if (scope.Drill != null)
        {
            FireDrillRuntimeState state = scope.Data.GetData<FireDrillRuntimeState>();
            saved = state?.Input?.itemSlots?.Count == 2 && state.Output?.itemSlots?.Count == 1;
        }
        else
        {
            List<string> state = scope.Data.GetData<List<string>>();
            saved = state?.Count == 1 && state[0] == "configured";
        }
        bool idPreserved = scope.Data.ModuleId == (drill ? "钻木取火模块" : "打火石模块");
        scope.Tool.Unload();
        bool unbound = scope.EventsUnbound;
        scope.Tool.Load();
        return new JObject
        {
            ["loaded"] = loaded, ["reloaded"] = scope.Tool.IsRuntimeLoaded,
            ["tickInterval"] = scope.Tool.FixedTickInterval,
            ["inputSlots"] = inputSlots, ["outputSlots"] = outputSlots,
            ["configuredRaw"] = configuredRaw, ["configuredInventory"] = scope.Input.StorageMaxWeightKg == 23f,
            ["configuredToolValue"] = configuredToolValue,
            ["bound"] = bound, ["unbound"] = unbound, ["rebound"] = scope.EventsBound,
            ["saved"] = saved, ["idPreserved"] = idPreserved
        };
    }

    #endregion

    #region 真实面板与按钮

    private static JObject CapturePanel(Item player, JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        bool drill = (string)args["kind"] == "drill";
        using var scope = new Fixture(drill, 1, configure: false, scene: player.gameObject.scene, player: player);
        GameObject source = GameRes.ExistingInstance.GetPrefab(drill ? "BuildingFeature_SparkMaker_Mod_FireDrill" : "FlintStrike");
        FireMakingInventoryPanelModule prototype = source != null ? source.GetComponentInChildren<FireMakingInventoryPanelModule>(true) : null;
        GameObject prefab = prototype?.UI_Prefab ?? throw new InvalidOperationException("正式取火工具未引用自身面板。");
        bool prefabFields = prototype != null && prototype.ModSaveData != null && prototype.InputInventory?.Data != null &&
                            prototype.OutputInventory?.Data != null && prototype.UI_Prefab != null &&
                            prototype.ModSaveData.ID == (drill ? "钻木取火模块" : "打火石模块");
        if (scope.Drill != null) scope.Drill.UI_Prefab = prefab;
        else scope.Flint.UI_Prefab = null;
        Inventory hand = player.GetComponentInChildren<Mod_Hand>()?.HandInventory ??
            throw new InvalidOperationException("本地主角手部库存尚未就绪。");
        ItemData material = GameRes.ExistingInstance.CreateItemData(drill ? "Cotton" : "Leaf");
        if (material?.Stack == null) throw new InvalidOperationException("正式取火材料不存在。");
        material.Stack.Amount = drill ? 1f : 2f;
        if (drill)
        {
            material.MatterState = new ItemMatterState { Initialized = true, TemperatureCelsius = 20f };
            scope.Drill.HeatSourceTemperature = 150f;
            scope.Drill.TemperaturePerClick = 10f;
        }
        else scope.Flint.SuccessChancePerClick = 1f;
        scope.Input.Data.SetOne_ItemData(0, material);
        scope.Host.Owner = player;
        scope.Tool.Load();
        scope.Receiver.OnAction_Start.Invoke(player);
        BasePanel panel = scope.Panel;
        bool opened = panel != null && panel.IsOpen();
        bool gameplayWindow = opened && !panel.IsGameplayInputBlocking && panel.AllowsHeldItemWorldDrop &&
                              panel.IsGamepadNavigationPrepared && panel.IsCancelShortcutTarget;
        bool slotBindings = scope.InputSlot?.BoundInventory == scope.Input && scope.OutputSlot?.BoundInventory == scope.Output;
        bool targets = ReferenceEquals(scope.Input.DefaultTarget_Inventory, hand) && ReferenceEquals(scope.Output.DefaultTarget_Inventory, hand);
        bool preview = scope.OutputSlot != null && scope.OutputSlot.GetComponent<CraftingOutputPreview>() != null;
        bool previewEvents = scope.Input.Data.Event_OnDataChanged.HasCalls &&
                             (scope.Output.Data.Event_OnDataChanged.HasCalls == !drill);
        scope.Receiver.OnAction_Stop.Invoke(player);
        bool closedByCancel = !panel.IsOpen();
        bool targetsCleared = scope.Input.DefaultTarget_Inventory == null && scope.Output.DefaultTarget_Inventory == null;
        scope.Host.OnAct.Invoke();
        bool reopened = panel.IsOpen();
        bool localUseToggled = false;
        if (!drill)
        {
            scope.Flint.PresentLocalUse();
            bool localUseClosed = !panel.IsOpen();
            scope.Flint.PresentLocalUse();
            localUseToggled = localUseClosed && panel.IsOpen();
        }
        scope.Receiver.OnAction_Start.Invoke(player);
        bool closedByToggle = !panel.IsOpen() && scope.Input.DefaultTarget_Inventory == null && scope.Output.DefaultTarget_Inventory == null;
        scope.Receiver.OnAction_Start.Invoke(player);
        panel.Close();
        bool closedByPanel = !panel.IsOpen() && scope.Input.DefaultTarget_Inventory == null && scope.Output.DefaultTarget_Inventory == null;
        scope.Receiver.OnAction_Start.Invoke(player);
        PhysicalDeviceArtworkView drillArtwork = drill ? panel.GetComponentInChildren<PhysicalDeviceArtworkView>(true) : null;
        int strokeBefore = drillArtwork != null ? drillArtwork.StrokeRevision : 0;
        scope.ActionButton.onClick.Invoke();
        bool drillStrokeAnimated = drillArtwork != null && drillArtwork.StrokeRevision == strokeBefore + 1;
        FlintStrikeToolGraphic artwork = panel.GetComponentInChildren<FlintStrikeToolGraphic>(true);
        bool artworkBound = artwork != null && artwork.ArtworkAtlas != null;
        bool successAnimated = artworkBound && artwork.StrikeRevision == 1 && artwork.LastStrikeSucceeded;
        ItemData after = scope.Input.Data.GetItemSlot(0)?.itemData;
        ItemData output = scope.Output.Data.GetItemSlot(0)?.itemData;
        float inputAmount = after?.Stack?.Amount ?? 0f;
        float temperature = after?.MatterState?.TemperatureCelsius ?? 0f;
        string outputId = output?.IDName ?? string.Empty;
        float outputAmount = output?.Stack?.Amount ?? 0f;
        float blockedInput = 0f;
        string blockedOutput = string.Empty;
        bool blockedAnimationUnchanged = false;
        bool failedStrikeAnimated = false;
        var drillDefinitionDurability = new JArray();
        var drillBreakMaterials = new JObject();
        if (drill)
        {
            foreach (string id in new[] { "SparkMaker_Summoner", "SparkMaker" })
            {
                ItemData definition = GameRes.ExistingInstance.CreateItemData(id);
                drillDefinitionDurability.Add(new JObject
                {
                    ["durability"] = definition.Durability, ["maximum"] = definition.MaxDurability
                });
            }
            foreach (ItemData drop in Mod_FireDrill.CreateBrokenFireDrillMaterialDrops())
                drillBreakMaterials[drop.IDName] = ((float?)drillBreakMaterials[drop.IDName] ?? 0f) + drop.Stack.Amount;
        }
        if (!drill)
        {
            scope.Output.Data.RemoveItemAll(scope.Output.Data.GetItemSlot(0), 0);
            scope.Output.Data.SetOne_ItemData(0, GameRes.ExistingInstance.CreateItemData("Log"));
            scope.ActionButton.onClick.Invoke();
            blockedInput = scope.Input.Data.GetItemSlot(0)?.itemData?.Stack?.Amount ?? 0f;
            blockedOutput = scope.Output.Data.GetItemSlot(0)?.itemData?.IDName ?? string.Empty;
            blockedAnimationUnchanged = artworkBound && artwork.StrikeRevision == 1;
            scope.Output.Data.RemoveItemAll(scope.Output.Data.GetItemSlot(0), 0);
            scope.Flint.SuccessChancePerClick = 0f;
            scope.ActionButton.onClick.Invoke();
            failedStrikeAnimated = artworkBound && artwork.StrikeRevision == 2 && !artwork.LastStrikeSucceeded &&
                scope.Input.Data.GetItemSlot(0)?.itemData?.Stack?.Amount == inputAmount &&
                scope.Output.Data.GetItemSlot(0)?.itemData == null;
        }
        JObject shortcuts = CapturePanelShortcutAdmission(player, panel);
        scope.Tool.Unload();
        return new JObject
        {
            ["opened"] = opened, ["slotBindings"] = slotBindings, ["transferTargets"] = targets,
            ["drillStrokeAnimated"] = drillStrokeAnimated,
            ["gameplayWindow"] = gameplayWindow,
            ["ownPanelPrefab"] = prefab.name == (drill ? "UI_FireDrill" : Mod_FlintStrike.PanelPrefabKey),
            ["formalFlintModules"] = !drill && scope.Host.itemData.ModuleDataDic.ContainsKey("打火石模块") &&
                                      scope.Host.itemData.ModuleDataDic.ContainsKey(ModText.Interact),
            ["localUseToggled"] = localUseToggled,
            ["artworkBound"] = artworkBound, ["successAnimated"] = successAnimated,
            ["blockedAnimationUnchanged"] = blockedAnimationUnchanged, ["failedStrikeAnimated"] = failedStrikeAnimated,
            ["shortcuts"] = shortcuts,
            ["preview"] = preview, ["previewEvents"] = previewEvents, ["prefabFields"] = prefabFields,
            ["closedByCancel"] = closedByCancel, ["reopened"] = reopened,
            ["closedByPanel"] = closedByPanel,
            ["closedByToggle"] = closedByToggle, ["closedTargetsCleared"] = targetsCleared,
            ["eventDetached"] = scope.EventsUnbound && !scope.Input.Data.Event_OnDataChanged.HasCalls &&
                                !scope.Output.Data.Event_OnDataChanged.HasCalls,
            ["panelReleased"] = scope.Panel == null,
            ["inputAmount"] = inputAmount, ["temperature"] = temperature,
            ["outputId"] = outputId, ["outputAmount"] = outputAmount,
            ["drillDefinitionDurability"] = drillDefinitionDurability, ["drillBreakMaterials"] = drillBreakMaterials,
            ["toolDurability"] = scope.Host.itemData.Durability,
            ["blockedInputAmount"] = blockedInput, ["blockedOutputId"] = blockedOutput
        };
    }

    // 用真实面板叠加模态和租约锁，采样窗口开关与玩法锁的独立边界。
    private static JObject CapturePanelShortcutAdmission(Item player, BasePanel panel)
    {
        Mod_GameController controller = player.GetComponent<Mod_GameController>();
        bool previousExplicitLock = controller.IsGameplayInputExplicitlyLocked;
        panel.SetGameplayInputBlocking(true);
        controller.AcquireGameplayInputLock(panel);
        try
        {
            var result = new JObject
            {
                ["gameplayLocked"] = controller.IsGameplayInputLocked,
                ["openOtherAllowed"] = controller.CanToggleGameplayWindow(null),
                ["closeAllowed"] = controller.CanToggleGameplayWindow(panel)
            };
            panel.Close();
            result["reopenAllowed"] = controller.CanToggleGameplayWindow(panel);
            panel.Open();
            controller.SetGameplayInputLocked(true);
            result["explicitLockBlocksOpen"] = !controller.CanToggleGameplayWindow(null);
            result["explicitLockAllowsClose"] = controller.CanToggleGameplayWindow(panel);
            return result;
        }
        finally
        {
            controller.SetGameplayInputLocked(previousExplicitLock);
            controller.ReleaseGameplayInputLock(panel);
            panel.PrepareForGameplayWindow();
        }
    }

    #endregion

    #region 独立模块夹具

    private sealed class Fixture : IDisposable
    {
        public readonly GameObject Root;
        public readonly Item Host;
        public readonly Mod_InteractReciver Receiver;
        public readonly Mod_FireDrill Drill;
        public readonly Mod_FlintStrike Flint;
        public readonly Ex_ModData_MemoryPackable Data;
        private readonly bool spawnedRuntimeItem;
        public Module Tool => Drill != null ? (Module)Drill : Flint;
        public Inventory Input => Drill != null ? Drill.InputInventory : Flint.InputInventory;
        public Inventory Output => Drill != null ? Drill.OutputInventory : Flint.OutputInventory;
        public BasePanel Panel => Drill != null ? Drill.basePanel : Flint.basePanel;
        public ItemSlot_UI InputSlot => Drill != null ? Drill.InputSlotUI : Flint.InputSlotUI;
        public ItemSlot_UI OutputSlot => Drill != null ? Drill.OutputSlotUI : Flint.OutputSlotUI;
        public Button ActionButton => Drill != null ? Drill.FrictionButton : Flint.StrikeButton;
        public bool EventsBound => Host.OnAct.HasCalls && Receiver.OnAction_Start.HasCalls && Receiver.OnAction_Stop.HasCalls;
        public bool EventsUnbound => !Host.OnAct.HasCalls && !Receiver.OnAction_Start.HasCalls && !Receiver.OnAction_Stop.HasCalls;

        public Fixture(bool drill, int inputSlots, bool configure, Scene scene = default, Item player = null)
        {
            if (!drill && !configure)
            {
                // 从正式定义装配，避免手工添加组件掩盖目录漏配能力。
                Host = ItemMgr.Instance.InstantiateHeldItem(GameRes.ExistingInstance.CreateItemData("FlintStrike"), player, player.transform);
                Root = Host.gameObject;
                spawnedRuntimeItem = true;
                try
                {
                    Host.Load();
                    Flint = Host.itemMods.GetMod_ByID<Mod_FlintStrike>("打火石模块") ??
                        throw new InvalidOperationException("正式打火石未装配取火模块。");
                    Receiver = Host.itemMods.GetMod_ByID<Mod_InteractReciver>(ModText.Interact) ??
                        throw new InvalidOperationException("正式打火石未装配交互模块。");
                    Data = Flint.ModSaveData;
                    return;
                }
                catch { Dispose(); throw; }
            }
            Root = new GameObject("[FireMakingToolProbe]") { hideFlags = HideFlags.HideAndDontSave };
            Root.SetActive(false);
            if (scene.IsValid()) SceneManager.MoveGameObjectToScene(Root, scene);
            try
            {
                Host = Root.AddComponent<GameItem>();
                Host.BindData(new Data_GeneralItem { IDName = "probe:fire_tool", Stack = new ItemStack { Amount = 1f },
                    Durability = 300f, MaxDurability = 300f });
                Receiver = Root.AddComponent<Mod_InteractReciver>();
                Receiver.modData = new Ex_ModData { ID = ModText.Interact, Name = "probe-interaction" };
                Receiver.ModuleInit(Host, Receiver.modData);
                Host.itemMods.AddMod(Receiver);
                Data = new Ex_ModData_MemoryPackable { ID = drill ? "钻木取火模块" : "打火石模块", Name = "probe-fire-tool" };
                if (drill) { Drill = Root.AddComponent<Mod_FireDrill>(); Drill.ModSaveData = Data; }
                else { Flint = Root.AddComponent<Mod_FlintStrike>(); Flint.ModSaveData = Data; }
                if (configure)
                {
                    string specific = drill ? "\"HeatSourceTemperature\":175" : "\"SuccessChancePerClick\":0.75";
                    ModuleJsonConfigurator.Apply(Tool, "probe:fire_tool", "probe-fire-tool", Data.ID,
                        "{\"RawData\":[\"configured\"],\"InputInventory\":{\"StorageMaxWeightKg\":23}," + specific + "}");
                }
                var slots = new List<ItemSlot>();
                for (int index = 0; index < inputSlots; index++) slots.Add(new ItemSlot(index));
                Input.Data = new Inventory_Data(slots, "输入");
                Output.Data = new Inventory_Data(new List<ItemSlot> { new ItemSlot(0) }, "输出");
                Tool.ModuleInit(Host, Data);
                Host.itemMods.AddMod(Tool);
                Tool.Load();
            }
            catch { Dispose(); throw; }
        }

        public void Dispose()
        {
            if (spawnedRuntimeItem)
            {
                ItemMgr.Instance.DespawnItem(Host, saveData: false);
                return;
            }
            if (Tool != null)
            {
                Tool.Unload();
                Input?.UnbindRuntimeDataEvents();
                Output?.UnbindRuntimeDataEvents();
            }
            if (Root != null)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(Root);
                else UnityEngine.Object.DestroyImmediate(Root);
            }
        }
    }

    #endregion
}
#endif
