#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace FlatWorld.EditorTools
{
	[InitializeOnLoad]
	public static class AutoAssetRefresher
	{
		#region Prefs
		private const string PrefEnabled = "FW_AutoAssetRefresher_Enabled";
		private const string PrefForceUnityAutoRefresh = "FW_AutoAssetRefresher_ForceUnityAutoRefresh";
		#endregion

		#region Watchers
		private static FileSystemWatcher _watchCs;
		private static FileSystemWatcher _watchAsmdef;
		private static FileSystemWatcher _watchAsmref;
		private static bool _pending;
		private static double _refreshAt;
		private static double _cooldownUntil;
		private static string _lastPath;
		#endregion

		static AutoAssetRefresher()
		{
			AssemblyReloadEvents.beforeAssemblyReload -= DisposeWatchers;
			AssemblyReloadEvents.beforeAssemblyReload += DisposeWatchers;

			EditorApplication.quitting -= DisposeWatchers;
			EditorApplication.quitting += DisposeWatchers;

			EditorApplication.update -= OnEditorUpdate;
			EditorApplication.update += OnEditorUpdate;

			if (IsEnabled)
			{
				TryApplyUnityAutoRefreshSettings();
				TryStartWatchers();
			}
		}

		#region Menu
		private const string MenuRoot = "Tools/FlatWorld/自动刷新资产(后台)/";

		[MenuItem(MenuRoot + "启用", false, 1)]
		private static void Enable()
		{
			EditorPrefs.SetBool(PrefEnabled, true);
			TryApplyUnityAutoRefreshSettings();
			TryStartWatchers();
			Debug.Log("[AutoAssetRefresher] 已启用（后台监听 Assets 变更）");
		}

		[MenuItem(MenuRoot + "禁用", false, 2)]
		private static void Disable()
		{
			EditorPrefs.SetBool(PrefEnabled, false);
			DisposeWatchers();
			Debug.Log("[AutoAssetRefresher] 已禁用");
		}

		[MenuItem(MenuRoot + "立刻Refresh一次", false, 50)]
		private static void RefreshNow()
		{
			RequestRefresh("Manual");
		}

		[MenuItem(MenuRoot + "打印状态", false, 51)]
		private static void PrintStatus()
		{
			var sb = new System.Text.StringBuilder();
			sb.AppendLine("[AutoAssetRefresher] Status");
			sb.AppendLine($"  Enabled: {IsEnabled}");
			sb.AppendLine($"  ForceUnityAutoRefresh: {ForceUnityAutoRefresh}");
			sb.AppendLine($"  Application.runInBackground: {Application.runInBackground}");

			try
			{
				var dirProp = typeof(EditorSettings).GetProperty("directoryMonitoring", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				if (dirProp != null && dirProp.PropertyType == typeof(bool))
					sb.AppendLine($"  EditorSettings.directoryMonitoring: {dirProp.GetValue(null)}");
				else
					sb.AppendLine("  EditorSettings.directoryMonitoring: <not available>");

				var prop = typeof(EditorSettings).GetProperty("refreshImportMode", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				if (prop != null)
					sb.AppendLine($"  EditorSettings.refreshImportMode: {prop.GetValue(null)}");
				else
					sb.AppendLine("  EditorSettings.refreshImportMode: <not available>");
			}
			catch (Exception ex)
			{
				sb.AppendLine($"  Reflection error: {ex.Message}");
			}

			Debug.Log(sb.ToString());
		}

		[MenuItem(MenuRoot + "应用Unity自动刷新设置(推荐)", false, 10)]
		private static void ApplyUnitySettingsMenu()
		{
			EditorPrefs.SetBool(PrefForceUnityAutoRefresh, true);
			TryApplyUnityAutoRefreshSettings(forceLog: true);
		}

		[MenuItem(MenuRoot + "强制Unity自动刷新(开/关)", false, 11)]
		private static void ToggleForceUnityAutoRefresh()
		{
			var next = !ForceUnityAutoRefresh;
			EditorPrefs.SetBool(PrefForceUnityAutoRefresh, next);
			if (next)
				TryApplyUnityAutoRefreshSettings(forceLog: true);
			Debug.Log($"[AutoAssetRefresher] 强制Unity自动刷新：{(next ? "开启" : "关闭")}");
		}

		[MenuItem(MenuRoot + "强制Unity自动刷新(开/关)", true)]
		private static bool ToggleForceUnityAutoRefreshValidate()
		{
			Menu.SetChecked(MenuRoot + "强制Unity自动刷新(开/关)", ForceUnityAutoRefresh);
			return true;
		}

		[MenuItem(MenuRoot + "启用", true)]
		private static bool EnableValidate()
		{
			Menu.SetChecked(MenuRoot + "启用", IsEnabled);
			return !IsEnabled;
		}

		[MenuItem(MenuRoot + "禁用", true)]
		private static bool DisableValidate()
		{
			Menu.SetChecked(MenuRoot + "禁用", !IsEnabled);
			return IsEnabled;
		}
		#endregion

		private static bool IsEnabled => EditorPrefs.GetBool(PrefEnabled, true);
		private static bool ForceUnityAutoRefresh => EditorPrefs.GetBool(PrefForceUnityAutoRefresh, true);

		private static void TryApplyUnityAutoRefreshSettings(bool forceLog = false)
		{
			if (!ForceUnityAutoRefresh)
				return;

			try
			{
				if (!Application.runInBackground)
				{
					Application.runInBackground = true;
					if (forceLog)
						Debug.Log("[AutoAssetRefresher] 已设置 Application.runInBackground = true");
				}

				var dirProp = typeof(EditorSettings).GetProperty("directoryMonitoring", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				if (dirProp != null && dirProp.PropertyType == typeof(bool))
				{
					var cur = (bool)dirProp.GetValue(null);
					if (!cur)
					{
						dirProp.SetValue(null, true);
						if (forceLog)
							Debug.Log("[AutoAssetRefresher] 已设置 EditorSettings.directoryMonitoring = true");
					}
				}

				var prop = typeof(EditorSettings).GetProperty("refreshImportMode", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				if (prop == null)
					return;

				var enumType = prop.PropertyType;
				if (enumType == null || !enumType.IsEnum)
					return;

				var enabledValue = Enum.Parse(enumType, "Enabled");
				var currentValue = prop.GetValue(null);
				if (!Equals(currentValue, enabledValue))
				{
					prop.SetValue(null, enabledValue);
					if (forceLog)
						Debug.Log($"[AutoAssetRefresher] 已设置 EditorSettings.refreshImportMode = Enabled");
				}
			}
			catch (Exception ex)
			{
				Debug.LogError($"[AutoAssetRefresher] 应用Unity自动刷新设置失败：{ex}");
			}
		}

		private static void TryStartWatchers()
		{
			DisposeWatchers();

			var assetsPath = Application.dataPath;
			if (string.IsNullOrEmpty(assetsPath) || !Directory.Exists(assetsPath))
			{
				Debug.LogError($"[AutoAssetRefresher] Assets 路径无效：{assetsPath}");
				return;
			}

			_watchCs = CreateWatcher(assetsPath, "*.cs");
			_watchAsmdef = CreateWatcher(assetsPath, "*.asmdef");
			_watchAsmref = CreateWatcher(assetsPath, "*.asmref");
		}

		private static FileSystemWatcher CreateWatcher(string root, string filter)
		{
			var w = new FileSystemWatcher(root, filter)
			{
				IncludeSubdirectories = true,
				NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
				EnableRaisingEvents = true,
			};

			w.Changed += OnFsEvent;
			w.Created += OnFsEvent;
			w.Renamed += OnFsEvent;
			w.Deleted += OnFsEvent;

			return w;
		}

		private static void OnFsEvent(object sender, FileSystemEventArgs e)
		{
			if (!IsEnabled)
				return;

			// 避免 meta/临时文件噪声；我们只关心脚本/asmdef/asmref 本体
			var path = e.FullPath;
			if (string.IsNullOrEmpty(path))
				return;

			if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
				return;

			_lastPath = path;
			RequestRefresh($"FS:{e.ChangeType}");
			EditorApplication.delayCall -= TryRunRefreshOnce;
			EditorApplication.delayCall += TryRunRefreshOnce;
		}

		private static void RequestRefresh(string reason)
		{
			_pending = true;
			_refreshAt = EditorApplication.timeSinceStartup + 0.35;
		}

		private static void TryRunRefreshOnce()
		{
			// 仍然走 OnEditorUpdate 的防抖/冷却逻辑
			OnEditorUpdate();
		}

		private static void OnEditorUpdate()
		{
			if (!_pending)
				return;

			var now = EditorApplication.timeSinceStartup;
			if (now < _refreshAt)
				return;

			if (now < _cooldownUntil)
				return;

			_pending = false;
			_cooldownUntil = now + 0.25;

			try
			{
				AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
				CompilationPipeline.RequestScriptCompilation();
			}
			catch (Exception ex)
			{
				Debug.LogError($"[AutoAssetRefresher] Refresh 失败：{ex}\nlastPath={_lastPath}");
			}
		}

		private static void DisposeWatchers()
		{
			DisposeWatcher(ref _watchCs);
			DisposeWatcher(ref _watchAsmdef);
			DisposeWatcher(ref _watchAsmref);
		}

		private static void DisposeWatcher(ref FileSystemWatcher w)
		{
			if (w == null)
				return;

			try
			{
				w.EnableRaisingEvents = false;
				w.Changed -= OnFsEvent;
				w.Created -= OnFsEvent;
				w.Renamed -= OnFsEvent;
				w.Deleted -= OnFsEvent;
				w.Dispose();
			}
			catch (Exception ex)
			{
				Debug.LogError($"[AutoAssetRefresher] Watcher 释放失败：{ex}");
			}
			finally
			{
				w = null;
			}
		}
	}
}
#endif
