# Desktop Recording Review Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a user open one project-owned EEG recording in a full-screen review workspace, default to its acquisition montage, and switch to any compatible viewing montage without changing raw data or playback time.

**Architecture:** Add a read-only indexed local-recording session below the project workspace. It reads bounded sample-counter windows from immutable chunk files, resolves compatible montage snapshots, and supplies a dedicated review ViewModel/canvas; `MainWindow` only coordinates immersive navigation. Extract montage display projection from the live-only frame builder so live and review use one formula implementation while keeping Python-owned filters and scientific analysis outside this slice.

**Tech Stack:** C# 14, .NET 10, WPF/MVVM, SciChart WPF, xUnit, JSON manifests, sample-major float64 local chunks, OpenSpec.

**Spec:** `openspec/changes/add-desktop-recording-review/specs/desktop/recording-review/spec.md`

## Global Constraints

- Raw `samples-*.bin`, `manifest.json`, and `audit.jsonl` files are read-only and SHALL never be rewritten by review.
- EEG values remain V/float64 internally; UI formatting may display µV only through an explicit conversion boundary.
- Scientific time uses sample counter plus `SamplingRateHz`; PC receive ticks are diagnostic only.
- Trigger and sample-counter columns remain available for timing/events but never count as EEG montage sources.
- `采集导联` comes from the recording snapshot; `当前查看导联` is review-session state and never overwrites it.
- A montage switch preserves absolute position and visible duration even when its output count changes.
- Missing/corrupt data is unavailable with a structured reason; never substitute zeros, stale frames, guessed montages, or synthetic EEG.
- Offline filters, algorithms, AnalysisRuns, and report export remain Python-owned and are not implemented in this first review slice.
- New hand-written business files target at most 400 lines; any larger touched file requires `docs/code-size-policy.json` review.

## Review Focus

- A legacy manifest without a montage snapshot must show an explicit unavailable acquisition montage and a clearly labeled raw-signal fallback; Task 3 tests this.
- Duplicate labels such as two `M1` signal columns must make dependent montages incompatible instead of selecting the first match; Task 3 tests this.
- A window that crosses a gap must return separate segments and render no connecting line; Tasks 2 and 5 test this.
- A delayed old window read must not overwrite traces after the user seeks or switches montage; Task 4 tests cancellation/revision gating.
- A 4 kHz, 30-column recording must open by indexing headers only and keep window memory bounded; Tasks 2 and 6 test this.

---

### Task 1: Freeze the review contract in OpenSpec

**Files:**
- Create: `openspec/changes/add-desktop-recording-review/proposal.md`
- Create: `openspec/changes/add-desktop-recording-review/design.md`
- Create: `openspec/changes/add-desktop-recording-review/specs/desktop/recording-review/spec.md`
- Create: `openspec/changes/add-desktop-recording-review/tasks.md`

**Interfaces:**
- Consumes: Current `desktop/acquisition-core` raw persistence and display montage contracts.
- Produces: Normative review behavior for all later tasks in this plan.

- [ ] **Step 1: Review the new change against current acquisition contracts**

Confirm the change explicitly covers immutable raw chunks, counter-based time,
acquisition/viewing montage separation, compatible source labels, 20-to-13
trace switching, immersive project navigation, gaps, and legacy recordings.

- [ ] **Step 2: Validate the change before implementation**

Run:

```powershell
openspec validate add-desktop-recording-review --strict --no-interactive
```

Expected: `add-desktop-recording-review` is valid with no missing scenario or delta-format errors.

- [ ] **Step 3: Commit the contract**

```powershell
git add openspec/changes/add-desktop-recording-review
git commit -m "docs: specify desktop recording review"
```

### Task 2: Add a bounded, corruption-aware raw recording reader

**Files:**
- Create: `desktop-client/BrainPlatform.Desktop/Review/LocalRecordingManifest.cs`
- Create: `desktop-client/BrainPlatform.Desktop/Review/LocalRawRecordingIndex.cs`
- Create: `desktop-client/BrainPlatform.Desktop/Review/LocalRawRecordingReader.cs`
- Create: `desktop-client/BrainPlatform.Desktop/Review/RecordingReviewWindow.cs`
- Create: `desktop-client/BrainPlatform.Desktop.Tests/Review/LocalRawRecordingReaderTests.cs`

