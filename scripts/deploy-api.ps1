<#
.SYNOPSIS
  Publish and FTP-deploy MadAuthor.Api to 1-grid Plesk hosting.

.DESCRIPTION
  Reads FTP credentials from .env at the repo root:
    API_FTP_HOST   - FTP host (e.g. 41.185.110.61)
    API_FTP_USER   - FTP username
    API_FTP_PASS   - FTP password
    API_FTP_PATH   - path on the FTP server pointing at the domain folder
                     (e.g. /madaiapi.madproducts.co.za)
    API_FTP_USE_TLS  - optional, "true" to use FTPS (explicit TLS) on port 21

  Workflow:
    1. dotnet publish -c Release -o publish/api
    2. Upload app_offline.htm first (forces IIS to release file locks)
    3. Recursively upload publish output, skipping log/storage runtime data
    4. Delete remote app_offline.htm to bring the API back online

.EXAMPLE
  ./scripts/deploy-api.ps1
  ./scripts/deploy-api.ps1 -NoBuild      # re-upload the existing publish output
  ./scripts/deploy-api.ps1 -DryRun       # show what would be uploaded, do nothing
#>
param(
  [string] $Configuration = 'Release',
  [switch] $NoBuild,
  [switch] $DryRun
)

$ErrorActionPreference = 'Stop'
$ProgressPreference   = 'SilentlyContinue'

$repoRoot   = Resolve-Path (Join-Path $PSScriptRoot '..')
$envFile    = Join-Path $repoRoot '.env'
$csproj     = Join-Path $repoRoot 'apps\api\MadAuthor.Api\MadAuthor.Api.csproj'
$publishDir = Join-Path $repoRoot 'publish\api'

if (-not (Test-Path $envFile)) { throw "Missing .env at $envFile" }
if (-not (Test-Path $csproj))  { throw "Missing csproj at $csproj" }

# ---- read .env ------------------------------------------------------------
$envMap = @{}
foreach ($line in Get-Content $envFile) {
  if ($line -match '^\s*#') { continue }
  if ($line -match '^\s*([A-Z0-9_]+)\s*=\s*(.*)$') {
    $envMap[$Matches[1]] = $Matches[2].Trim('"').Trim()
  }
}

function Req($key) {
  if (-not $envMap.ContainsKey($key) -or [string]::IsNullOrWhiteSpace($envMap[$key])) {
    throw "Required key '$key' missing from .env"
  }
  return $envMap[$key]
}

$ftpHost = Req 'API_FTP_HOST'
$ftpUser = Req 'API_FTP_USER'
$ftpPass = Req 'API_FTP_PASS'
$ftpPath = (Req 'API_FTP_PATH').TrimEnd('/').TrimStart('/')
$useTls  = ($envMap['API_FTP_USE_TLS'] -eq 'true')

Write-Host "Deploy target: ftp://$ftpUser@$ftpHost/$ftpPath  (TLS=$useTls)" -ForegroundColor Cyan

# ---- publish --------------------------------------------------------------
if (-not $NoBuild) {
  if (Test-Path $publishDir) {
    Write-Host "Cleaning $publishDir" -ForegroundColor DarkGray
    Remove-Item -Recurse -Force $publishDir
  }
  Write-Host "Publishing $($csproj | Split-Path -Leaf)..." -ForegroundColor Cyan
  & dotnet publish $csproj -c $Configuration -o $publishDir --nologo
  if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }
}
if (-not (Test-Path $publishDir)) { throw "Publish output not found at $publishDir" }

# ---- exclusions -----------------------------------------------------------
# These directories ship empty - real data lives only on the server.
$excludeDirs  = @('logs', 'storage')

