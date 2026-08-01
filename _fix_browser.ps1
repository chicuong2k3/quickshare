$ErrorActionPreference = "Stop"
$OMP_PROFILE = "C:\Users\nguye\AppData\Local\Temp\omp-chrome-profile-j4ZWvc"
$EXT_SRC = "C:\Users\nguye\.opencode-browser\extension"
$EXT_ID = "ifmehklpnfdibgfgeoeeglaaepomklmp"
$EDGE_EXE = "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"

Write-Host "========================================"
Write-Host "STEP 1: Kill all Edge processes"
Write-Host "========================================"
try {
    Get-Process msedge -ErrorAction Stop | Stop-Process -Force
    Write-Host "All Edge processes killed."
} catch {
    Write-Host "No Edge processes running or error killing: $_"
}
Start-Sleep -Seconds 3

Write-Host ""
Write-Host "========================================"
Write-Host "STEP 2: Copy extension to OMP profile"
Write-Host "========================================"
$extDest = "$OMP_PROFILE\Default\Extensions\$EXT_ID\1.0.0_0"
if (Test-Path $extDest) {
    Remove-Item -Path $extDest -Recurse -Force
}
New-Item -ItemType Directory -Path $extDest -Force | Out-Null
Copy-Item "$EXT_SRC\*" -Destination $extDest -Recurse -Force
Write-Host "Extension copied to: $extDest"

# Verify copy
$files = Get-ChildItem $extDest -Recurse | Measure-Object
Write-Host "Files copied: $($files.Count)"
Get-ChildItem $extDest | ForEach-Object { Write-Host "  $($_.Name)" }

Write-Host ""
Write-Host "========================================"
Write-Host "STEP 3: Inject extension into Secure Preferences"
Write-Host "========================================"
$securePrefs = "$OMP_PROFILE\Default\Secure Preferences"
if (-not (Test-Path $securePrefs)) {
    Write-Host "ERROR: Secure Preferences not found at $securePrefs"
    exit 1
}

# Backup
Copy-Item $securePrefs "$securePrefs.backup" -Force
Write-Host "Backup created at $securePrefs.backup"

# Read and parse
$raw = Get-Content $securePrefs -Raw -Encoding UTF8
$json = $raw | ConvertFrom-Json

# Ensure extensions object exists
if (-not $json.extensions) {
    $json | Add-Member -MemberType NoteProperty -Name 'extensions' -Value @{} -Force
}
if (-not $json.extensions.settings) {
    $json.extensions | Add-Member -MemberType NoteProperty -Name 'settings' -Value @{} -Force
}

# Build the extension entry matching Chrome's expected format
$extEntry = [ordered]@{
    active_permissions = @{
        api = @("tabs","activeTab","scripting","nativeMessaging","storage","notifications","alarms")
        explicit_host = @("<all_urls>")
        manifest_permissions = @()
        scriptable_host = @()
    }
    commands = @{}
    content_settings = @{}
    creation_flags = 0
    events = @()
    from_bookmark = $false
    from_webstore = $false
    granted_permissions = @{
        api = @("tabs","activeTab","scripting","nativeMessaging","storage","notifications","alarms")
        explicit_host = @("<all_urls>")
        manifest_permissions = @()
        scriptable_host = @()
    }
    id = $EXT_ID
    incognito = "split"
    install_time = "13430048000000000"
    location = 4
    manifest = @{
        action = @{
            default_icon = @{
                "16" = "icons/icon16.png"
                "48" = "icons/icon48.png"
                "128" = "icons/icon128.png"
            }
            default_title = "OpenCode Browser"
        }
        background = @{
            service_worker = "background.js"
            type = "module"
        }
        description = "Browser automation for OpenCode via Native Messaging"
        host_permissions = @("<all_urls>")
        icons = @{
            "16" = "icons/icon16.png"
            "48" = "icons/icon48.png"
            "128" = "icons/icon128.png"
        }
        manifest_version = 3
        name = "OpenCode Browser Automation"
        permissions = @("tabs","activeTab","scripting","nativeMessaging","storage","notifications","alarms")
        version = "1.0.0"
    }
    path = $extDest
    state = 1
    was_installed_by_default = $false
}

