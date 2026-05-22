# MADAuthor autonomous worker -- Task Scheduler entry point.
#
# Task Scheduler fires this every minute (the heartbeat). The script:
#   1. Peeks the /claude-tasks/next queue with the worker token.
#   2. If empty, increments the streak counter.
#   3. If a task is waiting, runs the FULL Claude Code worker iteration
#      and resets the streak to 0.
#   4. Records the new streak + lastFiredAt to .claude/worker/state.json.
#   5. Sleeps (via no-op) until the next bucket window if we're still inside it.
#
# Bucket ladder (seconds-since-last-fire required before firing again):
#    streak 0          ->  60   (1 min)
#    streak 1-4        -> 300   (5 min)
#    streak 5-9        -> 600   (10 min)
#    streak 10-14      -> 1800  (30 min)
#    streak 15+        -> 3600  (1 hour)
#
# Reset on any non-empty fetch (back to streak 0 / 1-min cadence).
#
# Token is generated fresh per environment via [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLower() -- store in .env as CLAUDE_WORKER_TOKEN and replace the placeholder below.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ProgressPreference    = 'SilentlyContinue'

$workerDir   = $PSScriptRoot
$repoRoot    = (Resolve-Path (Join-Path $workerDir '..\..')).Path
$statePath   = Join-Path $workerDir 'state.json'
$promptPath  = Join-Path $workerDir 'worker-prompt.md'
$logPath     = Join-Path $workerDir 'worker.log'

$apiBase   = 'https://madauthorapi.madproducts.co.za/api'
$token     = '***REDACTED-CLAUDE-WORKER-TOKEN***'
$claudeBin = "$env:USERPROFILE\.local\bin\claude.exe"

# --- helpers ----------------------------------------------------------------

function Write-Log {
    param([string]$msg)
    $line = '{0:yyyy-MM-ddTHH:mm:ssZ}  {1}' -f (Get-Date).ToUniversalTime(), $msg
    Add-Content -Path $logPath -Value $line -Encoding UTF8
}

function Read-State {
    if (-not (Test-Path $statePath)) {
        return @{ streak = 0; lastFiredAt = [datetime]::MinValue }
    }
    try {
        $raw = Get-Content $statePath -Raw | ConvertFrom-Json
        return @{
            streak      = [int]$raw.streak
            lastFiredAt = if ($raw.lastFiredAt) { [datetime]$raw.lastFiredAt } else { [datetime]::MinValue }
        }
    } catch {
        Write-Log "WARN  malformed state.json, resetting: $($_.Exception.Message)"
        return @{ streak = 0; lastFiredAt = [datetime]::MinValue }
    }
}

function Write-State {
    param([int]$streak, [datetime]$lastFiredAt)
    $obj = [pscustomobject]@{
        streak      = $streak
        lastFiredAt = $lastFiredAt.ToUniversalTime().ToString('o')
    }
    $obj | ConvertTo-Json | Out-File $statePath -Encoding utf8 -Force
}

function BucketSeconds {
    param([int]$streak)
    if ($streak -le 0)  { return 60 }    # 1 min
    if ($streak -le 4)  { return 300 }   # 5 min
    if ($streak -le 9)  { return 600 }   # 10 min
    if ($streak -le 14) { return 1800 }  # 30 min
    return 3600                          # 1 hour
}

# --- main -------------------------------------------------------------------

$state = Read-State
$now   = Get-Date
$bucket = BucketSeconds $state.streak
$secondsSinceFire = if ($state.lastFiredAt -eq [datetime]::MinValue) { [int]::MaxValue } else { ($now - $state.lastFiredAt).TotalSeconds }

# Heartbeat fires every minute. Skip if we're still inside the bucket cooldown.
if ($secondsSinceFire -lt $bucket) {
    Write-Log ("SKIP  streak={0} bucket={1}s elapsed={2:N0}s -- not yet" -f $state.streak, $bucket, $secondsSinceFire)
    exit 0
}

Write-Log ("FIRE  streak={0} bucket={1}s elapsed={2:N0}s -- checking queue" -f $state.streak, $bucket, $secondsSinceFire)

# Cheap peek to decide if a full Claude session is worth invoking.
try {
    $resp = Invoke-WebRequest -Uri "$apiBase/claude-tasks/next" `
        -Headers @{ 'X-Worker-Token' = $token } `
        -Method GET -UseBasicParsing -ErrorAction Stop
    $statusCode = [int]$resp.StatusCode
} catch {
    # Some non-2xx codes throw under Invoke-WebRequest. 204 is a 2xx so it won't.
    $statusCode = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
    Write-Log "WARN  /next call failed: status=$statusCode err=$($_.Exception.Message)"
    # Treat unknown as 'try again next bucket window' -- bump as if empty.
    Write-State -streak ($state.streak + 1) -lastFiredAt $now
    exit 0
}

if ($statusCode -eq 204) {
    $newStreak = $state.streak + 1
    Write-Log "EMPTY queue (204). streak $($state.streak) -> $newStreak. Next bucket: $(BucketSeconds $newStreak)s."
    Write-State -streak $newStreak -lastFiredAt $now
    exit 0
}

# Queue has work -- invoke Claude Code with the worker prompt.
Write-Log "WORK  queue non-empty. Invoking Claude Code worker session..."

Write-State -streak 0 -lastFiredAt $now

if (-not (Test-Path $claudeBin)) {
    Write-Log "FATAL claude.exe not found at $claudeBin"
    exit 2
}
if (-not (Test-Path $promptPath)) {
    Write-Log "FATAL worker-prompt.md not found at $promptPath"
    exit 2
}

$prompt = Get-Content $promptPath -Raw

Push-Location $repoRoot
try {
    # --print runs Claude headlessly: read prompt from stdin, emit final
    # assistant output to stdout, exit when done. We append both stdout and
    # stderr to the log; the actual API queue is the source of truth for
    # what got done.
    #
    # --dangerously-skip-permissions lets the worker run unattended.
    # --add-dir gives the worker access to the repo root.
    $start = Get-Date
    $prompt | & $claudeBin `
        --print `
        --dangerously-skip-permissions `
        --add-dir $repoRoot 2>&1 |
        ForEach-Object { Add-Content -Path $logPath -Value "  $_" -Encoding UTF8 }
    $exit = $LASTEXITCODE
    $elapsed = (Get-Date) - $start
    Write-Log ("DONE  claude exit={0} elapsed={1:N0}s" -f $exit, $elapsed.TotalSeconds)
} finally {
    Pop-Location
}