**Interfaces:**
- Consumes: `AcquisitionChannel`, `AcquisitionBatch`, manifest JSON, `samples-*.bin`, and `audit.jsonl`.
- Produces: `Task<LocalRawRecording> OpenAsync(string recordingDirectory, CancellationToken)` and `Task<RecordingReviewWindow> ReadWindowAsync(double startSeconds, double durationSeconds, CancellationToken)`.

- [ ] **Step 1: Write failing manifest and index tests**

Create fixtures with two chunk files and assert that opening returns the exact
session ID, sampling rate, ordered channel table, first/last counters, duration,
gaps, project snapshot, and optional acquisition montage JSON. Assert opening
does not read sample payloads by using a counting stream seam.

```csharp
Assert.Equal(4000, recording.Manifest.SamplingRateHz);
Assert.Equal(["F3", "F4", "Trigger", "Counter"],
    recording.Manifest.Channels.Select(channel => channel.Label));
Assert.Equal(0, countingSource.PayloadBytesRead);
```

Use an internal test seam instead of touching production behavior:

```csharp
internal interface IRecordingFileSource
{
    Stream OpenRead(string path);
}
```

The production implementation returns a read-only sequential `FileStream`;
the test implementation counts bytes read after each indexed payload offset.

- [ ] **Step 2: Run the focused tests and verify failure**

```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj -c Release --no-restore -p:UseAppHost=false --filter FullyQualifiedName~LocalRawRecordingReaderTests
```

Expected: FAIL because the review reader contracts do not exist.

- [ ] **Step 3: Implement typed manifest parsing and checked header indexing**

Implement immutable records whose public names carry units:

```csharp
public sealed record LocalRawRecordingManifest(
    Guid SessionId,
    string PayloadFormat,
    int SamplingRateHz,
    long? SampleCounterChannelIndex,
    DateTimeOffset RecordingStartUtc,
    IReadOnlyList<AcquisitionChannel> Channels,
    AcquisitionProjectContext Project,
    IReadOnlyDictionary<string, string> HardwareConfiguration);

public sealed record RawBatchIndexEntry(
    string ChunkPath,
    long HeaderOffset,
    long PayloadOffset,
    long FirstSampleCounter,
    int SampleCount,
    int ChannelCount,
    DateTimeOffset ReceivedAtUtc);

public sealed record LocalRawRecording(
    LocalRawRecordingManifest Manifest,
    LocalRawRecordingIndex Index,
    IReadOnlyList<AcquisitionGap> Gaps,
    LocalRawRecordingReader Reader);
```

Reject unknown payload format, nonpositive sampling/channel/sample counts,
channel-count disagreement, integer overflow, overlapping counters, and payload
length beyond the file. Parse audit gaps separately and preserve their counter
intervals.

- [ ] **Step 4: Write failing bounded-window and gap tests**

Assert a requested interval seeks only intersecting payloads, returns values in
sample-major order, clips first/last batches without changing channel order,
and produces two segments across a gap. Add truncated-payload and cancellation
tests with explicit error codes such as `RECORDING_CHUNK_TRUNCATED`.

- [ ] **Step 5: Implement counter-based window reads**

Use checked byte offsets and `RandomAccess.ReadAsync` or independently opened
`FileStream`s. Return:

```csharp
public sealed record RecordingReviewWindow(
    double RequestedStartSeconds,
    double ActualStartSeconds,
    double ActualEndSeconds,
    IReadOnlyList<RecordingReviewSegment> Segments);

public sealed record RecordingReviewSegment(
    long FirstSampleCounter,
    int SampleCount,
    int ChannelCount,
    double[] SampleMajorValues);
```

Each segment keeps its first sample counter and sample-major V/float64 array.
Never allocate for the full recording and never fill counter gaps.

- [ ] **Step 6: Run reader tests**

Run the Task 2 command. Expected: PASS.

- [ ] **Step 7: Commit the reader**

