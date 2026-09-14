<script setup lang="ts">
import { computed, onBeforeUnmount, ref, toRef, watch } from 'vue'
import { useSpectrum } from '../composables/useSpectrum'
import type { SpectrumBand } from '../types/spectrum'
import SpectrumAlgorithmDialog from './SpectrumAlgorithmDialog.vue'
import BandPowerChart from './BandPowerChart.vue'
import SpectrumPsdChart from './SpectrumPsdChart.vue'
import { playbackPageStart } from '../utils/waveformViewport'

const props = defineProps<{ recordingId?: string; startS: number; channels: string[]; positionS?: number; playing?: boolean; totalDurationS?: number; screenDurationS?: number; selectedRange?: { start: number; end: number } | null }>()
const emit = defineEmits<{ activeRangeChange: [start: number, end: number, source: string] }>()
const mode = ref<'static' | 'dynamic'>('static')
const dynamicStart = ref(0)
const dynamicEnd = ref(0)
const dynamicWindow = ref(10)
const refreshStep = ref(1)
const rounded = (value: number) => Math.round(value * 1000) / 1000
const staticInputs = ref({ start: rounded(props.startS), end: rounded(Math.min(props.totalDurationS ?? props.startS + 30, props.startS + 30)) })
const staticRange = ref({ ...staticInputs.value })
const rangeError = ref('')
const customStaticRange = ref(false)
const pendingRangeSource = ref<'custom' | 'selection'>('custom')
const syncedDisplayPageStart = ref(playbackPageStart(props.startS, props.screenDurationS ?? 10, props.totalDurationS))
let refreshTimer: ReturnType<typeof setInterval> | undefined
function syncDynamicWindow() {
  const end = Math.max(0, Math.floor(props.positionS ?? 0))
  dynamicEnd.value = end
  dynamicStart.value = Math.max(0, end - dynamicWindow.value)
}
function stopTimer() { if (refreshTimer) { clearInterval(refreshTimer); refreshTimer = undefined } }
function updateTimer() {
  stopTimer()
  if (mode.value === 'dynamic' && props.playing) { syncDynamicWindow(); refreshTimer = setInterval(syncDynamicWindow, refreshStep.value * 1000) }
}
const analysisStart = computed(() => mode.value === 'dynamic' ? dynamicStart.value : staticRange.value.start)
const analysisWindow = computed(() => mode.value === 'dynamic' ? Math.max(0, dynamicEnd.value - dynamicStart.value) : staticRange.value.end - staticRange.value.start)
const { result, loading, error, reload } = useSpectrum(toRef(props, 'recordingId'), analysisStart, analysisWindow, toRef(props, 'channels'), mode, refreshStep, dynamicWindow)
const bands: SpectrumBand[] = ['delta', 'theta', 'alpha', 'beta']
const selectedChannel = ref('')
const algorithmOpen = ref(false)
watch(() => [props.playing, mode.value, refreshStep.value, dynamicWindow.value], updateTimer)
watch(() => props.recordingId, () => useCurrentThirtySeconds())
watch(() => props.totalDurationS, () => { if (mode.value === 'static' && !customStaticRange.value) useCurrentThirtySeconds() })
watch(() => props.startS, () => {
  // Continuous playback updates the visible viewport every packet. Static PSD
  // must not turn those display updates into a spectral request storm.
  // windowStartS is a fixed page start during playback, so this watcher fires
  // only when that page changes, not once per 0.05 s packet.
  const pageStart = playbackPageStart(props.startS, props.screenDurationS ?? 10, props.totalDurationS)
  if (Math.abs(pageStart - syncedDisplayPageStart.value) < 1e-9) return
  syncedDisplayPageStart.value = pageStart
  if (mode.value === 'static' && !customStaticRange.value) useCurrentThirtySeconds()
})
watch(() => props.selectedRange, (range) => {
  if (!range || range.end - range.start < 4) return
  const start = rounded(Math.max(0, range.start)); const end = rounded(Math.min(props.totalDurationS ?? range.end, range.end))
  staticInputs.value = { start, end }
  if (end - start < 4) { rangeError.value = '框选区间不足 4 秒或超出文件范围'; return }
  rangeError.value = ''; customStaticRange.value = true; pendingRangeSource.value = 'selection'; mode.value = 'static'
})
onBeforeUnmount(stopTimer)
const firstChannel = computed(() => selectedChannel.value || result.value?.channels[0] || '')
function applyStaticRange() {
  if (staticInputs.value.start < 0 || staticInputs.value.end <= staticInputs.value.start) { rangeError.value = '结束时间必须大于开始时间'; return }
  if (staticInputs.value.end - staticInputs.value.start < 4) { rangeError.value = '分析区间至少需要 4 秒'; return }
  if (props.totalDurationS !== undefined && staticInputs.value.end > props.totalDurationS) { rangeError.value = '结束时间不能超过文件时长'; return }
  rangeError.value = ''
  customStaticRange.value = true
  staticRange.value = { ...staticInputs.value }
  emit('activeRangeChange', staticRange.value.start, staticRange.value.end, pendingRangeSource.value)
  pendingRangeSource.value = 'custom'
}
function useCurrentThirtySeconds() {
  const start = Math.max(0, props.startS); const end = Math.min(props.totalDurationS ?? start + 30, start + 30)
  customStaticRange.value = false
  staticInputs.value = { start: rounded(start), end: rounded(end) }; staticRange.value = { start: rounded(start), end: rounded(end) }; emit('activeRangeChange', staticRange.value.start, staticRange.value.end, 'current_30s')
}
function useCurrentScreen() {
  const start = Math.max(0, props.startS); const end = Math.min(props.totalDurationS ?? start + (props.screenDurationS ?? 10), start + (props.screenDurationS ?? 10))
  customStaticRange.value = false
  staticInputs.value = { start: rounded(start), end: rounded(end) }; staticRange.value = { start: rounded(start), end: rounded(end) }; emit('activeRangeChange', staticRange.value.start, staticRange.value.end, 'current_screen')
}
</script>
<template>
  <section class="spectrum-panel" aria-label="频谱分析">
    <header class="spectrum-header"><strong>PSD 与频段功率</strong><span v-if="result">{{ mode === 'dynamic' ? `动态频谱 · 最近 ${dynamicWindow} s · 每 ${refreshStep} s 更新` : '静态频谱 · 已提交分析区间' }} · {{ result.window_start_s.toFixed(2) }}–{{ (result.window_start_s + result.window_duration_s).toFixed(2) }} s</span></header>
    <template v-if="result">
      <p v-if="loading" class="spectrum-loading-status" role="status">正在更新数据…</p>
      <p v-if="error" class="spectrum-error" role="alert">{{ error }}</p>
      <div class="spectrum-controls"><label>分析方式 <select v-model="mode"><option value="static">静态频谱 · 稳定摘要</option><option value="dynamic">动态频谱 · 播放中更新</option></select></label><label v-if="mode === 'dynamic'">分析窗口 <select v-model.number="dynamicWindow"><option :value="5">最近 5 s</option><option :value="10">最近 10 s</option><option :value="20">最近 20 s</option><option :value="30">最近 30 s</option></select></label><label v-if="mode === 'dynamic'">更新频率 <select v-model.number="refreshStep"><option :value="1">每 1 s</option><option :value="2">每 2 s</option><option :value="5">每 5 s</option></select></label><label>查看通道 <select :value="firstChannel" @change="selectedChannel = ($event.target as HTMLSelectElement).value"><option v-for="channel in result.channels" :key="channel" :value="channel">{{ channel }}</option></select></label><button type="button" @click="reload">更新结果</button><button type="button" @click="algorithmOpen = true">计算与校验</button></div>
      <div v-if="mode === 'static'" class="spectrum-range-controls"><label>开始 <input v-model.number="staticInputs.start" type="number" min="0" step="0.1"> s</label><label>结束 <input v-model.number="staticInputs.end" type="number" min="4" step="0.1"> s</label><button type="button" @click="applyStaticRange">分析该区间</button><button type="button" @click="useCurrentThirtySeconds">当前 30 秒</button><button type="button" @click="useCurrentScreen">当前屏幕</button></div>
      <p v-if="rangeError && mode === 'static'" class="spectrum-error" role="alert">{{ rangeError }}</p>
      <SpectrumPsdChart :frequencies-hz="result.frequencies_hz" :values="result.psd[firstChannel] ?? []" :channel="firstChannel" />
      <div class="band-share-chart"><header><strong>频段占比</strong><span>各通道 RBP · 总和约 100%</span></header><BandPowerChart :channels="result.channels" :relative-band-power="result.relative_band_power" /></div>
      <div class="spectrum-table"><div class="spectrum-row spectrum-row-head"><span>通道</span><span v-for="band in bands" :key="band">{{ band }} µV² / RBP</span></div><div v-for="channel in result.channels" :key="channel" class="spectrum-row"><strong>{{ channel }}</strong><span v-for="band in bands" :key="band">{{ result.band_power[channel][band].toFixed(2) }} / {{ (result.relative_band_power[channel][band] * 100).toFixed(1) }}%</span></div></div>
      <small class="spectrum-meta">离线分析参考：源记录（不进行软件重参考） · PSD：{{ result.units.psd }} · 4 s Welch 分段 / 50% 重叠 · 有效片段 {{ result.quality.clean_segments }}/{{ result.quality.total_segments }}</small>
    </template>
    <p v-else-if="loading" class="spectrum-empty">正在计算频谱…</p>
    <p v-else-if="error" class="spectrum-error" role="alert">{{ error }}</p>
    <p v-else class="spectrum-empty">暂无频谱数据</p>
    <SpectrumAlgorithmDialog v-if="algorithmOpen && result" :result="result" :channel="firstChannel" :recording-id="props.recordingId" :mode="mode" :dynamic-window-s="dynamicWindow" :refresh-step-s="refreshStep" @close="algorithmOpen = false" />
  </section>
</template>
