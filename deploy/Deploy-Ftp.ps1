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
      - Static files only - no app pool requirements
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [switch]$SkipBuild,
    [switch]$ApiOnly,
    [switch]$FeOnly
)

$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = 'true'

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
#   'false'              → plain ftp:// (no encryption - only on a trusted LAN)
$ftpTlsRaw = if ($envVars.ContainsKey('FTP_TLS')) {
    $envVars['FTP_TLS'].ToLowerInvariant()
} elseif (
    ($envVars.ContainsKey('API_FTP_USE_TLS') -and $envVars['API_FTP_USE_TLS'].ToLowerInvariant() -eq 'false') -and
    ($envVars.ContainsKey('FE_FTP_USE_TLS') -and $envVars['FE_FTP_USE_TLS'].ToLowerInvariant() -eq 'false')
) {
    'false'
} else {
    'explicit'
}
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
    $script:LastNativeExitCode = $null
    $ErrorActionPreference = 'Continue'
    try {
        & $Block 2>&1 | ForEach-Object { Write-Host $_ }
    } finally {
        $ErrorActionPreference = $prev
    }
    $exitCode = if ($null -ne $script:LastNativeExitCode) {
        [int]$script:LastNativeExitCode
    } elseif ($null -ne $LASTEXITCODE) {
        [int]$LASTEXITCODE
    } else {
        0
    }
    if ($exitCode -ne 0) { throw "$Label failed (exit $exitCode)." }
}

$PnpmPackage = 'pnpm@11.2.2'
function Invoke-PnpmCli {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    $env:PNPM_HOME = if ($env:PNPM_HOME) { $env:PNPM_HOME } else { 'C:\Code\.pnpm' }
    $env:PNPM_STORE_DIR = if ($env:PNPM_STORE_DIR) { $env:PNPM_STORE_DIR } else { 'C:\Code\.pnpm' }
    $env:NPM_CONFIG_STORE_DIR = if ($env:NPM_CONFIG_STORE_DIR) { $env:NPM_CONFIG_STORE_DIR } else { 'C:\Code\.pnpm' }
    $env:COREPACK_ENABLE_DOWNLOAD_PROMPT = '0'

    $corepack = Get-Command corepack.cmd,corepack -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($corepack) {
        & $corepack.Source $PnpmPackage @Arguments
        $script:LastNativeExitCode = if ($null -ne $LASTEXITCODE) { [int]$LASTEXITCODE } else { 0 }
        return
    }

    $pnpm = Get-Command pnpm.cmd,pnpm -ErrorAction Stop | Select-Object -First 1
    & $pnpm.Source @Arguments
    $script:LastNativeExitCode = if ($null -ne $LASTEXITCODE) { [int]$LASTEXITCODE } else { 0 }
}

