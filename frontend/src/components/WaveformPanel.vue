<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { CSS_PIXELS_PER_MM } from '../utils/waveformGeometry'
import EventMarkerControls from './EventMarkerControls.vue'
import type { EventMarker } from '../api/recordings'

type NumericSeries = ArrayLike<number>
type Waveform = { elapsed_s: NumericSeries; channels: Record<string, NumericSeries> }
type StreamInfo = { sfreq: number; channelNames: string[]; startS: number } | null

const props = defineProps<{ waveform: Waveform; stream?: StreamInfo; positionS?: number; totalDurationS?: number; windowStartS?: number; windowDurationS?: number; sensitivityUvPerMm?: number; events?: EventMarker[] }>()
const emit = defineEmits<{ windowRequested: [startS: number]; renderStats: [summary: string]; progress: [positionS: number, windowStartS: number]; createEvent: [timeS: number, label: string, durationS: number | null]; jumpEvent: [timeS: number]; removeEvent: [markerId: string] }>()
const COLORS = ['#ef5350', '#5b9bd5', '#62bd69', '#ad65c7', '#ff943f']
const PLOT_LEFT_PX = 74
const PLOT_RIGHT_PX = 12
const canvas = ref<HTMLCanvasElement | null>(null)
const surface = ref<HTMLDivElement | null>(null)
const timelineSurface = ref<HTMLDivElement | null>(null)
const viewStart = ref(0)
const viewDuration = ref(10)
const plotDragging = ref(false)
const timelineDragging = ref(false)
const pendingTimelineStart = ref<number | null>(null)
let renderer: Worker | null = null
let resizeObserver: ResizeObserver | undefined
let dragX = 0
let dragStart = 0
let timelineDragX = 0
let timelineDragStart = 0

// 流式回放使用当前环形缓冲的窗口边界；静态阅图才使用响应式波形数据边界。
const dataStart = computed(() => props.stream
  ? (props.windowStartS ?? props.stream.startS)
  : (props.waveform.elapsed_s[0] ?? props.windowStartS ?? 0))
const dataEnd = computed(() => props.stream
  ? (props.windowStartS ?? props.stream.startS) + (props.windowDurationS ?? 1)
  : (props.waveform.elapsed_s.length ? props.waveform.elapsed_s[props.waveform.elapsed_s.length - 1] : dataStart.value + (props.windowDurationS ?? 1)))
const dataDuration = computed(() => Math.max(0.1, dataEnd.value - dataStart.value))
const totalS = computed(() => Math.max(dataEnd.value, props.totalDurationS ?? 0, 1))
const viewEnd = computed(() => Math.min(dataEnd.value, viewStart.value + viewDuration.value))
const channelNames = computed(() => props.stream?.channelNames ?? Object.keys(props.waveform.channels))
const labels = computed(() => channelNames.value.map((name, index) => ({ name, color: COLORS[index % COLORS.length], top: `${(index + .58) / (channelNames.value.length + 1) * 100}%` })))
const axisTickStep = computed(() => {
  if (viewDuration.value <= 2) return 0.1
  if (viewDuration.value <= 5) return 0.5
  if (viewDuration.value <= 15) return 1
  if (viewDuration.value <= 30) return 2
  return 5
})
const ticks = computed(() => {
  const step = axisTickStep.value
  const end = viewStart.value + viewDuration.value
  const first = Math.ceil((viewStart.value - Number.EPSILON) / step) * step
  const values: Array<{ time: number; label: string }> = []
  for (let time = first; time <= end + Number.EPSILON; time += step) {
    values.push({ time, label: formatAxisTime(time) })
  }
  return values
})
const overviewTicks = computed(() => Array.from({ length: 5 }, (_, index) => ({ time: totalS.value * index / 4, label: formatTime(totalS.value * index / 4) })))
const shownTimelineStart = computed(() => pendingTimelineStart.value ?? viewStart.value)
const timelineWindowStyle = computed(() => ({ left: `${shownTimelineStart.value / totalS.value * 100}%`, width: `${Math.max(2, Math.min(100, viewDuration.value / totalS.value * 100))}%` }))
const sensitivity = computed(() => props.sensitivityUvPerMm ?? 7)
const referenceBarHeight = computed(() => `${50 / sensitivity.value * CSS_PIXELS_PER_MM}px`)
const visibleEvents = computed(() => (props.events ?? []).filter((event) => event.time_s >= viewStart.value && event.time_s <= viewEnd.value))
function createEvent(timeS: number, label: string, durationS: number | null) { emit('createEvent', timeS, label, durationS) }

