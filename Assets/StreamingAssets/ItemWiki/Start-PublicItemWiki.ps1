$ErrorActionPreference = 'Stop'

$wikiPort = 8766
$tunnelName = 'FlatWorld_Wiki'
$wikiDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
$serverScript = Join-Path $wikiDirectory 'wiki_server.py'
$sakuraConfigPath = Join-Path $env:ProgramData 'SakuraFrpService\config.json'
$sakuraServicePath = Join-Path $env:ProgramFiles 'SakuraFrpLauncher\SakuraFrpService.exe'
$sakuraLogDirectory = Join-Path $env:ProgramData 'SakuraFrpService\Logs'
$apiBase = 'https://api.natfrp.com/v4'
$localWikiUrl = "http://127.0.0.1:$wikiPort/Assets/StreamingAssets/ItemWiki/"

# 读取 SakuraFrp 本机配置，仅在内存中使用访问密钥，不把密钥写入项目文件或命令行参数。
function Get-SakuraRuntimeConfig {
    if (-not (Test-Path $sakuraConfigPath)) {
        throw '未找到 SakuraFrp 启动器配置，请先登录一次 SakuraFrp 启动器。'
    }
    $config = Get-Content $sakuraConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace([string]$config.token)) {
        throw 'SakuraFrp 启动器尚未配置访问密钥，请先登录启动器。'
    }
    return $config
}

# 调用 SakuraFrp 开放 API；Authorization 只存在于当前 PowerShell 进程内存中。
function Invoke-SakuraApi {
    param(
        [Parameter(Mandatory = $true)][string]$Token,
        [Parameter(Mandatory = $true)][ValidateSet('GET', 'POST')][string]$Method,
        [Parameter(Mandatory = $true)][string]$Path,
        [object]$Body = $null
    )

    $headers = @{ Authorization = "Bearer $Token" }
    $parameters = @{
        Uri = "$apiBase$Path"
        Headers = $headers
        Method = $Method
        ErrorAction = 'Stop'
    }
    if ($null -ne $Body) {
        $parameters.ContentType = 'application/json; charset=utf-8'
        $parameters.Body = $Body | ConvertTo-Json -Depth 8 -Compress
    }
    try {
        return Invoke-RestMethod @parameters
    }
    catch {
        $statusCode = $null
        $responseBody = $null
        if ($null -ne $_.Exception.Response) {
            try {
                $statusCode = [int]$_.Exception.Response.StatusCode
                $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
                $responseBody = $reader.ReadToEnd()
                $reader.Dispose()
            }
            catch {
                $responseBody = $null
            }
        }
        $detail = if ([string]::IsNullOrWhiteSpace($responseBody)) { $_.Exception.Message } else { $responseBody }
        throw "SakuraFrp API $Method $Path 失败（HTTP $statusCode）：$detail"
    }
}

# 统一 SakuraFrp 隧道列表返回形态，兼容 API 直接数组和包装对象两种形式。
function ConvertTo-SakuraTunnelList {
    param([object]$Response)

    if ($null -eq $Response) {
        return @()
    }
    if ($Response.PSObject.Properties.Name -contains 'tunnels') {
        return @($Response.tunnels)
    }
    if ($Response.PSObject.Properties.Name -contains 'data' -and $Response.data -is [System.Array]) {
        return @($Response.data)
    }
    return @($Response)
}

