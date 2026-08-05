param(
    [string]$ProjectRoot = (Get-Location).Path,
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [System.Text.UTF8Encoding]::new()

$project = [System.IO.Path]::GetFullPath($ProjectRoot)
$assetsRoot = [System.IO.Path]::GetFullPath((Join-Path $project 'Assets'))
$allowedPrefix = $assetsRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

if (-not (Test-Path -LiteralPath $assetsRoot -PathType Container)) {
    throw "Unity Assets directory not found: $assetsRoot"
}

$utf8Strict = [System.Text.UTF8Encoding]::new($false, $true)
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$gbkStrict = [System.Text.Encoding]::GetEncoding(
    936,
    [System.Text.EncoderExceptionFallback]::new(),
    [System.Text.DecoderExceptionFallback]::new())

$candidates = [System.Collections.Generic.List[object]]::new()
$unrecoverable = [System.Collections.Generic.List[string]]::new()
$replacementCharacters = [System.Collections.Generic.List[string]]::new()

foreach ($file in Get-ChildItem -LiteralPath $assetsRoot -Recurse -File -Filter '*.cs') {
    $target = [System.IO.Path]::GetFullPath($file.FullName)
    if (-not $target.StartsWith($allowedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Resolved source path escaped Assets: $target"
    }

    $bytes = [System.IO.File]::ReadAllBytes($target)
    try {
        $text = $utf8Strict.GetString($bytes)
        if ($text.Contains([char]0xFFFD)) {
            $replacementCharacters.Add($target.Substring($project.Length + 1))
        }
        continue
    }
    catch [System.Text.DecoderFallbackException] {
        # Continue with the only legacy encoding currently present in the project.
    }

    try {
        $decoded = $gbkStrict.GetString($bytes)
        $roundTrip = $gbkStrict.GetBytes($decoded)
        if ([Convert]::ToBase64String($bytes) -ne [Convert]::ToBase64String($roundTrip)) {
            $unrecoverable.Add($target.Substring($project.Length + 1))
            continue
        }

        $candidates.Add([pscustomobject]@{
            Path = $target
            RelativePath = $target.Substring($project.Length + 1)
            Text = $decoded
        })
    }
    catch [System.Text.DecoderFallbackException] {
        $unrecoverable.Add($target.Substring($project.Length + 1))
    }
}

Write-Output "GBK_ROUNDTRIP_CANDIDATES=$($candidates.Count)"
$candidates.RelativePath | Sort-Object
Write-Output "UTF8_WITH_REPLACEMENT_CHARACTER=$($replacementCharacters.Count)"
$replacementCharacters | Sort-Object
Write-Output "UNRECOVERABLE=$($unrecoverable.Count)"
$unrecoverable | Sort-Object

if ($unrecoverable.Count -gt 0) {
    exit 2
}

if (-not $Apply) {
    if ($candidates.Count -gt 0 -or $replacementCharacters.Count -gt 0) {
        exit 1
    }
    exit 0
}

foreach ($candidate in $candidates) {
    $temporary = $candidate.Path + '.codex-utf8-' + [Guid]::NewGuid().ToString('N') + '.tmp'
    try {
        [System.IO.File]::WriteAllText($temporary, $candidate.Text, $utf8NoBom)
        $null = $utf8Strict.GetString([System.IO.File]::ReadAllBytes($temporary))
        Move-Item -LiteralPath $temporary -Destination $candidate.Path -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporary) {
            Remove-Item -LiteralPath $temporary -Force
        }
    }
}

Write-Output "CONVERTED=$($candidates.Count)"
if ($replacementCharacters.Count -gt 0) {
    exit 1
}
