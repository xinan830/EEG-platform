import numpy as np
from scipy import signal
from collections import deque

from app.eeg_core.iapf_estimator import IAPFEstimator

RBP_EXCLUDED_CHANNELS = {"TRIGGER", "STATUS", "REF", "GND", "A1", "A2", "M1", "M2"}
RBP_EXCLUDED_PREFIXES = ("TRIGGER", "STATUS", "SAMPLE", "COUNTER", "EOG", "ECG", "EKG", "EXG")

RBP_BAND_EDGES = {
    "delta": (1.0, 4.0),
    "theta": (4.0, 8.0),
    "alpha": (8.0, 13.0),
    "beta": (13.0, 30.0),
}

# 实时指标链路的低截止下限。UI 的带通控件允许低到 0.1Hz，但指标这一路必须钳在 1Hz：
# 1) 最低频段 delta 的下沿就是 1Hz（RBP_BAND_EDGES），再低没有任何频段用得上；
# 2) 4s 窗 / 2s 子段 Welch 的分辨率是 0.5Hz，1Hz 以下只有一两个频点，估计不可信；
# 3) `filter_settle_s` 是按 1Hz 拐点定的（约 3 个周期）。拐点每降一个数量级建立时间
#    就涨一个数量级，0.1Hz 需要约 30s，settle 门会失效、瞬态会漏进 BrainBeat warmup。
# 波形显示与 IAPF 路径不受此钳制（各自另有量程需求，见 AGENTS.md §5.2）。
METRICS_MIN_BP_LOW = 1.0


def band_power(freqs, pxx, f_low, f_high):
    """整段 PSD 上 [f_low, f_high] 的梯形积分功率；点数不足返回 0。"""
    freqs = np.asarray(freqs, dtype=float)
    pxx = np.asarray(pxx, dtype=float)
    idx = np.logical_and(freqs >= f_low, freqs <= f_high)
    if np.sum(idx) < 2:
        return 0.0
    return float(np.trapezoid(pxx[idx], freqs[idx]))


def band_relative_power(freqs, pxx):
    """δ/θ/α/β 相对功率，和为 1；总功率<=0 时全 0。"""
    powers = {
        name: band_power(freqs, pxx, low, high)
        for name, (low, high) in RBP_BAND_EDGES.items()
    }
    total = sum(powers.values())
    if not np.isfinite(total) or total <= 0:
        return {name: 0.0 for name in RBP_BAND_EDGES}
    return {name: powers[name] / total for name in RBP_BAND_EDGES}


# HAI（快慢波比）频段：固定经典划分，**不随 IAPF 变动**——避免 IAPF 从 10Hz
# 默认值锁定到真实值的瞬间，在 HAI 曲线上留下一个纯属人为的台阶。
# 分母写成 [1,8] 单段，而非 delta[1,4]+theta[4,8] 两段相加。两者结果**完全相等**：
# band_power 用梯形积分，相邻区间共享的 4.0Hz 端点在两段里各占一半权重，合起来正好
# 是一份（RBP 面板把四个频段相加当分母，同理无重复计入）。单段只是更直接。
HAI_LOW_BAND = (1.0, 8.0)     # delta + theta
HAI_BETA_BAND = (13.0, 25.0)


def compute_hai(freqs, pxx):
    """HAI = log10(Power[13,25] / Power[1,8])，用**原始 PSD**（不去 1/f）。

    任一频段功率 <=0（频点不足 2 个，例如带通被改窄到截断了该频段）或结果非有限
    时返回 None，由调用方决定沿用上次值。
    """
    beta = band_power(freqs, pxx, *HAI_BETA_BAND)
    low = band_power(freqs, pxx, *HAI_LOW_BAND)
    if beta <= 0.0 or low <= 0.0:
        return None
    value = float(np.log10(beta / low))
    return value if np.isfinite(value) else None


# ── 幅度伪迹阈值 ────────────────────────────────────────────────────────
# 单一常量：BrainBeat 的逐帧质量门与 FAA 的逐 epoch 判定共用同一个 ±150µV 门限，
# 避免同一段数据在不同指标里按两套阈值判「坏」。
ARTIFACT_THRESHOLD_UV = 150.0


# ── FAA（额叶 alpha 不对称）────────────────────────────────────────────
# FAA = ln(P_F4) − ln(P_F3)，只在「开始X/结束X」两个事件之间整段算一次，
# 不进入每秒出帧的实时链路（单窗 FAA 噪声大，文献口径本就是整段平均）。
FAA_CHANNELS = ('F3', 'F4')
FAA_BAND = (8.0, 13.0)          # 固定经典 alpha，不随 IAPF 变动（FAA 文献口径）
FAA_DISCARD_S = 12.0            # 丢弃开头过渡期：体位调整、眨眼未平息（10–15s 取中值）
FAA_EPOCH_S = 2.0               # epoch 长度（0.5Hz 分辨率，alpha 带内 11 个频点）
FAA_EPOCH_OVERLAP = 0.5
FAA_MIN_CLEAN_EPOCHS = 10       # 干净 epoch 少于此数不出值（≈12s 有效数据）
FAA_PERIOD_CAP_S = 1800.0       # 单时段缓冲上限，防止漏点「结束X」时无限增长


