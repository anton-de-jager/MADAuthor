$j = Get-Content 'C:\Code\madauthor\.deploy\ctx-eeddb020.json' -Raw | ConvertFrom-Json
Write-Output '--- top-level keys ---'
$j.PSObject.Properties.Name
Write-Output ''
Write-Output '--- job keys ---'
$j.job.PSObject.Properties.Name
Write-Output ''
Write-Output '--- project keys ---'
$j.project.PSObject.Properties.Name
Write-Output ''
Write-Output '--- request keys ---'
$j.request.PSObject.Properties.Name
Write-Output ''
Write-Output '--- first chapter keys ---'
$j.existingChapters[0].PSObject.Properties.Name
