<#
.SYNOPSIS
  Build MADAuthor and FTP-deploy to two Plesk/IIS sites:
    - .NET API publish output → $API_FTP_PATH
    - Angular dist (browser)  → $FE_FTP_PATH

.DESCRIPTION
  Reads FTP credentials from C:\Code\madauthor\.env:
    API_FTP_HOST / API_FTP_USER / API_FTP_PASS / API_FTP_PATH
    FE_FTP_HOST  / FE_FTP_USER  / FE_FTP_PASS  / FE_FTP_PATH
    FTP_TLS (optional, default true → ftps://)

  Pipeline:
    1. ng build --configuration production  → dist/web/browser/
    2. dotnet publish (win-x64, framework-dependent) → .deploy/staging/api/
    3. Copies web.config (API hosting) + sanitized .env into API staging.
    4. Copies a SPA-fallback web.config into FE staging next to dist/.
    5. Uploads each staging tree to its FTP target via curl.exe.

  -SkipBuild      : reuse last staged outputs.
  -ApiOnly / -FeOnly : upload only one side (handy when iterating).
  -WhatIf         : dry-run, log what would upload.

.NOTES
  Host prerequisites (one-time):
    API site:
      - .NET 8.0 Hosting Bundle installed
      - App pool: No Managed Code
      - App pool identity has read/write on the site folder (storage/, logs/)
    FE site:
      - URL Rewrite module installed (web.config falls back to index.html)
      - Static files only — no app pool requirements
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [switch]$SkipBuild,
    [switch]$ApiOnly,
    [switch]$FeOnly
)

$ErrorActionPreference = 'Stop'

# ---- Paths --------------------------------------------------------------
$repoRoot    = Resolve-Path (Join-Path $PSScriptRoot '..')
$apiProj     = Join-Path $repoRoot 'apps\api\MadAuthor.Api\MadAuthor.Api.csproj'
$webDir      = Join-Path $repoRoot 'apps\web'
$webDist     = Join-Path $webDir 'dist\web\browser'
$apiStaging  = Join-Path $repoRoot '.deploy\staging\api'
$feStaging   = Join-Path $repoRoot '.deploy\staging\fe'
$envFile     = Join-Path $repoRoot '.env'

# ---- Load .env -----------------------------------------------------------
if (-not (Test-Path $envFile)) {
    throw "Couldn't find $envFile."
}

$envVars = @{}
Get-Content $envFile | ForEach-Object {
    $line = $_.Trim()
    if ($line -and -not $line.StartsWith('#') -and $line.Contains('=')) {
        $kv = $line -split '=', 2
        $envVars[$kv[0].Trim()] = $kv[1].Trim().Trim('"').Trim("'")
    }
}

function Require-Var($name) {
    if (-not $envVars.ContainsKey($name) -or [string]::IsNullOrWhiteSpace($envVars[$name])) {
        throw "Required variable '$name' is missing from .env."
    }
    return $envVars[$name]
}

$apiHost = Require-Var 'API_FTP_HOST'
$apiUser = Require-Var 'API_FTP_USER'
$apiPass = Require-Var 'API_FTP_PASS'
$apiPath = Require-Var 'API_FTP_PATH'

$feHost  = Require-Var 'FE_FTP_HOST'
$feUser  = Require-Var 'FE_FTP_USER'
$fePass  = Require-Var 'FE_FTP_PASS'
$fePath  = Require-Var 'FE_FTP_PATH'

# FTP_TLS values (default 'explicit' which is what Plesk uses on port 21):
#   'explicit' / 'true'  → ftp://  + --ssl-reqd  (port 21, AUTH TLS upgrade)
#   'implicit'           → ftps:// (port 990, old-style, rare)
#   'false'              → plain ftp:// (no encryption — only on a trusted LAN)
$ftpTlsRaw = if ($envVars.ContainsKey('FTP_TLS')) { $envVars['FTP_TLS'].ToLowerInvariant() } else { 'explicit' }
switch ($ftpTlsRaw) {
    'implicit' { $scheme = 'ftps'; $tlsFlag = $null }
    'false'    { $scheme = 'ftp';  $tlsFlag = $null }
    default    { $scheme = 'ftp';  $tlsFlag = '--ssl-reqd' }   # explicit / true / anything else
}

