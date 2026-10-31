$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$coordinator = Get-Content (Join-Path $repoRoot 'desktop-client\BrainPlatform.Desktop\Shared\Time\RecordingPlaybackTimeCoordinator.cs') -Raw
$tnkPlayback = Get-Content (Join-Path $repoRoot 'desktop-client\BrainPlatform.Desktop\Modules\Algorithms\ViewModels\Shared\TnkWorkbenchPlayback.cs') -Raw
$catalogPlayback = Get-Content (Join-Path $repoRoot 'desktop-client\BrainPlatform.Desktop\Modules\Algorithms\ViewModels\Catalog\AlgorithmListViewModel.OnDemand.cs') -Raw
$delivery = Get-Content (Join-Path $repoRoot 'desktop-client\BrainPlatform.Desktop\Modules\Algorithms\ViewModels\Catalog\AlgorithmListViewModel.TnkPlaybackDelivery.cs') -Raw

if ($tnkPlayback -match 'DispatcherTimer|new\s+RecordingPlaybackTimeCoordinator') {
    throw 'TNK playback must consume the workspace coordinator and cannot own a second clock.'
}
if ($catalogPlayback -match 'cursor\s*[+\-]=|DynamicPreviewCursorSeconds\s*[+\-]=') {
    throw 'Algorithm preview code must not advance the playback cursor itself.'
}
if ($delivery -notmatch 'PlaybackGeneration:\s*tnkPlaybackGeneration' -or
    $delivery -notmatch 'currentPlaybackGeneration != tnkPlaybackGeneration') {
    throw 'TNK playback delivery must retain and fence the playback generation that created it.'
}
foreach ($member in 'CursorSample', 'Generation', 'RecordingId', 'PlaybackRate') {
    if ($coordinator -notmatch "\b$member\b") {
        throw "Playback coordinator is missing required cursor member: $member"
    }
}

Write-Output 'Playback architecture boundary checks passed.'