def faa_epoch_bounds(n_samples, epoch_samples, step_samples):
    """切 epoch 的 [start, stop) 列表；不足一个完整 epoch 返回空（末尾零头丢弃）。"""
    if epoch_samples <= 0 or step_samples <= 0 or n_samples < epoch_samples:
        return []
    return [(start, start + epoch_samples)
            for start in range(0, n_samples - epoch_samples + 1, step_samples)]


def compute_faa(f3, f4, sfreq, epoch_s=FAA_EPOCH_S, overlap=FAA_EPOCH_OVERLAP,
                artifact_uv=ARTIFACT_THRESHOLD_UV, band=FAA_BAND,
                min_clean_epochs=FAA_MIN_CLEAN_EPOCHS):
    """一段**已滤波**的 F3/F4 信号 → FAA = ln(P_F4) − ln(P_F3)。

    切 2s / 50% 重叠的 Hann 窗 epoch → 逐 epoch 幅度伪迹判定（|x| 超阈值整个 epoch
    作废，且 **F3/F4 成对作废**：两通道必须落在同一批时间片上，否则两侧功率来自不同
    时段，作差没有意义）→ 干净 epoch 各自 FFT → **线性功率域**平均得平均 PSD →
    8–13Hz 梯形积分 → 取对数作差。

    输入单位为伏特（与数据源一致），阈值以 µV 给出。恒返回 dict、不抛异常：
    `faa is None` 时 `reason` 说明原因（too_short / too_few_clean_epochs /
    band_truncated / non_finite）。
    """
    f3 = np.asarray(f3, dtype=float).ravel()
    f4 = np.asarray(f4, dtype=float).ravel()
    n = int(min(len(f3), len(f4)))
    epoch_samples = int(round(float(sfreq) * float(epoch_s)))
    step = max(1, int(round(epoch_samples * (1.0 - float(overlap)))))
    bounds = faa_epoch_bounds(n, epoch_samples, step)
    report = {
        'faa': None, 'p_f3': None, 'p_f4': None,
        'total_epochs': len(bounds), 'clean_epochs': 0, 'clean_ratio': 0.0,
        'epoch_s': float(epoch_s), 'artifact_uv': float(artifact_uv),
        'band': (float(band[0]), float(band[1])), 'reason': '',
    }
    if not bounds:
        report['reason'] = 'too_short'
        return report

    threshold = float(artifact_uv) * 1e-6
    clean_f3, clean_f4 = [], []
    for start, stop in bounds:
        # 逐 epoch 去直流后判幅度：残余直流是电极漂移，不该被当成伪迹幅度计入
        a = f3[start:stop] - np.mean(f3[start:stop])
        b = f4[start:stop] - np.mean(f4[start:stop])
        if np.max(np.abs(a)) > threshold or np.max(np.abs(b)) > threshold:
            continue
        clean_f3.append(a)
        clean_f4.append(b)
    report['clean_epochs'] = len(clean_f3)
    report['clean_ratio'] = len(clean_f3) / len(bounds)
    if len(clean_f3) < int(min_clean_epochs):
        report['reason'] = 'too_few_clean_epochs'
        return report

    window = signal.get_window('hann', epoch_samples)
    # 单边功率谱密度标定，与 scipy.signal.welch(scaling='density') 同口径
    scale = 1.0 / (float(sfreq) * float(np.sum(window ** 2)))
    freqs = np.fft.rfftfreq(epoch_samples, d=1.0 / float(sfreq))

    def _mean_psd(epochs):
        spec = np.fft.rfft(np.vstack(epochs) * window, axis=-1)
        pxx = (np.abs(spec) ** 2) * scale
        # 折叠负频：DC 与（偶数长度时的）Nyquist 不翻倍，其余翻倍
        if epoch_samples % 2 == 0:
            pxx[:, 1:-1] *= 2.0
        else:
            pxx[:, 1:] *= 2.0
        return pxx.mean(axis=0)   # 线性功率域平均（不是 log 域）

    psd_f3 = _mean_psd(clean_f3)
    psd_f4 = _mean_psd(clean_f4)
    p_f3 = band_power(freqs, psd_f3, *band)
    p_f4 = band_power(freqs, psd_f4, *band)
    if p_f3 <= 0.0 or p_f4 <= 0.0:
        report['reason'] = 'band_truncated'   # 带通被改窄到截断了 alpha
        return report
    faa = float(np.log(p_f4) - np.log(p_f3))
    if not np.isfinite(faa):
        report['reason'] = 'non_finite'
        return report
    report.update({'faa': faa, 'p_f3': float(p_f3), 'p_f4': float(p_f4)})
    return report


def residual_spectrum(freqs, psd, offset, exponent):
    """去 1/f 线性残差谱 clip(PSD - 10^(offset - exponent*log10 f), 0)；参数无效返回 None。

    非周期成分为参数化幂律，对任意 f 有定义，故用 3-30Hz 拟合出的 offset/exponent
    外推到 1Hz，在完整频段逐点相减（负值截零）——delta(1-4Hz) 全段可参与。
    """
    if offset is None or exponent is None or not (np.isfinite(offset) and np.isfinite(exponent)):
        return None
    freqs = np.asarray(freqs, dtype=float)
    psd = np.asarray(psd, dtype=float)
    valid = freqs > 0
    ap_lin = np.zeros_like(freqs)
    ap_lin[valid] = np.power(10.0, offset - exponent * np.log10(freqs[valid]))
    return np.clip(psd - ap_lin, 0.0, None)


