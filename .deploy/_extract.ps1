$j = Get-Content 'C:\Code\madauthor\.deploy\ctx-eeddb020.json' -Raw | ConvertFrom-Json
$cid = $j.job.inputJson.chapterId
$target = $j.existingChapters | Where-Object { $_.id -eq $cid }
$target.contentMarkdown | Set-Content -Path 'C:\Code\madauthor\.deploy\_chapter10_in.md' -Encoding utf8
Write-Output ("wrote chapter md, length chars = " + $target.contentMarkdown.Length)
