# parse_am.ps1 - THROWAWAY: parse actormotion.txt for skin_class 6 and 11 idle motions
# NOT COMMITTED.
$enc = [System.Text.Encoding]::GetEncoding(949)
$bytes = [System.IO.File]::ReadAllBytes('D:/dump/actormotion.txt')
$text = $enc.GetString($bytes)
$lines = $text -split "`r`n" | Select-Object -Skip 1 | Where-Object { $_ -ne '' }
$targets = @(6, 11)
Write-Host "mob_id sc idle(col15) col16"
foreach ($line in $lines) {
    $cols = $line -split "`t"
    if ($cols.Length -lt 16) { continue }
    $sc = [int]$cols[2]
    if ($targets -contains $sc) {
        $col16 = if ($cols.Length -gt 16) { $cols[16] } else { '' }
        Write-Host ("{0} {1} {2} {3}" -f $cols[1], $cols[2], $cols[15], $col16)
    }
}
