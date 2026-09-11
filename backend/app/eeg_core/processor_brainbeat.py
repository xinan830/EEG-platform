"""BrainBeat, fatigue, and relative-band-power calculations."""

import numpy as np
from scipy import signal

from app.eeg_core.realtime_spectral import residual_spectrum


class ProcessorBrainbeatMixin:
    def _calculate_brainbeat(self, fz_filtered=None, pz_filtered=None):
        if self.buffer_filled < self.window_samples:
            return None
        if not self._brainbeat_epoch_signal_quality_ok(fz_filtered, pz_filtered):
            return self.last_brainbeat

        powers = self._calculate_brainbeat_epoch_powers(fz_filtered, pz_filtered)
        if powers is None:
            return None
        theta_fz, alpha_pz = powers

        log_bb = np.log(theta_fz + self.brainbeat_power_epsilon) - np.log(alpha_pz + self.brainbeat_power_epsilon)
        if self.brainbeat_log_ema is None:
            self.brainbeat_warmup_buf.append(float(log_bb))
            if len(self.brainbeat_warmup_buf) < self.brainbeat_warmup_epochs:
                return None
            self.brainbeat_log_ema = float(np.mean(self.brainbeat_warmup_buf))
        else:
            alpha = self.brainbeat_log_ema_alpha
            self.brainbeat_log_ema = float(alpha * log_bb + (1.0 - alpha) * self.brainbeat_log_ema)
        self.last_brainbeat = float(np.exp(self.brainbeat_log_ema))
        return self.last_brainbeat

    def _calculate_brainbeat_flat(self, fz_filtered=None, pz_filtered=None):
        """去 1/f 残差谱口径的实时脑负荷指数（与原始 brainbeat 并存，仅观察）。

        结构镜像 _calculate_brainbeat，但 θ_Fz/α_Pz 在各自去 1/f 残差谱上算，
        且用独立的 warmup/EMA 状态。1/f 拟合不可用时保留上次（宁缺勿跳变）。
        """
        if self.buffer_filled < self.window_samples:
            return None
        if not self._brainbeat_epoch_signal_quality_ok(fz_filtered, pz_filtered):
            return self.last_brainbeat_flat

        powers = self._calculate_brainbeat_flat_epoch_powers(fz_filtered, pz_filtered)
        if powers is None:
            return self.last_brainbeat_flat
        theta_fz, alpha_pz = powers

        log_bb = np.log(theta_fz + self.brainbeat_power_epsilon) - np.log(alpha_pz + self.brainbeat_power_epsilon)
        if self.brainbeat_flat_log_ema is None:
            self.brainbeat_flat_warmup_buf.append(float(log_bb))
            if len(self.brainbeat_flat_warmup_buf) < self.brainbeat_warmup_epochs:
                return None
            self.brainbeat_flat_log_ema = float(np.mean(self.brainbeat_flat_warmup_buf))
        else:
            alpha = self.brainbeat_log_ema_alpha
            self.brainbeat_flat_log_ema = float(alpha * log_bb + (1.0 - alpha) * self.brainbeat_flat_log_ema)
        self.last_brainbeat_flat = float(np.exp(self.brainbeat_flat_log_ema))
        return self.last_brainbeat_flat

    def _calculate_fatigue_index(self, psd_dict, freqs, theta_band, beta_band):
        """疲劳指数 = 各通道 Theta/Beta 功率比值（Fz/Pz/Oz），仅实时观察、不计入综合分。

        频段沿用与其余指标一致的 IAPF 相对频段（theta=[max(4,iapf-6), iapf-2]、
        beta=[iapf+2, 30]）。比值在**同一通道内**做，相对功率的分母（该通道 1-30Hz
        总功率）会约掉，故直接取两段积分功率之比即可，无需先算相对功率。

        4s 窗逐帧抖动较大，与同族的脑负荷指数一样在 log 域平滑：前
        `brainbeat_warmup_epochs` 帧取均值作 EMA 初值，之后按 `brainbeat_log_ema_alpha`
        更新，再指数还原。本帧无谱的通道（被计算通道选择排除）不更新、保留上次值。

        写入并返回 `self.last_fatigue` 的快照 `{ch: θ/β}`（仍在 warmup 的通道尚未出现）。
        """
        if self.buffer_filled < self.window_samples:
            return dict(self.last_fatigue)
        theta_low, theta_high = theta_band
        beta_low, beta_high = beta_band
        eps = self.brainbeat_power_epsilon
        ema_alpha = self.brainbeat_log_ema_alpha
        for ch in self.fatigue_channels:
            pxx = psd_dict.get(ch)
            if pxx is None:
                continue
            theta_p = self._band_power(freqs, pxx, theta_low, theta_high)
            beta_p = self._band_power(freqs, pxx, beta_low, beta_high)
            if theta_p <= 0.0 or beta_p <= 0.0:
                continue
            log_ratio = float(np.log(theta_p + eps) - np.log(beta_p + eps))
            if self.fatigue_log_ema[ch] is None:
                buf = self.fatigue_warmup_buf[ch]
                buf.append(log_ratio)
                if len(buf) < self.brainbeat_warmup_epochs:
                    continue
                self.fatigue_log_ema[ch] = float(np.mean(buf))
            else:
                self.fatigue_log_ema[ch] = float(
                    ema_alpha * log_ratio + (1.0 - ema_alpha) * self.fatigue_log_ema[ch]
                )
            self.last_fatigue[ch] = float(np.exp(self.fatigue_log_ema[ch]))
        return dict(self.last_fatigue)

    def fatigue_index(self):
        """最近一帧的疲劳指数快照 `{ch: θ/β}`；尚无任何有效值时返回 None。"""
        return dict(self.last_fatigue) if self.last_fatigue else None

    def _band_power(self, freqs, pxx, f_low, f_high):
        idx = np.logical_and(freqs >= f_low, freqs <= f_high)
        if np.sum(idx) < 2:
            return 0.0
        return float(np.trapezoid(pxx[idx], freqs[idx]))

    def _compute_rbp_frame(self, channel_names=None):
        if not self._rbp_active:
            return None

        requested = list(self.rbp_indices.keys()) if channel_names is None else list(channel_names)
        invalid_channels = {}
        band_edges = self._rbp_band_edges()
        bands = {
            name: [float(low), float(high)]
            for name, (low, high) in band_edges.items()
        }
        channels = {}
        n_samples = self.buffer_filled
        buf = self.buffer[-n_samples:] if n_samples else self.buffer[:0]
        nperseg = int(self.sfreq * 2)
        noverlap = nperseg // 2
        min_filtfilt_samples = 3 * max(len(self.a_notch), len(self.b_notch), len(self.a_bp), len(self.b_bp))

        for name in requested:
            idx = self.rbp_indices.get(name)
            if idx is None:
                invalid_channels[name] = "non_eeg_or_unavailable"
                continue
            if n_samples <= min_filtfilt_samples or n_samples < nperseg:
                invalid_channels[name] = "invalid_denominator"
                continue

            # 同 calculate_metrics：buf 来自已连续滤波的 self.buffer，不再重复滤波
            sig = buf[:, idx]
            sig = sig - np.mean(sig) if len(sig) else sig
            freqs, pxx = signal.welch(
                sig,
                self.sfreq,
                nperseg=nperseg,
                noverlap=noverlap,
                window="hann",
            )
            powers = {
                band: self._band_power(freqs, pxx, low, high)
                for band, (low, high) in band_edges.items()
            }
            denominator = sum(powers.values())
            if not np.isfinite(denominator) or denominator <= 0:
                invalid_channels[name] = "invalid_denominator"
                continue

            channels[name] = {
                band: float(powers[band] / denominator)
                for band in bands
            }

        payload = {
            "elapsed_s": self.elapsed_s(),
            "bands": bands,
            "channels": channels,
        }
        if invalid_channels:
            payload["invalid_channels"] = invalid_channels
        return payload

    def _calculate_brainbeat_epoch_powers(self, fz_sig, pz_sig):
        if fz_sig is None or pz_sig is None:
            return None

        iapf = self.iapf_global
        theta_low, theta_high = max(4.0, iapf - 6), iapf - 2
        alpha_low, alpha_high = iapf - 2, iapf + 2

        freqs, fz_psd = self._brainbeat_welch(fz_sig)
        _, pz_psd = self._brainbeat_welch(pz_sig)

        # 为了避免启动阶段数据不稳定或除以极小值导致指标飞飘（数量级过大）
        # 将频段能量结果转化为相对功率（占该通道全频段能量的比值）
        total_fz = self._band_power(freqs, fz_psd, 1.0, 30.0) + self.brainbeat_power_epsilon
        total_pz = self._band_power(freqs, pz_psd, 1.0, 30.0) + self.brainbeat_power_epsilon

        theta_fz = self._band_power(freqs, fz_psd, theta_low, theta_high) / total_fz
        alpha_pz = self._band_power(freqs, pz_psd, alpha_low, alpha_high) / total_pz

        return theta_fz, alpha_pz

    def _calculate_brainbeat_flat_epoch_powers(self, fz_sig, pz_sig):
        """去 1/f 残差谱口径的 θ_Fz/α_Pz 相对功率。

        对 Fz/Pz 各自的 BrainBeat Welch PSD 单独做一次 _fit_spectrum 取 offset/exponent，
        用 residual_spectrum 去 1/f 后再算相对功率（分母为该通道残差 1-30Hz 总功率）。
        在自身 PSD 上拟合并相减以保证量纲自洽；任一通道拟合不可用则返回 None。
        """
        if fz_sig is None or pz_sig is None:
            return None

        iapf = self.iapf_global
        theta_low, theta_high = max(4.0, iapf - 6), iapf - 2
        alpha_low, alpha_high = iapf - 2, iapf + 2

        freqs, fz_psd = self._brainbeat_welch(fz_sig)
        _, pz_psd = self._brainbeat_welch(pz_sig)

        fz_fit = self.summary_estimator._fit_spectrum(freqs, fz_psd)
        pz_fit = self.summary_estimator._fit_spectrum(freqs, pz_psd)
        fz_res = residual_spectrum(freqs, fz_psd, fz_fit.get('aperiodic_offset'), fz_fit.get('aperiodic_exponent'))
        pz_res = residual_spectrum(freqs, pz_psd, pz_fit.get('aperiodic_offset'), pz_fit.get('aperiodic_exponent'))
        if fz_res is None or pz_res is None:
            return None

        eps = self.brainbeat_power_epsilon
        total_fz = self._band_power(freqs, fz_res, 1.0, 30.0) + eps
        total_pz = self._band_power(freqs, pz_res, 1.0, 30.0) + eps
        theta_fz = self._band_power(freqs, fz_res, theta_low, theta_high) / total_fz
        alpha_pz = self._band_power(freqs, pz_res, alpha_low, alpha_high) / total_pz
        return theta_fz, alpha_pz

    def _brainbeat_epoch_signal_quality_ok(self, fz_sig, pz_sig):
        for sig in (fz_sig, pz_sig):
            if sig is None:
                continue
            if np.any(np.abs(sig) > self.brainbeat_artifact_threshold):
                return False
        return True

    def _brainbeat_welch(self, sig):
        nperseg = min(self.brainbeat_welch_nperseg, len(sig))
        noverlap = min(self.brainbeat_welch_noverlap, nperseg - 1)
        nfft = max(self.brainbeat_welch_nfft, nperseg)
        return signal.welch(
            sig,
            self.sfreq,
            nfft=nfft,
            nperseg=nperseg,
            noverlap=noverlap,
            window='hann',
        )
