"""Global mutable state controls for ``EEGProcessor``."""

from app.eeg_core.realtime_spectral import RBP_BAND_EDGES


RBP_EXCLUDED_CHANNELS = {
    "TRIGGER", "STATUS", "REF", "GND", "A1", "A2", "M1", "M2",
}
RBP_EXCLUDED_PREFIXES = (
    "TRIGGER", "STATUS", "SAMPLE", "COUNTER", "EOG", "ECG", "EKG", "EXG",
)


class ProcessorStateMixin:
    def set_computation_channels(self, channel_names):
        if channel_names is None:
            self.computation_channels = None
        else:
            selected = {str(name).upper() for name in channel_names}
            self.computation_channels = {
                canonical
                for canonical in self.req_indices.keys()
                if canonical.upper() in selected
            }
        self.last_iapf_update_sample = None
        self.last_iapf_info = None
        self.last_visualization = None

    def reset_buffers(self):
        self.buffer.fill(0)
        self.calib_buffer.fill(0)
        self._raw_ring.fill(0)
        self._raw_ring_filled = 0
        self._reset_stream_filter_state()
        self.buffer_filled = 0
        self.calib_buffer_filled = 0
        self.calib_buffer_write_pos = 0
        self.samples_since_update = 0
        self.total_samples_seen = 0
        self.last_iapf_update_sample = None
        self.last_iapf_info = None
        self.last_visualization = None
        for history in self.stability_alpha_history.values():
            history.clear()
        self.calibrated = False
        self.last_calibration_result = None
        self.iapf_global = 10.0
        self.iapf_live = 10.0
        self.last_iapf_result = None
        self.iapf_locked = False
        self.iapf_lock_candidates = []
        self.iapf_lock_candidate_results = []
        self._last_lock_attempt_sample = -10**12
        self._iapf_segment_samples_seen = 0
        self._last_iapf_lock_candidate_sample = None
        self._meditation_epoch_count = 0
        self._meditation_start_sample = 0
        self._rbp_active = False
        self._rbp_start_sample = 0
        self.brainbeat_warmup_buf = []
        self.brainbeat_log_ema = None
        self.last_brainbeat = None
        self.brainbeat_flat_warmup_buf = []
        self.brainbeat_flat_log_ema = None
        self.last_brainbeat_flat = None
        self.fatigue_warmup_buf = {ch: [] for ch in self.fatigue_channels}
        self.fatigue_log_ema = {ch: None for ch in self.fatigue_channels}
        self.last_fatigue = {}
        self.segment_chunks = []
        self.segment_samples = 0
        self.segment_active = False
        self._faa_chunks = []
        self._faa_samples = 0
        self._faa_truncated = False
        self._aperiodic_ema = {ch: None for ch in self.summary_channels}
        self._aperiodic_offset_ema = {ch: None for ch in self.summary_channels}
        self._aperiodic_ema_pooled = None
        self._aperiodic_offset_ema_pooled = None
        self._aperiodic_frame = None
        self._hai_latest = {ch: None for ch in self.summary_channels}
        self._hai_frame = None

    def start_meditation(self):
        self._meditation_epoch_count = 0
        self._meditation_start_sample = self.total_samples_seen
        for history in self.stability_alpha_history.values():
            history.clear()

    def start_rbp_session(self):
        self._rbp_active = True
        self._rbp_start_sample = self.total_samples_seen

    def stop_rbp_session(self):
        self._rbp_active = False

    def elapsed_s(self):
        return float(
            (self.total_samples_seen - self._rbp_start_sample) / self.sfreq
        )

    def _is_rbp_channel(self, channel_name):
        normalized = str(channel_name).strip().upper().replace(" ", "")
        if normalized in RBP_EXCLUDED_CHANNELS:
            return False
        return not any(
            normalized.startswith(prefix) for prefix in RBP_EXCLUDED_PREFIXES
        )

    def _rbp_band_edges(self):
        return dict(RBP_BAND_EDGES)

    def _is_computation_channel(self, channel_name):
        return (
            self.computation_channels is None
            or channel_name in self.computation_channels
        )

    def _available_channels(self, channel_names):
        return [
            channel
            for channel in channel_names
            if channel in self.req_indices
            and self._is_computation_channel(channel)
        ]
