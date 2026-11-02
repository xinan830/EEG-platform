# Design

`WaveformDisplayRequestContext` is the cross-consumer publication identity: the local recording-session GUID, the immutable source revision, a monotonically increasing view generation, and a unique request ID. `WaveformDisplayFrame<TPayload>` carries that identity beside a reader-specific, already-complete payload.

`WaveformDisplayFramePublisher` commits only when the candidate identity equals the currently active request and its cancellation token is still active. The consumer remains responsible for validating its payload contract before calling the publisher. Review keeps its bounded frame cache and local reader; TNK keeps its signal-preview API reader and verifies the backend recording/montage response. Both derive the shared recording identity and source revision from the workspace recording-access lease.

This gate covers asynchronous completion. Review cache hits remain governed by the existing cache key and only reuse previously built frames. No time coordinate, sample coordinate, unit, channel list, filter, or scientific result is transformed by the shared contract.
