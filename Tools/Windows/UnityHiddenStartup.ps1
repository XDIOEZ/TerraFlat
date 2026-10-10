param(
    [ValidateSet('Start', 'Show', 'Status')]
    [string]$Mode = 'Start',
    [string]$ProjectPath = (Join-Path $env:USERPROFILE 'Desktop\TerraFlat-master')
)

$ErrorActionPreference = 'Stop'
$projectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
$stateDirectory = Join-Path $env:LOCALAPPDATA 'TerraFlat\UnityAutostart'
$processIdFile = Join-Path $stateDirectory 'Unity.pid'
$showRequestFile = Join-Path $stateDirectory 'Show.request'
$startupLogFile = Join-Path $stateDirectory 'Startup.log'
$unityLogFile = Join-Path $stateDirectory 'Editor.log'
New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null

#region 状态与项目实例
function Write-StartupLog([string]$message) {
    Add-Content -LiteralPath $startupLogFile -Encoding UTF8 -Value "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $message"
}

function Get-ProjectUnity {
    # 只匹配本项目，避免触碰用户正在操作的其他 Unity 编辑器。
    @(Get-CimInstance Win32_Process -Filter "Name = 'Unity.exe'" | Where-Object {
        $_.CommandLine -and $_.CommandLine.IndexOf($projectPath, [StringComparison]::OrdinalIgnoreCase) -ge 0
    })
}
#endregion

#region Unity 窗口控制
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class TerraFlatUnityWindow
{
    private delegate bool EnumWindowsCallback(IntPtr window, IntPtr data);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr data);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(IntPtr window, int command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    public static int VisibleCount(int editorPid)
    {
        int count = 0;
        EnumWindows((window, unused) => {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner == (uint)editorPid && IsWindowVisible(window)) count++;
            return true;
        }, IntPtr.Zero);
        return count;
    }

    public static int Hide(int editorPid)
    {
        int count = 0;
        EnumWindows((window, unused) => {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner == (uint)editorPid && IsWindowVisible(window)) {
                ShowWindowAsync(window, 0); // SW_HIDE：窗口与任务栏条目一起消失。
                count++;
            }
            return true;
        }, IntPtr.Zero);
        return count;
    }

    public static int Show(int editorPid)
    {
        int count = 0;
        IntPtr mainWindow = IntPtr.Zero;
        EnumWindows((window, unused) => {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner != (uint)editorPid) return true;
            StringBuilder caption = new StringBuilder(512);
            GetWindowText(window, caption, caption.Capacity);
            if (caption.ToString().IndexOf("Unity", StringComparison.OrdinalIgnoreCase) < 0) return true;
            ShowWindowAsync(window, 9); // SW_RESTORE：恢复正常编辑器窗口。
            if (mainWindow == IntPtr.Zero) mainWindow = window;
            count++;
            return true;
        }, IntPtr.Zero);
        if (mainWindow != IntPtr.Zero) SetForegroundWindow(mainWindow);
        return count;
    }
}
'@
#endregion

#region 启动、恢复与检查
try {
    $editors = @(Get-ProjectUnity)

    if ($Mode -eq 'Status') {
        if ($editors.Count -eq 0) {
            Write-Output 'TerraFlat Unity: not running'
        } else {
            foreach ($editor in $editors) {
                Write-Output "TerraFlat Unity: PID=$($editor.ProcessId), visibleWindows=$([TerraFlatUnityWindow]::VisibleCount([int]$editor.ProcessId))"
            }
        }
        exit 0
    }

    if ($Mode -eq 'Show') {
        # 恢复时通知后台监视器停止隐藏，不会关闭 Unity 进程。
        New-Item -ItemType File -Path $showRequestFile -Force | Out-Null
        Start-Sleep -Milliseconds 600
        foreach ($editor in $editors) {
            $restored = [TerraFlatUnityWindow]::Show([int]$editor.ProcessId)
            Write-Output "Unity PID=$($editor.ProcessId): restored $restored window(s)"
        }
        exit 0
    }

    if ($editors.Count -gt 0) {
        Write-StartupLog "Project already open (PID=$($editors[0].ProcessId)); leaving its windows untouched."
        exit 0
    }

    $versionText = Get-Content -LiteralPath (Join-Path $projectPath 'ProjectSettings\ProjectVersion.txt') -Raw
    if ($versionText -notmatch '(?m)^m_EditorVersion:\s*(\S+)') {
        throw 'Cannot determine the Unity Editor version from ProjectVersion.txt.'
    }
    $version = $Matches[1]
    $editorExecutable = Join-Path ${env:ProgramFiles} "Unity\Hub\Editor\$version\Editor\Unity.exe"
    if (-not (Test-Path -LiteralPath $editorExecutable)) {
        throw "Unity Editor not installed: $editorExecutable"
    }

    Remove-Item -LiteralPath $showRequestFile -Force -ErrorAction SilentlyContinue
    $arguments = @('-projectPath', "`"$projectPath`"", '-logFile', "`"$unityLogFile`"")
    $unityProcess = Start-Process -FilePath $editorExecutable -ArgumentList $arguments -WorkingDirectory $projectPath -WindowStyle Hidden -PassThru
    $unityProcessId = [int]$unityProcess.Id
    Set-Content -LiteralPath $processIdFile -Value $unityProcessId -Encoding ASCII
    Write-StartupLog "Started Unity $version for $projectPath (PID=$unityProcessId)."

    $hiddenWindows = 0
    while (Get-Process -Id $unityProcessId -ErrorAction SilentlyContinue) {
        if (Test-Path -LiteralPath $showRequestFile) {
            Write-StartupLog "Show requested; background window monitor stopped (PID=$unityProcessId)."
            break
        }
        $hiddenWindows += [TerraFlatUnityWindow]::Hide($unityProcessId)
        Start-Sleep -Milliseconds 350
    }
    Write-StartupLog "Window monitor finished (PID=$unityProcessId, hidden=$hiddenWindows)."
    Remove-Item -LiteralPath $processIdFile -Force -ErrorAction SilentlyContinue
} catch {
    Write-StartupLog "ERROR: $($_.Exception.Message)"
    throw
}
#endregion
