# Design

## Root cause addressed

`LocalAcquisitionRawWriter` previously wrote the complete sample payload with synchronous `Stream.Write` inside the serialized acquisition batch path. At high channel count/sample rate, storage latency could delay the vendor read loop. Separately, `LatestWaveformFrameWorker` discarded every completion whose request sequence was no longer latest; when frame construction exceeded the 33 ms refresh interval, the renderer could starve indefinitely.

## Behavior

- Raw sample payloads use cancellable asynchronous stream writes with a pooled byte buffer. The bytes and ordering remain identical.
- A completed display frame may be delivered even when a newer request exists. `LiveSciChartCanvas` accepts it unless it would move an unchanged montage/page backward in sample-counter time.
- A null display frame does not clear an already committed complete frame. Initial empty state behavior is unchanged.
- Real counter discontinuities and non-finite values continue to create explicit segment breaks. No samples are synthesized.

## Boundaries and risks

This does not prove that a physical device or USB driver cannot drop samples. The raw audit and sample-counter continuity path remain the evidence for that distinction. Python display filtering remains optional and cannot block raw persistence.
