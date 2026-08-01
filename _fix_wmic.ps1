Write-Host "=== Full Edge command via wmic ==="
$output = wmic path win32_process where "name='msedge.exe' and processid=10520" get commandline /format:list 2>$null
Write-Host $output
