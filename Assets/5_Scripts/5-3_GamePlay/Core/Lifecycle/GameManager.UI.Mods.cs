// AI-Context: 主菜单 MOD 页绑定正式 Prefab，配置写入档案并由现有资源加载链接入。

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FlatWorld.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class GameManager
{
    #region MOD 管理面板

    private const int ModRowsPerPage = 6;
    private const int MaximumModPreviewBytes = 8 * 1024 * 1024;
    private const int MaximumModPreviewDimension = 4096;
    private const string ModPanelKey = RuntimeUIPrefabKeys.ModManager;

    private sealed class ModMenuState
    {
        public BasePanel Panel;
        public readonly List<InstalledModInfo> Mods = new();
        public int Page;
        public string SelectedFolder;
        public string DraggedFolder;
        public ModMenuDragController DragController;
        public string Notice = string.Empty;
        public string PreviewPath;
        public Texture2D PreviewTexture;
        public Sprite PreviewSprite;
    }

    private ModMenuState modMenuState;

    /// <summary>从开始界面打开可用的 MOD 管理页面。</summary>
    public void OpenMainMenuMods()
    {
        if (UIManager.Instance == null || GameRes.Instance == null)
        {
            Debug.LogError("[GameManager] MOD 管理页无法打开：UI 或资源管理器未就绪。", this);
            return;
        }

        if (UIManager.Instance.TryGetPanel(ModPanelKey, out BasePanel existing))
        {
            modMenuState ??= new ModMenuState { Panel = existing };
            modMenuState.Panel = existing;
            RefreshModMenu(modMenuState);
            existing.Open();
            return;
        }

        GameObject prefab = GameRes.Instance.GetPrefab(ModPanelKey, false);
        if (prefab == null)
        {
            Debug.LogError("[GameManager] 缺少 UI_ModManager Prefab，请检查启动资源和 Addressables。", this);
            return;
        }

        BasePanel panel = UIManager.Instance.CreatePanelFromGameObject(prefab, ModPanelKey);
        if (panel == null)
        {
            Debug.LogError("[GameManager] UI_ModManager 未获得 BasePanel。", this);
            return;
        }

        var state = new ModMenuState { Panel = panel };
        modMenuState = state;
        panel.SetButtonOnClick("刷新", () =>
        {
            state.Notice = string.Empty;
            RefreshModMenu(state);
        });
        panel.SetButtonOnClick("打开目录", () => OpenModsFolder(state));
        panel.SetButtonOnClick("启用切换", () => ToggleSelectedMod(state));
        panel.SetButtonOnClick("应用重载", () => ReloadMainMenuMods(state));
        panel.SetButtonOnClick("上一页", () => { state.Page--; RenderModMenu(state); });
        panel.SetButtonOnClick("下一页", () => { state.Page++; RenderModMenu(state); });
        for (int slot = 0; slot < ModRowsPerPage; slot++)
        {
            int selectedSlot = slot;
            panel.SetButtonOnClick($"MOD条目_{slot + 1}", () =>
            {
                int index = state.Page * ModRowsPerPage + selectedSlot;
                if (index < 0 || index >= state.Mods.Count) return;
                state.SelectedFolder = state.Mods[index].FolderPath;
                RenderModMenu(state);
            });
        }

        state.DragController = panel.GetComponentInChildren<ModMenuDragController>(true);
        if (state.DragController != null)
        {
            state.DragController.Bind(
                slot => ModAtSlot(state, slot)?.Valid == true,
                slot =>
                {
                    state.DraggedFolder = ModAtSlot(state, slot)?.FolderPath;
                    state.SelectedFolder = state.DraggedFolder;
                    RenderModMenu(state);
                },
                slot => MoveDraggedMod(state, slot),
                direction => { state.Page += direction; RenderModMenu(state); },
                () => state.DraggedFolder = null);
            panel.Closed += state.DragController.CancelDrag;
        }
        panel.Closed += () => ReleaseModPreview(state);

        RefreshModMenu(state);
        panel.PrepareForGamepadNavigation(state.Mods.Count > 0 ? "MOD条目_1" : "刷新");
        panel.Open();
    }

    private static ModRuntimeManager GetModManager()
        => ModRuntimeManager.Instance ?? ModRuntimeManager.Ensure(GameRes.Instance.gameObject);

    private static void OpenModsFolder(ModMenuState state)
    {
        try
        {
            string path = GetModManager().ModsRootPath;
            Directory.CreateDirectory(path);
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            // 只打开游戏生成的 MOD 根目录，不接受 MOD 清单传入的任意路径。
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
            {
                UseShellExecute = true
            });
            state.Notice = "已打开 MOD 安装目录。";
#else
            GUIUtility.systemCopyBuffer = path;
            state.Notice = "当前平台已复制 MOD 安装目录，请在文件管理器中粘贴。";
#endif
        }
        catch (Exception error)
        {
            state.Notice = "打开 MOD 安装目录失败：" + error.Message;
            Debug.LogWarning("[GameManager] " + state.Notice);
        }
        RenderModMenu(state);
    }

    private static void RefreshModMenu(ModMenuState state)
    {
        state.DragController?.CancelDrag();
        ReleaseModPreview(state);
        try
        {
            state.Mods.Clear();
            state.Mods.AddRange(GetModManager().DiscoverInstalledMods());
            ModProfile profile = ModProfileStore.LoadActiveProfile();
            state.Mods.Sort((left, right) =>
            {
                int order = profile.GetSoftLoadOrder(left.Id).CompareTo(profile.GetSoftLoadOrder(right.Id));
                return order != 0 ? order : string.Compare(left.FolderName, right.FolderName, StringComparison.OrdinalIgnoreCase);
            });
            if (state.Mods.Count > 0 && !state.Mods.Any(value => value.FolderPath == state.SelectedFolder))
            {
                state.SelectedFolder = state.Mods[0].FolderPath;
                state.Page = 0;
            }
            if (state.Mods.Count == 0)
                state.SelectedFolder = null;
        }
        catch (Exception error)
        {
            state.Notice = "读取 MOD 列表失败：" + error.Message;
        }
        RenderModMenu(state);
    }

    private static InstalledModInfo SelectedMod(ModMenuState state)
        => state.Mods.FirstOrDefault(value => value.FolderPath == state.SelectedFolder);

    private static InstalledModInfo ModAtSlot(ModMenuState state, int slot)
    {
        int index = state.Page * ModRowsPerPage + slot;
        return index >= 0 && index < state.Mods.Count ? state.Mods[index] : null;
    }

    private static void RenderModMenu(ModMenuState state)
    {
        if (state.Panel == null) return;
        state.Page = Mathf.Clamp(state.Page, 0, Math.Max(0, (state.Mods.Count - 1) / ModRowsPerPage));
        ModRuntimeManager manager = GetModManager();
        bool managedRestartRequired = manager.HasManagedMods ||
            state.Mods.Any(value => value.Valid && value.Enabled && value.HasManagedCode);
        SetModText(state.Panel, "加载状态", manager.IsSafeModeActive
            ? "安全模式：本次启动已跳过全部 MOD"
            : $"当前加载 {manager.LoadedManifests.Count} 个 MOD · {manager.State}");
        SetModText(state.Panel, "目录路径", manager.ModsRootPath);
        SetModText(state.Panel, "页码", $"{state.Page + 1} / {Math.Max(1, (state.Mods.Count + ModRowsPerPage - 1) / ModRowsPerPage)}");
        SetModText(state.Panel, "操作提示", string.IsNullOrWhiteSpace(state.Notice)
            ? managedRestartRequired
                ? "当前启用了 C# MOD：配置保存后请重启游戏，不能在进程内热替换补丁。"
                : "启停与顺序保存到本机配置；加载中的修改需等本次完成后重载。"
            : state.Notice);

        for (int slot = 0; slot < ModRowsPerPage; slot++)
        {
            int index = state.Page * ModRowsPerPage + slot;
            bool visible = index < state.Mods.Count;
            string key = $"MOD条目_{slot + 1}";
            Button row = state.Panel.GetButton(key);
            if (row != null) row.gameObject.SetActive(visible);
            if (!visible) continue;
            InstalledModInfo info = state.Mods[index];
            SetModText(state.Panel, key + "_序号", (index + 1).ToString("00"));
            SetModText(state.Panel, key + "_标题", info.Valid ? (info.Name ?? info.Id) : info.FolderName);
            SetModText(state.Panel, key + "_说明", !info.Valid ? "包有错误" :
                info.Loaded ? "已加载 · " + info.Version :
                info.Enabled ? "待加载 · " + info.Version : "已禁用 · " + info.Version);
            Image background = row != null ? row.GetComponent<Image>() : null;
            if (background != null)
                background.color = info.FolderPath == state.SelectedFolder
                    ? new Color32(92, 85, 64, 255) : new Color32(67, 67, 67, 255);
        }

        InstalledModInfo selected = SelectedMod(state);
        RefreshModPreview(state, selected);
        string details = selected == null ? "没有找到 MOD。点击“打开 MOD 文件夹”，放入带 manifest.json 的 MOD 文件夹，然后点刷新。" :
            !selected.Valid ? $"{selected.FolderName}\n无法加载：{selected.Error}\n可用右侧的禁用按钮跳过这个目录。" :
            $"{selected.Name ?? selected.Id}  v{selected.Version}\n" +
            $"ID：{selected.Id}\n" +
            $"作者：{(string.IsNullOrWhiteSpace(selected.Author) ? "未填写" : selected.Author)}\n" +
            $"简介：{(string.IsNullOrWhiteSpace(selected.Description) ? "无说明" : selected.Description)}\n" +
            $"依赖：{(selected.Dependencies.Count == 0 ? "无" : string.Join("、", selected.Dependencies))}\n" +
            $"内容类型：{(selected.HasManagedCode ? "C# MOD" : "资源 / JSON / Lua")}";
        SetModText(state.Panel, "详情内容", details);
        SetModText(state.Panel, "代码说明", selected?.HasManagedCode == true
            ? "C# MOD 的启停、顺序或代码修改需重启游戏后生效。"
            : string.Empty);
        SetModText(state.Panel, "启用切换_标题", selected == null ? "启用 / 禁用" :
            !selected.Valid ? "禁用目录" : selected.Enabled ? "禁用 MOD" : "启用 MOD");
        SetModButton(state.Panel, "启用切换", selected != null);
        SetModButton(state.Panel, "上一页", state.Page > 0);
        SetModButton(state.Panel, "下一页", (state.Page + 1) * ModRowsPerPage < state.Mods.Count);
        bool resourcesCanReload = GameRes.Instance != null &&
            GameRes.Instance.LoadState != ResourceLoadState.Loading &&
            GameRes.Instance.LoadState != ResourceLoadState.Disposed;
        SetModButton(state.Panel, "应用重载", resourcesCanReload && !managedRestartRequired);
        state.Panel.RefreshGamepadNavigationState();
    }

    /// <summary>从 InstalledModInfo 的已校验路径读取展示图，并只保留当前选中项的一张运行时预览。</summary>
    private static void RefreshModPreview(ModMenuState state, InstalledModInfo selected)
    {
        Image image = state.Panel?.GetImage("MOD展示图");
        if (image == null) return;

        string path = selected?.Valid == true ? selected.PreviewImagePath : null;
        if (string.Equals(path, state.PreviewPath, StringComparison.OrdinalIgnoreCase) &&
            state.PreviewSprite != null)
        {
            image.sprite = state.PreviewSprite;
            image.color = Color.white;
            SetModPreviewPlaceholder(state, false, string.Empty);
            return;
        }

        ReleaseModPreview(state);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        Texture2D texture = null;
        Sprite sprite = null;
        try
        {
            string extension = Path.GetExtension(path);
            bool supported = extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                             extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                             extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
            if (!supported)
                throw new InvalidDataException("展示图只支持 PNG / JPG / JPEG。");

            FileInfo file = new(path);
            if (file.Length <= 0 || file.Length > MaximumModPreviewBytes)
                throw new InvalidDataException($"展示图大小必须在 1 字节到 {MaximumModPreviewBytes / (1024 * 1024)} MB 之间。");

            byte[] bytes = File.ReadAllBytes(path);
            if (!TryReadModPreviewSize(extension, bytes, out int width, out int height))
                throw new InvalidDataException("无法读取展示图尺寸或图片格式损坏。");
            if (width > MaximumModPreviewDimension || height > MaximumModPreviewDimension)
                throw new InvalidDataException($"展示图最大尺寸为 {MaximumModPreviewDimension}×{MaximumModPreviewDimension}。");

            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = $"MOD Preview - {selected.Id}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            if (!ImageConversion.LoadImage(texture, bytes, true))
            {
                UnityEngine.Object.Destroy(texture);
                texture = null;
                throw new InvalidDataException("展示图解码失败。");
            }

            sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.DontSave;

            state.PreviewPath = path;
            state.PreviewTexture = texture;
            state.PreviewSprite = sprite;
            image.sprite = sprite;
            image.color = Color.white;
            image.preserveAspect = true;
            SetModPreviewPlaceholder(state, false, string.Empty);

            // 所有权转交给 state，后续统一由 ReleaseModPreview 释放。
            texture = null;
            sprite = null;
        }
        catch (Exception error)
        {
            if (sprite != null)
                UnityEngine.Object.Destroy(sprite);
            if (texture != null)
                UnityEngine.Object.Destroy(texture);
            Debug.LogWarning($"[GameManager] 无法读取 MOD 展示图 {path}：{error.Message}");
            SetModPreviewPlaceholder(state, true, "展示图无法读取");
        }
    }

    private static void ReleaseModPreview(ModMenuState state)
    {
        if (state == null) return;

        if (state.Panel != null)
        {
            Image image = state.Panel.GetImage("MOD展示图");
            if (image != null)
            {
                image.sprite = null;
                image.color = Color.clear;
            }
        }

        if (state.PreviewSprite != null)
            UnityEngine.Object.Destroy(state.PreviewSprite);
        if (state.PreviewTexture != null)
            UnityEngine.Object.Destroy(state.PreviewTexture);
        state.PreviewSprite = null;
        state.PreviewTexture = null;
        state.PreviewPath = null;
        SetModPreviewPlaceholder(state, true, "暂无展示图");
    }

    private static void SetModPreviewPlaceholder(ModMenuState state, bool visible, string value)
    {
        TextMeshProUGUI placeholder = state?.Panel?.GetText("MOD展示图占位");
        if (placeholder == null) return;
        placeholder.text = FlatWorldLocalizationService.GetUiText(value ?? string.Empty);
        placeholder.gameObject.SetActive(visible);
    }

    private static bool TryReadModPreviewSize(string extension, byte[] bytes, out int width, out int height)
    {
        if (extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
            return TryReadPngSize(bytes, out width, out height);
        return TryReadJpegSize(bytes, out width, out height);
    }

    private static bool TryReadPngSize(byte[] bytes, out int width, out int height)
    {
        width = height = 0;
        if (bytes == null || bytes.Length < 24 ||
            bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47 ||
            bytes[4] != 0x0D || bytes[5] != 0x0A || bytes[6] != 0x1A || bytes[7] != 0x0A)
            return false;

        width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
        return width > 0 && height > 0;
    }

    private static bool TryReadJpegSize(byte[] bytes, out int width, out int height)
    {
        width = height = 0;
        if (bytes == null || bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
            return false;

        int offset = 2;
        while (offset + 3 < bytes.Length)
        {
            while (offset < bytes.Length && bytes[offset] != 0xFF) offset++;
            while (offset < bytes.Length && bytes[offset] == 0xFF) offset++;
            if (offset >= bytes.Length) return false;

            byte marker = bytes[offset++];
            if (marker == 0xD9 || marker == 0xDA) return false;
            if (marker == 0x01 || marker is >= 0xD0 and <= 0xD7)
                continue;
            if (offset + 1 >= bytes.Length) return false;

            int segmentLength = (bytes[offset] << 8) | bytes[offset + 1];
            if (segmentLength < 2 || offset + segmentLength > bytes.Length)
                return false;

            bool isStartOfFrame = marker is 0xC0 or 0xC1 or 0xC2 or 0xC3 or
                0xC5 or 0xC6 or 0xC7 or 0xC9 or 0xCA or 0xCB or 0xCD or 0xCE or 0xCF;
            if (isStartOfFrame)
            {
                if (segmentLength < 7) return false;
                height = (bytes[offset + 3] << 8) | bytes[offset + 4];
                width = (bytes[offset + 5] << 8) | bytes[offset + 6];
                return width > 0 && height > 0;
            }

            offset += segmentLength;
        }
        return false;
    }

    private static void SetModText(BasePanel panel, string key, string value)
    {
        TextMeshProUGUI text = panel.GetText(key);
        if (text != null) text.text = FlatWorldLocalizationService.GetUiText(value ?? string.Empty);
    }

    private static void SetModButton(BasePanel panel, string key, bool enabled)
    {
        Button button = panel.GetButton(key);
        if (button != null) button.interactable = enabled;
    }

    private static void ToggleSelectedMod(ModMenuState state)
    {
        InstalledModInfo info = SelectedMod(state);
        if (info == null) return;
        try
        {
            if (!info.Valid)
            {
                GetModManager().SetPackageFolderDisabled(info.FolderPath, true);
                state.Notice = "已禁用损坏的 MOD 目录；重新打开游戏即可正常扫描。";
            }
            else
            {
                bool enable = !info.Enabled;
                if (enable && info.DisabledByFile)
                    GetModManager().SetPackageFolderDisabled(info.FolderPath, false);
                ModProfileStore.SetEnabled(info.Id, enable);
                state.Notice = $"已{(enable ? "启用" : "禁用")} {info.Id}。" +
                    (info.HasManagedCode ? "C# MOD 需重启游戏后生效。" : "可在加载完成后点“应用并重载”。");
            }
            RefreshModMenu(state);
        }
        catch (Exception error)
        {
            state.Notice = "保存 MOD 状态失败：" + error.Message;
            RenderModMenu(state);
        }
    }

    private static void MoveDraggedMod(ModMenuState state, int insertionSlot)
    {
        string folder = state.DraggedFolder;
        state.DraggedFolder = null;
        int from = state.Mods.FindIndex(value => value.FolderPath == folder);
        if (from < 0 || !state.Mods[from].Valid) return;
        int to = Mathf.Clamp(state.Page * ModRowsPerPage + insertionSlot, 0, state.Mods.Count);
        // 插入位置按原列表计算，取出条目后修正向下拖动的偏移。
        if (from < to) to--;
        if (from == to) return;
        try
        {
            var ordered = new List<InstalledModInfo>(state.Mods);
            InstalledModInfo moved = ordered[from];
            ordered.RemoveAt(from);
            ordered.Insert(to, moved);
            ModProfileStore.SetLoadOrder(ordered.Where(value => value.Valid).Select(value => value.Id));
            state.Mods.Clear();
            state.Mods.AddRange(ordered);
            state.SelectedFolder = moved.FolderPath;
            state.Page = to / ModRowsPerPage;
            state.Notice = "顺序已保存；依赖要求始终优先于手动顺序。";
            RenderModMenu(state);
        }
        catch (Exception error)
        {
            state.Notice = "保存顺序失败：" + error.Message;
            RefreshModMenu(state);
        }
    }

    private static void ReloadMainMenuMods(ModMenuState state)
    {
        ModRuntimeManager manager = GetModManager();
        if (manager.HasManagedMods || state.Mods.Any(value => value.Valid && value.Enabled && value.HasManagedCode))
        {
            state.Notice = "已启用 C# MOD，请重启游戏应用；运行中的补丁不能安全热替换。";
            RenderModMenu(state);
            return;
        }
        state.Panel.Close();
        if (!GameRes.Instance.TryReloadResources())
        {
            state.Notice = "现在无法重载资源，请等加载结束后重试。";
            state.Panel.Open();
            RenderModMenu(state);
        }
    }

    #endregion
}
