# Keep timeline position during seek

- Timeline seeking now commits the selected start position immediately in the waveform panel.
- The parent still fetches the corresponding waveform window asynchronously.
- The returned window remains the final authority and can correct the optimistic position after sample-index quantization.
- This prevents the overview slider from snapping back to the old position while a seek request is in flight.