```powershell
git add desktop-client/BrainPlatform.Desktop/Review desktop-client/BrainPlatform.Desktop.Tests/Review/LocalRawRecordingReaderTests.cs
git commit -m "feat: add bounded local recording reader"
```

### Task 3: Resolve acquisition and compatible viewing montages

**Files:**
- Create: `desktop-client/BrainPlatform.Desktop/Review/RecordingMontageCatalog.cs`
- Create: `desktop-client/BrainPlatform.Desktop/Review/RecordingMontageCompatibility.cs`
- Create: `desktop-client/BrainPlatform.Desktop.Tests/Review/RecordingMontageCatalogTests.cs`
- Modify: `desktop-client/BrainPlatform.Desktop/Configuration/MontageValidation.cs`

**Interfaces:**
- Consumes: `LocalRawRecordingManifest`, embedded `montage_configuration_snapshot_json`, and current `MontageProfileStore` profiles.
- Produces: `RecordingMontageCatalog.Build(LocalRawRecordingManifest, IEnumerable<MontageProfile>)` returning `AcquisitionMontage`, `CompatibleViewingMontages`, and per-profile incompatibility reasons.

The result contract is:

```csharp
public sealed record RecordingMontageCatalogResult(
    MontageProfile? AcquisitionMontage,
    AcquisitionMontageStatus AcquisitionMontageStatus,
    RawSignalViewDefinition RawSignalView,
    IReadOnlyList<CompatibleRecordingMontage> CompatibleViewingMontages,
    IReadOnlyList<IncompatibleRecordingMontage> Incompatible);
```

- [ ] **Step 1: Write failing compatibility tests**

Cover exact case-insensitive signal-label resolution, missing labels, duplicate
labels, Trigger/Counter rejection, non-V signal units, malformed snapshot JSON,
and a globally deleted acquisition montage that still loads from the embedded
snapshot.

```csharp
Assert.Equal("采集导联 A", catalog.AcquisitionMontage!.Name);
Assert.DoesNotContain(catalog.CompatibleViewingMontages,
    item => item.Profile.Name == "需要缺失 M2");
Assert.Contains("M2", catalog.Incompatible.Single().Reason);
```

- [ ] **Step 2: Run tests and verify failure**

```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj -c Release --no-restore -p:UseAppHost=false --filter FullyQualifiedName~RecordingMontageCatalogTests
```

Expected: FAIL because no recording montage resolver exists.

- [ ] **Step 3: Implement explicit source resolution**

Build a case-insensitive label map from only `Reference` and `Bipolar` manifest
channels. Require exactly one source for each `PositiveLabel` and every
`NegativeLabel`. Call existing montage validation before accepting the profile.
Represent a legacy raw fallback as a separate `RawSignalViewDefinition`, not a
fabricated `MontageProfile` or acquisition snapshot.

- [ ] **Step 4: Prove legacy behavior**

Add a test where the manifest has no montage JSON. Assert
`AcquisitionMontageStatus == MissingSnapshot`, the catalog does not claim any
current profile was used during acquisition, and the raw signal view is
available with its explicit label.

- [ ] **Step 5: Run compatibility tests**

Run the Task 3 command. Expected: PASS.

- [ ] **Step 6: Commit montage compatibility**

```powershell
git add desktop-client/BrainPlatform.Desktop/Review desktop-client/BrainPlatform.Desktop/Configuration/MontageValidation.cs desktop-client/BrainPlatform.Desktop.Tests/Review/RecordingMontageCatalogTests.cs
git commit -m "feat: resolve recording review montages"
```

### Task 4: Add review session state with race-safe montage switching

**Files:**
- Create: `desktop-client/BrainPlatform.Desktop/Review/MontageDisplayProjector.cs`
- Create: `desktop-client/BrainPlatform.Desktop/Review/RecordingReviewSession.cs`
- Create: `desktop-client/BrainPlatform.Desktop/ViewModels/RecordingReviewViewModel.cs`
- Create: `desktop-client/BrainPlatform.Desktop.Tests/Review/MontageDisplayProjectorTests.cs`
- Create: `desktop-client/BrainPlatform.Desktop.Tests/Review/RecordingReviewViewModelTests.cs`
- Modify: `desktop-client/BrainPlatform.Desktop/Views/WaveformDisplayFrameBuilder.cs`
- Modify: `desktop-client/BrainPlatform.Desktop/Views/MontageReferenceSignalCache.cs`

