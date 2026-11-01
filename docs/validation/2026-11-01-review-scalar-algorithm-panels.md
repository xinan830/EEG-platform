# Review scalar algorithm panels validation

Automated validation for `add-review-panels-spectral-metrics`:

- Isolated WPF build: `dotnet build desktop-client/BrainPlatform.Desktop/BrainPlatform.Desktop.csproj --no-restore -o .isolated-build/review-scalar-20261101` passed before the final projector extraction; the final focused test build also rebuilt the WPF project into `.isolated-build/review-metric-tests/` successfully.
- Focused desktop tests: 68 passed, 0 failed. Coverage included four algorithm input configurations, strict F3/F4 and Fz/Pz role mapping, scalar projection and unit rejection, panel cancellation/close, existing RBP/STFT lifecycle, and algorithm-owned output/provenance tests.
- Scalar and RBP chart origin regressions: opening a panel at 30 seconds sets the visible left edge to 30 seconds; the scalar origin remains fixed and the RBP range advances from it.
- `openspec validate add-review-panels-spectral-metrics --strict` passed.
- `git diff --check` passed. `docs/code-size-policy.json` parses as JSON.
- No backend files changed; formulas, preprocessing contracts, scientific versions, cache identity, and dynamic policy are unchanged.

Manual acceptance remains pending because no local recording was run in this turn. Verify in the WPF data replay page:

1. Seek to 30 seconds and open RBP, FAA, IAPF, Theta/Beta, and Brainbeat one at a time. Confirm each chart starts at 30 seconds, contains no blank 0-to-30-second interval, and keeps its left edge as playback advances. Confirm the selected recording and paused cursor remain unchanged when a panel opens or switches.
2. Play or seek through enough data for complete windows. Confirm values appear at backend window times with the declared unit; rejected or unavailable windows show a gap and a reason.
3. Confirm FAA selects F3 then F4 and Brainbeat selects Fz then Pz from the active montage. Use a montage missing a required role and confirm it reports the missing role without substituting another channel.
4. While each panel is loading, close it or switch to another panel. Confirm the old response does not repopulate the closed panel and waveform playback remains usable.
5. FAA and Brainbeat retain their declared continuous preprocessing behavior. Their first-window wait remains a known limitation of this scope.
