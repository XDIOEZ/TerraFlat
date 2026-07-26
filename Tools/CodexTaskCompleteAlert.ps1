<#
.SYNOPSIS
    当 Codex 所有任务完成时，显示一次明显、短暂且自动结束的屏幕呼吸提示。

.DESCRIPTION
    调用端应先检查 Codex 任务列表：若仍有 active 或 queued 的其他任务，传入
    -PendingConversations。脚本会立即退出，不产生声音或屏幕覆盖层。

    默认调用会启动一个隐藏的后台提醒进程，因此不会阻塞任务的最终回复。

.PARAMETER PendingConversations
    指示仍有其他对话正在运行或排队；此时不提醒。

.PARAMETER DurationMilliseconds
    一次完整呼吸提示的持续时间。

.PARAMETER Silent
    不播放系统提示音。
#>
[CmdletBinding()]
param(
    # 保留旧调用兼容性，不再使用次数限制。
    [ValidateRange(1, 5)]
    [int]$Flashes = 2,

    [ValidateRange(600, 10000)]
    [int]$DurationMilliseconds = 2200,

    [switch]$Silent,

    [switch]$PendingConversations,

    [switch]$AlertWorker
)

if ($PendingConversations) {
    return
}

if (-not $AlertWorker) {
    $workerArguments = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', $PSCommandPath,
        '-AlertWorker',
        '-DurationMilliseconds', $DurationMilliseconds
    )

    if ($Silent) {
        $workerArguments += '-Silent'
    }

    Start-Process -FilePath 'powershell.exe' -ArgumentList $workerArguments -WindowStyle Hidden
    return
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type -TypeDefinition @"
using System;
using System.Windows.Forms;

public sealed class CodexBreathingOverlay : Form
{
    protected override bool ShowWithoutActivation
    {
        get { return true; }
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT
            parameters.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            parameters.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW
            return parameters;
        }
    }
}
"@ -ReferencedAssemblies System.Windows.Forms

$screenBounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
$overlay = New-Object CodexBreathingOverlay
$overlay.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::None
$overlay.StartPosition = [System.Windows.Forms.FormStartPosition]::Manual
$overlay.Bounds = $screenBounds
$overlay.BackColor = [System.Drawing.Color]::FromArgb(70, 170, 255)
$overlay.Opacity = 0.04
$overlay.TopMost = $true
$overlay.ShowInTaskbar = $false

try {
    if (-not $Silent) {
        [System.Media.SystemSounds]::Exclamation.Play()
    }

    $minimumOpacity = 0.04
    $maximumOpacity = 0.34
    $visibleMilliseconds = [System.Math]::Max($DurationMilliseconds, 1800)
    $animationTimer = [System.Diagnostics.Stopwatch]::StartNew()
    $frameTimer = New-Object System.Windows.Forms.Timer
    $frameTimer.Interval = 16
    $frameTimer.Add_Tick({
        $elapsed = $animationTimer.ElapsedMilliseconds
        $cycleProgress = [System.Math]::Min(
            1.0,
            $elapsed / [double]$visibleMilliseconds)
        $breathAmount = [System.Math]::Sin(
            [System.Math]::PI * $cycleProgress)
        $overlay.Opacity =
            $minimumOpacity +
            (($maximumOpacity - $minimumOpacity) * $breathAmount)

        if ($elapsed -ge $visibleMilliseconds) {
            $frameTimer.Stop()
            $overlay.Close()
        }
    })

    $overlay.Add_Shown({
        $animationTimer.Restart()
        $frameTimer.Start()
    })

    [System.Windows.Forms.Application]::Run($overlay)
}
finally {
    if ($frameTimer -ne $null) {
        $frameTimer.Stop()
        $frameTimer.Dispose()
    }
    $overlay.Close()
    $overlay.Dispose()
}
