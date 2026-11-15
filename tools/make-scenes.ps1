# Renders a still preview of every live shell background (shell\bg-<design>.html, each palette) with a headless Chromium browser,
# for the scene tiles in the Wallpapers library: assets\scenes\<design>-<palette>.png
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'assets\scenes'
New-Item -ItemType Directory $out -Force | Out-Null
$browser = @("$env:ProgramFiles\Microsoft\Edge\Application\msedge.exe", "${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe",
             "$env:ProgramFiles\Google\Chrome\Application\chrome.exe", "${env:ProgramFiles(x86)}\Google\Chrome\Application\chrome.exe",
             "$env:LOCALAPPDATA\Google\Chrome\Application\chrome.exe", "$env:ProgramFiles\BraveSoftware\Brave-Browser\Application\brave.exe") |
           Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $browser) { throw 'No Chromium browser (Edge, Chrome or Brave) found' }
$profile = Join-Path $env:TEMP 'tc-scenes-profile'
$scenes = [ordered]@{ sharmaji = @('sharmaji', 'transcrypto', 'alien'); orbital = @('aurora', 'lagoon', 'ember'); projector = @('platinum', 'solar', 'rosso') }
foreach ($design in $scenes.Keys) {
    $page = 'file:///' + ((Join-Path $root "shell\bg-$design.html") -replace '\\', '/')
    foreach ($pal in $scenes[$design]) {
        $png = Join-Path $out "$design-$pal.png"
        # Start-Process: Chromium writes log noise to stderr, which PowerShell 5.1 would turn into terminating errors
        $argList = @('--headless=new', '--hide-scrollbars', '--no-first-run', "--user-data-dir=`"$profile`"", '--window-size=960,540',
                     '--virtual-time-budget=3000', "--screenshot=`"$png`"", "`"$($page)?pal=$pal`"")
        Start-Process -FilePath $browser -ArgumentList $argList -Wait -WindowStyle Hidden
        if (Test-Path $png) { "ok  $design-$pal" } else { "FAILED $design-$pal" }
    }
}