watch(() => [dataStart.value, dataDuration.value, props.windowDurationS] as const, ([start, duration, requested]) => {
  viewStart.value = start
  viewDuration.value = Math.min(requested ?? duration, duration)
  sendViewport()
}, { immediate: true })
watch(() => props.stream, (stream) => {
  if (stream) {
    // 回放会话的服务端起点是扫屏和时间轴的共同基准，不能沿用静态预览的旧窗口。
    viewStart.value = stream.startS
    viewDuration.value = props.windowDurationS ?? 10
  }
  sendFrame()
})
watch(() => props.waveform, () => { if (!props.stream) sendFrame() })
function attachWheel() { surface.value?.addEventListener('wheel', zoom, { passive: false }) }
watch(channelNames, () => nextTick(() => { ensureRenderer(); attachWheel() }))

function formatTime(seconds: number) {
  const value = Math.max(0, Math.round(seconds))
  return `${String(Math.floor(value / 60)).padStart(2, '0')}:${String(value % 60).padStart(2, '0')}`
}

function formatAxisTime(seconds: number) {
  const precision = axisTickStep.value < 1 ? 1 : 0
  if (!precision) return formatTime(seconds)
  const value = Math.max(0, seconds)
  return `${String(Math.floor(value / 60)).padStart(2, '0')}:${(value % 60).toFixed(precision).padStart(2 + 1 + precision, '0')}`
}

function sendFrame() {
  if (!renderer) return
  if (props.stream) {
    // Vue props 中的数组可能是 Proxy；Worker 只能接收可结构化克隆的数据。
    renderer.postMessage({ type: 'stream', sfreq: props.stream.sfreq, channelNames: [...props.stream.channelNames], startS: props.stream.startS, windowSeconds: props.windowDurationS ?? 10 })
  } else {
    renderer.postMessage({ type: 'static', frame: { elapsed: props.waveform.elapsed_s, channels: props.waveform.channels } })
  }
  sendViewport()
}

function sendViewport() { renderer?.postMessage({ type: 'viewport', startS: viewStart.value, durationS: viewDuration.value, sensitivityUvPerMm: sensitivity.value }) }
function sendResize() { if (surface.value) renderer?.postMessage({ type: 'resize', width: surface.value.clientWidth, height: surface.value.clientHeight, dpr: window.devicePixelRatio || 1 }) }

/** WebSocket ArrayBuffer 转移至 Worker，不在主线程解析、拷贝或绘图。 */
function appendBinaryWaveform(buffer: ArrayBuffer) {
  if (!renderer) return false
  renderer.postMessage({ type: 'append', buffer }, [buffer])
  return true
}

function clampDataStart(value: number, duration = viewDuration.value) { return Math.max(dataStart.value, Math.min(dataEnd.value - duration, value)) }
function zoom(event: WheelEvent) {
  event.preventDefault()
  const target = event.currentTarget as HTMLElement
  const ratio = Math.max(0, Math.min(1, (event.clientX - target.getBoundingClientRect().left - PLOT_LEFT_PX) / Math.max(1, target.clientWidth - PLOT_LEFT_PX - PLOT_RIGHT_PX)))
  const nextDuration = Math.max(.5, Math.min(dataDuration.value, viewDuration.value * (event.deltaY > 0 ? 1.2 : .82)))
  const anchor = viewStart.value + viewDuration.value * ratio
  viewDuration.value = nextDuration
  viewStart.value = clampDataStart(anchor - nextDuration * ratio, nextDuration)
  sendViewport()
}

function beginDrag(event: PointerEvent) {
  plotDragging.value = true
  dragX = event.clientX
  dragStart = viewStart.value
  ;(event.currentTarget as HTMLElement).setPointerCapture(event.pointerId)
}
function moveDrag(event: PointerEvent) {
  if (!plotDragging.value) return
  const target = event.currentTarget as HTMLElement
  viewStart.value = clampDataStart(dragStart - (event.clientX - dragX) / Math.max(1, target.clientWidth - PLOT_LEFT_PX) * viewDuration.value)
  sendViewport()
}
function clampTimelineStart(value: number, duration = viewDuration.value) {
  return Math.max(0, Math.min(Math.max(0, totalS.value - duration), value))
}
function beginTimelineDrag(event: PointerEvent) {
  const target = event.currentTarget as HTMLElement
  const ratio = Math.max(0, Math.min(1, (event.clientX - target.getBoundingClientRect().left) / target.clientWidth))
  timelineDragStart = clampTimelineStart(totalS.value * ratio - viewDuration.value / 2)
  pendingTimelineStart.value = timelineDragStart
  timelineDragging.value = true
  timelineDragX = event.clientX
  timelineSurface.value?.setPointerCapture(event.pointerId)
}
function beginTimelineWindowDrag(event: PointerEvent) {
  event.stopPropagation()
  timelineDragStart = viewStart.value
  pendingTimelineStart.value = viewStart.value
  timelineDragging.value = true
  timelineDragX = event.clientX
  timelineSurface.value?.setPointerCapture(event.pointerId)
}
function moveTimelineDrag(event: PointerEvent) {
  if (!timelineDragging.value) return
  const target = event.currentTarget as HTMLElement
  pendingTimelineStart.value = clampTimelineStart(timelineDragStart + (event.clientX - timelineDragX) / Math.max(1, target.clientWidth) * totalS.value)
}
function endTimelineDrag() {
  if (!timelineDragging.value) return
  timelineDragging.value = false
  const start = pendingTimelineStart.value
  pendingTimelineStart.value = null
  if (start !== null) emit('windowRequested', start)
}

