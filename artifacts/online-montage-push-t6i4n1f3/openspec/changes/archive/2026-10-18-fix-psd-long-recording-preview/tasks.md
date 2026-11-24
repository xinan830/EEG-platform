# Tasks

- [x] Increase the PSD desktop preview budget within the existing API ceiling.
- [x] Test long dynamic previews, static band shares, seek and rejected gaps.
- [x] Verify the supplied recording artifacts using the new request budget.
- [x] Build WPF, run desktop tests, validate OpenSpec and git diff checks.
- [x] Manually confirm the rebuilt desktop page renders and playback works.

Manual acceptance: the user confirmed all outstanding manual checks passed
on 2026-10-18. This is user-reported acceptance, not an agent-observed GUI or
hardware test; no additional recording, duration, or measurement is inferred.

Verification: WPF builds with zero errors; 14 focused regression tests and
2 temporary real-recording WPF/Chart tests pass. The full desktop suite has
370 passing and 3 failing tests in IAPF visibility, FAA notch capability and
Brainbeat timeline initialization. These are not certified resolved here.
OpenSpec strict validation and git diff checks pass. The personal recording
and the temporary real-data test remain outside the repository.
