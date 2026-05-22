# MADAuthor /claude scheduler registration -- one-shot install script.
# ASCII-only.
#
# Registers two Windows Task Scheduler entries:
#   - MADAuthorClaudeWorker  : every 1 minute, adaptive backoff handled by the script itself.
#   - MADAuthorClaudeScanner : every 1 hour, fixed cadence.
#
# Both run as the currently-logged-on interactive user with Limited run level.
# MultipleInstances=IgnoreNew prevents concurrent fires when a slow iteration spans
# multiple heartbeats. ExecutionTimeLimit caps a runaway iteration at 1 hour.
#
# Re-running the script is idempotent -- the -Force flag on Register-ScheduledTask
# overwrites the existing entries.
#
# After running, verify with:
#     Get-ScheduledTask -TaskName "MADAuthorClaude*"
#
# To disable both temporarily:
#     Disable-ScheduledTask -TaskName "MADAuthorClaude*"
# To re-enable:
#     Enable-ScheduledTask  -TaskName "MADAuthorClaude*"
# To remove entirely:
#     Unregister-ScheduledTask -TaskName "MADAuthorClaude*" -Confirm:$false

[CmdletBinding()]
param(
    [string]$RepoPath = "C:\Code\madauthor"
)

$ErrorActionPreference = 'Stop'

$workerScript  = Join-Path $RepoPath ".claude\worker\worker-iteration.ps1"
$scannerScript = Join-Path $RepoPath ".claude\scanner\scanner-iteration.ps1"

if (-not (Test-Path $workerScript))  { throw "Worker script not found: $workerScript" }
if (-not (Test-Path $scannerScript)) { throw "Scanner script not found: $scannerScript" }

# Shared settings + principal -----------------------------------------------------
$baseSettings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -ExecutionTimeLimit (New-TimeSpan -Hours 1) `
    -MultipleInstances IgnoreNew

$principal = New-ScheduledTaskPrincipal `
    -UserId $env:USERNAME `
    -LogonType Interactive `
    -RunLevel Limited

# Worker: every 1 minute ----------------------------------------------------------
$workerAction = New-ScheduledTaskAction `
    -Execute "powershell.exe" `
    -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$workerScript`"" `
    -WorkingDirectory $RepoPath

$workerTrigger = New-ScheduledTaskTrigger `
    -Once -At (Get-Date).Date.AddMinutes(15) `
    -RepetitionInterval (New-TimeSpan -Minutes 1) `
    -RepetitionDuration ([TimeSpan]::FromDays(3650))

Register-ScheduledTask `
    -TaskName "MADAuthorClaudeWorker" `
    -Description "MADAuthor autonomous worker -- drains /api/claude-tasks queue every minute (adaptive backoff)" `
    -Action $workerAction `
    -Trigger $workerTrigger `
    -Settings $baseSettings `
    -Principal $principal `
    -Force | Out-Null

Write-Host "Registered MADAuthorClaudeWorker (1 minute heartbeat, adaptive backoff in script)."

# Scanner: every 1 hour -----------------------------------------------------------
$scannerAction = New-ScheduledTaskAction `
    -Execute "powershell.exe" `
    -Argument "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$scannerScript`"" `
    -WorkingDirectory $RepoPath

$scannerTrigger = New-ScheduledTaskTrigger `
    -Once -At (Get-Date).Date.AddMinutes(15) `
    -RepetitionInterval (New-TimeSpan -Hours 1) `
    -RepetitionDuration ([TimeSpan]::FromDays(3650))

Register-ScheduledTask `
    -TaskName "MADAuthorClaudeScanner" `
    -Description "MADAuthor hourly codebase scanner -- posts findings to /api/claude-tasks/import-bulk" `
    -Action $scannerAction `
    -Trigger $scannerTrigger `
    -Settings $baseSettings `
    -Principal $principal `
    -Force | Out-Null

Write-Host "Registered MADAuthorClaudeScanner (1 hour cadence)."

# Final status report -------------------------------------------------------------
Write-Host ""
Write-Host "=== Final state ==="
Get-ScheduledTask -TaskName "MADAuthorClaude*" |
    Select-Object TaskName, State, @{n='Cadence';e={$_.Triggers[0].Repetition.Interval}} |
    Format-Table -AutoSize

Write-Host "Next steps:"
Write-Host "  1. Generate a CLAUDE_WORKER_TOKEN: [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)).ToLower()"
Write-Host "  2. Add it to .env: CLAUDE_WORKER_TOKEN=<value>"
Write-Host "  3. Replace <PLACE_TOKEN_HERE> in:"
Write-Host "     - $workerScript"
Write-Host "     - $($workerScript.Replace('worker-iteration.ps1', 'worker-prompt.md'))"
Write-Host "     - $($scannerScript.Replace('scanner-iteration.ps1', 'scanner-prompt.md'))"
Write-Host "  4. Restart the MADAuthor API so the new token is picked up."
Write-Host "  5. Test fire:  Start-ScheduledTask -TaskName 'MADAuthorClaudeWorker'"
Write-Host "                 then tail .claude\worker\worker.log"
