<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from 'vue'
import { getSpectrogram } from '../api/spectrogram'

const props = defineProps<{ recordingId?: string; startS: number; channels: string[] }>()
const result = ref<Awaited<ReturnType<typeof getSpectrogram>> | null>(null)
const loading = ref(false)
const error = ref('')
const selected = ref('')
const canvas = ref<HTMLCanvasElement | null>(null)
let requestId = 0
let resizeObserver: ResizeObserver | null = null
const channel = computed(() => selected.value || result.value?.channels[0] || '')

function color(value: number, min: number, max: number): string {
  const ratio = Math.max(0, Math.min(1, (value - min) / Math.max(max - min, 1e-9)))
  const stops = [[8, 26, 112], [45, 48, 220], [90, 160, 220], [255, 138, 34]]
  const position = ratio * (stops.length - 1); const left = Math.floor(position); const amount = position - left
  const a = stops[Math.min(left, stops.length - 1)]; const b = stops[Math.min(left + 1, stops.length - 1)]
  return `rgb(${Math.round(a[0] + (b[0] - a[0]) * amount)},${Math.round(a[1] + (b[1] - a[1]) * amount)},${Math.round(a[2] + (b[2] - a[2]) * amount)})`
}

function draw() {
  const target = canvas.value; const matrix = result.value?.power[channel.value]
  if (!target || !matrix?.length || !matrix[0]?.length) return
  const rect = target.getBoundingClientRect(); const ratio = window.devicePixelRatio || 1
  target.width = Math.max(1, Math.floor(rect.width * ratio)); target.height = Math.max(1, Math.floor(rect.height * ratio))
  const context = target.getContext('2d'); if (!context) return; context.setTransform(ratio, 0, 0, ratio, 0, 0)
  const width = rect.width; const height = rect.height; const values = matrix.flat().map((value) => 10 * Math.log10(Math.max(value, 1e-20)))
  values.sort((a, b) => a - b); const min = values[Math.floor(values.length * 0.05)] ?? -120; const max = values[Math.floor(values.length * 0.98)] ?? min + 1
  const columns = matrix.length; const rows = matrix[0].length; const cellWidth = width / columns; const cellHeight = height / rows
  for (let x = 0; x < columns; x += 1) for (let y = 0; y < rows; y += 1) { const db = 10 * Math.log10(Math.max(matrix[x][rows - y - 1], 1e-20)); context.fillStyle = color(db, min, max); context.fillRect(x * cellWidth, y * cellHeight, Math.ceil(cellWidth), Math.ceil(cellHeight)) }
}

async function reload() {
  if (!props.recordingId || !props.channels.length) return
  const current = ++requestId; loading.value = true; error.value = ''
  try { const data = await getSpectrogram(props.recordingId, props.startS, 30, props.channels); if (current === requestId) { result.value = data; if (!data.channels.includes(selected.value)) selected.value = ''; await nextTick(); draw() } }
  catch (cause) { if (current === requestId) error.value = cause instanceof Error ? cause.message : '时频图读取失败' }
  finally { if (current === requestId) loading.value = false }
}

watch(() => [props.recordingId, props.channels], reload, { immediate: true })
watch(channel, () => nextTick(draw))
onBeforeUnmount(() => resizeObserver?.disconnect())
watch(canvas, (target) => { if (!target) return; resizeObserver = new ResizeObserver(draw); resizeObserver.observe(target) })
</script>
<template>
  <section class="spectrogram-panel" aria-label="时频图">
    <header class="spectrum-header"><strong>时频图</strong><span v-if="result">{{ result.window_start_s.toFixed(2) }}–{{ (result.window_start_s + result.window_duration_s).toFixed(2) }} s · dB µV²/Hz</span></header>
    <p v-if="loading" class="spectrum-empty">正在计算时频图…</p><p v-else-if="error" class="spectrum-error">{{ error }}</p>
    <template v-else-if="result"><div class="spectrum-controls"><label>通道 <select :value="channel" @change="selected = ($event.target as HTMLSelectElement).value"><option v-for="name in result.channels" :key="name">{{ name }}</option></select></label><button type="button" @click="reload">刷新时频图</button></div><div class="spectrogram-plot"><div class="spectrogram-y-axis"><span>30 Hz</span><span>15 Hz</span><span>1 Hz</span></div><canvas ref="canvas" class="spectrogram-canvas" aria-label="功率时频图"></canvas></div><div class="spectrogram-x-axis"><span>{{ result.times_s[0]?.toFixed(1) }} s</span><span>{{ result.times_s.at(-1)?.toFixed(1) }} s</span></div><div class="spectrogram-legend"><span>低功率</span><i></i><span>高功率</span></div></template><p v-else class="spectrum-empty">暂无时频数据</p>
  </section>
</template>
