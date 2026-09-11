import numpy as np
from scipy import signal
from collections import deque

from app.eeg_core.iapf_estimator import IAPFEstimator
from app.eeg_core.faa import (
    ARTIFACT_THRESHOLD_UV, FAA_CHANNELS, FAA_DISCARD_S, FAA_PERIOD_CAP_S,
)
from app.eeg_core.processor_iapf import ProcessorIAPFMixin
from app.eeg_core.processor_brainbeat import ProcessorBrainbeatMixin
from app.eeg_core.processor_metrics import ProcessorMetricsMixin
from app.eeg_core.iapf_diagnostics import (
    iapf_attempt_log_line,
    iapf_waiting_log_line,
)
from app.eeg_core.realtime_spectral import (
    HAI_BETA_BAND,
    HAI_LOW_BAND,
    RBP_BAND_EDGES,
    band_power,
    band_relative_power,
    clip_map,
    compute_hai,
    logistic_map,
    residual_spectrum,
    segment_brainbeat,
)
from app.eeg_core.processor_sessions import (
    ProcessorSessionMixin,
)
from app.eeg_core.processor_state import (
    ProcessorStateMixin,
    RBP_EXCLUDED_CHANNELS,
    RBP_EXCLUDED_PREFIXES,
)
from app.eeg_core.stream_filter import METRICS_MIN_BP_LOW, StreamFilterMixin

# Preserve the original module-level helper imports while implementation lives in
# focused modules. Existing callers can keep importing them from ``processor``.

# 实时指标链路的低截止下限。UI 的带通控件允许低到 0.1Hz，但指标这一路必须钳在 1Hz：
# 1) 最低频段 delta 的下沿就是 1Hz（RBP_BAND_EDGES），再低没有任何频段用得上；
# 2) 4s 窗 / 2s 子段 Welch 的分辨率是 0.5Hz，1Hz 以下只有一两个频点，估计不可信；
# 3) `filter_settle_s` 是按 1Hz 拐点定的（约 3 个周期）。拐点每降一个数量级建立时间
#    就涨一个数量级，0.1Hz 需要约 30s，settle 门会失效、瞬态会漏进 BrainBeat warmup。
# 波形显示与 IAPF 路径不受此钳制（各自另有量程需求，见 AGENTS.md §5.2）。
class EEGProcessor(
    StreamFilterMixin,
    ProcessorStateMixin,
    ProcessorSessionMixin,
    ProcessorIAPFMixin,
    ProcessorMetricsMixin,
    ProcessorBrainbeatMixin,
):
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
