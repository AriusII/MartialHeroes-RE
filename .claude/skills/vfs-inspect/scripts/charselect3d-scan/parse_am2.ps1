# parse_am2.ps1 - THROWAWAY: scan actormotion.txt for all distinct skin_classes and idle motions
param([int]$MaxRows = 100)
$enc = [System.Text.Encoding]::GetEncoding(949)
$bytes = [System.IO.File]::ReadAllBytes('D:/dump/actormotion.txt')
$text = $enc.GetString($bytes)
$lines = ($text -split "`r`n") | Select-Object -Skip 1 | Where-Object { $_.Length -gt 0 }

Write-Host "mob_id`tskin_class`tidle_col15"
$count = 0
foreach ($line in $lines) {
    if ($count -ge $MaxRows) { break }
    $cols = $line -split "`t"
    if ($cols.Count -lt 16) { continue }
    Write-Host ("{0}`t{1}`t{2}" -f $cols[1], $cols[2], $cols[15])
    $count++
}

Write-Host ""
Write-Host "=== Distinct skin_classes ==="
$scs = @{}
foreach ($line in $lines) {
    $cols = $line -split "`t"
    if ($cols.Count -lt 3) { continue }
    $sc = $cols[2]
    if (-not $scs.ContainsKey($sc)) { $scs[$sc] = 0 }
    $scs[$sc]++
}
$scs.GetEnumerator() | Sort-Object { [int]$_.Key } | ForEach-Object {
    Write-Host ("  skin_class={0}: {1} rows" -f $_.Key, $_.Value)
}