# 复用同名 Wiki 隧道；首次运行时基于当前账户已有隧道的节点创建一条独立 TCP + 自动 HTTPS 隧道。
function Get-OrCreateWikiTunnel {
    param(
        [Parameter(Mandatory = $true)][string]$Token,
        [Parameter(Mandatory = $true)]$SakuraConfig
    )

    $tunnels = @(ConvertTo-SakuraTunnelList (Invoke-SakuraApi -Token $Token -Method GET -Path '/tunnels'))
    $existing = $tunnels | Where-Object { $_.name -eq $tunnelName } | Select-Object -First 1
    if ($null -ne $existing) {
        if ($existing.type -ne 'tcp' -or [string]$existing.local_ip -ne '127.0.0.1' -or [int]$existing.local_port -ne $wikiPort) {
            throw "已存在同名隧道 $tunnelName，但其目标不是 127.0.0.1:$wikiPort；为避免误改现有隧道，已停止。"
        }
        return $existing
    }

    $autoStartIds = @($SakuraConfig.auto_start_tunnels) | ForEach-Object { [string]$_ }
    $reference = $tunnels |
        Where-Object { $autoStartIds -contains [string]$_.id } |
        Select-Object -First 1
    if ($null -eq $reference) {
        $reference = $tunnels | Select-Object -First 1
    }
    if ($null -eq $reference -or $null -eq $reference.node) {
        throw '当前 SakuraFrp 账户没有可用于选择节点的既有隧道，请先在启动器中创建任意一条可用隧道。'
    }

    try {
        $created = Invoke-SakuraApi -Token $Token -Method POST -Path '/tunnels' -Body @{
            name = $tunnelName
            type = 'tcp'
            node = [int]$reference.node
            note = 'FlatWorld Item Wiki 公开只读入口'
            local_ip = '127.0.0.1'
            local_port = $wikiPort
            extra = 'auto_https = auto'
        }
    }
    catch {
        if ($_.Exception.Message -match '隧道数量已到达上限') {
            $existingSummary = ($tunnels | ForEach-Object { "#$($_.id) $($_.name)" }) -join '、'
            throw "SakuraFrp 隧道名额已满，无法创建 $tunnelName。现有隧道：$existingSummary。脚本不会自动删除或覆盖它们；释放一个名额后重新运行即可自动创建。"
        }
        throw
    }
    Write-Host "[SakuraFrp] 已创建 Wiki 隧道：$($created.name) (#$($created.id))"

    $tunnels = @(ConvertTo-SakuraTunnelList (Invoke-SakuraApi -Token $Token -Method GET -Path '/tunnels'))
    $resolved = $tunnels | Where-Object { [int]$_.id -eq [int]$created.id } | Select-Object -First 1
    if ($null -eq $resolved) {
        throw '隧道已经创建，但无法从 SakuraFrp API 重新读取，请稍后重新运行。'
    }
    return $resolved
}

# 确保公开 Wiki 隧道加入 SakuraFrp 自动启动列表；保留其他有效配置，并使用无 BOM UTF-8 写回。
function Ensure-WikiAutoStart {
    param(
        [Parameter(Mandatory = $true)]$SakuraConfig,
        [Parameter(Mandatory = $true)]$Tunnel
    )

    $tunnelId = [int64]$Tunnel.id
    $autoStartIds = @($SakuraConfig.auto_start_tunnels) | ForEach-Object { [int64]$_ }
    if ($autoStartIds -contains $tunnelId) {
        return
    }

    $SakuraConfig.auto_start_tunnels = @($autoStartIds + $tunnelId | Select-Object -Unique)
    $serializedConfig = ($SakuraConfig | ConvertTo-Json -Depth 32) + [Environment]::NewLine
    [System.IO.File]::WriteAllText(
        $sakuraConfigPath,
        $serializedConfig,
        (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "[SakuraFrp] 已将 Wiki 隧道 #$tunnelId 加入自动启动列表。"
}

# 返回当天 SakuraFrpService 日志文件路径。
function Get-SakuraLogPath {
    return Join-Path $sakuraLogDirectory ("SakuraFrpService.{0}.log" -f (Get-Date -Format 'yyyyMMdd'))
}

# 持续输出当前 Wiki 隧道的 frpc 日志，并在连接地址出现时显示完整 HTTPS Wiki 地址。
function Watch-WikiTunnelLog {
    param(
        [Parameter(Mandatory = $true)]$ServiceProcess,
        [long]$InitialLength = 0
    )

    $logPath = Get-SakuraLogPath
    $offset = $InitialLength
    while (-not $ServiceProcess.HasExited) {
        Start-Sleep -Milliseconds 250
        if (-not (Test-Path $logPath)) {
            continue
        }

        $stream = $null
        $reader = $null
        try {
            $stream = New-Object System.IO.FileStream(
                $logPath,
                [System.IO.FileMode]::Open,
                [System.IO.FileAccess]::Read,
                [System.IO.FileShare]::ReadWrite)
            if ($offset -gt $stream.Length) {
                $offset = 0
            }
            [void]$stream.Seek($offset, [System.IO.SeekOrigin]::Begin)
            $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::UTF8, $true, 4096, $true)
            while (-not $reader.EndOfStream) {
                $line = $reader.ReadLine()
                if ($line -match 'Tunnel/FlatWorld_Wiki' -or $line -match 'frpc\[FlatWorld_Wiki\|') {
                    Write-Host $line
                }
                if ($line -match '使用 >>([^<]+)<< 连接你的隧道') {
                    Write-Host "[SakuraFrp] HTTPS 公开地址：https://$($Matches[1])/Assets/StreamingAssets/ItemWiki/"
                }
            }
            $offset = $stream.Position
        }
        finally {
            if ($null -ne $reader) { $reader.Dispose() }
            if ($null -ne $stream) { $stream.Dispose() }
        }
    }
}

# 返回可用 Python 启动命令与参数前缀，兼容 Windows py Launcher 与直接 python。
function Get-PythonLaunchInfo {
    $py = Get-Command py -ErrorAction SilentlyContinue
    if ($py) {
        return @{ FilePath = $py.Source; PrefixArgs = @('-3') }
    }
    $python = Get-Command python -ErrorAction SilentlyContinue
    if ($python) {
        return @{ FilePath = $python.Source; PrefixArgs = @() }
    }
    throw '未找到 Python，请先安装 Python 或将其加入 PATH。'
}

# 清理上一次异常退出遗留的公开 Wiki Python 服务，不影响原 8765 开发者 Wiki。
function Stop-StalePublicWikiServer {
    Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -match '^python(w)?\.exe$' -and $_.CommandLine -and
            $_.CommandLine -like "*$serverScript*" -and
            $_.CommandLine -match '--public-readonly'
        } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
}

