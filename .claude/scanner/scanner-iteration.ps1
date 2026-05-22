# MADAuthor codebase scanner -- Task Scheduler entry point.
#
# Task Scheduler fires this hourly. The script invokes a fresh Claude Code
# session with scanner-prompt.md. Claude scans the repo for stubs, TODOs,
# gaps, and bugs, then POSTs new findings to /api/claude-tasks/import-bulk
# so the autonomous worker (separate Task Scheduler entry) picks them up.
#
# This script is READ-ONLY (Claude is instructed not to modify the repo).
# It runs alongside the worker -- both can fire concurrently because:
#   - Scanner only reads + POSTs (no git mutations)
#   - Worker pre-flight bails on uncommitted work, so a worker mid-iteration
#     and a scanner mid-iteration don't fight over the working tree.
#
# Logs to scanner.log next to this script.
#
# ASCII-ONLY: PowerShell 5.1 reads UTF-8-no-BOM as cp1252. Keep this file
# pure ASCII -- no em-dashes, no curly quotes, no non-breaking spaces.

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$ProgressPreference    = 'SilentlyContinue'

$scannerDir = $PSScriptRoot
$repoRoot   = (Resolve-Path (Join-Path $scannerDir '..\..')).Path
$statePath  = Join-Path $scannerDir 'state.json'
$promptPath = Join-Path $scannerDir 'scanner-prompt.md'
$logPath    = Join-Path $scannerDir 'scanner.log'

$claudeBin = "$env:USERPROFILE\.local\bin\claude.exe"

# --- helpers ----------------------------------------------------------------

function Write-Log {
    param([string]$msg)
    $line = '{0:yyyy-MM-ddTHH:mm:ssZ}  {1}' -f (Get-Date).ToUniversalTime(), $msg
    Add-Content -Path $logPath -Value $line -Encoding UTF8
}

# --- main -------------------------------------------------------------------

if (-not (Test-Path $claudeBin)) {
    Write-Log "FATAL claude.exe not found at $claudeBin"
    exit 2
}
if (-not (Test-Path $promptPath)) {
    Write-Log "FATAL scanner-prompt.md not found at $promptPath"
    exit 2
}

Write-Log "START scanner iteration"

$prompt = Get-Content $promptPath -Raw

Push-Location $repoRoot
try {
    # Same invocation pattern as the worker: --print runs headlessly,
    # stdin = prompt, stdout/stderr -> log. --dangerously-skip-permissions
    # lets the scanner run unattended (even though it should only read +
    # POST -- the worker prompt is the actual guard against writes).
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

# Persist lastRanAt so an external monitor (or a status page) can show
# when the scanner last completed.
$state = [pscustomobject]@{
    lastRanAt = (Get-Date).ToUniversalTime().ToString('o')
}
$state | ConvertTo-Json | Out-File $statePath -Encoding utf8 -Force