**Interfaces:**
- Consumes: `RecordingReviewWindow`, compatible `MontageProfile`, and V/float64 source channels.
- Produces: immutable `RecordingReviewFrame`, `SelectedViewingMontage`, `AcquisitionMontageText`, `PositionSeconds`, `VisibleDurationSeconds`, and `OutputChannelCount`.

The frame contract is:

```csharp
public sealed record RecordingReviewFrame(
    Guid RecordingSessionId,
    long Revision,
    double WindowStartSeconds,
    double WindowEndSeconds,
    string ViewingMontageFingerprint,
    IReadOnlyList<ProjectedMontageSegment> Segments,
    IReadOnlyList<string> OutputChannelNames);
```

- [ ] **Step 1: Write failing shared projection tests**

Test hardware-reference identity, `A-B`, `A-Mean(group)`, specified-pair mean,
multiple output counts, NaN propagation, source-array immutability, and separate
trace segments around a gap.

- [ ] **Step 2: Extract one montage display projector**

Move source resolution/reference caching out of the live-only builder without
changing formulas. Its core result must be immutable and segment-aware:

```csharp
public sealed record ProjectedMontageSegment(
    long FirstSampleCounter,
    int SampleCount,
    IReadOnlyList<ProjectedMontageChannel> Channels);
```

Keep V internally. Adapt `WaveformDisplayFrameBuilder` to call this projector so
live and review cannot drift into two formula implementations.

- [ ] **Step 3: Write failing 20-to-13 state tests**

Open at `PositionSeconds = 125`, `VisibleDurationSeconds = 10`, select a 20-row
montage, then a 13-row montage. Assert position/duration remain unchanged,
output count becomes 13, all rows are replaced together, and raw values and
manifest snapshots remain unchanged.

- [ ] **Step 4: Implement revision-gated asynchronous window loads**

Increment a request revision on seek, window-duration change, and montage
change. Cancel the prior read when possible and publish a frame only when its
captured revision still equals the current revision. Keep the old complete
frame visible while the replacement is built; never expose a half-filled list.

- [ ] **Step 5: Test stale completion and disposal**

Use controllable reader tasks so an older read completes after a newer montage
request. Assert only the newer result reaches `CurrentFrame`. Dispose the
ViewModel and assert pending work is cancelled and cannot update properties.

- [ ] **Step 6: Run projection and state tests**

```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj -c Release --no-restore -p:UseAppHost=false --filter "FullyQualifiedName~MontageDisplayProjectorTests|FullyQualifiedName~RecordingReviewViewModelTests|FullyQualifiedName~WaveformDisplayFrameBuilderTests"
```

Expected: PASS, including existing live montage tests.

- [ ] **Step 7: Commit review state**

```powershell
git add desktop-client/BrainPlatform.Desktop/Review desktop-client/BrainPlatform.Desktop/ViewModels/RecordingReviewViewModel.cs desktop-client/BrainPlatform.Desktop/Views/WaveformDisplayFrameBuilder.cs desktop-client/BrainPlatform.Desktop/Views/MontageReferenceSignalCache.cs desktop-client/BrainPlatform.Desktop.Tests/Review desktop-client/BrainPlatform.Desktop.Tests/Acquisition/WaveformDisplayFrameBuilderTests.cs
git commit -m "feat: add race-safe recording review session"
```

### Task 5: Connect project recording selection to immersive review

**Files:**
- Modify: `desktop-client/BrainPlatform.Desktop/ViewModels/ProjectWorkspaceViewModel.cs`
- Modify: `desktop-client/BrainPlatform.Desktop/ViewModels/DesktopWorkspaceViewModel.cs`
- Modify: `desktop-client/BrainPlatform.Desktop/Views/ProjectListView.xaml`
- Modify: `desktop-client/BrainPlatform.Desktop/Views/ProjectListView.xaml.cs`
- Modify: `desktop-client/BrainPlatform.Desktop/MainWindow.xaml.cs`
- Create: `desktop-client/BrainPlatform.Desktop.Tests/Projects/ProjectRecordingReviewNavigationTests.cs`