def segment_brainbeat(freqs, fz_psd, pz_psd, iapf, epsilon=1e-20):
    """整段单值脑负荷 = θ_Fz / α_Pz（相对功率口径），频段由本期 IAPF 决定。"""
    theta_low, theta_high = max(4.0, iapf - 6.0), iapf - 2.0
    alpha_low, alpha_high = iapf - 2.0, iapf + 2.0
    total_fz = band_power(freqs, fz_psd, 1.0, 30.0) + epsilon
    total_pz = band_power(freqs, pz_psd, 1.0, 30.0) + epsilon
    theta_fz = band_power(freqs, fz_psd, theta_low, theta_high) / total_fz
    alpha_pz = band_power(freqs, pz_psd, alpha_low, alpha_high) / total_pz
    return float(theta_fz / (alpha_pz + epsilon))


def clip_map(v, v_min, v_max):
    """Map a value into a 0-100 score range based on empirical bounds."""
    if np.isnan(v) or np.isinf(v):
        return 0
    mapped = (v - v_min) / (v_max - v_min) * 100
    return max(0, min(100, mapped))


def logistic_map(v, center, slope):
    """Map a value into 0-100 via a logistic (sigmoid) curve."""
    if np.isnan(v) or np.isinf(v):
        return 0
    return 100.0 / (1.0 + np.exp(-(v - center) / slope))


def _fmt_diag(value, digits=3):
    try:
        v = float(value)
    except (TypeError, ValueError):
        return "nan"
    if not np.isfinite(v):
        return "nan"
    return f"{v:.{digits}f}"


def _signed_diff(a, b):
    """带符号差 a−b；任一非有限则返回 nan（供 cog−peak 观察偏移方向）。"""
    try:
        av, bv = float(a), float(b)
    except (TypeError, ValueError):
        return float('nan')
    if not (np.isfinite(av) and np.isfinite(bv)):
        return float('nan')
    return av - bv


def iapf_attempt_log_line(
    result,
    locked,
    candidates,
    target_count,
    iapf_live,
    iapf_global,
    segment_samples,
    sfreq,
):
    gate = result.gate_failed or "pass"
    buffer_s = float(segment_samples) / float(sfreq) if sfreq else 0.0
    return (
        "[IAPF] "
        f"locked={bool(locked)} calibrated={bool(result.calibrated)} gate={gate} "
        f"iapf={_fmt_diag(result.iapf, 2)}Hz live={_fmt_diag(iapf_live, 2)}Hz "
        f"global={_fmt_diag(iapf_global, 2)}Hz candidates={int(candidates)}/{int(target_count)} "
        f"clean={_fmt_diag(result.clean_duration_s, 2)}/{_fmt_diag(result.total_duration_s, 2)}s "
        f"bad={_fmt_diag(result.bad_duration_s, 2)}s signal_quality={_fmt_diag(result.signal_quality, 2)} "
        f"r2={_fmt_diag(result.model_r2, 3)} mae={_fmt_diag(result.model_error, 3)} "
        f"cog={_fmt_diag(result.cog, 2)}Hz peak={_fmt_diag(result.gaussian_cf, 2)}Hz "
        f"cog_minus_peak={_fmt_diag(_signed_diff(result.cog, result.gaussian_cf), 2)}Hz "
        f"source={result.iapf_source or 'none'} "
        f"peak_exists={bool(result.peak_exists)} "
        f"quality={result.peak_quality or 'unknown'} peak_power={_fmt_diag(result.peak_power, 3)} "
        f"alpha_ratio={_fmt_diag(result.alpha_residual_ratio, 3)} buffer={_fmt_diag(buffer_s, 2)}s"
    )


def iapf_waiting_log_line(segment_samples, window_samples, sfreq):
    have_s = float(segment_samples) / float(sfreq) if sfreq else 0.0
    need_s = float(window_samples) / float(sfreq) if sfreq else 0.0
    return f"[IAPF] waiting_buffer buffer={have_s:.2f}/{need_s:.2f}s"


