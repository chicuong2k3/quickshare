Write-Host "=== Full Edge command line ==="
$p = Get-CimInstance Win32_Process -Filter "ProcessId=10520"
$cmd = $p.CommandLine
Write-Host $cmd
Write-Host ""
Write-Host "Length: $($cmd.Length)"

# Extract flags for restart
Write-Host ""
Write-Host "=== Flags extracted ==="
# The cmd starts with the exe path in quotes, then flags
if ($cmd -match '^"([^"]+)"\s+(.+)$') {
    $exe = $Matches[1]
    $flags = $Matches[2]
    Write-Host "EXE: $exe"
    Write-Host ""
    Write-Host "ORIGINAL FLAGS: $flags"
}
