"""Session buffers and report lifecycles for ``EEGProcessor``."""

import numpy as np
from scipy import signal

from app.eeg_core.faa import compute_faa as compute_faa_shared
from app.eeg_core.realtime_spectral import segment_brainbeat


class ProcessorSessionMixin:
    """Manage calibration, analysis-period, RBP, and FAA session state."""

    def start_calibration(self):
        self.calib_buffer.fill(0)
        self.calib_buffer_filled = 0
        self.calib_buffer_write_pos = 0
        self.calibrating = True
        self.calibrated = False

    def add_calib_chunk(self, chunk):
        if not self.calibrating:
            return
        if chunk.shape[1] > len(self.ch_names):
            chunk = chunk[:, :len(self.ch_names)]
        n = len(chunk)
        cap = len(self.calib_buffer)
        if n >= cap:
            self.calib_buffer[:] = chunk[-cap:]
            self.calib_buffer_filled = cap
            self.calib_buffer_write_pos = 0
        else:
            write_pos = self.calib_buffer_write_pos
            first = min(n, cap - write_pos)
            self.calib_buffer[write_pos:write_pos + first] = chunk[:first]
            remaining = n - first
            if remaining:
                self.calib_buffer[:remaining] = chunk[first:]
            self.calib_buffer_write_pos = (write_pos + n) % cap
            self.calib_buffer_filled = min(
                self.calib_buffer_filled + n,
                cap,
            )

    def calibration_buffer_view(self):
        if self.calib_buffer_filled < len(self.calib_buffer):
            return self.calib_buffer[:self.calib_buffer_filled]
        pos = self.calib_buffer_write_pos
        if pos == 0:
            return self.calib_buffer
        return np.concatenate(
            [self.calib_buffer[pos:], self.calib_buffer[:pos]],
            axis=0,
        )

    def finalize_calibration(self, label, duration_s=0.0):
        buf = self.calibration_buffer_view()
        result = self.iapf_estimator.compute(buf, label, duration_s=duration_s)
        self.iapf_global = result.iapf
        self.last_calibration_result = result
        self.calibrating = False
        self.calibrated = True
        return result

    def start_segment(self):
        self.segment_chunks = []
        self.segment_samples = 0
        self.segment_active = True

    def add_segment_chunk(self, chunk):
        if not self.segment_active:
            return
        if chunk.shape[1] > len(self.ch_names):
            chunk = chunk[:, :len(self.ch_names)]
        self.segment_chunks.append(
            np.asarray(chunk[:, self.summary_col_idx], dtype=float)
        )
        self.segment_samples += len(chunk)
        self._iapf_segment_samples_seen += len(chunk)
        while (
            self.segment_samples > self.segment_sample_cap
            and len(self.segment_chunks) > 1
        ):
            dropped = self.segment_chunks.pop(0)
            self.segment_samples -= len(dropped)

    def segment_buffer_view(self):
        if not self.segment_chunks:
            return np.zeros((0, len(self.summary_channels)))
        return np.concatenate(self.segment_chunks, axis=0)

    def finalize_segment(self, label, set_global=False):
        buf = self.segment_buffer_view()
        duration_s = self.segment_samples / self.sfreq
        result = self.summary_estimator.compute(buf, label, duration_s=duration_s)
        self.segment_active = False
        if set_global:
            self.iapf_global = result.iapf
            self.last_calibration_result = result
            self.calibrated = True
        return result

    def start_period(self):
        self._period_chunks = []
        self._period_samples = 0

    def add_period_chunk(self, chunk):
        if chunk.shape[1] > len(self.ch_names):
            chunk = chunk[:, :len(self.ch_names)]
        self._period_chunks.append(
            np.asarray(chunk[:, self.summary_col_idx], dtype=float)
        )
        self._period_samples += len(chunk)

    def compute_period_report(self, label):
        if not self._period_chunks:
            return None
        buf = np.concatenate(self._period_chunks, axis=0)
        duration_s = self._period_samples / self.sfreq
        result = self.summary_estimator.compute(buf, label, duration_s=duration_s)
        brainbeat = None
        if result.fz_psd is not None and result.pz_psd is not None:
            brainbeat = segment_brainbeat(
                result.freqs,
                result.fz_psd,
                result.pz_psd,
                result.iapf,
            )
        return {
            "iapf": float(result.iapf),
            "cortical_excitation": float(result.aperiodic_exponent),
            "brainbeat": brainbeat,
            "result": result,
        }

    def start_faa_period(self):
        self._faa_chunks = []
        self._faa_samples = 0
        self._faa_truncated = False

    def add_faa_period_chunk(self, chunk):
        if not self.faa_available:
            return
        if chunk.shape[1] > len(self.ch_names):
            chunk = chunk[:, :len(self.ch_names)]
        if self._faa_samples >= self.faa_period_cap_samples:
            self._faa_truncated = True
            return
        self._faa_chunks.append(
            np.asarray(chunk[:, self.faa_col_idx], dtype=float)
        )
        self._faa_samples += len(chunk)

    def _faa_filter(self, buf):
        x = np.asarray(buf, dtype=float)
        if x.size == 0:
            return None
        x = x - x.mean(axis=0, keepdims=True)
        min_len = 3 * (
            2 * max(len(self._sos_notch), len(self._sos_bp)) + 1
        )
        if len(x) <= min_len:
            return None
        y = signal.sosfiltfilt(self._sos_notch, x, axis=0)
        return signal.sosfiltfilt(self._sos_bp, y, axis=0)

    def compute_faa_report(self, label):
        if not self.faa_available or not self._faa_chunks:
            return None
        buf = np.concatenate(self._faa_chunks, axis=0)
        duration_s = self._faa_samples / self.sfreq
        report = {
            "label": str(label),
            "duration_s": float(duration_s),
            "discard_s": float(self.faa_discard_s),
            "analyzed_s": 0.0,
            "truncated": bool(self._faa_truncated),
            "notch": float(self.metrics_notch),
            "bp_low": float(self.metrics_bp_low),
            "bp_high": float(self.metrics_bp_high),
            "faa": None,
            "p_f3": None,
            "p_f4": None,
            "total_epochs": 0,
            "clean_epochs": 0,
            "clean_ratio": 0.0,
            "reason": "too_short",
        }
        filtered = self._faa_filter(buf)
        if filtered is None:
            return report
        start = int(round(self.faa_discard_s * self.sfreq))
        segment = filtered[start:]
        if len(segment) == 0:
            return report
        report["analyzed_s"] = float(len(segment) / self.sfreq)
        report.update(
            compute_faa_shared(
                segment[:, 0],
                segment[:, 1],
                self.sfreq,
                artifact_uv=self.artifact_threshold_uv,
            )
        )
        return report
