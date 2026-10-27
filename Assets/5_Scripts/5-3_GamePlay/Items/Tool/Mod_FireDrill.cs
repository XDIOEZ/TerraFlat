using System;
using FlatWorld.Networking;
using UnityEngine;
using UnityEngine.UI;

public class Mod_FireDrill : FireMakingInventoryPanelModule
{
    public override ModuleTickMode TickMode => ModuleTickMode.FixedInterval;
    public override float FixedTickInterval => 0.1f;

#region 基础参数

    [Header("UI")]
    public Button FrictionButton;

    [Header("钻木参数")]
    [Range(100f, 200f)] public float HeatSourceTemperature = 150f; // 摩擦热源的最高温度。
    [Min(0.01f)] public float TemperaturePerClick = 10f; // 每次摩擦提供一份材料的升温量。

    public MaterialHeatingProcessor Heater { get; private set; }
    TMPro.TMP_Text _temperatureLabel;
    Player _currentInteractingPlayer;

#endregion

#region 生命周期

    private void OnValidate()
    {
        ModSaveData ??= new Ex_ModData_MemoryPackable();
        ModSaveData.ID = "钻木取火模块";
    }

    protected override void OnLoad()
    {
        ModSaveData ??= new Ex_ModData_MemoryPackable { ID = FireDrillLogic.ModuleId };
        EnsureFireMakingInventories(requireSingleInputSlot: false);
        if (ModSaveData.BitData?.Length > 0)
        {
            FireDrillRuntimeState saved = ModSaveData.GetData<FireDrillRuntimeState>();
            if (saved?.Input == null || saved.Output == null) throw new InvalidOperationException("取火器库存快照无效。");
            InputInventory.Data = saved.Input;
            OutputInventory.Data = saved.Output;
        }
        MachineInventory.Rebase(InputInventory.Data);
        MachineInventory.Rebase(OutputInventory.Data);
        InputInventory.InitData();
        OutputInventory.InitData();
        Heater = new MaterialHeatingProcessor(InputInventory, OutputInventory);
        SyncHeatingSettings();
        BindFireMakingInteractionEvents();
    }

    protected override void OnSave()
    {
        ModSaveData.WriteData(new FireDrillRuntimeState
        {
            Input = InputInventory.Data, Output = OutputInventory.Data
        });
    }

    protected override void OnUnload()
    {
        base.OnUnload();
        Heater = null;
    }

    public override void ModUpdate(float deltaTime)
    {
        if (!GameNetwork.HasStateAuthority || Heater == null) return;
        InputInventory?.ModUpdate(deltaTime);
        OutputInventory?.ModUpdate(deltaTime);
        SyncHeatingSettings();
        Heater.Tick(GetAmbientTemperature(), deltaTime);
        UpdateOutputPreviewProgress();
    }

#endregion

#region 钻木面板扩展

    protected override Button FireMakingActionButton { get => FrictionButton; set => FrictionButton = value; }

    protected override void OnFireMakingInteractionBeginning(Item playerItem)
    {
        _currentInteractingPlayer = ResolvePlayer(playerItem);
    }

    protected override void OnFireMakingInteractionEnded() => _currentInteractingPlayer = null;
    protected override void OnFireMakingPanelDestroyed() => _temperatureLabel = null;

    protected override void BindFireMakingBuildingActionsOnInteraction()
    {
        BuildingPanelActions buildingActions = basePanel.GetComponent<BuildingPanelActions>();
        if (buildingActions == null)
            throw new InvalidOperationException("[Mod_FireDrill] 钻木取火面板缺少 BuildingPanelActions，正式 Prefab 未完成建筑操作绑定。");
        buildingActions.Bind(item);
    }

    protected override bool TryHandleFireMakingPlacementAction()
    {
        return item.itemMods.GetMod_ByID<Mod_Building>(ModText.Building)?.TryHandlePlacementAction() == true;
    }

    protected override void ConfigureFireMakingPanel()
    {
        _temperatureLabel = basePanel.GetText("FWUI_FooterHint");
        var frictionText = FrictionButton.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
        if (frictionText != null)
        {
            var binder = frictionText.GetComponent<FlatWorld.Localization.LocalizedTextBinder>() ??
                         frictionText.gameObject.AddComponent<FlatWorld.Localization.LocalizedTextBinder>();
            binder.Configure(FlatWorld.Localization.FlatWorldLocalizationService.UiTable,
                FlatWorld.Localization.FlatWorldLocalizationService.GetUiTextKey("摩擦"), "摩擦");
        }
    }

#endregion

#region 钻木流程

    protected override void OnFireMakingActionButtonClick()
    {
        if (!GameNetwork.HasStateAuthority || Heater == null) return;
        SyncHeatingSettings();
        bool transformed = Heater.Rub(GetAmbientTemperature(), _currentInteractingPlayer);
        RefreshOutputPreview();
        Save();
        if (transformed) _outputPreview?.PlaySuccess();
    }

    private void SyncHeatingSettings()
    {
        Heater.SourceTemperature = HeatSourceTemperature;
        Heater.TemperaturePerClick = TemperaturePerClick;
    }

    private float GetAmbientTemperature()
    {
        Vector3 position = item.Owner != null ? item.Owner.transform.position : item.transform.position;
        TemperatureMgr.Instance.TryGetAmbientTemperature(position, out float temperature);
        return temperature;
    }

    private static Player ResolvePlayer(Item actorItem)
    {
        return actorItem as Player ?? actorItem?.GetComponentInParent<Player>();
    }

    protected override void RefreshOutputPreview()
    {
        if (_outputPreview == null || Heater == null)
            return;

        if (Heater.PreviewOutput() is ItemData preview)
            _outputPreview.Show(preview, Heater.Progress01);
        else
            _outputPreview.Clear();
        UpdateOutputPreviewProgress();
    }

    private void UpdateOutputPreviewProgress()
    {
        if (Heater == null) return;
        _outputPreview?.SetProgress(Heater.Progress01);
        if (_temperatureLabel != null) _temperatureLabel.text = Heater.Status;
        if (FrictionButton != null) FrictionButton.interactable = Heater.CanRub;
    }

#endregion

}
