$j = Get-Content 'C:\Code\madauthor\.deploy\ctx-eeddb020.json' -Raw | ConvertFrom-Json
$cid = $j.job.inputJson.chapterId
Write-Output ("target chapterId = " + $cid)
Write-Output ''
Write-Output '--- project tone fields ---'
Write-Output ("title = " + $j.project.title)
Write-Output ("subtitle = " + $j.project.subtitle)
Write-Output ("genre = " + $j.project.genre)
Write-Output ("fictionOrNonfiction = " + $j.project.fictionOrNonfiction)
Write-Output ("writingTone = " + $j.project.writingTone)
Write-Output ("targetAudience = " + $j.project.targetAudience)
Write-Output ("targetReadingLevel = " + $j.project.targetReadingLevel)
Write-Output ("language = " + $j.project.language)
Write-Output ("description = " + $j.project.description)
Write-Output ''
Write-Output '--- chapter list (id | # | status | title | words) ---'
$j.existingChapters | ForEach-Object {
    "{0} | #{1} | status={2} | title={3} | words={4}" -f $_.id, $_.chapterNumber, $_.status, $_.title, $_.wordCount
}
Write-Output ''
Write-Output '--- target chapter ---'
$target = $j.existingChapters | Where-Object { $_.id -eq $cid }
Write-Output ("number = " + $target.chapterNumber)
Write-Output ("title = " + $target.title)
Write-Output ("status = " + $target.status)
Write-Output ("words = " + $target.wordCount)
Write-Output ("summary = " + $target.summary)
$num = [int]$target.chapterNumber
$prev = $j.existingChapters | Where-Object { [int]$_.chapterNumber -eq ($num - 1) }
$next = $j.existingChapters | Where-Object { [int]$_.chapterNumber -eq ($num + 1) }
Write-Output ''
Write-Output ("PREV (#{0}) status={1} title={2}" -f $prev.chapterNumber, $prev.status, $prev.title)
Write-Output ("NEXT (#{0}) title={1}" -f $next.chapterNumber, $next.title)
Write-Output ("NEXT summary = " + $next.summary)