class EEGProcessor:
    def __init__(
        self,
        sfreq,
        ch_names,
        window_dur=4.0,
        update_dur=1.0,
        notch_freq=50.0,
        bp_low=1.0,
        bp_high=30.0,
        alpha_search_range=(7.0, 13.0),
        iapf_smoothing_alpha=0.25,
        stability_alpha_history_len=10,
        brainbeat_log_ema_alpha=0.15,
        brainbeat_welch_nperseg=None,
        brainbeat_welch_noverlap=None,
        brainbeat_welch_nfft=None,
        brainbeat_artifact_threshold_uv=ARTIFACT_THRESHOLD_UV,
        brainbeat_power_epsilon=1e-20,
        brainbeat_warmup_epochs=3,
        meditation_warmup_epochs=5,
    ):
        self.sfreq = sfreq
        self.ch_names = ch_names
        self.ch_idx = {name: idx for idx, name in enumerate(ch_names)}
        self.total_samples_seen = 0
        self.last_iapf_update_sample = None
        self.alpha_search_range = alpha_search_range
        self.iapf_smoothing_alpha = iapf_smoothing_alpha
        _bb_nperseg = int(sfreq * 2) if brainbeat_welch_nperseg is None else int(brainbeat_welch_nperseg)
        _bb_noverlap = _bb_nperseg // 2 if brainbeat_welch_noverlap is None else int(brainbeat_welch_noverlap)
        self.brainbeat_welch_nperseg = _bb_nperseg
        self.brainbeat_welch_noverlap = _bb_noverlap
        self.brainbeat_welch_nfft = _bb_nperseg if brainbeat_welch_nfft is None else int(brainbeat_welch_nfft)
        self.brainbeat_log_ema_alpha = float(brainbeat_log_ema_alpha)
        self.brainbeat_power_epsilon = float(brainbeat_power_epsilon)
        # µV 原值同时供 FAA 的逐 epoch 判定使用（两处共用同一门限，见 ARTIFACT_THRESHOLD_UV）
        self.artifact_threshold_uv = float(brainbeat_artifact_threshold_uv)
        self.brainbeat_artifact_threshold = self.artifact_threshold_uv * 1e-6
        self.brainbeat_warmup_epochs = int(brainbeat_warmup_epochs)
        self.meditation_warmup_epochs = int(meditation_warmup_epochs)
        self._meditation_epoch_count = 0
        self.brainbeat_warmup_buf = []
        self.brainbeat_log_ema = None
        self.last_brainbeat = None
        # 并存的「去 1/f 残差」脑负荷指数（brainbeat_flat）：独立 warmup/EMA 状态，仅实时、仅观察
        self.brainbeat_flat_warmup_buf = []
        self.brainbeat_flat_log_ema = None
        self.last_brainbeat_flat = None
        self.iapf_global = 10.0
        self.iapf_live = 10.0                  # 实时显示的 IAPF：直接取本窗原始估计（不平滑，便于观察逐窗变化）
        self.last_iapf_result = None           # 最近一窗的完整估计结果，供 UI「实时分析」每秒动态重绘
        self.iapf_locked = False
        self.iapf_lock_candidates = []
        self.iapf_lock_candidate_results = []  # 与 candidates 并行的完整结果，锁定时取中位数窗做定格快照
        self.iapf_lock_target_count = 3        # K：连续 K 次过门禁才锁定第一个 IAPF
        self.iapf_window_s = 30.0              # IAPF 估计滑动窗（近 30 秒），与锁定/实时共用
        self.iapf_window_samples = int(sfreq * self.iapf_window_s)
        self.lock_attempt_interval_samples = int(sfreq * 5.0)  # 每 5 秒试行一次
        # 与试行间隔一致：每次试行都够格成为锁定候选
        self.iapf_lock_candidate_interval_samples = int(sfreq * 5.0)
        self._last_lock_attempt_sample = -10**12
        self._iapf_segment_samples_seen = 0
        self._last_iapf_lock_candidate_sample = None
        self.last_iapf_info = None
        self.last_visualization = None
        self.computation_channels = None

        # Key channels for metrics
        self.posterior_roi = ['Pz', 'Oz']
        self.metric_frontal_alpha_roi = ['Fz']
        self.metric_occipital_alpha_roi = ['Pz', 'Oz']
        self.required_chs = ['Fz', 'Pz', 'Oz']
        # Handle cases where Pz might be PZ, etc.
        all_upper = [c.upper() for c in ch_names]
        self.req_indices = {}
        for req in self.required_chs:
            if req.upper() in all_upper:
                idx = all_upper.index(req.upper())
                self.req_indices[req] = idx
        # ── FAA（额叶 alpha 不对称）通道：F3/F4 ──
        # 只做「提取」，不并入 required_chs：F3/F4 不参与每秒出帧的实时指标，
        # 仅在时段结束时整段参与一次 FAA，故不必每帧多算两路 Welch。
        self.faa_channels = [c for c in FAA_CHANNELS if c.upper() in all_upper]
        self.faa_col_idx = [all_upper.index(c.upper()) for c in self.faa_channels]
        self.faa_available = len(self.faa_channels) == len(FAA_CHANNELS)
        self.faa_discard_s = FAA_DISCARD_S
        self.faa_period_cap_samples = int(sfreq * FAA_PERIOD_CAP_S)
        self._faa_chunks = []
        self._faa_samples = 0
        self._faa_truncated = False

        # RBP（原始与去 1/f 残差）口径统一为 Fz/Pz/Oz 三通道（大小写不敏感、存在才纳入）。
        # 保留 _is_rbp_channel/排除表以备未来复用。
        _rbp_targets = ("FZ", "PZ", "OZ")
        self.rbp_indices = {
            name: idx
            for idx, name in enumerate(ch_names)
            if name.upper() in _rbp_targets
        }
        self._meditation_start_sample = 0
        self._rbp_active = False
        self._rbp_start_sample = 0
        
        self.window_samples = int(sfreq * window_dur)
        self.update_samples = int(sfreq * update_dur)
        _calib_cap = int(sfreq * 300.0)
        
        self.buffer = np.zeros((self.window_samples, len(ch_names)))
        self.calib_buffer = np.zeros((_calib_cap, len(ch_names)))
        self.buffer_filled = 0
        self.calib_buffer_filled = 0
        self.calib_buffer_write_pos = 0
        self.samples_since_update = 0
        self.stability_channels = tuple(c for c in ('Fz', 'Pz', 'Oz') if c in self.req_indices)
        self.stability_weights = {'Fz': 0.2, 'Pz': 0.4, 'Oz': 0.4}
        self.stability_alpha_history = {
            ch: deque(maxlen=int(stability_alpha_history_len)) for ch in self.stability_channels
        }
        # ── 疲劳指数（Theta/Beta 功率比值）──
        # 逐通道 Fz/Pz/Oz，仅实时展示、不计入综合分。与脑负荷指数同族（同为频段比值、
        # 同一 4s 窗、同一份 psd_dict），故复用其 warmup/EMA 参数在 log 域平滑。
        self.fatigue_channels = tuple(c for c in ('Fz', 'Pz', 'Oz') if c in self.req_indices)
        self.fatigue_warmup_buf = {ch: [] for ch in self.fatigue_channels}
        self.fatigue_log_ema = {ch: None for ch in self.fatigue_channels}
        self.last_fatigue = {}


        # 遗留 ba 系数：滤波已改为流式 SOS（_build_stream_filters），这两组现在只被
        # _compute_rbp_frame 的最小样本数守卫读长度用，不参与任何滤波，故不做低截止钳制。
        self.b_notch, self.a_notch = signal.iirnotch(notch_freq, 30.0, sfreq)
        self.b_bp, self.a_bp = signal.butter(4, [bp_low, bp_high], btype='bandpass', fs=sfreq)

        # ── 流式连续滤波（见 _filter_stream）──
        # self.buffer 里存的是滤波后的数据，滤波在 push_chunk 入口一次完成、状态跨 chunk
        # 保持。settle 期内的样本不允许进入指标：4 阶 1Hz 高通的建立瞬态是宽带的且能量偏
        # 低频，会把 θ 抬得比 α 多，落进 BrainBeat 的 warmup 均值就会锚偏整段会话。
        self.filter_settle_s = 3.0
        self.filter_settle_samples = int(sfreq * self.filter_settle_s)
        self._sos_notch = None
        self._sos_bp = None
        self._zi_notch = None
        self._zi_bp = None
        self._dc_offset = None
        self._filtered_samples = 0
        self._build_stream_filters(notch_freq, bp_low, bp_high)
        # 原始环：仅为「改滤波参数后重滤重建」保留，容量刚好覆盖 settle + 一个指标窗
        self._raw_ring_capacity = self.window_samples + self.filter_settle_samples
        self._raw_ring = np.zeros((self._raw_ring_capacity, len(ch_names)))
        self._raw_ring_filled = 0

        self.iapf_estimator = IAPFEstimator(
            sfreq, ch_names,
            posterior_roi=tuple(self.posterior_roi),
            notch_freq=notch_freq, bp_low=bp_low, bp_high=bp_high,
            alpha_search_range=self.alpha_search_range,
        )

        # 滚动锁定缓冲：仅保留 Fz/Pz/Oz（按此固定列序），持续累积，超过上限丢弃最旧 chunk
        self.summary_channels = [c for c in ('Fz', 'Pz', 'Oz') if c in self.req_indices]
        self.summary_col_idx = [self.req_indices[c] for c in self.summary_channels]
        self.segment_chunks = []
        self.segment_samples = 0
        # 冥想报告：按事件标记独立累积的时段缓冲（静息/冥想/评估）
        self._period_chunks = []
        self._period_samples = 0
        self.segment_active = False
        self.segment_sample_cap = int(sfreq * 60.0)  # 滚动锁定缓冲上限 60s
        self.summary_estimator = IAPFEstimator(
            sfreq, self.summary_channels,
            posterior_roi=tuple(c for c in ('Pz', 'Oz') if c in self.summary_channels),
            frontal_roi=tuple(c for c in ('Fz',) if c in self.summary_channels),
            notch_freq=notch_freq, bp_low=bp_low, bp_high=bp_high,
            alpha_search_range=self.alpha_search_range,
            # 30s 实时窗与整段报告共用；质量门只剩比例门（quality_min_ratio=0.75），
            # 对两种时长同一口径，R² 门禁保持默认 0.80
        )
        # ── 非周期指数（1/f 斜率）实时观察量 ──
        # 仅用于趋势展示，不计入综合分。**不再自行滤波/算 PSD**：窗长、节拍、滤波、
        # 伪迹剔除、PSD 全部来自 IAPF 路径的同一次 summary_estimator.compute()
        # （见 _derive_aperiodic），因此实时趋势与时段报告的「皮质兴奋/抑制」按构造相等。
        # 不叠 EMA：30s 窗 + 5s 步进相邻帧共享 83% 数据，本身已足够平滑。
        self._aperiodic_frame = None    # 待取走的本轮快照（消费一次）
        # 下面四个字段是「上次有效值」缓存：本轮拿不到新值的通道沿用上次
        self._aperiodic_ema = {ch: None for ch in self.summary_channels}
        # 1/f 截距（aperiodic_offset）与斜率同源同步（同一次 _fit_spectrum）
        self._aperiodic_offset_ema = {ch: None for ch in self.summary_channels}
        # Pz+Oz 平均 PSD 再拟合的 1/f 斜率/截距（先平均谱、后拟合，非逐通道斜率平均）
        self._aperiodic_ema_pooled = None
        self._aperiodic_offset_ema_pooled = None
        # ── HAI（快慢波比）实时观察量 ──
        # HAI = log10(beta / (delta+theta))，逐通道，仅趋势展示、不计入综合分。
        # 与 1/f 斜率同源：窗长/节拍/滤波/伪迹剔除/PSD 全部来自 IAPF 路径的同一次
        # summary_estimator.compute()（见 _derive_hai）。用**原始 PSD**，不去 1/f。
        # 同样不叠 EMA：30s 窗 + 5s 步进相邻帧共享 83% 数据，本身已足够平滑。
        self._hai_frame = None          # 待取走的本轮快照（消费一次）
        self._hai_latest = {ch: None for ch in self.summary_channels}  # 上次有效值缓存

        self.calibrating = False
        self.calibrated = False
        self.last_calibration_result = None

    def set_computation_channels(self, channel_names):
        """Restrict metric calculations to the currently selected display channels."""
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
        # 连同流式滤波器一起重置：restart_playback 会让信号从文件末尾跳回开头，
        # 保留 zi 会把这个不连续当成真实跳变滤出一段振铃。代价是重新走一遍 settle。
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
        for _hist in self.stability_alpha_history.values():
            _hist.clear()
        # Bug ③: reset calibration state so next session starts uncalibrated
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
        """Called when entering meditation phase. Resets warmup counter so scores
        blend from neutral (50) toward real values over the first few epochs."""
        self._meditation_epoch_count = 0
        self._meditation_start_sample = self.total_samples_seen
        for _hist in self.stability_alpha_history.values():
            _hist.clear()

    def start_rbp_session(self):
        self._rbp_active = True
        self._rbp_start_sample = self.total_samples_seen

    def stop_rbp_session(self):
        self._rbp_active = False

    def elapsed_s(self):
        """会话数据时间（秒）= 已收样本数 ÷ 采样率，以 start_rbp_session() 为原点。

        所有趋势图的统一横坐标：它只随真实收到的样本前进，与墙上时钟、UI 出队时机、
        BDF 回放倍速都无关，因此各 Tab 的曲线可以直接叠着比对。
        """
        return float((self.total_samples_seen - self._rbp_start_sample) / self.sfreq)

    def _is_rbp_channel(self, channel_name):
        normalized = str(channel_name).strip().upper().replace(" ", "")
        if normalized in RBP_EXCLUDED_CHANNELS:
            return False
        return not any(normalized.startswith(prefix) for prefix in RBP_EXCLUDED_PREFIXES)

    def _rbp_band_edges(self):
        return {
            "delta": (1.0, 4.0),
            "theta": (4.0, 8.0),
            "alpha": (8.0, 13.0),
            "beta": (13.0, 30.0),
        }

    def _is_computation_channel(self, channel_name):
        return self.computation_channels is None or channel_name in self.computation_channels

    def _available_channels(self, channel_names):
        return [
            ch for ch in channel_names
            if ch in self.req_indices and self._is_computation_channel(ch)
        ]

    # ── 流式连续滤波 ──────────────────────────────────────────────────────
    # 与旧实现（每帧对 4s epoch 单独 filtfilt）的区别：滤波在数据进入时一次完成，
    # 滤波器状态 zi 跨 chunk 保持。同一个样点只被滤一次，相邻重叠帧不再出现「同一
    # 样点滤出不同值」的抖动，也没有每帧两端的建立瞬态（filtfilt 的 padlen 只有
    # 3*max(len(a),len(b)) 个样点，远不足以覆盖 1Hz 高通 1~3s 的建立时间）。
    # 代价：因果滤波无法零相位，幅频从 filtfilt 的 |H|² 变为 |H|，带边衰减减半。
    # 频段功率只取幅度不取相位，故指标口径不变，但绝对值会整体平移——依赖绝对值
    # 标定过的阈值（BrainBeat warmup 基线、150µV 伪迹门、SD 地板）需要复核。

    def _build_stream_filters(self, notch_freq, bp_low, bp_high):
        """构造流式滤波器系数。用 SOS 形式：高采样率下 4 阶带通的 ba 形式数值条件差。

        低截止按 `METRICS_MIN_BP_LOW` 钳制（见其说明）。生效值记在 `self.metrics_bp_low`，
        与请求值不同时 `self.metrics_bp_low_clamped` 为 True，供上层日志/界面提示。
        """
        requested_low = float(bp_low)
        effective_low = max(requested_low, METRICS_MIN_BP_LOW)
        self.metrics_bp_low = effective_low
        self.metrics_bp_low_clamped = effective_low > requested_low
        self.metrics_bp_high = float(bp_high)
        self.metrics_notch = float(notch_freq)
        self._sos_notch = signal.tf2sos(*signal.iirnotch(notch_freq, 30.0, self.sfreq))
        self._sos_bp = signal.butter(
            4, [effective_low, bp_high], btype='bandpass', fs=self.sfreq, output='sos')

    def _reset_stream_filter_state(self):
        """清掉滤波器状态与直流基准，下一个 chunk 重新播种并重新计 settle。"""
        self._zi_notch = None
        self._zi_bp = None
        self._dc_offset = None
        self._filtered_samples = 0

    def _filter_stream(self, chunk):
        """对一个 chunk 连续滤波，返回同形状结果。chunk: (n_samples, n_ch)。

        首个 chunk 取其首样点作为固定直流基准，并用它播种 zi（`sosfilt_zi` 给的是单位
        阶跃下的稳态），使滤波器像「已经看了这个恒定值无穷久」。电极半电池电位是 mV
        量级、比脑电大三个数量级，zi 若从零起会产生巨大的阶跃振铃——旧实现是靠每帧
        `sig - np.mean(sig)` 先去直流才躲开的，连续滤波必须在这里补回这层保护。
        """
        chunk = np.asarray(chunk, dtype=float)
        if self._dc_offset is None:
            self._dc_offset = chunk[0].copy()
        x = (chunk - self._dc_offset).T  # (n_ch, n_samples)
        if self._zi_notch is None:
            first = x[:, 0]
            self._zi_notch = signal.sosfilt_zi(self._sos_notch)[:, None, :] * first[None, :, None]
            self._zi_bp = signal.sosfilt_zi(self._sos_bp)[:, None, :] * first[None, :, None]
        y, self._zi_notch = signal.sosfilt(self._sos_notch, x, axis=-1, zi=self._zi_notch)
        y, self._zi_bp = signal.sosfilt(self._sos_bp, y, axis=-1, zi=self._zi_bp)
        return y.T

    def _push_raw_ring(self, chunk):
        n = len(chunk)
        if n >= self._raw_ring_capacity:
            self._raw_ring[:] = chunk[-self._raw_ring_capacity:]
            self._raw_ring_filled = self._raw_ring_capacity
        else:
            self._raw_ring = np.roll(self._raw_ring, -n, axis=0)
            self._raw_ring[-n:] = chunk
            self._raw_ring_filled = min(self._raw_ring_filled + n, self._raw_ring_capacity)

    def _rebuild_filtered_buffer(self):
        """滤波参数变更后用保留的原始环重滤一遍，指标不会因此出现空档或瞬态。"""
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
        """运行时更新陷波/带通滤波参数：重算系数并同步到 IAPF 估计器。"""
        self.b_notch, self.a_notch = signal.iirnotch(notch_freq, 30.0, self.sfreq)
        self.b_bp, self.a_bp = signal.butter(4, [bp_low, bp_high], btype='bandpass', fs=self.sfreq)
        self._build_stream_filters(notch_freq, bp_low, bp_high)
        self._rebuild_filtered_buffer()
        for est in (self.iapf_estimator, self.summary_estimator):
            est.notch_freq = float(notch_freq)
            est.bp_low = float(bp_low)
            est.bp_high = float(bp_high)

    def push_chunk(self, chunk_np):
        """Add new data chunk to the sliding window buffer.

        注意 self.buffer 里存的是**滤波后**的数据：滤波已在此处一次性完成，下游
        calculate_metrics / _compute_rbp_frame 直接取用，不再各自 filtfilt。
        """
        chunk_len = len(chunk_np)
        # Ensure chunk has correct number of channels
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
            self.buffer_filled = min(self.buffer_filled + chunk_len, self.window_samples)

        self.samples_since_update += chunk_len
        self.total_samples_seen += chunk_len

    def should_update(self):
        """Check if enough data has been collected to perform an update.

        除缓冲填满与出帧间隔外，还要求缓冲里最老的样点也已越过滤波器建立期，
        否则第一帧会吃到高通瞬态（见 _filter_stream / filter_settle_s）。
        """
        return (
            self.buffer_filled == self.window_samples
            and self._filtered_samples >= self.window_samples + self.filter_settle_samples
            and self.samples_since_update >= self.update_samples
        )

    def start_calibration(self):
        self.calib_buffer.fill(0)
        self.calib_buffer_filled = 0
        self.calib_buffer_write_pos = 0
        self.calibrating = True
        self.calibrated = False

    def add_calib_chunk(self, chunk):
        """Accumulate raw chunk into calibration rolling buffer (max 5 min)."""
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
            self.calib_buffer_filled = min(self.calib_buffer_filled + n, cap)

    def calibration_buffer_view(self):
        """Return calibration samples in chronological order."""
        if self.calib_buffer_filled < len(self.calib_buffer):
            return self.calib_buffer[:self.calib_buffer_filled]
        pos = self.calib_buffer_write_pos
        if pos == 0:
            return self.calib_buffer
        return np.concatenate([self.calib_buffer[pos:], self.calib_buffer[:pos]], axis=0)

    def finalize_calibration(self, label, duration_s=0.0):
        buf = self.calibration_buffer_view()
        result = self.iapf_estimator.compute(buf, label, duration_s=duration_s)
        self.iapf_global = result.iapf
        self.last_calibration_result = result
        self.calibrating = False
        self.calibrated = True
        return result

    def start_segment(self):
        """开始累积本期整段缓冲（不触碰 calibrated 标志）。"""
        self.segment_chunks = []
        self.segment_samples = 0
        self.segment_active = True

    def add_segment_chunk(self, chunk):
        if not self.segment_active:
            return
        if chunk.shape[1] > len(self.ch_names):
            chunk = chunk[:, :len(self.ch_names)]
        self.segment_chunks.append(np.asarray(chunk[:, self.summary_col_idx], dtype=float))
        self.segment_samples += len(chunk)
        self._iapf_segment_samples_seen += len(chunk)
        # 滚动丢弃最旧 chunk，使总样本不超过上限
        while self.segment_samples > self.segment_sample_cap and len(self.segment_chunks) > 1:
            dropped = self.segment_chunks.pop(0)
            self.segment_samples -= len(dropped)

    def segment_buffer_view(self):
        if not self.segment_chunks:
            return np.zeros((0, len(self.summary_channels)))
        return np.concatenate(self.segment_chunks, axis=0)

    def finalize_segment(self, label, set_global=False):
        """对本期整段缓冲算一次 PSD → CalibrationResult；set_global 时锁定供实时使用。"""
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
        """开始累积一个报告时段（静息/冥想/评估）的 Fz/Pz/Oz 缓冲。"""
        self._period_chunks = []
        self._period_samples = 0

    def add_period_chunk(self, chunk):
        if chunk.shape[1] > len(self.ch_names):
            chunk = chunk[:, :len(self.ch_names)]
        self._period_chunks.append(np.asarray(chunk[:, self.summary_col_idx], dtype=float))
        self._period_samples += len(chunk)

    def compute_period_report(self, label):
        """对本时段缓冲算一次 IAPF、皮质兴奋/抑制与脑负荷；返回 dict 或 None（无数据）。"""
        if not self._period_chunks:
            return None
        buf = np.concatenate(self._period_chunks, axis=0)
        duration_s = self._period_samples / self.sfreq
        result = self.summary_estimator.compute(buf, label, duration_s=duration_s)
        brainbeat = None
        if result.fz_psd is not None and result.pz_psd is not None:
            brainbeat = segment_brainbeat(
                result.freqs, result.fz_psd, result.pz_psd, result.iapf)
        return {
            'iapf': float(result.iapf),
            'cortical_excitation': float(result.aperiodic_exponent),
            'brainbeat': brainbeat,
            'result': result,
        }

    # ── FAA 时段（静息/冥想/评估/睁眼，四对事件都算）──────────────────────
    # 与冥想报告的时段缓冲并行、互不影响：报告时段只有三个（睁眼不计入报告），
    # 而 FAA 四对都要，且需要的是 F3/F4 而不是 Fz/Pz/Oz。
    def start_faa_period(self):
        """开始累积一个 FAA 时段的 F3/F4 原始缓冲。"""
        self._faa_chunks = []
        self._faa_samples = 0
        self._faa_truncated = False

    def add_faa_period_chunk(self, chunk):
        if not self.faa_available:
            return
        if chunk.shape[1] > len(self.ch_names):
            chunk = chunk[:, :len(self.ch_names)]
        if self._faa_samples >= self.faa_period_cap_samples:
            self._faa_truncated = True   # 漏点「结束X」时封顶，不再无限增长
            return
        self._faa_chunks.append(np.asarray(chunk[:, self.faa_col_idx], dtype=float))
        self._faa_samples += len(chunk)

    def _faa_filter(self, buf):
        """整段离线滤波：系数与实时指标完全一致（同一组 self._sos_notch/_sos_bp，
        低截止同样钳到 METRICS_MIN_BP_LOW），但用零相位 sosfiltfilt——时段分析不需要
        因果性，零相位不引入群延迟；两端的建立瞬态由随后丢弃的开头过渡期一并吃掉。

        数据太短（不够 filtfilt 的边界填充）返回 None。
        """
        x = np.asarray(buf, dtype=float)
        if x.size == 0:
            return None
        x = x - x.mean(axis=0, keepdims=True)  # 先去电极直流，避免边界填充放大阶跃
        # sosfiltfilt 的 padlen 上界；取上界作守卫，短于它直接判数据不足
        min_len = 3 * (2 * max(len(self._sos_notch), len(self._sos_bp)) + 1)
        if len(x) <= min_len:
            return None
        y = signal.sosfiltfilt(self._sos_notch, x, axis=0)
        y = signal.sosfiltfilt(self._sos_bp, y, axis=0)
        return y

    def compute_faa_report(self, label):
        """对本 FAA 时段整段算一次 FAA；无 F3/F4 或本时段无数据时返回 None。

        流程：与实时指标同参数滤波 → 丢弃开头 `faa_discard_s` 秒过渡期 →
        2s/50% Hann epoch + ±`artifact_threshold_uv` 幅度伪迹剔除 → 干净 epoch
        线性功率域平均 PSD → 8–13Hz 梯形积分 → ln(P_F4) − ln(P_F3)。
        """
        if not self.faa_available or not self._faa_chunks:
            return None
        buf = np.concatenate(self._faa_chunks, axis=0)
        duration_s = self._faa_samples / self.sfreq
        report = {
            'label': str(label),
            'duration_s': float(duration_s),
            'discard_s': float(self.faa_discard_s),
            'analyzed_s': 0.0,
            'truncated': bool(self._faa_truncated),
            'notch': float(self.metrics_notch),
            'bp_low': float(self.metrics_bp_low),
            'bp_high': float(self.metrics_bp_high),
            'faa': None, 'p_f3': None, 'p_f4': None,
            'total_epochs': 0, 'clean_epochs': 0, 'clean_ratio': 0.0,
            'reason': 'too_short',
        }
        filtered = self._faa_filter(buf)
        if filtered is None:
            return report
        start = int(round(self.faa_discard_s * self.sfreq))
        seg = filtered[start:]
        if len(seg) == 0:
            return report
        report['analyzed_s'] = float(len(seg) / self.sfreq)
        report.update(compute_faa(
            seg[:, 0], seg[:, 1], self.sfreq,
            artifact_uv=self.artifact_threshold_uv))
        return report

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