**Interfaces:**
- Consumes: `ProjectRecordingRow.RecordingDirectory` and `RecordingReviewViewModel.OpenAsync`.
- Produces: `SelectedRecording`, `CanReviewSelectedRecording`, `MainWindow.ShowRecordingReviewViewAsync(...)`, and return-to-owning-project behavior.

- [ ] **Step 1: Write failing selection/navigation tests**

Assert refreshing preserves recording selection by `SessionId`, only readable
completed/aborted records can enter review, and returning preserves the owning
project ID. A missing directory must publish an error without navigating.

- [ ] **Step 2: Add stable selected-recording state**

Bind `DataGrid.SelectedItem` to `Projects.SelectedRecording`. Add a row action
or adjacent `回溯` button whose command parameter is the exact
`ProjectRecordingRow`; do not infer a recording from list order or display name.

- [ ] **Step 3: Add immersive navigation lifecycle**

`MainWindow.ShowRecordingReviewViewAsync` must call the review ViewModel first,
then hide navigation only after successful open. `ShowProjectListView` disposes
the review session, restores normal chrome, reselects the owning project, and
refreshes recordings. Reuse a neutral `SetImmersiveChrome(bool)` name rather
than extending acquisition-specific semantics.

- [ ] **Step 4: Run navigation tests**

```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj -c Release --no-restore -p:UseAppHost=false --filter "FullyQualifiedName~ProjectWorkspaceViewModel|FullyQualifiedName~ProjectRecordingReviewNavigationTests"
```

Expected: PASS.

- [ ] **Step 5: Commit project entry and navigation**

```powershell
git add desktop-client/BrainPlatform.Desktop/ViewModels/ProjectWorkspaceViewModel.cs desktop-client/BrainPlatform.Desktop/ViewModels/DesktopWorkspaceViewModel.cs desktop-client/BrainPlatform.Desktop/Views/ProjectListView.xaml desktop-client/BrainPlatform.Desktop/Views/ProjectListView.xaml.cs desktop-client/BrainPlatform.Desktop/MainWindow.xaml.cs desktop-client/BrainPlatform.Desktop.Tests/Projects
git commit -m "feat: open project recordings in review"
```

### Task 6: Build the full-screen review workspace and playback

**Files:**
- Create: `desktop-client/BrainPlatform.Desktop/Views/RecordingReviewView.xaml`
- Create: `desktop-client/BrainPlatform.Desktop/Views/RecordingReviewView.xaml.cs`
- Create: `desktop-client/BrainPlatform.Desktop/Views/RecordingReviewSciChartCanvas.cs`
- Create: `desktop-client/BrainPlatform.Desktop/Review/RecordingPlaybackController.cs`
- Create: `desktop-client/BrainPlatform.Desktop.Tests/Review/RecordingPlaybackControllerTests.cs`
- Create: `desktop-client/BrainPlatform.Desktop.Tests/Review/RecordingReviewSmokeTests.cs`

**Interfaces:**
- Consumes: `RecordingReviewViewModel.CurrentFrame`, montage choices, position, duration, and load/error state.
- Produces: back, play/pause, seek, visible-duration, current-montage selection, complete waveform track replacement, and explicit status UI.

- [ ] **Step 1: Write failing playback-controller tests**

Use a fake monotonic clock. Verify play advances by elapsed wall time, pause
freezes position, seek clamps to `[0, DurationSeconds]`, end-of-file enters
completed state, and changing montage does not alter position or play/pause
state.

- [ ] **Step 2: Implement playback independent of UI frame rate**

The controller derives position from a monotonic clock and a playback anchor;
it does not increment by a guessed timer interval. Rendering requests are
coalesced so only the latest position is processed. UI refresh cadence may be
bounded, but scientific position remains clock/counter-based.

- [ ] **Step 3: Add the full-screen page shell**