function Normalize-Path($p) {
    if (-not $p.StartsWith('/')) { $p = "/$p" }
    if (-not $p.EndsWith('/')) { $p = "$p/" }
    return $p
}
$apiPath = Normalize-Path $apiPath
$fePath  = Normalize-Path $fePath

# ---- Build ----------------------------------------------------------------
# Helper: run a native command (node / dotnet) with stderr captured to stdout so PowerShell 5.1
# doesn't treat stderr lines as terminating errors under $ErrorActionPreference='Stop'.
function Invoke-NativeBuild {
    param([string]$Label, [scriptblock]$Block)
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $Block 2>&1 | ForEach-Object { Write-Host $_ }
    } finally {
        $ErrorActionPreference = $prev
    }
    if ($LASTEXITCODE -ne 0) { throw "$Label failed (exit $LASTEXITCODE)." }
}

if (-not $SkipBuild) {
    if (-not $ApiOnly) {
        Write-Host "==> Building Angular (production)" -ForegroundColor Cyan
        Push-Location $webDir
        try {
            $ngCmd = "$env:APPDATA\npm\node_modules\@angular\cli\bin\ng.js"
            if (-not (Test-Path $ngCmd)) {
                throw "Angular CLI not found at $ngCmd. Run: npm install -g @angular/cli@19"
            }
            Invoke-NativeBuild 'Angular build' { node $ngCmd build --configuration production }
        } finally {
            Pop-Location
        }
    }

    if (-not $FeOnly) {
        Write-Host "==> Publishing .NET API (Release, win-x64, framework-dependent)" -ForegroundColor Cyan
        if (Test-Path $apiStaging) { Remove-Item $apiStaging -Recurse -Force }
        New-Item -ItemType Directory -Path $apiStaging | Out-Null

        Invoke-NativeBuild 'dotnet publish' {
            dotnet publish $apiProj `
                --configuration Release `
                --runtime win-x64 `
                --self-contained false `
                --output $apiStaging `
                --nologo `
                /p:PublishSingleFile=false `
                /p:UseAppHost=false
        }

        Write-Host "==> Staging API: web.config + sanitized .env + logs/ + storage/" -ForegroundColor Cyan
        Copy-Item (Join-Path $PSScriptRoot 'web.config') (Join-Path $apiStaging 'web.config') -Force

        # Server only needs runtime config — strip FTP_* and other deploy-only vars.
        $serverEnv = Get-Content $envFile | Where-Object {
            $line = $_.Trim()
            if (-not $line -or $line.StartsWith('#')) { return $true }
            $key = ($line -split '=', 2)[0].Trim()
            return -not (
                $key -like 'API_FTP_*' -or
                $key -like 'FE_FTP_*' -or
                $key -eq 'FTP_TLS' -or
                $key -eq 'WORKER_ID'
            )
        }
        $serverEnv | Set-Content -Path (Join-Path $apiStaging '.env') -Encoding UTF8

        New-Item -ItemType Directory -Path (Join-Path $apiStaging 'logs') -Force | Out-Null
        New-Item -ItemType Directory -Path (Join-Path $apiStaging 'storage') -Force | Out-Null
    }

    if (-not $ApiOnly) {
        Write-Host "==> Staging FE: Angular dist + SPA-fallback web.config" -ForegroundColor Cyan
        if (Test-Path $feStaging) { Remove-Item $feStaging -Recurse -Force }
        New-Item -ItemType Directory -Path $feStaging | Out-Null
        Copy-Item "$webDist\*" $feStaging -Recurse
        Copy-Item (Join-Path $PSScriptRoot 'fe-web.config') (Join-Path $feStaging 'web.config') -Force
    }
} else {
    if ((-not $FeOnly) -and -not (Test-Path $apiStaging)) {
        throw "-SkipBuild set but $apiStaging doesn't exist. Run once without -SkipBuild first."
    }
    if ((-not $ApiOnly) -and -not (Test-Path $feStaging)) {
        throw "-SkipBuild set but $feStaging doesn't exist. Run once without -SkipBuild first."
    }
    Write-Host "==> Skipping build, reusing existing staging." -ForegroundColor Yellow
}

# ---- Upload helper -------------------------------------------------------
$curlCmd = Get-Command curl.exe -ErrorAction SilentlyContinue
if (-not $curlCmd) {
    throw "curl.exe not found on PATH (ships in C:\Windows\System32 on Windows 10+)."
}
$curl = $curlCmd.Source

function Upload-Tree {
    param(
        [string]$LocalRoot,
        [string]$Scheme,
        [string]$RemoteHost,
        [string]$RemotePath,
        [string]$User,
        [string]$Pass,
        [string]$Label
    )
    $files = Get-ChildItem -Path $LocalRoot -Recurse -File
    $total = $files.Count
    Write-Host "==> Uploading $total $Label files to ${Scheme}://${RemoteHost}${RemotePath}" -ForegroundColor Cyan

    $i = 0
    $failed = @()
    foreach ($file in $files) {
        $i++
        $relPath = $file.FullName.Substring($LocalRoot.Length).TrimStart('\').Replace('\', '/')
        $remoteUrl = "${Scheme}://${RemoteHost}${RemotePath}${relPath}"

        Write-Progress -Activity "$Label → $RemoteHost" -Status $relPath `
            -PercentComplete (($i / $total) * 100)

        if ($PSCmdlet.ShouldProcess($remoteUrl, "PUT")) {
            $curlArgs = @(
                '--silent', '--show-error', '--fail', '--ftp-create-dirs',
                '--user', "${User}:${Pass}",
                '--upload-file', $file.FullName
            )
            if ($tlsFlag) { $curlArgs += $tlsFlag }
            # Plesk + many hosts present a self-signed FTPS cert. Trust it for FTPS only
            # (still requires valid login). Remove --insecure if your host has a CA-signed cert.
            $curlArgs += '--insecure'
            $curlArgs += $remoteUrl
            & $curl @curlArgs
            if ($LASTEXITCODE -ne 0) {
                $failed += $relPath
                Write-Host "  X $relPath" -ForegroundColor Red
            }
        }
    }
    Write-Progress -Activity "$Label → $RemoteHost" -Completed

    if ($failed.Count -gt 0) {
        Write-Host ""
        Write-Host "$($failed.Count) $Label file(s) failed:" -ForegroundColor Red
        $failed | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
        return $false
    }
    Write-Host "  $total $Label files uploaded OK." -ForegroundColor Green
    return $true
}

# ---- Helpers for single-file curl FTP ops (used for app_offline.htm) -----
function Push-OneFile {
    param([string]$LocalFile, [string]$RemoteUrl, [string]$User, [string]$Pass)
    $args = @('--silent', '--show-error', '--fail', '--ftp-create-dirs',
        '--user', "${User}:${Pass}", '--upload-file', $LocalFile)
    if ($tlsFlag) { $args += $tlsFlag }
    $args += '--insecure'
    $args += $RemoteUrl
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $curl @args 2>&1 | ForEach-Object { Write-Host "  $_" } } finally { $ErrorActionPreference = $prev }
    return ($LASTEXITCODE -eq 0)
}