# 等待公开 Wiki 的只读状态 API 可访问，并确认服务没有误启用写入能力。
function Wait-PublicWikiReady {
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        Start-Sleep -Milliseconds 200
        try {
            $status = Invoke-RestMethod -Uri "http://127.0.0.1:$wikiPort/api/wiki/status" -Method Get -TimeoutSec 2
            if ($status.publicReadOnly -eq $true -and $status.writable -eq $false) {
                return
            }
        }
        catch {
            # 服务启动的短暂窗口内连接失败属于正常重试路径。
        }
    }
    throw '公开 Wiki 服务未能在预期时间内进入只读状态。'
}

$serverProcess = $null
$sakuraServiceProcess = $null
$exitCode = 0

try {
    $sakuraConfig = Get-SakuraRuntimeConfig
    $token = [string]$sakuraConfig.token
    $tunnel = Get-OrCreateWikiTunnel -Token $token -SakuraConfig $sakuraConfig
    Ensure-WikiAutoStart -SakuraConfig $sakuraConfig -Tunnel $tunnel

    if (-not (Test-Path $sakuraServicePath)) {
        throw '未找到 SakuraFrpService.exe，请重新安装或修复 SakuraFrp 启动器。'
    }
    if (Get-Process -Name 'SakuraFrpService' -ErrorAction SilentlyContinue) {
        throw '检测到 SakuraFrpService 已在运行。请先关闭现有 SakuraFrp 启动器核心服务，再重新运行公开 Wiki，避免重复启动隧道。'
    }

    Stop-StalePublicWikiServer
    $python = Get-PythonLaunchInfo
    $serverArgs = @($python.PrefixArgs) + @(
        $serverScript,
        '--port', [string]$wikiPort,
        '--public-readonly',
        '--strict-port',
        '--no-browser'
    )
    $serverProcess = Start-Process -FilePath $python.FilePath -ArgumentList $serverArgs -PassThru -WindowStyle Hidden
    Wait-PublicWikiReady

    Write-Host "[Item Wiki] 公开只读服务：$localWikiUrl"
    Write-Host "[SakuraFrp] 正在启动隧道 #$($tunnel.id)。"
    Write-Host '[SakuraFrp] 朋友访问时，请使用下方日志给出的 HTTPS 公开地址。'
    Write-Host '[SakuraFrp] 关闭本窗口即可同时停止公开 Wiki 与本次隧道。'
    Start-Process $localWikiUrl

    $logPath = Get-SakuraLogPath
    $initialLogLength = if (Test-Path $logPath) { (Get-Item $logPath).Length } else { 0 }
    $sakuraServiceProcess = Start-Process -FilePath $sakuraServicePath -ArgumentList @(
        '-c', $sakuraConfigPath, '--daemon'
    ) -PassThru -NoNewWindow
    Watch-WikiTunnelLog -ServiceProcess $sakuraServiceProcess -InitialLength $initialLogLength
    if ($sakuraServiceProcess.ExitCode -ne 0) {
        throw "SakuraFrpService 异常退出，退出码：$($sakuraServiceProcess.ExitCode)。"
    }
}
catch {
    Write-Host "[公开 Wiki] $($_.Exception.Message)" -ForegroundColor Red
    $exitCode = 1
}
finally {
    if ($null -ne $serverProcess -and -not $serverProcess.HasExited) {
        Stop-Process -Id $serverProcess.Id -Force -ErrorAction SilentlyContinue
    }
    if ($null -ne $sakuraServiceProcess -and -not $sakuraServiceProcess.HasExited) {
        Stop-Process -Id $sakuraServiceProcess.Id -Force -ErrorAction SilentlyContinue
    }
}

exit $exitCode