$allFiles = Get-ChildItem $publishDir -Recurse -File | Where-Object {
  $rel = $_.FullName.Substring($publishDir.Length).TrimStart('\','/')
  $first = ($rel -split '[\\/]')[0]
  $excludeDirs -notcontains $first
}
$allDirs  = Get-ChildItem $publishDir -Recurse -Directory | Where-Object {
  $rel = $_.FullName.Substring($publishDir.Length).TrimStart('\','/')
  $first = ($rel -split '[\\/]')[0]
  $excludeDirs -notcontains $first
}

# Local app_offline.htm so it ships as part of the same FTP session
$appOfflineLocal = Join-Path $publishDir 'app_offline.htm'
@'
<!doctype html>
<html><head><meta charset="utf-8"><title>API updating</title>
<style>body{font-family:system-ui;background:#0b1020;color:#fff;text-align:center;padding:5rem}</style>
</head><body><h1>MadAuthor API</h1><p>Deploying a new build - back online shortly.</p></body></html>
'@ | Set-Content -Path $appOfflineLocal -Encoding UTF8

# ---- FTP helpers ----------------------------------------------------------
$scheme = if ($useTls) { 'ftp' } else { 'ftp' }   # FtpWebRequest uses ftp:// for both; EnableSsl=$true switches to FTPS explicit
function Get-Uri([string] $rel) {
  $r = $rel.Replace('\','/').TrimStart('/')
  return "${scheme}://$ftpHost/$ftpPath/$r"
}
function New-FtpRequest([string] $uri, [string] $method) {
  $req = [System.Net.FtpWebRequest]::Create($uri)
  $req.Credentials = New-Object System.Net.NetworkCredential($ftpUser, $ftpPass)
  $req.Method = $method
  $req.UsePassive = $true
  $req.KeepAlive = $false
  $req.UseBinary = $true
  $req.EnableSsl = $useTls
  $req.Timeout = 60000
  return $req
}
function Send-Dir([string] $rel) {
  $uri = Get-Uri $rel
  if ($DryRun) { Write-Host "[DRY MKDIR] $rel" -ForegroundColor DarkGray; return }
  try {
    $req = New-FtpRequest $uri ([System.Net.WebRequestMethods+Ftp]::MakeDirectory)
    $req.GetResponse().Close()
  } catch [System.Net.WebException] {
    $resp = $_.Exception.Response -as [System.Net.FtpWebResponse]
    if ($null -eq $resp) { throw }
    # 550 = directory already exists or path issue - treat as benign for MKD.
    if ($resp.StatusCode -ne [System.Net.FtpStatusCode]::ActionNotTakenFileUnavailable) {
      throw
    }
  }
}
function Send-File([string] $localPath, [string] $rel) {
  $uri = Get-Uri $rel
  if ($DryRun) { Write-Host "[DRY PUT] $rel" -ForegroundColor DarkGray; return }
  $req = New-FtpRequest $uri ([System.Net.WebRequestMethods+Ftp]::UploadFile)
  $bytes = [System.IO.File]::ReadAllBytes($localPath)
  $req.ContentLength = $bytes.Length
  $stream = $req.GetRequestStream()
  try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
  $resp = $req.GetResponse(); $resp.Close()
}
function Remove-FtpFile([string] $rel) {
  $uri = Get-Uri $rel
  if ($DryRun) { Write-Host "[DRY DEL] $rel" -ForegroundColor DarkGray; return }
  try {
    $req = New-FtpRequest $uri ([System.Net.WebRequestMethods+Ftp]::DeleteFile)
    $req.GetResponse().Close()
  } catch [System.Net.WebException] {
    $resp = $_.Exception.Response -as [System.Net.FtpWebResponse]
    if ($resp -and $resp.StatusCode -eq [System.Net.FtpStatusCode]::ActionNotTakenFileUnavailable) {
      return  # already gone
    }
    throw
  }
}

# ---- upload ---------------------------------------------------------------
Write-Host "`nUploading app_offline.htm first to stop running app..." -ForegroundColor Yellow
Send-File $appOfflineLocal 'app_offline.htm'
Start-Sleep -Seconds 3

Write-Host "Creating directories..." -ForegroundColor Cyan
foreach ($d in $allDirs) {
  $rel = $d.FullName.Substring($publishDir.Length + 1)
  Send-Dir $rel
}

$total = $allFiles.Count
$i = 0
$swatch = [System.Diagnostics.Stopwatch]::StartNew()
Write-Host "Uploading $total files..." -ForegroundColor Cyan
foreach ($f in $allFiles) {
  $i++
  $rel = $f.FullName.Substring($publishDir.Length + 1)
  if ($rel -eq 'app_offline.htm') { continue }  # already uploaded
  $pct = [math]::Round(($i / $total) * 100)
  Write-Host ("[{0,3}%] {1,5}/{2,-5} {3}" -f $pct, $i, $total, $rel)
  try { Send-File $f.FullName $rel }
  catch { Write-Warning "Upload failed for $rel : $_"; throw }
}
$swatch.Stop()
Write-Host ("Upload complete in {0:N1}s" -f $swatch.Elapsed.TotalSeconds) -ForegroundColor Green

Write-Host "Removing remote app_offline.htm - API coming back online..." -ForegroundColor Yellow
Remove-FtpFile 'app_offline.htm'

Write-Host "`nDone." -ForegroundColor Green
Write-Host "Smoke test:  https://madaiapi.madproducts.co.za/swagger" -ForegroundColor Cyan
Write-Host "Health:      https://madaiapi.madproducts.co.za/health" -ForegroundColor Cyan
