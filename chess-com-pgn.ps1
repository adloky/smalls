$User= Read-Host -Prompt "Which chess.com user would you like to download archives for?"
$Folder="d:"
IF (!(Test-Path $folder))
    {New-Item -ItemType  Directory -Path $folder}
Write-Output "Archive files will be saved to $($Folder)"
$Response=Invoke-RestMethod -uri "https://api.chess.com/pub/player/$($User)/games/archives"

$PGN=@()
foreach ($Month in $Response.archives)
    {
    Write-Output $Month
    $PGN+=(Invoke-RestMethod -uri "$Month").Games.pgn}
    ($PGN | ForEach-Object {$_ + "`r`n"}) |
    Out-File -FilePath $folder\$User.pgn -Encoding ascii