function ensureRenderer() {
  if (renderer) return
  const element = canvas.value
  if (!element || !('transferControlToOffscreen' in element)) {
    if (element) emit('renderStats', '当前浏览器不支持 OffscreenCanvas，无法启用高性能波形引擎')
    return
  }
  renderer = new Worker(new URL('../workers/waveformRenderer.worker.ts', import.meta.url), { type: 'module' })
  renderer.onmessage = (event: MessageEvent<{ type: string; summary?: string; elapsedS?: number; sweepStartS?: number }>) => {
    if (event.data.type === 'stats' && event.data.summary) emit('renderStats', event.data.summary)
    if (event.data.type === 'progress' && event.data.elapsedS !== undefined && event.data.sweepStartS !== undefined) emit('progress', event.data.elapsedS, event.data.sweepStartS)
  }
  const offscreen = element.transferControlToOffscreen()
  renderer.postMessage({ type: 'canvas', canvas: offscreen }, [offscreen])
  resizeObserver = new ResizeObserver(sendResize)
  if (surface.value) resizeObserver.observe(surface.value)
  nextTick(() => { sendResize(); sendFrame() })
}
onMounted(() => nextTick(() => { ensureRenderer(); attachWheel() }))
onBeforeUnmount(() => { resizeObserver?.disconnect(); renderer?.terminate(); surface.value?.removeEventListener('wheel', zoom) })
defineExpose({ appendBinaryWaveform })
</script>

<template>
  <section class="waveform-panel">
    <div class="waveform-titlebar"><span class="section-title">EEG 波形预览</span><span class="waveform-hint">{{ formatAxisTime(viewStart) }} – {{ formatAxisTime(viewEnd) }} · 滚轮调整时间尺度，拖动平移</span></div>
    <div ref="surface" class="plot-surface waveform-canvas-surface" :class="{ dragging: plotDragging }" @pointerdown="beginDrag" @pointermove="moveDrag" @pointerup="plotDragging = false" @pointercancel="plotDragging = false">
      <div v-if="!channelNames.length" class="empty-waveform-overlay">导入文件后显示波形</div>
      <canvas ref="canvas" class="waveform-canvas" aria-label="脑电波形"></canvas>
      <span v-for="label in labels" :key="label.name" class="channel-label-text" :style="{ color: label.color, top: label.top }">{{ label.name }}</span>
      <div class="scale-marker" aria-label="50 微伏垂直标尺"><span class="scale-marker-line" :style="{ height: referenceBarHeight }"></span><span>50 µV</span></div>
      <span class="scale-label-text">{{ sensitivity }} µV/mm</span>
    </div>
    <template v-if="channelNames.length">
      <div class="time-axis" aria-label="波形时间轴"><span v-for="tick in ticks" :key="tick.time" :style="{ left: `${(tick.time - viewStart) / viewDuration * 100}%` }">{{ tick.label }}</span></div>
      <div ref="timelineSurface" class="timeline-overview" :class="{ dragging: timelineDragging }" aria-label="拖动时间轴浏览波形" @pointerdown.prevent="beginTimelineDrag" @pointermove="moveTimelineDrag" @pointerup="endTimelineDrag" @pointercancel="endTimelineDrag">
        <span v-for="tick in overviewTicks" :key="tick.time" class="overview-tick" :style="{ left: `${tick.time / totalS * 100}%` }">{{ tick.label }}</span>
        <span v-for="event in visibleEvents" :key="`event-${event.id}`" class="event-marker-line" :style="{ left: `${(event.time_s - viewStart) / viewDuration * 100}%` }" :title="`${event.label} · ${event.time_s.toFixed(3)} s`"></span>
        <div class="timeline-window" :style="timelineWindowStyle" @pointerdown.prevent="beginTimelineWindowDrag"><span class="timeline-grip" aria-hidden="true"></span></div>
      </div>
    </template>
    <EventMarkerControls :markers="events ?? []" :current-time-s="positionS ?? viewStart" :total-duration-s="totalDurationS" @create="createEvent" @jump="emit('jumpEvent', $event)" @remove="emit('removeEvent', $event)" />
  </section>
</template>
