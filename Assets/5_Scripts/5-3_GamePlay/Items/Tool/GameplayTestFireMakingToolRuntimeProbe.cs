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

    private static JObject CapturePanel(Player player, JObject args)
    {
        GameplayTestRuntimeProbe.RequireIsolatedSaveRoot(player);
        bool drill = (string)args["kind"] == "drill";
        using var scope = new Fixture(drill, 1, configure: false, scene: player.gameObject.scene);
        GameObject prefab = GameRes.ExistingInstance?.GetPrefab("UI_FireDrill") ??
            throw new InvalidOperationException("正式取火面板未加载。");
        GameObject source = GameRes.ExistingInstance.GetPrefab(drill ? "BuildingFeature_SparkMaker_Mod_FireDrill" : "FlintStrike");
        FireMakingInventoryPanelModule prototype = source != null ? source.GetComponentInChildren<FireMakingInventoryPanelModule>(true) : null;
        bool prefabFields = prototype != null && prototype.ModSaveData != null && prototype.InputInventory?.Data != null &&
                            prototype.OutputInventory?.Data != null && prototype.UI_Prefab != null &&
                            prototype.ModSaveData.ID == (drill ? "钻木取火模块" : "打火石模块");
        if (scope.Drill != null) scope.Drill.UI_Prefab = prefab;
        else scope.Flint.UI_Prefab = prefab;
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
        scope.Receiver.OnAction_Start.Invoke(player);
        bool closedByToggle = !panel.IsOpen() && scope.Input.DefaultTarget_Inventory == null && scope.Output.DefaultTarget_Inventory == null;
        scope.Receiver.OnAction_Start.Invoke(player);
        scope.ActionButton.onClick.Invoke();
        ItemData after = scope.Input.Data.GetItemSlot(0)?.itemData;
        ItemData output = scope.Output.Data.GetItemSlot(0)?.itemData;
        float inputAmount = after?.Stack?.Amount ?? 0f;
        float temperature = after?.MatterState?.TemperatureCelsius ?? 0f;
        string outputId = output?.IDName ?? string.Empty;
        float outputAmount = output?.Stack?.Amount ?? 0f;
        float blockedInput = 0f;
        string blockedOutput = string.Empty;
        if (!drill)
        {
            scope.Output.Data.RemoveItemAll(scope.Output.Data.GetItemSlot(0), 0);
            scope.Output.Data.SetOne_ItemData(0, GameRes.ExistingInstance.CreateItemData("Log"));
            scope.ActionButton.onClick.Invoke();
            blockedInput = scope.Input.Data.GetItemSlot(0)?.itemData?.Stack?.Amount ?? 0f;
            blockedOutput = scope.Output.Data.GetItemSlot(0)?.itemData?.IDName ?? string.Empty;
        }
        scope.Tool.Unload();
        return new JObject
        {
            ["opened"] = opened, ["slotBindings"] = slotBindings, ["transferTargets"] = targets,
            ["preview"] = preview, ["previewEvents"] = previewEvents, ["prefabFields"] = prefabFields,
            ["closedByCancel"] = closedByCancel, ["reopened"] = reopened,
            ["closedByToggle"] = closedByToggle, ["closedTargetsCleared"] = targetsCleared,
            ["eventDetached"] = scope.EventsUnbound && !scope.Input.Data.Event_OnDataChanged.HasCalls &&
                                !scope.Output.Data.Event_OnDataChanged.HasCalls,
            ["panelReleased"] = scope.Panel == null,
            ["inputAmount"] = inputAmount, ["temperature"] = temperature,
            ["outputId"] = outputId, ["outputAmount"] = outputAmount,
            ["blockedInputAmount"] = blockedInput, ["blockedOutputId"] = blockedOutput
        };
    }

    #endregion

    #region 独立模块夹具

    private sealed class Fixture : IDisposable
    {
        public readonly GameObject Root;
        public readonly GameItem Host;
        public readonly Mod_InteractReciver Receiver;
        public readonly Mod_FireDrill Drill;
        public readonly Mod_FlintStrike Flint;
        public readonly Ex_ModData_MemoryPackable Data;
        public Module Tool => Drill != null ? (Module)Drill : Flint;
        public Inventory Input => Drill != null ? Drill.InputInventory : Flint.InputInventory;
        public Inventory Output => Drill != null ? Drill.OutputInventory : Flint.OutputInventory;
        public BasePanel Panel => Drill != null ? Drill.basePanel : Flint.basePanel;
        public ItemSlot_UI InputSlot => Drill != null ? Drill.InputSlotUI : Flint.InputSlotUI;
        public ItemSlot_UI OutputSlot => Drill != null ? Drill.OutputSlotUI : Flint.OutputSlotUI;
        public Button ActionButton => Drill != null ? Drill.FrictionButton : Flint.StrikeButton;
        public bool EventsBound => Host.OnAct.HasCalls && Receiver.OnAction_Start.HasCalls && Receiver.OnAction_Stop.HasCalls;
        public bool EventsUnbound => !Host.OnAct.HasCalls && !Receiver.OnAction_Start.HasCalls && !Receiver.OnAction_Stop.HasCalls;

        public Fixture(bool drill, int inputSlots, bool configure, Scene scene = default)
        {
            Root = new GameObject("[FireMakingToolProbe]") { hideFlags = HideFlags.HideAndDontSave };
            Root.SetActive(false);
            if (scene.IsValid()) SceneManager.MoveGameObjectToScene(Root, scene);
            try
            {
                Host = Root.AddComponent<GameItem>();
                Host.BindData(new Data_GeneralItem { IDName = "probe:fire_tool", Stack = new ItemStack { Amount = 1f } });
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