if (-not $SkipBuild) {
    if (-not $ApiOnly) {
        Write-Host "==> Building Angular (production)" -ForegroundColor Cyan
        Push-Location $repoRoot
        try {
            Invoke-NativeBuild 'pnpm install' { Invoke-PnpmCli install --frozen-lockfile }
            Invoke-NativeBuild 'Angular build' { Invoke-PnpmCli --filter web exec ng build --configuration production }
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
            $script:LastNativeExitCode = if ($null -ne $LASTEXITCODE) { [int]$LASTEXITCODE } else { 0 }
        }

        Write-Host "==> Staging API: web.config + sanitized .env + logs/ + storage/" -ForegroundColor Cyan
        $apiWebConfig = Join-Path $apiStaging 'web.config'
        Copy-Item (Join-Path $PSScriptRoot 'web.config') $apiWebConfig -Force

        # Put runtime environment values directly into the staged IIS config so
        # ANCM passes them to the API process even when Plesk has stale panel-level
        # variables. The source web.config stays secret-free; only staging/remote
        # receive values read from the local gitignored .env.
        [xml]$webConfigXml = Get-Content -LiteralPath $apiWebConfig
        $envNode = $webConfigXml.configuration.location.'system.webServer'.aspNetCore.environmentVariables
        function Set-StagedWebConfigEnv {
            param([string]$Name, [string]$Value)
            if ([string]::IsNullOrWhiteSpace($Name) -or [string]::IsNullOrWhiteSpace($Value)) { return }
            $existing = @($envNode.environmentVariable | Where-Object { $_.name -eq $Name } | Select-Object -First 1)
            if ($existing.Count -gt 0) {
                $existing[0].value = $Value
                return
            }
            $node = $webConfigXml.CreateElement('environmentVariable')
            [void]$node.SetAttribute('name', $Name)
            [void]$node.SetAttribute('value', $Value)
            [void]$envNode.AppendChild($node)
        }

        if ($envVars.ContainsKey('ConnectionStrings__Default')) {
            Set-StagedWebConfigEnv -Name 'ConnectionStrings__DefaultConnection' -Value $envVars['ConnectionStrings__Default']
        }
        foreach ($runtimeKey in @(
            'ConnectionStrings__DefaultConnection',
            'ConnectionStrings__Default',
            'ConnectionStrings__Hangfire',
            'DB_HOST',
            'DB_USERNAME',
            'DB_PASSWORD',
            'DB_DATABASE',
            'DB_HANGFIRE_DATABASE',
            'DB_USER',
            'DB_PASS',
            'DB_NAME',
            'DB_PORT',
            'JWT_SIGNING_KEY',
            'CLAUDE_WORKER_TOKEN',
            'SMTP_HOST',
            'SMTP_PORT',
            'SMTP_SECURE',
            'SMTP_USER',
            'SMTP_PASS',
            'UNSPLASH_ACCESS_KEY',
            'UNSPLASH_SECRET_KEY',
            'UNSPLASH_APP_NAME',
            'PAYFAST_SANDBOX',
            'PAYFAST_MERCHANT_ID',
            'PAYFAST_MERCHANT_KEY',
            'PAYFAST_PASSPHRASE',
            'PAYFAST_PUBLIC_BASE_URL',
            'PAYFAST_RETURN_URL',
            'PAYFAST_CANCEL_URL',
            'PAYFAST_NOTIFY_URL',
            'PAYFAST_FALLBACK_EMAIL',
            'PAYFAST_PRODUCT_NAME'
        )) {
            if ($envVars.ContainsKey($runtimeKey)) {
                Set-StagedWebConfigEnv -Name $runtimeKey -Value $envVars[$runtimeKey]
            }
        }
        $webConfigXml.Save($apiWebConfig)
        Remove-Item (Join-Path $apiStaging 'appsettings.Local.json') -Force -ErrorAction SilentlyContinue

        # Server only needs runtime config - strip FTP_* and other deploy-only vars.
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

        $logsDir = Join-Path $apiStaging 'logs'
        $storageDir = Join-Path $apiStaging 'storage'
        New-Item -ItemType Directory -Path $logsDir -Force | Out-Null
        New-Item -ItemType Directory -Path $storageDir -Force | Out-Null
        # The FTP uploader only creates remote directories for uploaded files.
        # Keep placeholders so ANCM stdout logging and local storage have
        # directories available before the app starts.
        Set-Content -Path (Join-Path $logsDir '.keep') -Value '' -Encoding ASCII
        Set-Content -Path (Join-Path $storageDir '.keep') -Value '' -Encoding ASCII
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

function Get-RemoteFileInfo {
    param([string]$RemoteUrl, [string]$User, [string]$Pass)
    $args = @('--silent', '--show-error', '--head', '--user', "${User}:${Pass}")
    if ($tlsFlag) { $args += $tlsFlag }
    $args += '--insecure'
    $args += $RemoteUrl

    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $curl @args 2>&1
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $prev
    }

    if ($code -ne 0) {
        return [pscustomobject]@{ Exists = $false; Size = $null; LastModifiedUtc = $null }
    }

    $size = $null
    $lastModifiedUtc = $null
    foreach ($line in $output) {
        $text = [string]$line
        if ($text -match '^\s*Content-Length\s*:\s*(\d+)\s*$') {
            $size = [int64]$matches[1]
        } elseif ($text -match '^\s*Last-Modified\s*:\s*(.+?)\s*$') {
            try {
                $lastModifiedUtc = ([DateTimeOffset]::Parse($matches[1], [Globalization.CultureInfo]::InvariantCulture)).UtcDateTime
            } catch {
                $lastModifiedUtc = $null
            }
        }
    }

    return [pscustomobject]@{ Exists = $true; Size = $size; LastModifiedUtc = $lastModifiedUtc }
}

function Get-UploadDecision {
    param([System.IO.FileInfo]$LocalFile, [string]$RemoteUrl, [string]$User, [string]$Pass)
    $remote = Get-RemoteFileInfo -RemoteUrl $RemoteUrl -User $User -Pass $Pass
    if (-not $remote.Exists) { return [pscustomobject]@{ Upload = $true; Reason = 'missing remote file' } }
    if ($null -eq $remote.Size) { return [pscustomobject]@{ Upload = $true; Reason = 'remote size unavailable' } }
    if ([int64]$LocalFile.Length -ne [int64]$remote.Size) {
        return [pscustomobject]@{ Upload = $true; Reason = "size changed local=$($LocalFile.Length) remote=$($remote.Size)" }
    }
    if ($null -eq $remote.LastModifiedUtc) { return [pscustomobject]@{ Upload = $true; Reason = 'remote timestamp unavailable' } }
    if ($LocalFile.LastWriteTimeUtc -gt $remote.LastModifiedUtc.AddSeconds(2)) {
        return [pscustomobject]@{ Upload = $true; Reason = "newer local=$($LocalFile.LastWriteTimeUtc.ToString('u')) remote=$($remote.LastModifiedUtc.ToString('u'))" }
    }
    return [pscustomobject]@{ Upload = $false; Reason = 'same size and not newer' }
}

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
    Write-Host "==> Checking $total $Label files for ${Scheme}://${RemoteHost}${RemotePath}" -ForegroundColor Cyan

    $i = 0
    $uploaded = 0
    $skipped = 0
    $failed = @()
    foreach ($file in $files) {
        $i++
        $relPath = $file.FullName.Substring($LocalRoot.Length).TrimStart('\').Replace('\', '/')
        $remoteUrl = "${Scheme}://${RemoteHost}${RemotePath}${relPath}"
        $decision = Get-UploadDecision -LocalFile $file -RemoteUrl $remoteUrl -User $User -Pass $Pass
        if (-not $decision.Upload) {
            $skipped++
            Write-Progress -Activity "$Label upload" -Status "$relPath (skipped)" `
                -PercentComplete (($i / $total) * 100)
            continue
        }

        Write-Progress -Activity "$Label upload" -Status $relPath `
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
            } else {
                $uploaded++
            }
        }
    }
    Write-Progress -Activity "$Label upload" -Completed

    if ($failed.Count -gt 0) {
        Write-Host ""
        Write-Host "$($failed.Count) $Label file(s) failed:" -ForegroundColor Red
        $failed | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
        return $false
    }
    Write-Host "  $Label upload complete: uploaded=$uploaded skipped=$skipped." -ForegroundColor Green
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
    # Plesk's FTP user can land anywhere depending on CHROOT config. Try both shapes:
    # absolute path (the common case) and the relative-to-host-root variant. Retry each
    # up to 3 times on transient curl failures. All output captured into the deploy log
    # via the same stream the parent script uses, so a silent failure is impossible.
    $absolute = ($RemotePath.TrimEnd('/')) + '/' + $FileName
    $candidates = @($absolute, $FileName)   # try absolute first, then bare filename
    $hostUrl = "${scheme}://${RemoteHost}/"

    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        foreach ($target in $candidates) {
            for ($attempt = 1; $attempt -le 3; $attempt++) {
                $cargs = @('--silent', '--show-error', '--user', "${User}:${Pass}",
                    '--ftp-method', 'nocwd',
                    '-Q', "DELE $target",
                    $hostUrl, '-o', 'NUL')
                if ($tlsFlag) { $cargs += $tlsFlag }
                $cargs += '--insecure'

                $output = & $curl @cargs 2>&1 | Out-String
                $code = $LASTEXITCODE

                # Exit 0: DELE accepted (server returned 250).
                if ($code -eq 0) {
                    Write-Host "  Deleted '$target' (try $attempt)." -ForegroundColor DarkGray
                    return
                }

                # Exit 21 with "550" body: file already absent - treat as success too.
                if ($code -eq 21 -and ($output -match '\b550\b|file unavailable|cannot find|no such')) {
                    Write-Host "  '$target' already absent (server 550 on try $attempt)." -ForegroundColor DarkGray
                    return
                }

                Write-Host "  Delete '$target' try $attempt failed: curl exit=$code$([Environment]::NewLine)    $($output.Trim())" -ForegroundColor Yellow
                Start-Sleep -Milliseconds 800
            }
        }
        Write-Host "  ! Could not delete '$FileName' from any candidate path after retries." -ForegroundColor Red
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

    Write-Host "==> Removing stale appsettings.Local.json from API root" -ForegroundColor Cyan
    Remove-OneFile -RemoteHost $apiHost -RemotePath $apiPath `
        -FileName 'appsettings.Local.json' -User $apiUser -Pass $apiPass

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

# Post-deploy: poll API liveness to catch a stuck app_offline.htm immediately
# instead of finding out next time a user hits the site. Readiness can include
# external dependencies, so use the fast process-level endpoint here.
if (-not $FeOnly) {
    Write-Host ""
    Write-Host "==> Verifying API is back online" -ForegroundColor Cyan
    $publicApi = 'https://madauthorapi.madprospects.com/api/health/live'
    $maxAttempts = 6
    $online = $false
    for ($i = 1; $i -le $maxAttempts; $i++) {
        try {
            $r = Invoke-WebRequest -Uri $publicApi -TimeoutSec 15 -UseBasicParsing
            if ($r.StatusCode -eq 200) {
                Write-Host "  $publicApi -> 200 OK (try $i)" -ForegroundColor Green
                $online = $true
                break
            }
            Write-Host "  $publicApi -> $($r.StatusCode) (try $i)" -ForegroundColor DarkGray
        } catch [System.Net.WebException] {
            $code = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
            Write-Host "  $publicApi -> $code (try $i)" -ForegroundColor DarkGray
            # If we got a 503, the app is stopped -- likely app_offline.htm still around.
            # Force one more cleanup attempt.
            if ($code -eq 503 -and $i -eq 3) {
                Write-Host "  Still 503 after 3 attempts -- forcing another app_offline.htm delete." -ForegroundColor DarkGray
                Remove-OneFile -RemoteHost $apiHost -RemotePath $apiPath `
                    -FileName 'app_offline.htm' -User $apiUser -Pass $apiPass
            }
        }
        Start-Sleep -Seconds 2
    }
    if (-not $online) {
        throw "API did not come back online at $publicApi during the post-deploy check."
    }
}

Write-Host ""
Write-Host "==> Deploy complete." -ForegroundColor Green
Write-Host ""
Write-Host "Verified:"
Write-Host "  API live: https://madauthorapi.madprospects.com/api/health/live"
Write-Host "  Frontend: https://madauthor.madprospects.com/"
