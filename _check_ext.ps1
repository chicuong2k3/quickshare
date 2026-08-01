$localAppData = [Environment]::GetFolderPath('LocalApplicationData')
$edgeUserData = Join-Path $localAppData 'Microsoft\Edge\User Data\Default'

Write-Host "=== Secure Preferences ==="
$sp = Join-Path $edgeUserData 'Secure Preferences'
if (Test-Path $sp) {
    $j = Get-Content $sp -Raw | ConvertFrom-Json
    if ($j.extensions -and $j.extensions.settings) {
        foreach ($prop in $j.extensions.settings.PSObject.Properties) {
            $id = $prop.Name
            $s = $prop.Value
            Write-Host "ID=$id name=$($s.name) state=$($s.state) location=$($s.manifest.location) path=$($s.path)"
        }
    } else {
        Write-Host "No extensions.settings in Secure Preferences"
        if ($j.extensions) {
            Write-Host "extensions keys: $($j.extensions.PSObject.Properties.Name -join ', ')"
        }
    }
} else {
    Write-Host "NOT FOUND: $sp"
}

Write-Host ""
Write-Host "=== Preferences ==="
$p = Join-Path $edgeUserData 'Preferences'
if (Test-Path $p) {
    $j = Get-Content $p -Raw | ConvertFrom-Json
    if ($j.extensions -and $j.extensions.settings) {
        foreach ($prop in $j.extensions.settings.PSObject.Properties) {
            $id = $prop.Name
            $s = $prop.Value
            Write-Host "ID=$id name=$($s.name) state=$($s.state)"
        }
    } else {
        Write-Host "No extensions.settings in Preferences"
    }
} else {
    Write-Host "NOT FOUND: $p"
}

Write-Host ""
Write-Host "=== Extension State dirs ==="
$extStateDir = Join-Path $edgeUserData 'Extension State'
if (Test-Path $extStateDir) {
    Get-ChildItem $extStateDir | ForEach-Object {
        Write-Host "Dir: $($_.Name)"
    }
} else {
    Write-Host "NOT FOUND: $extStateDir"
}
