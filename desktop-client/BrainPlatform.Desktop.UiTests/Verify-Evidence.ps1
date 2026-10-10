param([Parameter(Mandatory)][string]$EvidenceDirectory)
$ErrorActionPreference = 'Stop'
$testRoot = (Resolve-Path -LiteralPath $EvidenceDirectory).Path
if (Test-Path -LiteralPath (Join-Path $testRoot 'failure.txt')) { throw 'Test host recorded an exception.' }
$history = @(Get-Content -Raw -LiteralPath (Join-Path $testRoot 'history.json') | ConvertFrom-Json)
$current = Get-Content -Raw -LiteralPath (Join-Path $testRoot 'current.json') | ConvertFrom-Json
if (@($current.failures).Count -gt 0) { throw ($current.failures -join '; ') }
foreach ($phase in @('Previewing', 'Recording', 'Paused', 'Resumed', 'Stopped')) {
    $entry = @($history | Where-Object phase -EQ $phase)
    if ($entry.Count -ne 1) { throw "Missing or duplicate phase: $phase" }
    if ($entry[0].mapping.'3' -ne 'F88' -or $entry[0].mapping.'5' -ne 'F3') { throw "Wrong mapping at $phase" }
    if (@($entry[0].failures).Count -gt 0) { throw "Failed assertions at $phase" }
    if ($phase -ne 'Stopped') {
        if (@($entry[0].series).Count -ne 4 -or @($entry[0].series | Where-Object count -LE 0).Count -gt 0) {
            throw "Missing SciChart data at $phase"
        }
    }
}
$recordings = @(Get-ChildItem -LiteralPath (Join-Path $testRoot 'recordings') -Directory)
if ($recordings.Count -ne 1) { throw 'Expected exactly one synthetic recording.' }
$recording = $recordings[0].FullName
$manifest = Get-Content -Raw -LiteralPath (Join-Path $recording 'manifest.json') | ConvertFrom-Json
$summary = Get-Content -Raw -LiteralPath (Join-Path $recording 'recording-summary.json') | ConvertFrom-Json
$audit = @(Get-Content -LiteralPath (Join-Path $recording 'audit.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
if ($manifest.DeviceId -ne 'ui-test:1' -or $manifest.SamplingRateHz -ne 1000) { throw 'Not the expected synthetic fixture.' }
if ($summary.status -ne 'completed') { throw 'Recording did not complete.' }
foreach ($native in @(3, 5)) {
    $channel = @($manifest.Channels | Where-Object NativeChannelIndex -EQ $native)
    $expectedLabel = if ($native -eq 3) { 'F88' } else { 'F3' }
    if ($channel.Count -ne 1 -or $channel[0].Label -ne $expectedLabel -or $channel[0].Unit -ne 'V') { throw "Manifest mismatch at CH$native" }
}
$snapshot = $manifest.HardwareConfiguration.channel_configuration_snapshot_json | ConvertFrom-Json
if (($snapshot.Channels | Where-Object NativeChannelIndex -EQ 3).ElectrodeLabel -ne 'F88' -or
    ($snapshot.Channels | Where-Object NativeChannelIndex -EQ 5).ElectrodeLabel -ne 'F3') { throw 'Saved channel snapshot mismatch.' }
$lifecycle = @($audit | Where-Object type -EQ 'recording_lifecycle')
if (($lifecycle.boundary -join ',') -ne 'Started,Paused,Resumed,Stopped') { throw 'Lifecycle order mismatch.' }
$gaps = @($audit | Where-Object type -EQ 'sample_gap')
if ($gaps.Count -ne 1) { throw 'Expected one explicit pause gap.' }
if ($gaps[0].FirstMissingSampleCounter -ne $lifecycle[1].sample_counter + 1 -or
    $gaps[0].LastMissingSampleCounter -ne $lifecycle[2].sample_counter - 1) { throw 'Pause gap boundaries mismatch.' }
$sampleCount = 0L
$batchCount = 0
$firstCounter = $null
$lastCounter = $null
$observedGaps = @()
foreach ($chunk in Get-ChildItem -LiteralPath $recording -Filter 'samples-*.bin' | Sort-Object Name) {
    $reader = [System.IO.BinaryReader]::new([System.IO.File]::OpenRead($chunk.FullName))
    try {
        while ($reader.BaseStream.Position -lt $reader.BaseStream.Length) {
            $first = $reader.ReadInt64()
            $count = $reader.ReadInt32()
            $width = $reader.ReadInt32()
            $receivedTicks = $reader.ReadInt64()
            if ($count -ne 50 -or $width -ne 30 -or $receivedTicks -le 0) { throw 'Invalid batch header.' }
            $bytes = $reader.ReadBytes($count * $width * 8)
            if ($bytes.Length -ne $count * $width * 8) { throw 'Truncated batch.' }
            $values = [double[]]::new($count * $width)
            [Buffer]::BlockCopy($bytes, 0, $values, 0, $bytes.Length)
            if ($null -eq $firstCounter) { $firstCounter = $first }
            if ($null -ne $lastCounter -and $first -ne $lastCounter + 1) {
                $observedGaps += [pscustomobject]@{ First = $lastCounter + 1; Last = $first - 1 }
            }
            for ($sample = 0; $sample -lt $count; $sample++) {
                $counter = $first + $sample
                if ($values[$sample * $width + 29] -ne $counter) { throw 'Counter channel mismatch.' }
                foreach ($native in @(2, 3, 5)) {
                    $baseline = switch ($native) { 2 { 20 } 3 { 40 } 5 { 60 } }
                    $expected = ($baseline + 4 * [Math]::Sin(2 * [Math]::PI * (8 + $native) * $counter / 1000.0)) * 1e-6
                    if ([Math]::Abs($values[$sample * $width + $native] - $expected) -gt 1e-12) { throw "CH$native raw value mismatch." }
                }
            }
            $lastCounter = $first + $count - 1
            $sampleCount += $count
            $batchCount++
        }
    } finally { $reader.Dispose() }
}
if ($sampleCount -ne $summary.effective_sample_count) { throw 'Raw/summary sample count mismatch.' }
if ($firstCounter -ne $lifecycle[0].sample_counter -or $lastCounter -ne $lifecycle[3].sample_counter) { throw 'Start/stop not anchored to written samples.' }
if ($observedGaps.Count -ne 1 -or $observedGaps[0].First -ne $gaps[0].FirstMissingSampleCounter -or
    $observedGaps[0].Last -ne $gaps[0].LastMissingSampleCounter) { throw 'Written samples disagree with pause gap.' }
[pscustomobject]@{
    Result = 'PASS'; UiPhases = 'Previewing,Recording,Paused,Resumed,Stopped'; RawBatches = $batchCount
    WrittenSamples = $sampleCount; EffectiveSeconds = $summary.effective_duration_seconds
    PauseGapSamples = $gaps[0].MissingSampleCount; Ch3Label = 'F88'; Ch5Label = 'F3'
} | ConvertTo-Json
