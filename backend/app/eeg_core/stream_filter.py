"""Stateful causal filtering for the legacy real-time EEG processor."""

import numpy as np
from scipy import signal


METRICS_MIN_BP_LOW = 1.0


class StreamFilterMixin:
    """Own streaming SOS state and the filtered sliding window."""

    def _build_stream_filters(self, notch_freq, bp_low, bp_high):
        requested_low = float(bp_low)
        effective_low = max(requested_low, METRICS_MIN_BP_LOW)
        self.metrics_bp_low = effective_low
        self.metrics_bp_low_clamped = effective_low > requested_low
        self.metrics_bp_high = float(bp_high)
        self.metrics_notch = float(notch_freq)
        self._sos_notch = signal.tf2sos(
            *signal.iirnotch(notch_freq, 30.0, self.sfreq)
        )
        self._sos_bp = signal.butter(
            4,
            [effective_low, bp_high],
            btype="bandpass",
            fs=self.sfreq,
            output="sos",
        )

    def _reset_stream_filter_state(self):
        self._zi_notch = None
        self._zi_bp = None
        self._dc_offset = None
        self._filtered_samples = 0

    def _filter_stream(self, chunk):
        """Filter one ``(samples, channels)`` chunk while preserving SOS state."""
        chunk = np.asarray(chunk, dtype=float)
        if self._dc_offset is None:
            self._dc_offset = chunk[0].copy()
        x = (chunk - self._dc_offset).T
        if self._zi_notch is None:
            first = x[:, 0]
            self._zi_notch = (
                signal.sosfilt_zi(self._sos_notch)[:, None, :]
                * first[None, :, None]
            )
            self._zi_bp = (
                signal.sosfilt_zi(self._sos_bp)[:, None, :]
                * first[None, :, None]
            )
        y, self._zi_notch = signal.sosfilt(
            self._sos_notch,
            x,
            axis=-1,
            zi=self._zi_notch,
        )
        y, self._zi_bp = signal.sosfilt(
            self._sos_bp,
            y,
            axis=-1,
            zi=self._zi_bp,
        )
        return y.T

    def _push_raw_ring(self, chunk):
        n = len(chunk)
        if n >= self._raw_ring_capacity:
            self._raw_ring[:] = chunk[-self._raw_ring_capacity:]
            self._raw_ring_filled = self._raw_ring_capacity
        else:
            self._raw_ring = np.roll(self._raw_ring, -n, axis=0)
            self._raw_ring[-n:] = chunk
            self._raw_ring_filled = min(
                self._raw_ring_filled + n,
                self._raw_ring_capacity,
            )

    def _rebuild_filtered_buffer(self):
        """Re-filter retained raw samples after display parameter changes."""
        self._reset_stream_filter_state()
        n = self._raw_ring_filled
        self.buffer.fill(0)
        if n == 0:
            self.buffer_filled = 0
            return
        filtered = self._filter_stream(self._raw_ring[-n:])
        self._filtered_samples = n
        take = min(n, self.window_samples)
        self.buffer[-take:] = filtered[-take:]
        self.buffer_filled = take

    def set_filters(self, notch_freq, bp_low, bp_high):
        """Update filters and rebuild the current window from retained raw data."""
        self.b_notch, self.a_notch = signal.iirnotch(
            notch_freq,
            30.0,
            self.sfreq,
        )
        self.b_bp, self.a_bp = signal.butter(
            4,
            [bp_low, bp_high],
            btype="bandpass",
            fs=self.sfreq,
        )
        self._build_stream_filters(notch_freq, bp_low, bp_high)
        self._rebuild_filtered_buffer()
        for estimator in (self.iapf_estimator, self.summary_estimator):
            estimator.notch_freq = float(notch_freq)
            estimator.bp_low = float(bp_low)
            estimator.bp_high = float(bp_high)

    def push_chunk(self, chunk_np):
        """Filter a chunk once and append it to the processor's sliding window."""
        chunk_len = len(chunk_np)
        if chunk_np.shape[1] > len(self.ch_names):
            chunk_for_buffer = chunk_np[:, :len(self.ch_names)]
        else:
            chunk_for_buffer = chunk_np
        if chunk_len == 0:
            return

        self._push_raw_ring(np.asarray(chunk_for_buffer, dtype=float))
        filtered = self._filter_stream(chunk_for_buffer)
        self._filtered_samples += chunk_len

        if chunk_len >= self.window_samples:
            self.buffer[:] = filtered[-self.window_samples:]
            self.buffer_filled = self.window_samples
        else:
            self.buffer = np.roll(self.buffer, -chunk_len, axis=0)
            self.buffer[-chunk_len:] = filtered
            self.buffer_filled = min(
                self.buffer_filled + chunk_len,
                self.window_samples,
            )

        self.samples_since_update += chunk_len
        self.total_samples_seen += chunk_len

    def should_update(self):
        """Return true after the window, settle period, and update interval fill."""
        return (
            self.buffer_filled == self.window_samples
            and self._filtered_samples
            >= self.window_samples + self.filter_settle_samples
            and self.samples_since_update >= self.update_samples
        )
