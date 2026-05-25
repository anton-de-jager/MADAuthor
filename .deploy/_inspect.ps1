$j = Get-Content 'C:\Code\madauthor\.deploy\ctx-eeddb020.json' -Raw | ConvertFrom-Json
Write-Output '--- job.inputJson ---'
$j.job.inputJson | ConvertTo-Json -Depth 6
Write-Output ''
Write-Output '--- project tone/style ---'
Write-Output ("writingTone = " + $j.project.writingTone)
Write-Output ("desiredTone = " + $j.request.desiredTone)
Write-Output ("povStyle = " + $j.request.povStyle)
Write-Output ("simplicityLevel = " + $j.request.simplicityLevel)
Write-Output ''
Write-Output '--- request.variables ---'
$j.request.variables | ConvertTo-Json -Depth 6
Write-Output ''
Write-Output '--- chapter list ---'
$j.existingChapters | ForEach-Object {
    "{0} | #{1} | status={2} | title={3} | words={4}" -f $_.chapterId, $_.chapterNumber, $_.status, $_.title, $_.wordCount
}
