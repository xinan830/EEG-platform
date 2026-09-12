<script setup lang="ts">
import type { SpectrumResponse } from '../types/spectrum'
import type { SpectrumBand } from '../types/spectrum'

defineProps<{ result: SpectrumResponse; channel: string }>()
const emit = defineEmits<{ close: [] }>()
const bands: SpectrumBand[] = ['delta', 'theta', 'alpha', 'beta']
</script>

<template>
  <div class="modal-layer algorithm-modal-layer" @click.self="emit('close')">
    <section class="algorithm-dialog spectrum-check-dialog" role="dialog" aria-modal="true" aria-labelledby="spectrum-check-title" @click.stop>
      <header class="dialog-titlebar"><strong id="spectrum-check-title">频谱算法校验</strong><button type="button" class="dialog-close" aria-label="关闭频谱算法校验" @click="emit('close')">×</button></header>
      <div class="algorithm-body" aria-live="polite">
        <p class="algorithm-meta">只读校验后端返回结果，不在前端重新计算。当前通道：{{ channel }} · 分析区间：{{ result.window_start_s.toFixed(3) }}–{{ (result.window_start_s + result.window_duration_s).toFixed(3) }} s</p>
        <div class="spectrum-check-grid"><span>算法版本</span><strong>{{ result.algorithm_version }}</strong><span>参考方式</span><strong>{{ result.analysis_reference }}</strong><span>采样率</span><strong>{{ result.sfreq_hz }} Hz</strong><span>频率轴</span><strong>{{ result.frequencies_hz.length }} 点，{{ result.frequencies_hz[0] }}–{{ result.frequencies_hz.at(-1) }} Hz</strong><span>Welch</span><strong>4 s 分段 / 50% 重叠</strong><span>质量门</span><strong>{{ result.quality.clean_segments }}/{{ result.quality.total_segments }} clean（{{ (result.quality.clean_ratio * 100).toFixed(1) }}%）</strong></div>
        <h3>频段积分</h3>
        <p class="algorithm-meta">Pband = ∫ PSD(f) df；内部单位 V²/Hz，接口输出 µV²。</p>
        <div class="algorithm-table"><div v-for="band in bands" :key="band" class="algorithm-row"><strong>{{ band }}</strong><code>[{{ band === 'delta' ? '1,4)' : band === 'theta' ? '4,8)' : band === 'alpha' ? '8,13)' : '13,30]' }} Hz</code><span>{{ result.band_power[channel][band].toFixed(4) }} µV² · RBP {{ (result.relative_band_power[channel][band] * 100).toFixed(2) }}%</span></div></div>
        <p class="algorithm-meta">RBP 四频段合计：{{ (bands.reduce((sum, band) => sum + result.relative_band_power[channel][band], 0) * 100).toFixed(3) }}% · PSD 单位：{{ result.units.psd }}</p>
      </div>
      <footer class="channel-dialog-footer"><button type="button" @click="emit('close')">关闭</button></footer>
    </section>
  </div>
</template>