The header must show back, project/recording identity, sampling rate, duration,
signal channel count, immutable `采集导联`, selectable `当前查看导联`, and
current output count. The waveform occupies the dominant area. The compact
transport contains play/pause, absolute elapsed/total time, seek bar, visible
duration, and status; do not add algorithm/report controls in this slice.

- [ ] **Step 4: Render complete immutable frames in SciChart**

Create or replace all renderable series in one dispatcher operation after a
complete frame arrives. Preserve segment boundaries across gaps. Apply existing
temporal-extrema display reduction when samples exceed horizontal pixels, but
never mutate or decimate the reader's source window.

- [ ] **Step 5: Add explicit non-success states**

Render separate loading, no samples, missing acquisition snapshot, incompatible
montage, corrupt chunk, cancelled read, and unexpected error states. Keep the
last complete frame only during a normal seek/montage replacement; clear it on
recording identity change or corruption.

- [ ] **Step 6: Run playback and smoke tests**

```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj -c Release --no-restore -p:UseAppHost=false --filter "FullyQualifiedName~RecordingPlaybackControllerTests|FullyQualifiedName~RecordingReviewSmokeTests"
```

Expected: PASS.

- [ ] **Step 7: Commit the workspace**

```powershell
git add desktop-client/BrainPlatform.Desktop/Views/RecordingReview* desktop-client/BrainPlatform.Desktop/Review/RecordingPlaybackController.cs desktop-client/BrainPlatform.Desktop.Tests/Review
git commit -m "feat: add immersive EEG recording review"
```

### Task 7: Run end-to-end verification and close the change

**Files:**
- Modify: `openspec/changes/add-desktop-recording-review/tasks.md`
- Modify if required by line counts: `docs/code-size-policy.json`
- Create: `docs/validation/2026-09-20-desktop-recording-review-report.md`

**Interfaces:**
- Consumes: All deliverables from Tasks 1-6.
- Produces: Reproducible validation evidence and a merge-ready review slice.

- [ ] **Step 1: Add a 4 kHz bounded-memory regression test**

Generate sparse chunk files representing at least 30 minutes at 4 kHz × 30
columns by writing valid headers and extending file length without retaining
equivalent payload arrays in test memory. Assert opening
allocates/indexes by batch count rather than sample count, and a 10-second read
returns only that window. Record elapsed time and allocated bytes as regression
evidence, not as a universal hardware guarantee.

- [ ] **Step 2: Run the full desktop suite**

```powershell
dotnet test desktop-client/BrainPlatform.Desktop.Tests/BrainPlatform.Desktop.Tests.csproj -c Release --no-restore -p:UseAppHost=false
```

Expected: all desktop tests pass.

- [ ] **Step 3: Build the Release client**

```powershell
dotnet build desktop-client/BrainPlatform.Desktop/BrainPlatform.Desktop.csproj -c Release --no-restore -p:UseAppHost=false
```

Expected: build succeeds with zero errors.

- [ ] **Step 4: Validate contracts and repository hygiene**

```powershell
openspec validate add-desktop-recording-review --strict --no-interactive
git diff --check
```

Expected: both commands pass. Count every created/modified hand-written file;
split by responsibility or update `docs/code-size-policy.json` when required by
the 400-line policy.

- [ ] **Step 5: Perform hardware-independent manual acceptance**

Open a completed project recording, verify the recorded acquisition montage is
the initial selection, seek to 125 s, switch a 20-output montage to a 13-output
montage, and verify the page remains at 125 s while exactly 13 traces replace
the previous set. Return and verify the same project is selected. Repeat with a
missing-source montage and a deliberately truncated copied fixture; neither may
show fabricated or partial waveform data.

- [ ] **Step 6: Record evidence and mark tasks complete**

Write exact command outputs, test counts, fixture identities, observed channel
counts, time/memory measurements, and any environment limitation into the
validation report. Check each OpenSpec task only after its evidence exists.

- [ ] **Step 7: Commit verification**

```powershell
git add openspec/changes/add-desktop-recording-review/tasks.md docs/validation/2026-09-20-desktop-recording-review-report.md docs/code-size-policy.json
git commit -m "test: verify desktop recording review"
```
