$ErrorActionPreference = 'Stop'

$port = 8765
$wikiDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$serverScript = Join-Path $wikiDirectory 'wiki_server.py'

# 清理旧版 Item Wiki 启动器遗留的 Python 服务，避免 8765 被静态 http.server 抢占而进入只读模式。
Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Name -match '^python(w)?\.exe$' -and $_.CommandLine -and (
            $_.CommandLine -like "*$serverScript*" -or
            $_.CommandLine -match '-m\s+http\.server\s+8765(?:\s|$)'
        )
    } |
    ForEach-Object {
        Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
    }
Start-Sleep -Milliseconds 250

$python = Get-Command py -ErrorAction SilentlyContinue
if ($python) {
    & $python.Source -3 $serverScript --port $port
    exit $LASTEXITCODE
}

$python = Get-Command python -ErrorAction SilentlyContinue
if ($python) {
    & $python.Source $serverScript --port $port
    exit $LASTEXITCODE
}

Write-Host '[Item Wiki] Python was not found. Install Python or add it to PATH.'
Read-Host 'Press Enter to close'
exit 1
