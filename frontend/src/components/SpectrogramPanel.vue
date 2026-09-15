<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { getConfiguredSpectrogram } from '../api/spectrogram'
import SpectrogramAlgorithmDialog from './SpectrogramAlgorithmDialog.vue'
import SpectrogramChart from './SpectrogramChart.vue'
import BandPowerTrendChart from './BandPowerTrendChart.vue'
type Band = 'all' | 'delta' | 'theta' | 'alpha' | 'beta' | 'custom'

const props = defineProps<{ recordingId?: string; startS: number; durationS?: number; channels: string[]; positionS?: number; playing?: boolean; activeRange?: { start: number; end: number; source: string } | null }>()
const result = ref<Awaited<ReturnType<typeof getConfiguredSpectrogram>> | null>(null)
const loading = ref(false)
const error = ref('')
const selected = ref('')
const algorithmOpen = ref(false)
let requestId = 0
const channel = computed(() => selected.value || result.value?.channels[0] || '')
const mode = ref<'static' | 'dynamic'>('static')
const dynamicWindow = ref(10)
const refreshStep = ref(1)
const viewMode = ref<'trend' | 'heatmap' | 'both'>('trend')
const selectedBand = ref<Band>('all')
const customMinHz = ref(8)
const customMaxHz = ref(13)
const dynamicStart = ref(0)
let refreshTimer: ReturnType<typeof setInterval> | undefined
let queued = false
const requestedRange = computed(() => {
  const duration = props.durationS
  if (mode.value === 'dynamic') {
    const playbackEnd = Math.max(0, Math.floor(props.positionS ?? 0))
    const end = duration !== undefined ? Math.min(duration, playbackEnd) : playbackEnd
    return { start: Math.max(0, end - dynamicWindow.value), end }
  }
  const requestedStart = props.activeRange?.start ?? props.startS
  const requestedWindow = props.activeRange ? props.activeRange.end - props.activeRange.start : 30
  const start = duration !== undefined ? Math.min(Math.max(0, requestedStart), Math.max(0, duration - 4)) : Math.max(0, requestedStart)
  const end = duration !== undefined ? Math.min(duration, start + requestedWindow) : start + requestedWindow
  return { start, end }
})
const frequencyRange = computed(() => {
  const ranges: Record<Exclude<Band, 'all' | 'custom'>, [number, number]> = { delta: [1, 4], theta: [4, 8], alpha: [8, 13], beta: [13, 30] }
  if (selectedBand.value === 'custom') return { min: Math.min(customMinHz.value, customMaxHz.value), max: Math.max(customMinHz.value, customMaxHz.value) }
  const range = selectedBand.value === 'all' ? [1, 30] : ranges[selectedBand.value]
  return { min: range[0], max: range[1] }
})

function syncDynamicStart() {
  const end = Math.max(0, props.positionS ?? 0)
  dynamicStart.value = Math.max(0, end - dynamicWindow.value)
}
function stopTimer() { if (refreshTimer) { clearInterval(refreshTimer); refreshTimer = undefined } }
function updateTimer() {
  stopTimer()
  if (mode.value === 'dynamic' && props.playing) {
    syncDynamicStart(); void reload()
    refreshTimer = setInterval(() => { syncDynamicStart(); void reload() }, refreshStep.value * 1000)
  }
}

async function reload() {
  if (!props.recordingId || !props.channels.length) return
  if (loading.value) { queued = true; return }
  if (requestedRange.value.end - requestedRange.value.start < 4) return
  const current = ++requestId; loading.value = true; error.value = ''
  try {
    const data = await getConfiguredSpectrogram(props.recordingId, {
      mode: 'spectrogram',
      channels: props.channels,
      time: { start_s: requestedRange.value.start, end_s: requestedRange.value.end },
      dynamic_window_s: dynamicWindow.value,
      refresh_step_s: refreshStep.value,
      custom_frequency_range: selectedBand.value === 'custom' ? { low_hz: frequencyRange.value.min, high_hz: frequencyRange.value.max } : undefined,
    })
    if (current === requestId) { result.value = data; if (!data.channels.includes(selected.value)) selected.value = '' }
  }
  catch (cause) { if (current === requestId) error.value = cause instanceof Error ? cause.message : '时频图读取失败' }
  finally { if (current === requestId) loading.value = false; if (queued && current === requestId) { queued = false; void reload() } }
}