function Remove-OneFile {
    param([string]$RemoteHost, [string]$RemotePath, [string]$FileName, [string]$User, [string]$Pass)
    # Plesk's FTP user lands in their CHROOT home, which is NOT the same as the deploy path
    # (e.g. home=/, deploy=/madauthorapi.madproducts.co.za/). `DELE app_offline.htm` against
    # the parent-dir URL silently 550s because it's looking in home. Use the absolute server
    # path so the DELE targets the right file regardless of where the user was placed.
    $absolute = ($RemotePath.TrimEnd('/')) + '/' + $FileName
    $url = "${scheme}://${RemoteHost}/"
    $cargs = @('--silent', '--show-error', '--user', "${User}:${Pass}",
        '-Q', "DELE ${absolute}", $url, '-o', 'NUL')
    if ($tlsFlag) { $cargs += $tlsFlag }
    $cargs += '--insecure'
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $curl @cargs 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  ! Failed to delete $absolute (FTP exit $LASTEXITCODE)" -ForegroundColor Yellow
        }
    } finally { $ErrorActionPreference = $prev }
}

# ---- Upload ---------------------------------------------------------------
$allOk = $true
$apiOfflineStaged = $null

if (-not $FeOnly) {
    # AspNetCoreModuleV2 watches for app_offline.htm in the site root: presence stops the app and
    # releases all file locks (DLLs are otherwise held by the running w3wp.exe). Without this the
    # second deploy in a row hits FTP 550 ("file in use"). Upload first, deploy, delete after.
    $apiOfflineStaged = Join-Path $env:TEMP "madauthor_app_offline.htm"
    @"
<!DOCTYPE html><html><head><title>Deploying…</title></head>
<body style="font-family:sans-serif;color:#444;padding:2rem;">
<h1>MADAuthor is updating</h1>
<p>The API is briefly offline while a new build deploys. Refresh in a minute.</p>
</body></html>
"@ | Set-Content -Path $apiOfflineStaged -Encoding UTF8

    Write-Host "==> Taking API offline (app_offline.htm) so IIS releases DLL locks" -ForegroundColor Cyan
    $offlineUrl = "${scheme}://${apiHost}${apiPath}app_offline.htm"
    $null = Push-OneFile -LocalFile $apiOfflineStaged -RemoteUrl $offlineUrl -User $apiUser -Pass $apiPass
    # IIS needs a beat to notice and shut the worker.
    Start-Sleep -Seconds 4

    $ok = Upload-Tree -LocalRoot $apiStaging -Scheme $scheme `
        -RemoteHost $apiHost -RemotePath $apiPath `
        -User $apiUser -Pass $apiPass -Label 'API'
    if (-not $ok) { $allOk = $false }

    Write-Host "==> Bringing API back online (deleting app_offline.htm)" -ForegroundColor Cyan
    Remove-OneFile -RemoteHost $apiHost -RemotePath $apiPath `
        -FileName 'app_offline.htm' -User $apiUser -Pass $apiPass
}

if (-not $ApiOnly) {
    $ok = Upload-Tree -LocalRoot $feStaging -Scheme $scheme `
        -RemoteHost $feHost -RemotePath $fePath `
        -User $feUser -Pass $fePass -Label 'FE'
    if (-not $ok) { $allOk = $false }
}

if ($apiOfflineStaged -and (Test-Path $apiOfflineStaged)) { Remove-Item $apiOfflineStaged -Force }

if (-not $allOk) { exit 1 }

Write-Host ""
Write-Host "==> Deploy complete." -ForegroundColor Green
Write-Host ""
Write-Host "Post-deploy checklist:" -ForegroundColor Cyan
Write-Host "  1. Recycle the API app pool in Plesk (or wait ~5 min for IIS to pick up the new DLLs)."
Write-Host "  2. https://madauthorapi.madproducts.co.za/api/health/ready  →  should return db: true"
Write-Host "  3. https://madauthor.madproducts.co.za/                     →  Angular SPA"
Write-Host "  4. https://madauthor.madproducts.co.za/books/new            →  refreshing must NOT 404 (URL Rewrite working)"
