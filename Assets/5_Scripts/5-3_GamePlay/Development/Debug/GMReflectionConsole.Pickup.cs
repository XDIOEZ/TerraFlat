using FlatWorld.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed partial class GMReflectionConsole
{
    #region 玩家拾取倍率工具

    private Button pickupMultiplierButton;
    private Slider pickupMultiplierSlider;
    private TextMeshProUGUI pickupMultiplierValueText;

    /// <summary>复用公共滑条和玩家页滚动布局，开关独立于所选倍率。</summary>
    private void CreatePickupMultiplierControl(Transform parent)
    {
        GameObject row = CreateUiObject("Pickup Multiplier", parent);
        row.AddComponent<Image>().color = GmSurface;
        StyleGmOutline(row.AddComponent<Outline>());
        row.AddComponent<LayoutElement>().preferredHeight = 60f;
        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 0, 0);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        pickupMultiplierButton = CreateButton(row.transform, "拾取倍增：关", TogglePickupMultiplier, 170f, 40f);
        GameObject sliderObject = Instantiate(
            GameRes.ExistingInstance.GetPrefab(RuntimeUIPrefabKeys.SliderControl, false), row.transform, false);
        sliderObject.name = "Pickup Multiplier Slider";
        LayoutElement sliderLayout = sliderObject.GetComponent<LayoutElement>();
        sliderLayout.minWidth = 80f;
        sliderLayout.preferredWidth = -1f;
        sliderLayout.flexibleWidth = 1f;
        pickupMultiplierSlider = sliderObject.GetComponent<Slider>();
        pickupMultiplierSlider.minValue = Mod_ItemPicker.MinPickupAmountMultiplier;
        pickupMultiplierSlider.maxValue = Mod_ItemPicker.MaxPickupAmountMultiplier;
        pickupMultiplierSlider.wholeNumbers = true;
        pickupMultiplierSlider.direction = Slider.Direction.LeftToRight;
        pickupMultiplierSlider.SetValueWithoutNotify(GMConsolePreferences.PickupAmountMultiplier);
        pickupMultiplierSlider.onValueChanged.AddListener(SetPickupMultiplierFromSlider);

        pickupMultiplierValueText = CreateText(row.transform, "10 倍", 15f, GmTextPrimary);
        pickupMultiplierValueText.alignment = TextAlignmentOptions.Center;
        pickupMultiplierValueText.enableWordWrapping = false;
        pickupMultiplierValueText.raycastTarget = false;
        pickupMultiplierValueText.gameObject.AddComponent<LayoutElement>().preferredWidth = 76f;

        EventTrigger trigger = sliderObject.GetComponent<EventTrigger>() ?? sliderObject.AddComponent<EventTrigger>();
        foreach (EventTriggerType eventType in new[] { EventTriggerType.PointerUp, EventTriggerType.Deselect })
        {
            EventTrigger.Entry entry = new() { eventID = eventType };
            entry.callback.AddListener(_ => GMConsolePreferences.SavePendingChanges());
            trigger.triggers.Add(entry);
        }
        RegisterSearchEntry(GmPageId.Player, "拾取倍率", "拾取 捡 材料 食物 数量 倍率 倍增 pickup multiplier",
            (RectTransform)row.transform);
        AddPageHint(parent, "只放大本机玩家拾取的可堆叠物品，1～100 倍；容量照旧，整份放不下时留在地上。联机仅房主可用。", 42f);
        RefreshPickupMultiplierControl();
    }

    private void TogglePickupMultiplier()
    {
        if (!GameNetwork.HasStateAuthority || FindPreferenceItemPicker() == null)
            return;
        bool enabled = !GMConsolePreferences.PickupMultiplierEnabled;
        GMConsolePreferences.SetPickupMultiplier(enabled, GMConsolePreferences.PickupAmountMultiplier);
        GMConsolePreferences.SavePendingChanges();
        RefreshPickupMultiplierControl();
        SetStatus(enabled
            ? $"拾取倍增已开启：可堆叠物品 ×{GMConsolePreferences.PickupAmountMultiplier}。"
            : "拾取倍增已关闭，恢复正常拾取数量。", GmTextSecondary);
    }

    private void SetPickupMultiplierFromSlider(float value)
    {
        if (!GameNetwork.HasStateAuthority || FindPreferenceItemPicker() == null)
            return;
        GMConsolePreferences.SetPickupMultiplier(GMConsolePreferences.PickupMultiplierEnabled, Mathf.RoundToInt(value));
        RefreshPickupMultiplierControl();
    }

    /// <summary>显示与正式拾取器同步，普通客户端保持原倍率，避免两端库存收益不一致。</summary>
    private void RefreshPickupMultiplierControl()
    {
        if (pickupMultiplierSlider == null || pickupMultiplierButton == null)
            return;
        Mod_ItemPicker picker = FindPreferenceItemPicker();
        if (picker != null)
            ApplyPickupMultiplierPreference(picker);
        bool canEdit = picker != null && GameNetwork.HasStateAuthority;
        bool enabled = canEdit && GMConsolePreferences.PickupMultiplierEnabled;
        pickupMultiplierButton.GetComponentInChildren<TextMeshProUGUI>().text = enabled ? "拾取倍增：开" : "拾取倍增：关";
        pickupMultiplierButton.interactable = canEdit;
        SetGmButtonVisual(pickupMultiplierButton, enabled ? GmSelection : GmSurfaceRaised, enabled);
        pickupMultiplierSlider.interactable = canEdit;
        pickupMultiplierSlider.SetValueWithoutNotify(GMConsolePreferences.PickupAmountMultiplier);
        pickupMultiplierValueText.text = $"{GMConsolePreferences.PickupAmountMultiplier} 倍";
    }

    private static void ApplyPickupMultiplierPreference(Mod_ItemPicker picker)
        => picker.PickupAmountMultiplier = GMConsolePreferences.PickupMultiplierEnabled && GameNetwork.HasStateAuthority
            ? GMConsolePreferences.PickupAmountMultiplier : 1;

    /// <summary>本地角色原地重建时也恢复倍率，不依赖切换场景或重新打开 GM。</summary>
    private static void HandlePickupPickerLoaded(Mod_ItemPicker picker)
    {
        if (picker.item is Player player && player.IsLocalProfile)
            ApplyPickupMultiplierPreference(picker);
    }

    private static Mod_ItemPicker FindPreferenceItemPicker()
    {
        ItemMgr manager = ItemMgr.GetInstance();
        Player player = FindPreferencePlayer(manager != null ? manager.Player_DIC : null);
        return player != null ? player.itemMods?.GetMod_ByID<Mod_ItemPicker>(ModText.Picker) : null;
    }

    #endregion
}
