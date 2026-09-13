<script setup lang="ts">
import { computed } from 'vue'
import type { SpectrumResponse, SpectrumBand } from '../types/spectrum'

const props = defineProps<{ result: SpectrumResponse; channel: string; mode?: 'static' | 'dynamic'; dynamicWindowS?: number; refreshStepS?: number }>()
const emit = defineEmits<{ close: [] }>()
const bands: SpectrumBand[] = ['delta', 'theta', 'alpha', 'beta']
const bounds: Record<SpectrumBand, string> = { delta: '[1,4)', theta: '[4,8)', alpha: '[8,13)', beta: '[13,30]' }
const totalPower = computed(() => bands.reduce((sum, band) => sum + (props.result.band_power[props.channel]?.[band] ?? 0), 0))
const executedMode = computed(() => props.result.execution_config?.mode ?? props.mode ?? 'static')
const modeLabel = computed(() => executedMode.value === 'dynamic' ? '动态 PSD' : '静态 PSD')
</script>

<template>
  <div class="modal-layer algorithm-modal-layer" @click.self="emit('close')">
    <section class="algorithm-dialog spectrum-check-dialog" role="dialog" aria-modal="true" aria-labelledby="spectrum-check-title" @click.stop>
      <header class="dialog-titlebar"><strong id="spectrum-check-title">频谱算法校验</strong><button type="button" class="dialog-close" aria-label="关闭频谱算法校验" @click="emit('close')">×</button></header>
      <div class="algorithm-body" aria-live="polite">
        <p class="algorithm-meta">只读校验后端返回结果，不在前端重新计算。当前通道：{{ channel }} · 实际分析区间：{{ (result.actual_start_s ?? result.window_start_s).toFixed(3) }}–{{ (result.actual_end_s ?? result.window_start_s + result.window_duration_s).toFixed(3) }} s</p>
        <div class="spectrum-check-grid"><span>模式</span><strong>{{ modeLabel }}</strong><span>请求区间</span><strong>{{ result.requested_start_s?.toFixed(3) ?? '—' }}–{{ result.requested_end_s?.toFixed(3) ?? '—' }} s（{{ result.requested_window_s?.toFixed(3) ?? '—' }} s）</strong><span>实际区间</span><strong>{{ (result.actual_start_s ?? result.window_start_s).toFixed(3) }}–{{ (result.actual_end_s ?? result.window_start_s + result.window_duration_s).toFixed(3) }} s（{{ (result.actual_duration_s ?? result.window_duration_s).toFixed(3) }} s）</strong><span>刷新步长</span><strong>{{ executedMode === 'dynamic' ? `${result.execution_config?.refresh_step_s ?? refreshStepS ?? 1} s` : '手动提交分析区间' }}</strong><span>算法版本</span><strong>{{ result.algorithm_version }}</strong><span>基线算法</span><strong>{{ result.baseline_algorithm_version ?? result.algorithm_version }}</strong><span>配置指纹</span><strong>{{ result.analysis_config_hash ?? '固定 v3 契约' }}</strong><span>Warm-up</span><strong>{{ result.warmup ? '是，实际数据短于请求窗口' : '否' }}</strong><span>参考方式</span><strong>{{ result.analysis_reference }}</strong><span>采样率</span><strong>{{ result.sfreq_hz }} Hz</strong><span>频率范围</span><strong>1–30 Hz（{{ result.frequencies_hz.length }} 个频率点）</strong><span>Welch</span><strong>4 s Hann，50% overlap（2 s 步进）</strong><span>质量门</span><strong>{{ result.quality.clean_segments }}/{{ result.quality.total_segments }} clean（{{ (result.quality.clean_ratio * 100).toFixed(1) }}%）</strong></div>
        <h3>频段积分 · {{ channel }} 计算明细</h3>
        <p class="algorithm-meta">对当前通道的 PSD 使用梯形积分：Pband = ∫<sub>low</sub><sup>high</sup> PSD<sub>{{ channel }}</sub>(f) df。接口输出 µV²；RBP = Pband / P<sub>1–30</sub>。</p>
        <div class="algorithm-table"><div v-for="band in bands" :key="band" class="algorithm-row"><strong>{{ band[0].toUpperCase() + band.slice(1) }}</strong><code>{{ bounds[band] }} Hz</code><span>P = {{ result.band_power[channel][band].toFixed(4) }} µV²<br>RBP = {{ (result.relative_band_power[channel][band] * 100).toFixed(2) }}%</span></div></div>
        <p class="algorithm-meta">{{ channel }} 四频段功率合计 P<sub>1–30</sub> ≈ {{ totalPower.toFixed(4) }} µV² · RBP 合计 {{ (bands.reduce((sum, band) => sum + result.relative_band_power[channel][band], 0) * 100).toFixed(3) }}% · PSD 单位：{{ result.units.psd }}</p>
        <h3>全部通道频段结果</h3>
        <div class="algorithm-channel-table"><div class="algorithm-channel-head"><span>通道</span><span v-for="band in bands" :key="band">{{ band[0].toUpperCase() + band.slice(1) }}（RBP）</span></div><div v-for="name in result.channels" :key="name" class="algorithm-channel-row"><strong>{{ name }}</strong><span v-for="band in bands" :key="band">{{ result.band_power[name][band].toFixed(2) }} µV² / {{ (result.relative_band_power[name][band] * 100).toFixed(1) }}%</span></div></div>
      </div>
      <footer class="channel-dialog-footer"><button type="button" @click="emit('close')">关闭</button></footer>
    </section>
  </div>
</template>
