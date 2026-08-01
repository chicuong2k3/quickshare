$sp = 'C:\Users\nguye\.opencode-browser\.edge-test-profile\Default\Secure Preferences'
Write-Host "=== Test Profile Extensions ==="
$raw = Get-Content $sp -Raw
$j = $raw | ConvertFrom-Json
if ($j.extensions -and $j.extensions.settings) {
    foreach ($prop in $j.extensions.settings.PSObject.Properties) {
        $val = $prop.Value
        $id = $prop.Name
        $name = if ($val.name) { $val.name } else { '(built-in)' }
        $state = if ($val.state) { $val.state } else { '?' }
        Write-Host "$id | $name | state=$state | path=$($val.path)"
    }
} else {
    Write-Host "No extensions.settings"
}

Write-Host ""
Write-Host "=== Looking for opencode in test profile ==="
if ($raw -match 'opencode|browser.automation|browser_automation') {
    Write-Host "FOUND opencode reference!"
} else {
    Write-Host "NO opencode reference"
}
if ($raw -match 'ifmehklpnfdibgfgeoeeglaaepomklmp') {
    Write-Host "FOUND hardcoded extension ID ifmehklpnfdibgfgeoeeglaaepomklmp"
} else {
    Write-Host "NO hardcoded extension ID found"
}
