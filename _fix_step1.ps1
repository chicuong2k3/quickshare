Write-Host "=== Getting OMP temp profile path ==="
$procs = Get-CimInstance Win32_Process -Filter "name='msedge.exe'"
foreach ($p in $procs) {
    $cmd = $p.CommandLine
    if ($cmd -and $cmd -match '--user-data-dir[= ]"([^"]+)"') {
        $profile = $Matches[1]
        Write-Host "PID=$($p.ProcessId) user-data-dir=$profile"
        break
    } elseif ($cmd -and $cmd -match '--user-data-dir[= ](\S+)') {
        $profile = $Matches[1]
        Write-Host "PID=$($p.ProcessId) user-data-dir=$profile"
        break
    }
}

Write-Host ""
Write-Host "=== Extension manifest.json ==="
$manifest = Get-Content 'C:\Users\nguye\.opencode-browser\extension\manifest.json' -Raw
Write-Host $manifest

Write-Host ""
Write-Host "=== Check if manifest has 'key' field ==="
$j = $manifest | ConvertFrom-Json
if ($j.key) {
    Write-Host "KEY FIELD EXISTS: $($j.key)"
} else {
    Write-Host "NO key field in manifest - extension ID is path-hash based"
}

Write-Host ""
Write-Host "=== Current host.log tail ==="
Get-Content 'C:\Users\nguye\.opencode-browser\logs\host.log' -Tail 10
