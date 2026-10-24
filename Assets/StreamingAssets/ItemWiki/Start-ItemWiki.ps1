param(
    [switch]$NoBrowser,
    [ValidateRange(1024, 65535)][int]$Port = 8765,
    [switch]$PublicReadOnly,
    [switch]$PassThru
)

$ErrorActionPreference = 'Stop'
$wikiDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$python = Get-Command python -ErrorAction SilentlyContinue
$prefix = @()
if (-not $python) {
    $python = Get-Command py -ErrorAction SilentlyContinue
    $prefix = @('-3')
}
if (-not $python) { throw 'Python 3 was not found. Install Python or add it to PATH.' }

#region Wiki service launcher
# 后台启动、服务身份检查和复用统一交给 Python 服务入口。
$serverArgs = $prefix + @('-B', (Join-Path $wikiDirectory 'wiki_server.py'), '--port', [string]$Port, '--ensure-running', '--session-json')
if ($NoBrowser) { $serverArgs += '--no-browser' }
if ($PublicReadOnly) { $serverArgs += '--public-readonly' }
$result = & $python.Source @serverArgs
if ($LASTEXITCODE -ne 0) { throw 'Item Wiki failed to start. See the error above.' }
$session = $result | ConvertFrom-Json
Write-Host "[Item Wiki] $($session.url)"
if ($PassThru) { $session }
#endregion
