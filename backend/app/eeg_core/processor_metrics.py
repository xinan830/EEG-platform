"""Per-frame score calculation for the legacy real-time processor."""

import numpy as np
from scipy import signal

from app.eeg_core.realtime_spectral import (
    band_relative_power,
    clip_map,
    logistic_map,
)


class ProcessorMetricsMixin:
    def calculate_metrics(self):
        """Perform signal processing and calculate biofeedback metrics."""
        self.samples_since_update = 0
        self._meditation_epoch_count += 1

        req_data = {}
        for name, idx in self.req_indices.items():
            if not self._is_computation_channel(name):
                continue
            # buffer 已是连续滤波后的数据（push_chunk），此处只去残余直流：
            # 1Hz 高通之后均值本就近似 0，这一步是低通截止很低时的兜底。
            sig = self.buffer[:, idx]
            sig = sig - np.mean(sig)
            req_data[name] = sig

        # 注：不再因伪迹跳过整帧——每秒都需输出一个指数。单帧伪迹由下游
        # 综合分的 median(最近5)+惯性步进自然抹平；BrainBeat 仍有自身质量门（保留上值）。

        # Compute PSD using Welch — 2s segments with 50% overlap over the 4s buffer (~3 segments)
        psd_dict = {}
        freqs = None
        _nperseg = int(self.sfreq * 2)
        _noverlap = _nperseg // 2
        for name, sig in req_data.items():
            n = len(sig)
            seg = min(_nperseg, n)
            ovlp = min(_noverlap, seg - 1)
            f, pxx = signal.welch(sig, self.sfreq, nperseg=seg, noverlap=ovlp, window='hann')
            psd_dict[name] = pxx
            freqs = f

        if freqs is None:
            return None, None, None, None, None

        def get_power(pxx, f_low, f_high):
            idx = np.logical_and(freqs >= f_low, freqs <= f_high)
            if np.sum(idx) < 2:
                return 0.0
            return np.trapezoid(pxx[idx], freqs[idx])

        iapf = self.iapf_global
        if self.last_calibration_result is not None:
            r = self.last_calibration_result
            iapf_info = {
                'iapf_global': r.iapf,
                'calibrated': r.calibrated,
                'iapf_signal_quality': r.signal_quality,
                'individual_alpha_band': (r.iapf - 2.0, r.iapf + 2.0),
            }
        else:
            iapf_info = {
                'iapf_global': self.iapf_global,
                'calibrated': False,
                'iapf_signal_quality': 0.0,
                'individual_alpha_band': (self.iapf_global - 2.0, self.iapf_global + 2.0),
            }
        visualization = {}

        # Dynamic bands based on IAPF
        theta_low = max(4.0, iapf - 6)     # Bug ②: floor at 4Hz (matches BrainBeat & baseline)
        theta_high = iapf - 2
        alpha_low = iapf - 2
        alpha_high = iapf + 2
        beta_low = iapf + 2
        beta_high = 30.0

        # Metrics Calculation
        relax_total = relax_iapf = 0
        active_occipital_alpha_roi = self._available_channels(self.metric_occipital_alpha_roi)

        for ch in active_occipital_alpha_roi:
            if ch in psd_dict:
                relax_iapf += get_power(psd_dict[ch], iapf - 1, iapf + 1)
                relax_total += get_power(psd_dict[ch], 1, 30)
        relaxation = relax_iapf / relax_total if relax_total > 0 else 0

        brainbeat = self._calculate_brainbeat(
            fz_filtered=req_data.get('Fz'),
            pz_filtered=req_data.get('Pz'),
        )
        brainbeat_flat = self._calculate_brainbeat_flat(
            fz_filtered=req_data.get('Fz'),
            pz_filtered=req_data.get('Pz'),
        )
        # 疲劳指数（θ/β，逐通道）：与本帧其余指标共用同一份 psd_dict 与 IAPF 相对频段
        self._calculate_fatigue_index(
            psd_dict, freqs,
            theta_band=(theta_low, theta_high),
            beta_band=(beta_low, beta_high),
        )

        # 空间分布 = Fz alpha 相对功率 / Pz/Oz 平均 PSD 的 alpha 相对功率
        frontal_alpha_chs = [ch for ch in self.metric_frontal_alpha_roi if ch in psd_dict]
        active_occipital_in_psd = [ch for ch in active_occipital_alpha_roi if ch in psd_dict]

        frontal_alpha_ratio = 0.0
        if frontal_alpha_chs:
            frontal_psd = np.mean(np.vstack([psd_dict[ch] for ch in frontal_alpha_chs]), axis=0)
            frontal_total = get_power(frontal_psd, 1, 30)
            if frontal_total > 0:
                frontal_alpha_ratio = get_power(frontal_psd, alpha_low, alpha_high) / frontal_total

        posterior_alpha_ratio = 0.0
        if active_occipital_in_psd:
            posterior_stack = np.vstack([psd_dict[ch] for ch in active_occipital_in_psd])
            posterior_psd = np.mean(posterior_stack, axis=0)
            posterior_total_for_sd = get_power(posterior_psd, 1, 30)
            if posterior_total_for_sd > 0:
                posterior_alpha_ratio = get_power(posterior_psd, alpha_low, alpha_high) / posterior_total_for_sd
        sd_floor = 0.02  # 后枕 alpha 相对功率地板；低于此判 SD 不可靠
        sd_valid = posterior_alpha_ratio >= sd_floor
        sd = frontal_alpha_ratio / posterior_alpha_ratio if posterior_alpha_ratio > 0 else 0

        # 节律稳定性：Fz/Pz/Oz 各自的 Narrow Alpha [iapf-1,iapf+1] 相对功率历史独立求 CV 稳定性分，
        # 再按 self.stability_weights（Fz 权重较低，易受额肌/眼电伪迹污染）加权平均；
        # 缺失的通道（被计算通道选择排除）不参与本帧历史更新，权重在在场通道间重新归一化。
        _stability_min_samples = 8
        _stability_neutral = (0.40 + 0.90) / 2.0  # 映射后正好是 clip_map 中点 50 分
        per_channel_stability = {}
        for ch in self.stability_channels:
            if ch in psd_dict:
                narrow_total = get_power(psd_dict[ch], 1, 30)
                narrow_ratio = (get_power(psd_dict[ch], iapf - 1, iapf + 1) / narrow_total
                                if narrow_total > 0 else 0.0)
                self.stability_alpha_history[ch].append(float(narrow_ratio))
            hist = self.stability_alpha_history[ch]
            if len(hist) >= _stability_min_samples:
                arr = np.asarray(hist, dtype=float)
                mean = float(np.mean(arr))
                cv = float(np.std(arr) / mean) if mean > 0 else 1.0
                # 1/(1+CV) 而非 max(0,1-CV)：CV>=1 时后者直接清零、稍不稳定就 0 分；
                # 前者随 CV 平滑衰减、渐近趋近 0 但不会轻易触底。
                per_channel_stability[ch] = 1.0 / (1.0 + cv)
            else:
                # Insufficient data — return neutral midpoint; avoids false 100 from
                # near-zero CV when consecutive samples are all carry-over baseline data.
                per_channel_stability[ch] = _stability_neutral

        active_stability_chs = [ch for ch in self.stability_channels if ch in psd_dict]
        if active_stability_chs:
            weight_sum = sum(self.stability_weights[ch] for ch in active_stability_chs)
            stability_metric = sum(
                self.stability_weights[ch] * per_channel_stability[ch] for ch in active_stability_chs
            ) / weight_sum
            stability_ready = all(
                len(self.stability_alpha_history[ch]) >= _stability_min_samples for ch in active_stability_chs
            )
        else:
            stability_metric = _stability_neutral
            stability_ready = False
        metrics = {
            '放松度': logistic_map(relaxation, center=0.15, slope=0.055),
            '空间分布': logistic_map(sd, center=0.45, slope=0.15),
            '节律稳定性': clip_map(stability_metric, 0.40, 0.90),
            '_sd_valid': bool(sd_valid),
            '_stability_ready': bool(stability_ready),
        }
        if brainbeat is not None:
            metrics['brainbeat'] = brainbeat
        if brainbeat_flat is not None:
            metrics['brainbeat_flat'] = brainbeat_flat

        rbp = self._compute_rbp_frame()
        return metrics, iapf, iapf_info, visualization, rbp
