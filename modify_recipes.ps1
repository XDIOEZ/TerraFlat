@"
Get-ChildItem "d:\_Unity\_UnityProject\FlatWorld\Assets\4_ScriptObjects\4-5_Cook" -Filter "*.asset" | ForEach-Object {
    $content = Get-Content $_.FullName -Raw
    if ($content -notlike "*enableMirrorCrafting*") {
        $newContent = $content -replace '(\s+amount:\s+\d+)\r?\n(\s+)action:', "`$1`r`n   enableMirrorCrafting: 1`r`n`$2action:"
        if ($newContent -ne $content) {
            Set-Content $_.FullName -Value $newContent -NoNewline
            Write-Host "Modified: $($_.Name)"
        }
    }
}
"@ | Out-File -FilePath "d:\_Unity\_UnityProject\FlatWorld\modify_recipes.ps1" -Encoding UTF8