# Add the extension entry
$json.extensions.settings | Add-Member -MemberType NoteProperty -Name $EXT_ID -Value $extEntry -Force

# Also add to extensions.install_sources if it exists, or to extensions.last_chrome_version
# Need to ensure the extension ID appears in any "pinned" or ordering arrays if present

# Convert back to JSON - use Newtonsoft-style formatting  
# PowerShell ConvertTo-Json adds extra layers, use depth
$newJson = $json | ConvertTo-Json -Depth 20 -Compress

# Chrome expects no BOM, UTF8
[System.IO.File]::WriteAllText($securePrefs, $newJson, [System.Text.UTF8Encoding]::new($false))
Write-Host "Extension injected into Secure Preferences"
Write-Host "Extension ID: $EXT_ID"
Write-Host "Path: $extDest"

# Verify the injection
Write-Host ""
Write-Host "Verifying injection..."
$verify = Get-Content $securePrefs -Raw | ConvertFrom-Json
if ($verify.extensions.settings.$EXT_ID) {
    Write-Host "SUCCESS: Extension $EXT_ID found in Secure Preferences"
} else {
    Write-Host "WARNING: Extension $EXT_ID NOT found after injection!"
}

Write-Host ""
Write-Host "========================================"
Write-Host "STEP 4: Restart Edge with extension loaded"
Write-Host "========================================"

# Build restart arguments - use the essential flags from the OMP profile
$args = @(
    "--user-data-dir=`"$OMP_PROFILE`"",
    "--load-extension=`"$EXT_SRC`"",
    "--disable-background-networking",
    "--disable-background-timer-throttling",
    "--disable-backgrounding-occluded-windows",
    "--disable-breakpad",
    "--disable-crash-reporter",
    "--disable-dev-shm-usage",
    "--disable-hang-monitor",
    "--disable-infobars",
    "--disable-prompt-on-repost",
    "--disable-renderer-backgrounding",
    "--disable-search-engine-choice-screen",
    "--disable-sync",
    "--enable-automation",
    "--export-tagged-pdf",
    "--force-color-profile=srgb",
    "--generate-pdf-document-outline",
    "--no-first-run",
    "--password-store=basic",
    "--use-mock-keychain",
    "--disable-features=Translate,AcceptCHFrame,MediaRouter,OptimizationHints,WebUIReloadButton,ProcessPerSiteUpToMainFrameThreshold,IsolateSandboxedIframes",
    "--enable-features=PdfOopif",
    "--headless=new",
    "--hide-scrollbars",
    "--mute-audio",
    "about:blank",
    "--no-sandbox",
    "--disable-extensions-except=`"$EXT_SRC`"",
    "--remote-debugging-port=0"
)

$argString = $args -join " "
Write-Host "Launching: $EDGE_EXE $argString"

$proc = Start-Process -FilePath $EDGE_EXE -ArgumentList $argString -PassThru -WindowStyle Hidden
Write-Host "Edge started with PID: $($proc.Id)"

Write-Host ""
Write-Host "Waiting for Edge to initialize (10 seconds)..."
Start-Sleep -Seconds 10

Write-Host ""
Write-Host "========================================"
Write-Host "STEP 5: Verify"
Write-Host "========================================"

# Check process
$edgeProcs = Get-Process msedge -ErrorAction SilentlyContinue
if ($edgeProcs) {
    Write-Host "SUCCESS: msedge is running ($($edgeProcs.Count) processes)"
} else {
    Write-Host "ERROR: msedge NOT running!"
}

# Check host.log
Write-Host ""
Write-Host "=== host.log (last 15 lines) ==="
$logFile = "C:\Users\nguye\.opencode-browser\logs\host.log"
if (Test-Path $logFile) {
    Get-Content $logFile -Tail 15
} else {
    Write-Host "host.log not found"
}

Write-Host ""
Write-Host "========================================"
Write-Host "FIX COMPLETE"
Write-Host "========================================"
