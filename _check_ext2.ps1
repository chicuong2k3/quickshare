$localAppData = [Environment]::GetFolderPath('LocalApplicationData')
$edgeUserData = Join-Path $localAppData 'Microsoft\Edge\User Data\Default'

Write-Host "=== External Extensions ==="
$extExt = Join-Path $localAppData 'Microsoft\Edge\User Data\External Extensions'
if (Test-Path $extExt) {
    Get-ChildItem $extExt | ForEach-Object { Write-Host $_.Name }
} else {
    Write-Host "NOT FOUND: $extExt"
}

Write-Host ""
Write-Host "=== Extension Rules ==="
$extRules = Join-Path $edgeUserData 'Extension Rules'
if (Test-Path $extRules) {
    Get-ChildItem $extRules | ForEach-Object { Write-Host $_.Name }
} else {
    Write-Host "NOT FOUND: $extRules"
}

Write-Host ""
Write-Host "=== Extensions dirs ==="
$extDirs = Join-Path $edgeUserData 'Extensions'
if (Test-Path $extDirs) {
    Get-ChildItem $extDirs | ForEach-Object { 
        Write-Host "Dir: $($_.Name)"
        $subDirs = Get-ChildItem $_.FullName -Directory
        foreach ($sd in $subDirs) {
            Write-Host "  version: $($sd.Name)"
            $manifest = Join-Path $sd.FullName 'manifest.json'
            if (Test-Path $manifest) {
                $j = Get-Content $manifest -Raw | ConvertFrom-Json
                Write-Host "    name: $($j.name)"
            }
        }
    }
} else {
    Write-Host "NOT FOUND: $extDirs"
}

Write-Host ""
Write-Host "=== Looking for opencode in Secure Preferences JSON ==="
$sp = Join-Path $edgeUserData 'Secure Preferences'
$raw = Get-Content $sp -Raw
if ($raw -match 'opencode') {
    Write-Host "FOUND 'opencode' reference in Secure Preferences"
} else {
    Write-Host "NO 'opencode' reference in Secure Preferences"
}

Write-Host ""
Write-Host "=== Edge profile directories ==="
$edgeProfiles = Join-Path $localAppData 'Microsoft\Edge\User Data'
Get-ChildItem $edgeProfiles -Directory | ForEach-Object { Write-Host $_.Name }
