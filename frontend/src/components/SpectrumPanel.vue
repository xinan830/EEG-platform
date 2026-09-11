<script setup lang="ts">
import { computed, ref, toRef } from 'vue'
import { useSpectrum } from '../composables/useSpectrum'
import type { SpectrumBand } from '../types/spectrum'

const props = defineProps<{ recordingId?: string; startS: number; channels: string[] }>()
const { result, loading, error, reload } = useSpectrum(toRef(props, 'recordingId'), toRef(props, 'startS'), toRef(props, 'channels'))
const bands: SpectrumBand[] = ['delta', 'theta', 'alpha', 'beta']
const selectedChannel = ref('')
const firstChannel = computed(() => selectedChannel.value || result.value?.channels[0] || '')
const points = computed(() => {
  const values = result.value?.psd[firstChannel.value] ?? []
  if (!values.length) return ''
  const max = Math.max(...values, 1e-20)
  return values.map((value, index) => `${(index / Math.max(1, values.length - 1)) * 100},${100 - (value / max) * 92}`).join(' ')
})
</script>
<template>
  <section class="spectrum-panel" aria-label="频谱分析">
    <header class="spectrum-header"><strong>频谱分析</strong><span v-if="result">分析区间 {{ result.window_start_s.toFixed(2) }}–{{ (result.window_start_s + result.window_duration_s).toFixed(2) }} s · 分析时长 {{ result.window_duration_s.toFixed(0) }} s · {{ result.algorithm_version }}</span></header>
    <p v-if="loading" class="spectrum-empty">正在计算频谱…</p>
    <p v-else-if="error" class="spectrum-error">{{ error }}</p>
    <template v-else-if="result">
      <div class="spectrum-controls"><label>PSD 通道 <select :value="firstChannel" @change="selectedChannel = ($event.target as HTMLSelectElement).value"><option v-for="channel in result.channels" :key="channel" :value="channel">{{ channel }}</option></select></label><button type="button" @click="reload">刷新频谱</button></div>
      <div class="spectrum-chart-wrap"><svg class="spectrum-chart" viewBox="0 0 100 100" preserveAspectRatio="none" role="img" aria-label="PSD 曲线"><polyline :points="points" fill="none" stroke="#3c82f6" stroke-width="0.8" vector-effect="non-scaling-stroke" /></svg><div class="spectrum-axis"><span>{{ result.frequencies_hz[0]?.toFixed(0) }} Hz</span><span>{{ result.frequencies_hz.at(-1)?.toFixed(0) }} Hz</span></div></div>
      <div class="spectrum-table"><div class="spectrum-row spectrum-row-head"><span>通道</span><span v-for="band in bands" :key="band">{{ band }} µV² / RBP</span></div><div v-for="channel in result.channels" :key="channel" class="spectrum-row"><strong>{{ channel }}</strong><span v-for="band in bands" :key="band">{{ result.band_power[channel][band].toFixed(2) }} / {{ (result.relative_band_power[channel][band] * 100).toFixed(1) }}%</span></div></div>
      <small class="spectrum-meta">参考：{{ result.analysis_reference }} · PSD：{{ result.units.psd }} · Welch：4 s 分段 / 50% 重叠 · clean {{ result.quality.clean_segments }}/{{ result.quality.total_segments }}</small>
    </template>
    <p v-else class="spectrum-empty">暂无频谱数据</p>
  </section>
</template>
