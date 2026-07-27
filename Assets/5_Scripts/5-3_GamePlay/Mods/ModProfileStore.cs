using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

#region MOD 配置档案

[Serializable]
public sealed class ModProfile
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion = 1;

    [JsonProperty("id")]
    public string Id = "default";

    [JsonProperty("autoEnableNewMods")]
    public bool AutoEnableNewMods = true;

    [JsonProperty("enabledMods")]
    public List<string> EnabledMods = new();

    [JsonProperty("disabledMods")]
    public List<string> DisabledMods = new();

    [JsonProperty("loadOrder")]
    public List<string> LoadOrder = new();

    public bool IsEnabled(string modId)
    {
        if (DisabledMods.Any(id => string.Equals(id, modId, StringComparison.OrdinalIgnoreCase)))
            return false;
        if (EnabledMods.Any(id => string.Equals(id, modId, StringComparison.OrdinalIgnoreCase)))
            return true;
        return AutoEnableNewMods;
    }

    public int GetSoftLoadOrder(string modId)
    {
        for (int i = 0; i < LoadOrder.Count; i++)
        {
            if (string.Equals(LoadOrder[i], modId, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return int.MaxValue;
    }
}

/// <summary>
/// MOD 启停和软加载顺序的持久化入口。配置保存在包目录之外，不会污染内容哈希。
/// </summary>
public static class ModProfileStore
{
    #region 路径

    private const string ActiveProfileFileName = "active-profile.json";
    private const string SafeModeRequestFileName = "safe-mode.next";
    private const string LastFailureFileName = "last-load-failure.txt";

    public static string ProfilesRootPath => Path.Combine(Application.persistentDataPath, "ModProfiles");
    public static string ActiveProfilePath => Path.Combine(ProfilesRootPath, ActiveProfileFileName);

    #endregion

    #region 读取与保存

    public static ModProfile LoadActiveProfile()
    {
        Directory.CreateDirectory(ProfilesRootPath);
        if (!File.Exists(ActiveProfilePath))
            return new ModProfile();

        string json = File.ReadAllText(ActiveProfilePath);
        ModProfile profile = JsonConvert.DeserializeObject<ModProfile>(json) ?? new ModProfile();
        Normalize(profile);
        return profile;
    }

    public static void SaveActiveProfile(ModProfile profile)
    {
        if (profile == null)
            throw new ArgumentNullException(nameof(profile));

        Directory.CreateDirectory(ProfilesRootPath);
        Normalize(profile);
        WriteAtomic(ActiveProfilePath, JsonConvert.SerializeObject(profile, Formatting.Indented));
    }

    public static void SetEnabled(string modId, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(modId))
            throw new ArgumentException("MOD ID 不能为空", nameof(modId));

        ModProfile profile = LoadActiveProfile();
        RemoveIgnoreCase(profile.EnabledMods, modId);
        RemoveIgnoreCase(profile.DisabledMods, modId);
        (enabled ? profile.EnabledMods : profile.DisabledMods).Add(modId);
        SaveActiveProfile(profile);
    }

    public static void SetLoadOrder(IEnumerable<string> orderedModIds)
    {
        ModProfile profile = LoadActiveProfile();
        profile.LoadOrder = orderedModIds?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();
        SaveActiveProfile(profile);
    }

    #endregion

    #region 安全模式

    public static bool ConsumeSafeModeRequest()
    {
        Directory.CreateDirectory(ProfilesRootPath);
        string path = Path.Combine(ProfilesRootPath, SafeModeRequestFileName);
        if (!File.Exists(path))
            return false;

        File.Delete(path);
        return true;
    }

    public static void RequestSafeModeNextLaunch()
    {
        Directory.CreateDirectory(ProfilesRootPath);
        WriteAtomic(Path.Combine(ProfilesRootPath, SafeModeRequestFileName), DateTime.UtcNow.ToString("O"));
    }

    public static void RecordLoadFailure(string error)
    {
        Directory.CreateDirectory(ProfilesRootPath);
        WriteAtomic(Path.Combine(ProfilesRootPath, LastFailureFileName), error ?? "未知 MOD 加载错误");
        RequestSafeModeNextLaunch();
    }

    #endregion

    #region 内部工具

    private static void Normalize(ModProfile profile)
    {
        profile.SchemaVersion = 1;
        profile.Id = string.IsNullOrWhiteSpace(profile.Id) ? "default" : profile.Id.Trim();
        profile.EnabledMods = NormalizeIds(profile.EnabledMods);
        profile.DisabledMods = NormalizeIds(profile.DisabledMods);
        profile.LoadOrder = NormalizeIds(profile.LoadOrder);
    }

    private static List<string> NormalizeIds(IEnumerable<string> ids)
    {
        return ids?
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();
    }

    private static void RemoveIgnoreCase(List<string> values, string value)
    {
        values.RemoveAll(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));
    }

    private static void WriteAtomic(string path, string contents)
    {
        string temporaryPath = path + ".tmp";
        File.WriteAllText(temporaryPath, contents);
        if (File.Exists(path))
            File.Delete(path);
        File.Move(temporaryPath, path);
    }

    #endregion
}

#endregion
