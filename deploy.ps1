<#
.SYNOPSIS
  One-shot full deploy for MADAuthor.

  Builds + uploads the API and Angular SPA, then forces IIS to recycle the
  API app pool by dropping an app_offline.htm into the API root for a few
  seconds. Kills the "did I remember to recycle in Plesk?" failure mode.

.DESCRIPTION
  Thin wrapper around deploy\Deploy-Ftp.ps1. Forwards args:
      .\deploy.ps1                  # full deploy + recycle
      .\deploy.ps1 -ApiOnly         # API only + recycle
      .\deploy.ps1 -FeOnly          # FE only (no recycle - static files)
      .\deploy.ps1 -SkipBuild       # reuse staging
      .\deploy.ps1 -SkipRecycle     # don't touch app_offline.htm

  After the inner deploy succeeds AND the API side was uploaded, this:
    1. PUTs app_offline.htm into the API root via FTPS.
    2. Sleeps 3 seconds so IIS notices and unloads the app domain.
    3. DELETEs app_offline.htm so the next request triggers a clean re-init
       with the fresh DLLs.
    4. Warms the new worker by hitting /api/health/ready.
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [switch]$SkipBuild,
    [switch]$ApiOnly,
    [switch]$FeOnly,
    [switch]$SkipRecycle
)

$ErrorActionPreference = 'Stop'

# ---- Forward to the inner deploy ----------------------------------------
$innerScript = Join-Path $PSScriptRoot 'deploy\Deploy-Ftp.ps1'
if (-not (Test-Path $innerScript)) {
    throw "Couldn't find $innerScript. Are you running from the repo root?"
}

$forward = @{}
if ($SkipBuild) { $forward['SkipBuild'] = $true }
if ($ApiOnly)   { $forward['ApiOnly']   = $true }
if ($FeOnly)    { $forward['FeOnly']    = $true }
if ($WhatIfPreference) { $forward['WhatIf'] = $true }

& $innerScript @forward
if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) {
    throw "Inner deploy failed (exit $LASTEXITCODE)."
}

# ---- Force IIS app pool recycle ----------------------------------------
if ($FeOnly -or $SkipRecycle -or $WhatIfPreference) {
    Write-Host ""
    Write-Host "==> Skipping API recycle (FE-only, -SkipRecycle, or -WhatIf)." -ForegroundColor Yellow
    exit 0
}

# Re-load .env to get the FTP creds the recycle step needs.
$envFile = Join-Path $PSScriptRoot '.env'
if (-not (Test-Path $envFile)) { throw "Couldn't find $envFile." }

$envVars = @{}
Get-Content $envFile | ForEach-Object {
    $line = $_.Trim()
    if ($line -and -not $line.StartsWith('#') -and $line.Contains('=')) {
        $kv = $line -split '=', 2
        $envVars[$kv[0].Trim()] = $kv[1].Trim().Trim('"').Trim("'")
    }
}

$apiHost = $envVars['API_FTP_HOST']
$apiUser = $envVars['API_FTP_USER']
$apiPass = $envVars['API_FTP_PASS']
$apiPath = $envVars['API_FTP_PATH']
if (-not $apiHost -or -not $apiUser -or -not $apiPass -or -not $apiPath) {
    Write-Host "==> Skipping recycle: API_FTP_* vars not all set in .env." -ForegroundColor Yellow
    exit 0
}
if (-not $apiPath.StartsWith('/')) { $apiPath = "/$apiPath" }
if (-not $apiPath.EndsWith('/'))   { $apiPath = "$apiPath/" }

$curl = (Get-Command curl.exe).Source
$tempOffline = Join-Path $env:TEMP "madauthor_app_offline.htm"

# Plain-ASCII here-string. Closing '@ MUST be at column 0 with nothing else
# on the line; the pipe goes on the NEXT line. This is a Windows PowerShell 5.1
# parser requirement.
$offlineHtml = @'
<!doctype html>
<title>Updating</title>
<h1>MADAuthor is updating</h1>
<p>This page will refresh in a moment.</p>
'@
Set-Content -Path $tempOffline -Value $offlineHtml -Encoding UTF8

$remoteUrl = "ftp://${apiHost}${apiPath}app_offline.htm"

Write-Host ""
Write-Host "==> Forcing API app pool recycle via app_offline.htm" -ForegroundColor Cyan

# 1. PUT app_offline.htm so IIS sees it and unloads the app domain.
& $curl --silent --show-error --ssl-reqd --insecure --ftp-create-dirs `
        --user "${apiUser}:${apiPass}" `
        --upload-file $tempOffline $remoteUrl
if ($LASTEXITCODE -ne 0) {
    Write-Host "    ! Could not upload app_offline.htm (exit $LASTEXITCODE). Recycle manually in Plesk." -ForegroundColor Yellow
    exit 0
}
Write-Host "    + app_offline.htm uploaded - IIS unloading app domain."

# 2. Let IIS notice. 3 seconds is overkill but cheap insurance.
Start-Sleep -Seconds 3

# 3. DELETE app_offline.htm so the next request reloads with the new DLLs.
& $curl --silent --show-error --ssl-reqd --insecure `
        --user "${apiUser}:${apiPass}" `
        --quote "DELE ${apiPath}app_offline.htm" `
        "ftp://${apiHost}/" | Out-Null
if ($LASTEXITCODE -ne 0) {
    Write-Host "    ! Could not delete app_offline.htm (exit $LASTEXITCODE). Site will appear offline until you remove it manually!" -ForegroundColor Red
    exit 1
}
Write-Host "    + app_offline.htm removed - site is live with new DLLs."

# 4. Warm the worker process. Best-effort; tolerate cert / DNS failures.
$publicHost = 'madauthorapi.madproducts.co.za'
$healthUrl = "https://$publicHost/api/health/ready"
try {
    $resp = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 15 -ErrorAction Stop
    Write-Host "    + $healthUrl -> $($resp.StatusCode)"
} catch {
    Write-Host "    ! Could not warm $healthUrl ($($_.Exception.Message)). First request will pay cold-start cost." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "==> Deploy + recycle complete." -ForegroundColor Green
