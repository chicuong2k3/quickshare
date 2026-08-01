Write-Host "=== Edge Process Command Lines ==="
Get-CimInstance Win32_Process -Filter "name='msedge.exe'" | ForEach-Object {
    $cmd = $_.CommandLine
    if ($cmd) {
        # Check for test profile or user-data-dir
        if ($cmd -match 'edge-test-profile') {
            Write-Host "TEST_PROFILE PID=$($_.ProcessId) : user-data-dir=$($Matches[0])..."
        } elseif ($cmd -match 'user-data-dir') {
            Write-Host "CUSTOM_PROFILE PID=$($_.ProcessId) : $cmd" 
        } else {
            # Just show the first 200 chars
            Write-Host "DEFAULT PID=$($_.ProcessId) : $($cmd.Substring(0, [Math]::Min(200, $cmd.Length)))"
        }
    } else {
        Write-Host "PID=$($_.ProcessId) : (no cmdline)"
    }
}
