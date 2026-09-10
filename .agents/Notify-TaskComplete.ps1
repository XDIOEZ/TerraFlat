param(
    [string]$Title = "ChatGPT",
    [string]$Message = "Task completed.",
    [int]$DelaySeconds = 30,
    [int]$DurationMilliseconds = 5000
)

$ErrorActionPreference = "Stop"

if ($DelaySeconds -gt 0) {
    Start-Sleep -Seconds $DelaySeconds
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$notifyIcon = New-Object System.Windows.Forms.NotifyIcon

try {
    $notifyIcon.Icon = [System.Drawing.SystemIcons]::Information
    $notifyIcon.BalloonTipTitle = $Title
    $notifyIcon.BalloonTipText = $Message
    $notifyIcon.BalloonTipIcon = [System.Windows.Forms.ToolTipIcon]::Info
    $notifyIcon.Visible = $true
    $notifyIcon.ShowBalloonTip($DurationMilliseconds)

    $deadline = (Get-Date).AddMilliseconds([Math]::Max($DurationMilliseconds, 1000))
    while ((Get-Date) -lt $deadline) {
        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 100
    }
}
finally {
    $notifyIcon.Dispose()
}
