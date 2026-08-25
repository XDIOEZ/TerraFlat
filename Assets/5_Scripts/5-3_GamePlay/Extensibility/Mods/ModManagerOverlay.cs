using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 无需场景预制体的 MOD 管理入口。F10 可查看、启停、调整软顺序并安全重载内容。
/// </summary>
public sealed class ModManagerOverlay : MonoBehaviour
{
    #region 静态入口

    private static ModManagerOverlay instance;

    public static ModManagerOverlay Ensure(GameObject host, ModRuntimeManager manager)
    {
        if (instance != null)
            return instance;

        ModManagerOverlay overlay = host.GetComponent<ModManagerOverlay>();
        if (overlay == null)
            overlay = host.AddComponent<ModManagerOverlay>();
        overlay.manager = manager;
        return overlay;
    }

    #endregion

    #region 状态

    private ModRuntimeManager manager;
    private readonly List<InstalledModInfo> installedMods = new();
    private readonly HashSet<string> expandedSettings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> settingEdits = new(StringComparer.OrdinalIgnoreCase);
    private Vector2 scrollPosition;
    private Rect windowRect = new(80f, 60f, 720f, 620f);
    private bool visible;
    private string notice = string.Empty;

    #endregion

    #region Unity 生命周期

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }
        instance = this;
    }

    private void Update()
    {
        if (Keyboard.current?.f10Key.wasPressedThisFrame != true)
            return;

        visible = !visible;
        if (visible)
            RefreshList();
    }

    private void OnGUI()
    {
        if (!visible)
            return;

        windowRect = GUILayout.Window(GetInstanceID(), windowRect, DrawWindow, "FlatWorld MOD 管理器（F10 关闭）");
    }

    #endregion

    #region 界面

    private void DrawWindow(int windowId)
    {
        GUILayout.Label("启停和顺序修改会在下次资源重载时生效；进入世界后禁止热重载。");
        GUILayout.Label($"状态：{manager.State} | 已加载：{manager.LoadedManifests.Count} | 指纹：{ShortHash(manager.ModSetHash)}");
        if (manager.IsSafeModeActive)
            GUILayout.Label("当前处于安全模式，所有外部 MOD 均被跳过。");
        if (!string.IsNullOrWhiteSpace(manager.FailureReason))
            GUILayout.Label($"最近错误：{manager.FailureReason}");
        if (!string.IsNullOrWhiteSpace(notice))
            GUILayout.Label(notice);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("刷新列表", GUILayout.Width(110f)))
            RefreshList();
        if (GUILayout.Button("复制 MOD 目录", GUILayout.Width(130f)))
        {
            GUIUtility.systemCopyBuffer = manager.ModsRootPath;
            notice = "已复制 MOD 目录路径。";
        }
        if (GUILayout.Button("下次安全模式", GUILayout.Width(130f)))
        {
            ModProfileStore.RequestSafeModeNextLaunch();
            notice = "已请求下次使用安全模式。";
        }
        bool canReload = GameManager.Instance == null || !GameManager.Instance.IsInGameWorld;
        GUI.enabled = canReload;
        if (GUILayout.Button("立即重载内容", GUILayout.Width(130f)))
        {
            GameRes.Instance?.HotReloadAllResources();
            notice = "已开始重载本体资源与 MOD。";
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));
        for (int i = 0; i < installedMods.Count; i++)
            DrawModRow(installedMods[i], i);
        if (installedMods.Count == 0)
            GUILayout.Label("未发现 MOD。把包含 manifest.json 的目录放入 Mods 后刷新。");
        GUILayout.EndScrollView();

        GUI.DragWindow(new Rect(0f, 0f, windowRect.width, 28f));
    }

    private void DrawModRow(InstalledModInfo info, int index)
    {
        GUILayout.BeginVertical(GUI.skin.box);
        GUILayout.BeginHorizontal();

        if (info.Valid)
        {
            bool enabled = GUILayout.Toggle(info.Enabled, string.Empty, GUILayout.Width(22f));
            if (enabled != info.Enabled)
            {
                ModProfileStore.SetEnabled(info.Id, enabled);
                info.Enabled = enabled;
                notice = $"{info.Id} 已{(enabled ? "启用" : "禁用")}，重载后生效。";
            }
        }
        else
        {
            GUILayout.Space(26f);
        }

        string title = info.Valid
            ? $"{info.Name ?? info.Id}  {info.Version}  [{info.Id}]"
            : $"无效包：{info.FolderName}";
        GUILayout.Label(title, GUILayout.ExpandWidth(true));
        GUILayout.Label(info.Loaded ? "已加载" : "未加载", GUILayout.Width(58f));

        IReadOnlyList<ModSettingDefinition> settings = info.Valid
            ? ModSettingsRegistry.GetDefinitions(info.Id)
            : Array.Empty<ModSettingDefinition>();
        if (settings.Count > 0 && GUILayout.Button("设置", GUILayout.Width(48f)))
        {
            if (!expandedSettings.Add(info.Id))
                expandedSettings.Remove(info.Id);
        }

        GUI.enabled = index > 0;
        if (GUILayout.Button("↑", GUILayout.Width(30f)))
            Move(index, index - 1);
        GUI.enabled = index < installedMods.Count - 1;
        if (GUILayout.Button("↓", GUILayout.Width(30f)))
            Move(index, index + 1);
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        if (!info.Valid)
        {
            GUILayout.Label(info.Error ?? "未知错误");
            if (GUILayout.Button("禁用此目录", GUILayout.Width(110f)))
            {
                File.WriteAllText(Path.Combine(info.FolderPath, ".disabled"), "disabled by FlatWorld MOD manager");
                notice = $"已禁用目录 {info.FolderName}。";
                RefreshList();
            }
        }
        else if (expandedSettings.Contains(info.Id))
        {
            foreach (ModSettingDefinition setting in settings)
                DrawSetting(info.Id, setting);
        }

        GUILayout.EndVertical();
    }

    private void DrawSetting(string modId, ModSettingDefinition setting)
    {
        string label = ModLocalizationRegistry.Translate(setting.LabelKey, setting.Id);
        string current = ModSettingsRegistry.GetDisplayValue(setting.Id);
        bool editable = string.Equals(setting.Scope, "client", StringComparison.OrdinalIgnoreCase);

        GUILayout.BeginHorizontal();
        GUILayout.Label($"{label} [{setting.Scope}]", GUILayout.Width(260f));
        GUI.enabled = editable;

        if (setting.Type == "bool")
        {
            bool value = string.Equals(current, "true", StringComparison.OrdinalIgnoreCase);
            bool next = GUILayout.Toggle(value, value ? "开启" : "关闭", GUILayout.Width(80f));
            if (next != value)
                ApplySetting(modId, setting, next ? "true" : "false");
        }
        else if (setting.Type == "enum")
        {
            int currentIndex = Math.Max(0, setting.Options.FindIndex(option => string.Equals(option, current, StringComparison.OrdinalIgnoreCase)));
            int nextIndex = GUILayout.SelectionGrid(currentIndex, setting.Options.ToArray(), Math.Max(1, setting.Options.Count), GUILayout.MinWidth(220f));
            if (nextIndex != currentIndex && nextIndex >= 0 && nextIndex < setting.Options.Count)
                ApplySetting(modId, setting, JsonConvert.SerializeObject(setting.Options[nextIndex]));
        }
        else
        {
            if (!settingEdits.TryGetValue(setting.Id, out string edit))
                settingEdits[setting.Id] = edit = current;
            settingEdits[setting.Id] = GUILayout.TextField(edit, GUILayout.MinWidth(180f));
            if (GUILayout.Button("应用", GUILayout.Width(52f)))
            {
                string json = setting.Type is "string" or "color" or "content"
                    ? JsonConvert.SerializeObject(settingEdits[setting.Id])
                    : settingEdits[setting.Id];
                ApplySetting(modId, setting, json);
            }
        }

        GUI.enabled = true;
        GUILayout.EndHorizontal();
        if (!string.IsNullOrWhiteSpace(setting.DescriptionKey))
            GUILayout.Label(ModLocalizationRegistry.Translate(setting.DescriptionKey, string.Empty));
    }

    private void ApplySetting(string modId, ModSettingDefinition setting, string json)
    {
        try
        {
            ModSettingsRegistry.SetClientValue(modId, setting.Id, json);
            settingEdits[setting.Id] = ModSettingsRegistry.GetDisplayValue(setting.Id);
            notice = setting.RestartRequired ? "设置已保存，需重载内容后生效。" : "设置已保存。";
        }
        catch (Exception ex)
        {
            notice = $"设置保存失败：{ex.Message}";
        }
    }

    #endregion

    #region 列表操作

    private void RefreshList()
    {
        installedMods.Clear();
        installedMods.AddRange(manager.DiscoverInstalledMods());
        ModProfile profile = ModProfileStore.LoadActiveProfile();
        installedMods.Sort((left, right) =>
        {
            int order = profile.GetSoftLoadOrder(left.Id).CompareTo(profile.GetSoftLoadOrder(right.Id));
            return order != 0 ? order : string.Compare(left.Id ?? left.FolderName, right.Id ?? right.FolderName, StringComparison.OrdinalIgnoreCase);
        });
    }

    private void Move(int from, int to)
    {
        if (from < 0 || from >= installedMods.Count || to < 0 || to >= installedMods.Count)
            return;

        InstalledModInfo item = installedMods[from];
        installedMods.RemoveAt(from);
        installedMods.Insert(to, item);
        ModProfileStore.SetLoadOrder(installedMods.Where(info => info.Valid).Select(info => info.Id));
        notice = "已保存软加载顺序；依赖关系仍具有更高优先级。";
    }

    private static string ShortHash(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value.Substring(0, Math.Min(12, value.Length));
    }

    #endregion
}
