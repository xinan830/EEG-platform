import numpy as np
import mne

from app.eeg_core.models import CalibrationResult
from app.eeg_core.iapf_quality import (
    alpha_center_of_gravity,
    alpha_residual_ratio,
    annotate_peak_amplitude,
    peak_quality,
    select_alpha_peak,
)

try:
    from specparam import SpectralModel
except ImportError:
    try:
        from fooof import FOOOF as SpectralModel
    except ImportError:
        SpectralModel = None

_NAN = float('nan')

# 唯一质量门的阈值：signal_quality（干净信号比例）低于它则本窗无结果。
# 提为模块常量，便于调用方与测试引用同一个数，不必各处硬写 0.75。
DEFAULT_QUALITY_MIN_RATIO = 0.75


def _as_float(v):
    return float(v) if v is not None else _NAN


class IAPFEstimator:
    def __init__(
        self,
        sfreq,
        ch_names,
        posterior_roi=('Pz', 'Oz'),
        frontal_roi=('Fz',),
        notch_freq=50.0,
        bp_low=1.0,
        bp_high=30.0,
        alpha_search_range=(7.0, 13.0),
        welch_seg_seconds=4.0,
        welch_overlap=0.5,
        artifact_window_seconds=2.0,
        quality_min_ratio=DEFAULT_QUALITY_MIN_RATIO,
        gate_artifact_uv=150.0,
        gate_min_r2=0.80,
        gate_max_mae=0.2,
        gate_peak_prominence_min=0.1,
        peak_cog_agreement_hz=1.0,
        peak_accept_r2=0.80,
        peak_accept_mae=0.20,
        peak_accept_prominence=0.20,
        peak_accept_agreement_hz=0.5,
        fallback_iapf=10.0,
    ):
        self.sfreq = float(sfreq)
        self.ch_names = list(ch_names)
        self.alpha_search_range = alpha_search_range
        self.welch_seg_seconds = float(welch_seg_seconds)
        self.welch_overlap = float(welch_overlap)
        self.artifact_window_seconds = float(artifact_window_seconds)
        # 段内质量门（唯一时长/质量门）：signal_quality = 干净/总，低于此比例则本窗无结果
        # （返回 calibrated=False，由处理层保留上次）。绝对时长门已废除——比例门对任意
        # 窗长/整段时长都是同一个口径，短 buffer 由 _annotate_peak_amplitude 的短干净段
        # 吸收兜底（不足一个 Welch 子段的数据会被整体吞成 BAD → signal_quality=0 → 本门拦下）。
        self.quality_min_ratio = float(quality_min_ratio)
        self.gate_artifact_uv = float(gate_artifact_uv)
        self.gate_min_r2 = float(gate_min_r2)
        self.gate_max_mae = float(gate_max_mae)
        self.gate_peak_prominence_min = float(gate_peak_prominence_min)
        self.peak_cog_agreement_hz = float(peak_cog_agreement_hz)
        # 峰为主、CoG 兜底：仅 peak_accept_prominence 参与采纳（峰显著度达标才取峰，否则退 CoG）。
        # 【已废弃】peak_accept_r2/mae/agreement_hz 不再被任何代码读取（拟合质量统一由全局
        # gate_min_r2/gate_max_mae 负责，一致性门已移除）；保留构造参数与属性仅为兼容旧配置/调用，
        # 不参与任何计算，勿据此推断门控逻辑。
        self.peak_accept_r2 = float(peak_accept_r2)
        self.peak_accept_mae = float(peak_accept_mae)
        self.peak_accept_prominence = float(peak_accept_prominence)
        self.peak_accept_agreement_hz = float(peak_accept_agreement_hz)
        self.fallback_iapf = float(fallback_iapf)
        self.notch_freq = float(notch_freq)
        self.bp_low = float(bp_low)
        self.bp_high = float(bp_high)

        all_upper = [c.upper() for c in ch_names]
        self.posterior_indices = {}
        for name in posterior_roi:
            if name.upper() in all_upper:
                self.posterior_indices[name] = all_upper.index(name.upper())

        self.frontal_indices = {}
        for name in frontal_roi:
            if name.upper() in all_upper:
                self.frontal_indices[name] = all_upper.index(name.upper())
        # PSD 计算通道 = posterior ∪ frontal，保持稳定顺序
        self.psd_names = list(self.posterior_indices.keys()) + [
            n for n in self.frontal_indices if n not in self.posterior_indices
        ]
        self.psd_indices = {n: (self.posterior_indices.get(n, self.frontal_indices.get(n)))
                            for n in self.psd_names}

        if SpectralModel is not None:
            self.fm = SpectralModel(peak_width_limits=[1.0, 8.0], max_n_peaks=4,
                                    peak_threshold=1.5, verbose=False)
        else:
            self.fm = None

    def _buffer_to_mne_raw(self, buffer):
        names = list(self.psd_names)
        picks_idx = [self.psd_indices[name] for name in names]
        data = buffer[:, picks_idx].T  # MNE: (n_channels, n_samples)
        info = mne.create_info(ch_names=names, sfreq=self.sfreq, ch_types='eeg')
        return mne.io.RawArray(data, info, verbose=False)

    def compute(self, buffer, label, duration_s=0.0):
        """Compute IAPF once from a full raw calibration segment."""
        names = list(self.posterior_indices.keys())
        psd_names = list(self.psd_names)

        def _fallback(reason, signal_quality=0.0, total_dur=0.0, clean_dur=0.0,
                      bad_dur=0.0, freqs=None, avg_psd=None):
            f = np.empty(0) if freqs is None else np.asarray(freqs, dtype=float)
            p = np.empty(0) if avg_psd is None else np.asarray(avg_psd, dtype=float)
            return CalibrationResult(
                label=str(label), iapf=self.fallback_iapf, calibrated=False,
                signal_quality=float(signal_quality),
                total_duration_s=float(total_dur), clean_duration_s=float(clean_dur),
                bad_duration_s=float(bad_dur),
                duration_s=float(duration_s), freqs=f, avg_psd=p,
                aperiodic_fit=np.empty(0), model_fit=np.empty(0), fit_freqs=np.empty(0),
                aperiodic_exponent=_NAN, aperiodic_offset=_NAN,
                model_r2=_NAN, model_error=_NAN,
                gaussian_cf=_NAN, peak_power=0.0, peak_bw=_NAN,
                cog=_NAN, gate_failed=reason,
                fz_psd=np.empty(0), pz_psd=np.empty(0),
                peak_exists=False, peak_quality='unavailable',
                alpha_residual_ratio=_NAN,
            )

        if not names or buffer is None or len(buffer) == 0:
            return _fallback('insufficient_segments')

        buffer = np.asarray(buffer, dtype=float)

        # ── Step 1: 构建 MNE Raw ──
        raw = self._buffer_to_mne_raw(buffer)

        # ── Step 2: 整段预处理（只做一次） ──
        raw.notch_filter(self.notch_freq, verbose=False)
        raw.filter(self.bp_low, self.bp_high, verbose=False)

        # ── Step 3: 伪迹标注（滑窗峰值振幅检测 + 短干净段吸收） ──
        bad_annot = self._annotate_peak_amplitude(raw)
        raw.set_annotations(raw.annotations + bad_annot)

        # ── Step 4: 质量门 — 干净信号比例 ──
        # 用样本数而非 raw.times[-1]（=(n-1)/sfreq）算总时长：伪迹标注的 duration 是按
        # 样本数折算的，两者口径必须一致，否则整段皆坏时 clean 会算出负值。
        total_duration = len(raw.times) / self.sfreq
        bad_duration = sum(a['duration'] for a in bad_annot)
        clean_duration = max(0.0, total_duration - bad_duration)

        # 信号质量分：干净信号比例，恒定输出（含未过门的窗），供处理层/UI 判断 IAPF 是否更新
        signal_quality = clean_duration / total_duration if total_duration > 0 else 0.0

        # 唯一质量门：干净占比不足则本窗无结果，处理层保留上一个 IAPF
        if signal_quality < self.quality_min_ratio:
            return _fallback('low_quality', signal_quality=signal_quality,
                             total_dur=total_duration, clean_dur=clean_duration,
                             bad_dur=bad_duration)

        # ── Step 5: Welch PSD（自动跳过 BAD 段） ──
        # 显式 50% 重叠。Step 3 已把短于一个 Welch 子段的干净段吸收成 BAD，故 omit
        # 碎片化后每个干净段都 >= n_fft，不会触发 scipy 的 noverlap >= 缩减后 nperseg。
        n_fft = int(self.sfreq * self.welch_seg_seconds)
        n_overlap = int(round(n_fft * self.welch_overlap))

        psd = raw.compute_psd(
            method='welch',
            fmin=self.bp_low, fmax=self.bp_high,
            n_fft=n_fft,
            n_overlap=n_overlap,
            window='hann',
            reject_by_annotation='omit',
            verbose=False,
        )

        freqs = psd.freqs
        psd_data = psd.get_data()  # (n_psd_channels, n_freqs)，行序 = psd_names
        post_rows = [psd_names.index(n) for n in self.posterior_indices]
        avg_psd = np.maximum(psd_data[post_rows].mean(axis=0), 1e-20)

        def _row(name):
            return (np.maximum(psd_data[psd_names.index(name)], 1e-20)
                    if name in psd_names else np.zeros_like(freqs))
        fz_psd = _row('Fz')
        pz_psd = _row('Pz')
        # 逐通道谱一并交出：1/f 斜率与残差 RBP 直接复用，不再单独滤波/算谱
        channel_psds = {name: _row(name) for name in psd_names}

        # ── Step 6: specparam 拟合 ──
        fit = self._fit_spectrum(freqs, avg_psd)
        gaussian_cf, peak_power, peak_bw, peak_exists = self._select_alpha_peak(fit)
        cog = self._alpha_center_of_gravity(freqs, avg_psd, fit)
        alpha_residual_ratio = self._alpha_residual_ratio(freqs, avg_psd, fit)
        peak_quality = self._peak_quality(peak_exists, peak_power, gaussian_cf, cog)
        model_r2 = fit.get('model_r2')
        model_error = fit.get('model_error')

        # ── Step 7: 全局拟合门 → 主用 FOOOF 峰中心，无合格峰时以 CoG 兜底 ──
        # 拟合质量门（r²/MAE）对峰和 CoG 都生效：模型本身不可信时两者都不采纳。
        # 有合格峰（显著度达 prominence）→ 直接取峰中心；峰缺失/太弱 → 退 CoG；
        # 两者皆无 → 无结果，处理层保留上次。（不再做 |peak−CoG| 一致性门）
        peak_usable = (
            peak_exists
            and gaussian_cf is not None and np.isfinite(float(gaussian_cf))
            and peak_power >= self.peak_accept_prominence
        )
        cog_usable = cog is not None and np.isfinite(float(cog))

        gate_failed = ''
        iapf_source = ''
        iapf = self.fallback_iapf
        if model_r2 is None or model_r2 < self.gate_min_r2:
            gate_failed = 'low_r2'
        elif model_error is None or model_error > self.gate_max_mae:
            gate_failed = 'high_mae'
        elif peak_usable:
            iapf = float(gaussian_cf)   # 主：FOOOF 峰中心
            iapf_source = 'peak'
        elif cog_usable:
            iapf = float(cog)           # 兜底：去 1/f 残差重心
            iapf_source = 'cog'
        else:
            gate_failed = 'no_peak_no_cog'  # 峰与重心皆无 → 无结果，处理层保留上次

        calibrated = gate_failed == ''

        return CalibrationResult(
            label=str(label), iapf=iapf, calibrated=calibrated,
            signal_quality=signal_quality,
            total_duration_s=float(total_duration),
            clean_duration_s=float(clean_duration),
            bad_duration_s=float(bad_duration),
            duration_s=float(duration_s), freqs=freqs, avg_psd=avg_psd,
            aperiodic_fit=np.asarray(fit.get('aperiodic_fit', np.empty(0)), dtype=float),
            model_fit=np.asarray(fit.get('model_fit', np.empty(0)), dtype=float),
            fit_freqs=np.asarray(fit.get('fit_freqs', np.empty(0)), dtype=float),
            aperiodic_exponent=_as_float(fit.get('aperiodic_exponent')),
            aperiodic_offset=_as_float(fit.get('aperiodic_offset')),
            model_r2=_as_float(model_r2),
            model_error=_as_float(model_error),
            gaussian_cf=_as_float(gaussian_cf),
            peak_power=float(peak_power),
            peak_bw=_as_float(peak_bw),
            cog=_as_float(cog),
            gate_failed=gate_failed,
            fz_psd=fz_psd,
            pz_psd=pz_psd,
            peak_exists=bool(peak_exists),
            peak_quality=peak_quality,
            alpha_residual_ratio=_as_float(alpha_residual_ratio),
            iapf_source=iapf_source,
            channel_psds=channel_psds,
        )

    def _fit_spectrum(self, freqs, psd):
        fit = {
            'aperiodic_offset': None, 'aperiodic_exponent': None,
            'model_r2': None, 'model_error': None,
            'aperiodic_fit': np.full_like(freqs, np.nan, dtype=float),
            'model_fit': np.full_like(freqs, np.nan, dtype=float),
            'fit_freqs': freqs, 'peaks': np.empty((0, 3)),
        }
        if self.fm is None:
            return fit
        try:
            self.fm.fit(freqs, psd, [3, 30])
            params = getattr(getattr(self.fm, 'results', None), 'params', None)
            model = getattr(getattr(self.fm, 'results', None), 'model', None)
            metrics = getattr(getattr(self.fm, 'results', None), 'metrics', None)
            if params is not None:
                ap = getattr(params, 'aperiodic', None)
                periodic = getattr(params, 'periodic', None)
                if ap is not None and hasattr(ap, '_fit') and len(ap._fit) >= 2:
                    fit['aperiodic_offset'] = float(ap._fit[0])
                    fit['aperiodic_exponent'] = float(ap._fit[1])
                if periodic is not None and hasattr(periodic, '_fit') and periodic._fit is not None:
                    peaks = np.asarray(periodic._fit, dtype=float)
                    if peaks.ndim == 1 and peaks.size:
                        peaks = peaks.reshape(1, -1)
                    fit['peaks'] = peaks[:, :3] if peaks.size else np.empty((0, 3))
            if metrics is not None:
                for metric in getattr(metrics, 'metrics', []):
                    category = getattr(metric, 'category', '')
                    measure = getattr(metric, 'measure', '')
                    result = getattr(metric, 'result', None)
                    if category == 'gof' and measure == 'rsquared':
                        fit['model_r2'] = float(result)
                    elif category == 'error' and measure == 'mae':
                        fit['model_error'] = float(result)
            if model is not None:
                fit_freqs = freqs[(freqs >= 3) & (freqs <= 30)]
                fit['fit_freqs'] = fit_freqs
                if hasattr(model, '_ap_fit'):
                    fit['aperiodic_fit'] = np.asarray(model._ap_fit, dtype=float)
                if hasattr(model, 'modeled_spectrum'):
                    fit['model_fit'] = np.asarray(model.modeled_spectrum, dtype=float)
        except Exception:
            pass
        return fit

    def _select_alpha_peak(self, fit):
        return select_alpha_peak(fit, self.alpha_search_range)

    def _alpha_center_of_gravity(self, freqs, psd, fit):
        return alpha_center_of_gravity(freqs, psd, fit, self.alpha_search_range)

    def _alpha_residual_ratio(self, freqs, psd, fit):
        return alpha_residual_ratio(freqs, psd, fit, self.alpha_search_range)

    def _peak_quality(self, peak_exists, peak_power, gaussian_cf, cog):
        return peak_quality(
            peak_exists, peak_power, gaussian_cf, cog,
            self.gate_peak_prominence_min, self.peak_cog_agreement_hz,
        )

    def _absorb_short_good_spans(self, bad_mask):
        from app.eeg_core.iapf_quality import absorb_short_good_spans

        absorb_short_good_spans(bad_mask, self.sfreq, self.welch_seg_seconds)

    def _annotate_peak_amplitude(self, raw):
        return annotate_peak_amplitude(
            raw,
            self.sfreq,
            self.artifact_window_seconds,
            self.gate_artifact_uv,
            self.welch_seg_seconds,
        )