watch(() => [props.recordingId, props.durationS, props.channels.join('|'), props.activeRange?.start, props.activeRange?.end, mode.value, dynamicWindow.value], reload, { immediate: true })
watch(() => props.startS, () => {
  // Static spectrogram follows an explicitly committed analysis range. During
  // playback, startS is a 20 Hz display coordinate and is not an analysis event.
  if (mode.value === 'static' && !props.activeRange) void reload()
})
watch(() => [props.playing, mode.value, dynamicWindow.value, refreshStep.value], updateTimer)
watch(selectedBand, (band) => { if (band === 'custom') void reload() })
watch(() => [customMinHz.value, customMaxHz.value], () => { if (selectedBand.value === 'custom' && frequencyRange.value.max > frequencyRange.value.min) void reload() })
onBeforeUnmount(stopTimer)
</script>
<template>
  <section class="spectrogram-panel" aria-label="时频图">
    <header class="spectrum-header"><strong>时频图</strong><span v-if="result">实际分析范围 {{ (result.actual_start_s ?? result.window_start_s).toFixed(2) }}–{{ (result.actual_end_s ?? result.window_start_s + result.window_duration_s).toFixed(2) }} s · {{ result.algorithm_version }} · dB µV²/Hz</span></header>
    <p v-if="loading && !result" class="spectrum-empty">正在计算时频图…</p><p v-else-if="error && !result" class="spectrum-error">{{ error }}</p>
    <template v-if="result"><div class="spectrum-controls"><label>模式 <select v-model="mode"><option value="static">静态时频图</option><option value="dynamic">动态时频图</option></select></label><label v-if="mode === 'dynamic'">分析范围 <select v-model.number="dynamicWindow"><option :value="5">最近 5 s</option><option :value="10">最近 10 s</option><option :value="20">最近 20 s</option></select></label><label v-if="mode === 'dynamic'">更新间隔 <select v-model.number="refreshStep"><option :value="1">每 1 s</option><option :value="2">每 2 s</option><option :value="5">每 5 s</option></select></label><label>通道 <select :value="channel" @change="selected = ($event.target as HTMLSelectElement).value"><option v-for="name in result.channels" :key="name">{{ name }}</option></select></label><label>频段 <select v-model="selectedBand"><option value="all">全频 1–30 Hz</option><option value="delta">Delta 1–4 Hz</option><option value="theta">Theta 4–8 Hz</option><option value="alpha">Alpha 8–13 Hz</option><option value="beta">Beta 13–30 Hz</option><option value="custom">自定义范围</option></select></label><label v-if="selectedBand === 'custom'">起始 <input v-model.number="customMinHz" type="number" min="1" max="30" step="0.25" /></label><label v-if="selectedBand === 'custom'">结束 <input v-model.number="customMaxHz" type="number" min="1" max="30" step="0.25" /></label><label>显示 <select v-model="viewMode"><option value="trend">频段趋势</option><option value="heatmap">时频热图</option><option value="both">趋势 + 热图</option></select></label><button type="button" @click="reload">刷新时频图</button><button type="button" @click="algorithmOpen = true">算法校验</button><span v-if="loading" class="spectrum-status">正在更新</span></div><p class="spectrogram-reading-guide">{{ selectedBand === 'all' ? '频段趋势用于判断哪个波、什么时候变化；需要定位具体频率时选择热图。' : selectedBand === 'custom' ? `当前聚焦 ${frequencyRange.min}–${frequencyRange.max} Hz；趋势值由后端逐个频谱计算片段积分，热图仍使用完整精度矩阵。` : `当前聚焦 ${selectedBand.toUpperCase()} 频段。后端仍保留完整频率矩阵。` }}</p><div class="spectrogram-band-guide"><span>Delta 1–4 Hz</span><span>Theta 4–8 Hz</span><span>Alpha 8–13 Hz</span><span>Beta 13–30 Hz</span></div><p v-if="result.quality" class="spectrogram-quality-summary">质量门：{{ result.quality.clean_windows }}/{{ result.quality.total_windows }} clean<span v-if="result.quality.bad_windows"> · {{ result.quality.bad_windows }} 个计算片段已拒绝（灰色区域保留时间位置，悬停可查看原因）</span><span v-else> · 全部计算片段通过</span></p><BandPowerTrendChart v-if="result.band_power_timeseries && viewMode !== 'heatmap'" :result="result" :channel="channel" :band="selectedBand" /><p v-if="error" class="spectrum-error">{{ error }}</p><SpectrogramChart v-if="viewMode !== 'trend'" :result="result" :channel="channel" :frequency-range="frequencyRange" /></template><p v-else-if="!loading && !error" class="spectrum-empty">暂无时频数据</p>
    <SpectrogramAlgorithmDialog v-if="algorithmOpen && result" :result="result" :channel="channel" :recording-id="props.recordingId" @close="algorithmOpen = false" />
  </section>
</template>
