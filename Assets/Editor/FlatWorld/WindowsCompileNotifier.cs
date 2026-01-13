#if UNITY_EDITOR_WIN
using System;
using System.IO;
using System.Text;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using ProcessWindowStyle = System.Diagnostics.ProcessWindowStyle;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace FlatWorld.EditorTools
{
	[InitializeOnLoad]
	public static class WindowsCompileNotifier
	{
		#region EditorPrefs Keys
		private const string PrefEnabled = "FW_WindowsCompileNotifier_Enabled";
		private const string PrefStartUtcTicks = "FW_WindowsCompileNotifier_StartUtcTicks";
		private const string PrefPendingNotify = "FW_WindowsCompileNotifier_PendingNotify";
		#endregion

		#region NotifyIcon
		private const int BalloonTimeoutMs = 4000;
		private const int PowerShellLifetimeMs = 6500;
		#endregion

		static WindowsCompileNotifier()
		{
			CompilationPipeline.compilationStarted -= OnCompilationStarted;
			CompilationPipeline.compilationStarted += OnCompilationStarted;

			CompilationPipeline.compilationFinished -= OnCompilationFinished;
			CompilationPipeline.compilationFinished += OnCompilationFinished;

			AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload;
			AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;

			EditorApplication.quitting -= OnEditorQuitting;
			EditorApplication.quitting += OnEditorQuitting;
		}

		#region Menu
		private const string MenuRoot = "Tools/FlatWorld/编译完成通知/";

		[MenuItem(MenuRoot + "启用", false, 1)]
		private static void Enable() => EditorPrefs.SetBool(PrefEnabled, true);

		[MenuItem(MenuRoot + "禁用", false, 2)]
		private static void Disable() => EditorPrefs.SetBool(PrefEnabled, false);

		[MenuItem(MenuRoot + "测试通知", false, 50)]
		private static void TestNotify()
		{
			TryShowNotification("FlatWorld", "测试通知：Unity 编译完成提醒已启用");
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

		#region Compilation Hooks
		private static void OnCompilationStarted(object context)
		{
			if (!IsEnabled) return;

			EditorPrefs.SetString(PrefStartUtcTicks, DateTime.UtcNow.Ticks.ToString());
			EditorPrefs.SetBool(PrefPendingNotify, false);
		}

		private static void OnCompilationFinished(object context)
		{
			if (!IsEnabled) return;

			EditorPrefs.SetBool(PrefPendingNotify, true);
		}

		private static void OnAfterAssemblyReload()
		{
			if (!IsEnabled) return;
			if (!EditorPrefs.GetBool(PrefPendingNotify, false)) return;

			EditorPrefs.SetBool(PrefPendingNotify, false);

			var elapsedText = TryGetElapsedText(out var elapsedMs)
				? $"耗时 {elapsedMs / 1000f:0.0}s"
				: "";

			var message = string.IsNullOrEmpty(elapsedText)
				? "Unity 脚本编译完成"
				: $"Unity 脚本编译完成（{elapsedText}）";

			TryShowNotification("FlatWorld", message);
		}
		#endregion

		#region Notification
		private static bool TryGetElapsedText(out long elapsedMs)
		{
			elapsedMs = 0;
			var ticksText = EditorPrefs.GetString(PrefStartUtcTicks, string.Empty);
			if (string.IsNullOrEmpty(ticksText)) return false;
			if (!long.TryParse(ticksText, out var startTicks)) return false;

			var start = new DateTime(startTicks, DateTimeKind.Utc);
			var elapsed = DateTime.UtcNow - start;
			elapsedMs = Math.Max(0, (long)elapsed.TotalMilliseconds);
			return elapsedMs > 0;
		}

		private static void TryShowNotification(string title, string message)
		{
			try
			{
				RunPowerShellNotifyIcon(title, message);
			}
			catch (Exception ex)
			{
				Debug.LogError($"[WindowsCompileNotifier] 通知发送失败：{ex}");
			}
		}

		private static void RunPowerShellNotifyIcon(string title, string message)
		{
			var safeTitle = title ?? "Unity";
			var safeMessage = message ?? "";
			var projectName = Path.GetFileName(Directory.GetCurrentDirectory());

			var psScript =
				"Add-Type -AssemblyName System.Windows.Forms;" +
				"Add-Type -AssemblyName System.Drawing;" +
				$"$proj='{EscapePowerShellSingleQuoted(projectName)}';" +
				"$sig=@'\nusing System;\nusing System.Runtime.InteropServices;\npublic static class FWWin32{\n  [DllImport(\"user32.dll\")] public static extern bool SetForegroundWindow(IntPtr hWnd);\n  [DllImport(\"user32.dll\")] public static extern bool BringWindowToTop(IntPtr hWnd);\n  [DllImport(\"user32.dll\")] public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);\n  [DllImport(\"user32.dll\")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);\n  [DllImport(\"user32.dll\")] public static extern bool IsIconic(IntPtr hWnd);\n  [DllImport(\"user32.dll\")] public static extern IntPtr GetForegroundWindow();\n  [DllImport(\"user32.dll\")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);\n  [DllImport(\"user32.dll\")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);\n  [DllImport(\"kernel32.dll\")] public static extern uint GetCurrentThreadId();\n  [DllImport(\"user32.dll\")] public static extern IntPtr SetFocus(IntPtr hWnd);\n  [DllImport(\"user32.dll\")] public static extern IntPtr SetActiveWindow(IntPtr hWnd);\n  [DllImport(\"user32.dll\")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);\n  [DllImport(\"user32.dll\")] public static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);\n}\n'@;" +
				"Add-Type -TypeDefinition $sig;" +
				"function FW_ActivateUnity{\n" +
				"  try{\n" +
				"    $c = Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 -and ($_.ProcessName -eq 'Unity' -or $_.MainWindowTitle -like '*Unity*') };\n" +
				"    $p = $c | Where-Object { $_.MainWindowTitle -like ('*' + $proj + '*') } | Sort-Object -Property MainWindowTitle -Descending | Select-Object -First 1;\n" +
				"    if($p -eq $null){ $p = $c | Sort-Object -Property MainWindowTitle -Descending | Select-Object -First 1 }\n" +
				"    if($p -eq $null){ return }\n" +
				"    $h = $p.MainWindowHandle;\n" +
				"    try{ $ws = New-Object -ComObject WScript.Shell; $ws.SendKeys('%'); $ws.AppActivate($p.MainWindowTitle) | Out-Null; $ws.AppActivate($p.Id) | Out-Null }catch{}\n" +
				"    if([FWWin32]::IsIconic($h)){ [FWWin32]::ShowWindow($h, 9) | Out-Null } else { [FWWin32]::ShowWindow($h, 5) | Out-Null }\n" +
				"    [FWWin32]::ShowWindowAsync($h, 9) | Out-Null;\n" +
				"    $fg = [FWWin32]::GetForegroundWindow();\n" +
				"    $pid = 0; $tid = [FWWin32]::GetWindowThreadProcessId($fg, [ref]$pid);\n" +
				"    $cur = [FWWin32]::GetCurrentThreadId();\n" +
				"    [FWWin32]::AttachThreadInput($cur, $tid, $true) | Out-Null;\n" +
				"    [FWWin32]::BringWindowToTop($h) | Out-Null;\n" +
				"    try{ [FWWin32]::SwitchToThisWindow($h, $true) }catch{}\n" +
				"    try{ [FWWin32]::SetWindowPos($h, [IntPtr](-1), 0,0,0,0, 0x0003) | Out-Null; Start-Sleep -Milliseconds 10; [FWWin32]::SetWindowPos($h, [IntPtr](-2), 0,0,0,0, 0x0003) | Out-Null }catch{}\n" +
				"    [FWWin32]::SetActiveWindow($h) | Out-Null;\n" +
				"    [FWWin32]::SetFocus($h) | Out-Null;\n" +
				"    [FWWin32]::SetForegroundWindow($h) | Out-Null;\n" +
				"    [FWWin32]::AttachThreadInput($cur, $tid, $false) | Out-Null;\n" +
				"  }catch{}\n" +
				"}\n" +
				"$n = New-Object System.Windows.Forms.NotifyIcon;" +
				"$n.Icon = [System.Drawing.SystemIcons]::Information;" +
				"$n.Visible = $true;" +
				$"$n.BalloonTipTitle = '{EscapePowerShellSingleQuoted(safeTitle)}';" +
				$"$n.BalloonTipText = '{EscapePowerShellSingleQuoted(safeMessage)}';" +
				"$n.add_BalloonTipClicked({ FW_ActivateUnity });" +
				"$n.add_Click({ FW_ActivateUnity });" +
				"$n.ShowBalloonTip(" + BalloonTimeoutMs + ");" +
				"$end = (Get-Date).AddMilliseconds(" + PowerShellLifetimeMs + ");" +
				"while((Get-Date) -lt $end){ [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 50 }" +
				"$n.Dispose();";

			var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(psScript));
			var psi = new ProcessStartInfo
			{
				FileName = "powershell",
				Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}",
				UseShellExecute = false,
				CreateNoWindow = true,
				WindowStyle = ProcessWindowStyle.Hidden,
			};

			Process.Start(psi);
		}

		private static string EscapePowerShellSingleQuoted(string value)
		{
			return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("'", "''");
		}

		private static void OnEditorQuitting() { }
		#endregion
	}
}
#endif
