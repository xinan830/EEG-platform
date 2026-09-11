"""IAPF locking and derived real-time trend state."""

import numpy as np

from app.eeg_core.iapf_diagnostics import (
    iapf_attempt_log_line,
    iapf_waiting_log_line,
)
from app.eeg_core.realtime_spectral import compute_hai


class ProcessorIAPFMixin:
    def _push_iapf_live(self, value):
        """用本窗合格估计直接刷新 `iapf_live`（不再中位数平滑，便于观察逐窗真实变化）。"""
        self.iapf_live = float(value)

    def try_lock_iapf(self, force=False):
        """每 5s 在最近 30s 窗上估计 IAPF：

        - **锁定前**：每个合格窗立即刷新 `iapf_live`；满足最短 5s 数据间隔的合格窗才进入
          锁定候选（间隔与试行间隔一致，即每次试行都够格），累计 `iapf_lock_target_count`(K)
          个候选后取中位数永久锁定 `iapf_global`（无结果的窗跳过、不清零已积累候选）。
          返回锁定事件 result，否则 None。
        - **锁定后**：仅刷新 `iapf_live`（本次过门禁则更新为该值，否则保留上次），`iapf_global` 冻结不变。
          返回 None（不再产生锁定事件）。
        """
        if not force:
            if self.total_samples_seen - self._last_lock_attempt_sample < self.lock_attempt_interval_samples:
                return None
        self._last_lock_attempt_sample = self.total_samples_seen
        if self.segment_samples < self.iapf_window_samples:
            print(
                iapf_waiting_log_line(self.segment_samples, self.iapf_window_samples, self.sfreq),
                flush=True,
            )
            return None  # 30s 窗未攒满
        buf = self.segment_buffer_view()[-self.iapf_window_samples:]
        result = self.summary_estimator.compute(buf, 'lock', duration_s=self.iapf_window_s)
        self.last_iapf_result = result  # 每窗最新结果 → 供 UI「实时分析」动态重绘
        # 1/f 斜率与残差 RBP 同源派生：复用本窗的谱，不再单独滤波/算 PSD。
        # 放在所有分支返回之前——它们与 IAPF 是否锁定、是否过选值门都无关。
        self._derive_aperiodic(result)
        self._derive_hai(result)
        print(
            iapf_attempt_log_line(
                result=result,
                locked=self.iapf_locked,
                candidates=len(self.iapf_lock_candidates),
                target_count=self.iapf_lock_target_count,
                iapf_live=self.iapf_live,
                iapf_global=self.iapf_global,
                segment_samples=self.segment_samples,
                sfreq=self.sfreq,
            ),
            flush=True,
        )

        if self.iapf_locked:
            if result.calibrated:
                self._push_iapf_live(float(result.iapf))  # 每 5s 更新，仅供显示
            return None  # 未过门禁则保留上次 iapf_live

        if not result.calibrated:
            return None  # 本窗无结果：跳过、不清零已积累候选、保留上次 iapf_live
        self._push_iapf_live(float(result.iapf))  # 合格候选立即用于实时显示；global 仍等 K 次后锁定
        candidate_due = (
            self._last_iapf_lock_candidate_sample is None
            or self._iapf_segment_samples_seen - self._last_iapf_lock_candidate_sample
            >= self.iapf_lock_candidate_interval_samples
        )
        if not candidate_due:
            return None
        self.iapf_lock_candidates.append(float(result.iapf))
        self.iapf_lock_candidate_results.append(result)
        self._last_iapf_lock_candidate_sample = self._iapf_segment_samples_seen
        if len(self.iapf_lock_candidates) >= self.iapf_lock_target_count:
            cands = np.asarray(self.iapf_lock_candidates, dtype=float)
            self.iapf_global = float(np.median(cands))
            # 定格快照取「IAPF 最接近中位数的那一窗」，而非最后一窗：保证 IAPF 分析页顶部
            # 锁定值、绿色 FOOOF 峰线、来源与所示频谱同属一窗、完全自洽（K 为奇数时该窗
            # IAPF 恰等于 iapf_global；此窗亦供 calculate_metrics 的 individual_alpha_band）。
            median_idx = int(np.argmin(np.abs(cands - self.iapf_global)))
            lock_snapshot = self.iapf_lock_candidate_results[median_idx]
            self.iapf_live = self.iapf_global  # 锁定瞬间 live 与 global 一致
            self.calibrated = True
            self.iapf_locked = True
            self.last_calibration_result = lock_snapshot
            for _hist in self.stability_alpha_history.values():
                _hist.clear()  # 跨频段切换前清 CV 历史
            return lock_snapshot
        return None

    def _derive_aperiodic(self, result):
        """从一次 IAPF 估计的结果派生 1/f 斜率/截距，供趋势展示。

        **不自行滤波、不自行算 PSD**：逐通道谱与 Pz+Oz 平均谱的拟合全部来自同一次
        `summary_estimator.compute()`，因此实时「皮质兴奋/抑制」与时段报告的同名指标
        按构造相等，并同样享有该路径的伪迹剔除。

        质量门未过的窗没有谱（`channel_psds is None`）→ 本轮不更新任何值，仍推一帧
        携带上次有效值，趋势线走平（低质量原因由常驻的 signal_quality 数字说明）。

        IAPF 的选值门（low_r2 / no_peak_no_cog）不影响本方法：那几种情况谱与拟合都在，
        只是不采纳峰而已，1/f 照常出值。
        """
        psds = (result.channel_psds or {}) if result is not None else {}
        freqs = np.asarray(result.freqs) if result is not None else np.empty(0)
        if psds and freqs.size:
            for ch in self.summary_channels:
                pxx = psds.get(ch)
                if pxx is None:
                    continue
                self._update_aperiodic_latest(
                    ch, self.summary_estimator._fit_spectrum(freqs, pxx))
            # Pz+Oz 合并线直接取本窗 compute() 对后部平均谱的拟合，不重复拟合一次
            self._update_aperiodic_pooled_latest(
                {'aperiodic_exponent': result.aperiodic_exponent,
                 'aperiodic_offset': result.aperiodic_offset})
        out = {ch: self._aperiodic_ema[ch]
               for ch in self.summary_channels if self._aperiodic_ema[ch] is not None}
        self._aperiodic_frame = out or None

    def _derive_hai(self, result):
        """从一次 IAPF 估计的结果派生逐通道 HAI = log10(β/(δ+θ))，供趋势展示。

        **不自行滤波、不自行算 PSD**，也**不去 1/f**：直接用同一次
        `summary_estimator.compute()` 交出的原始逐通道谱 `channel_psds`，
        因此与「皮质兴奋/抑制」同窗、同伪迹剔除、同节拍（30s 窗 / 5s 步进）。

        质量门未过的窗没有谱（`channel_psds is None`）→ 本轮不更新任何通道，仍推一帧
        携带上次有效值，趋势线走平；某通道本窗算不出值时同样保留上次值。

        IAPF 的选值门（low_r2 / no_peak_no_cog）不影响本方法：那几种情况谱还在。
        """
        psds = (result.channel_psds or {}) if result is not None else {}
        freqs = np.asarray(result.freqs) if result is not None else np.empty(0)
        if psds and freqs.size:
            for ch in self.summary_channels:
                pxx = psds.get(ch)
                if pxx is None:
                    continue
                value = compute_hai(freqs, pxx)
                if value is not None:
                    self._hai_latest[ch] = value
        out = {ch: self._hai_latest[ch]
               for ch in self.summary_channels if self._hai_latest[ch] is not None}
        self._hai_frame = out or None

    def hai_index(self):
        """取走本轮 HAI 快照 `{ch: log10(β/(δ+θ))}`；无新帧时返回 None。仅供观察展示。

        值由 `try_lock_iapf` 每次估计后经 `_derive_hai` 同步派生，本方法只负责取。
        **消费一次**：派生节拍（5s）慢于调用方出帧节拍（1s），不置空会让同一时刻被重复追加。
        """
        frame, self._hai_frame = self._hai_frame, None
        return frame

    def estimate_aperiodic_exponents(self):
        """取走本轮 1/f 斜率快照 `{ch: exponent}`；无新帧时返回 None。仅供观察展示。

        值由 `try_lock_iapf` 每次估计后经 `_derive_aperiodic` 同步派生，本方法只负责取。
        **消费一次**：派生节拍（5s）慢于调用方出帧节拍（1s），不置空会让同一时刻被重复追加。
        """
        frame, self._aperiodic_frame = self._aperiodic_frame, None
        return frame

    def _update_aperiodic_latest(self, ch, fit):
        """按 fit 结果更新某通道的 1/f 斜率/截距（截距与斜率各自独立有效性判断）。

        不做平滑：直接写入本次拟合值。本帧拟合不出的量保持上次值不动。
        """
        off = fit.get('aperiodic_offset')
        if off is not None and np.isfinite(off):
            self._aperiodic_offset_ema[ch] = float(off)
        exp = fit.get('aperiodic_exponent')
        if exp is not None and np.isfinite(exp):
            self._aperiodic_ema[ch] = float(exp)

    def _update_aperiodic_pooled_latest(self, fit):
        """更新 Pz+Oz 平均谱拟合的 1/f 斜率/截距（同样不平滑）。"""
        off = fit.get('aperiodic_offset')
        if off is not None and np.isfinite(off):
            self._aperiodic_offset_ema_pooled = float(off)
        exp = fit.get('aperiodic_exponent')
        if exp is not None and np.isfinite(exp):
            self._aperiodic_ema_pooled = float(exp)

    def aperiodic_offsets(self):
        """返回当前各通道 1/f 截距 EMA 快照（由 estimate_aperiodic_exponents 同步更新）。

        仅供观察展示，不计入综合分。无可用值时返回 None。
        """
        out = {ch: self._aperiodic_offset_ema[ch]
               for ch in self.summary_channels if self._aperiodic_offset_ema[ch] is not None}
        return out or None

    def aperiodic_pooled(self):
        """Pz+Oz 平均 PSD 拟合的 1/f 斜率/截距 EMA 快照 `{'exponent','offset'}`。

        两者均由 estimate_aperiodic_exponents 同步更新；都为 None 时返回 None。仅观察。
        """
        exp = self._aperiodic_ema_pooled
        off = self._aperiodic_offset_ema_pooled
        if exp is None and off is None:
            return None
        return {'exponent': exp, 'offset': off}
