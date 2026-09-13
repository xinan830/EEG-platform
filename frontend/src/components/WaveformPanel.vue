<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { CSS_PIXELS_PER_MM } from '../utils/waveformGeometry'
import { clampPageViewportStart, playbackPageStart } from '../utils/waveformViewport'
import EventMarkerControls from './EventMarkerControls.vue'
import type { EventMarker } from '../api/recordings'

type NumericSeries = ArrayLike<number>
type Waveform = { elapsed_s: NumericSeries; channels: Record<string, NumericSeries> }
type StreamInfo = { sfreq: number; channelNames: string[]; startS: number } | null

const props = defineProps<{ waveform: Waveform; stream?: StreamInfo; positionS?: number; playing?: boolean; totalDurationS?: number; windowStartS?: number; windowDurationS?: number; sensitivityUvPerMm?: number; events?: EventMarker[] }>()
const emit = defineEmits<{ windowRequested: [startS: number]; renderStats: [summary: string]; progress: [positionS: number, windowStartS: number]; createEvent: [timeS: number, label: string, durationS: number | null]; jumpEvent: [timeS: number]; removeEvent: [markerId: string]; analysisRangeSelected: [startS: number, endS: number] }>()
const COLORS = ['#ef5350', '#5b9bd5', '#62bd69', '#ad65c7', '#ff943f']
const PLOT_LEFT_PX = 74
const PLOT_RIGHT_PX = 12
const canvas = ref<HTMLCanvasElement | null>(null)
const surface = ref<HTMLDivElement | null>(null)
const timelineSurface = ref<HTMLDivElement | null>(null)
const viewStart = ref(0)
const viewDuration = ref(10)
const streamPageStart = ref(0)
const plotDragging = ref(false)
const timelineDragging = ref(false)
const selectingAnalysis = ref(false)
const selectionStartX = ref<number | null>(null)
const selectionEndX = ref<number | null>(null)
const pendingTimelineStart = ref<number | null>(null)
const timelineHoverS = ref<number | null>(null)
const timelinePointerRatio = ref(0)
let renderer: Worker | null = null
let resizeObserver: ResizeObserver | undefined
let dragX = 0
let dragStart = 0
let timelineGrabOffsetS = 0

// Stream origin and visible viewport are separate. windowStartS must not
// redefine the Worker's internal buffer range on every playback packet.
const dataStart = computed(() => props.stream
  ? props.stream.startS
  : (props.waveform.elapsed_s[0] ?? props.windowStartS ?? 0))
const dataEnd = computed(() => props.stream
  ? Math.max(props.stream.startS + (props.windowDurationS ?? 1), props.totalDurationS ?? 0)
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
const timelineWindowStyle = computed(() => ({ left: `${shownTimelineStart.value / totalS.value * 100}%`, width: `${Math.min(100, viewDuration.value / totalS.value * 100)}%` }))
const sensitivity = computed(() => props.sensitivityUvPerMm ?? 7)
const referenceBarHeight = computed(() => `${50 / sensitivity.value * CSS_PIXELS_PER_MM}px`)
const timelineTargetStart = computed(() => pendingTimelineStart.value ?? (timelineHoverS.value === null ? null : clampTimelineStart(timelineHoverS.value - viewDuration.value / 2)))
const timelineTooltipStyle = computed(() => ({ left: `${Math.max(.08, Math.min(.92, timelinePointerRatio.value)) * 100}%` }))
const playbackHeadStyle = computed(() => ({ left: `${Math.max(0, Math.min(100, (props.positionS ?? viewStart.value) / totalS.value * 100))}%` }))
const selectionStyle = computed(() => {
  if (selectionStartX.value === null || selectionEndX.value === null) return { display: 'none' }
  const left = Math.min(selectionStartX.value, selectionEndX.value); const right = Math.max(selectionStartX.value, selectionEndX.value)
  return { left: `${left}px`, width: `${right - left}px` }
})
function createEvent(timeS: number, label: string, durationS: number | null) { emit('createEvent', timeS, label, durationS) }

watch(() => [dataStart.value, dataDuration.value, props.windowDurationS] as const, ([start, duration, requested]) => {
  if (props.stream) return
  viewStart.value = start
  viewDuration.value = Math.min(requested ?? duration, duration)
  sendViewport()
}, { immediate: true })
watch(() => props.stream, (stream) => {
  if (stream) {
    const pageDuration = props.windowDurationS ?? 10
    streamPageStart.value = playbackPageStart(stream.startS, pageDuration, props.totalDurationS)
    viewStart.value = streamPageStart.value
    viewDuration.value = pageDuration
  }
  sendFrame()
})
watch(() => [props.positionS, props.playing, props.stream] as const, ([position, playing, stream]) => {
  if (!stream || !playing || position === undefined) return
  const pageDuration = props.windowDurationS ?? 10
  const nextPageStart = playbackPageStart(position, pageDuration, props.totalDurationS)
  if (Math.abs(nextPageStart - streamPageStart.value) < 1e-9) return
  const pageOffset = clampPageViewportStart(viewStart.value, viewDuration.value, streamPageStart.value, pageDuration) - streamPageStart.value
  streamPageStart.value = nextPageStart
  viewStart.value = nextPageStart + pageOffset
})
watch(() => props.windowDurationS, (requested) => {
  if (!props.stream || requested === undefined) return
  streamPageStart.value = playbackPageStart(props.positionS ?? props.stream.startS, requested, props.totalDurationS)
  viewDuration.value = requested
  viewStart.value = streamPageStart.value
  sendViewport()
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

function sendViewport() { renderer?.postMessage({ type: 'viewport', startS: viewStart.value, durationS: viewDuration.value, pageDurationS: props.windowDurationS ?? viewDuration.value, sensitivityUvPerMm: sensitivity.value }) }
function sendResize() {
  if (!surface.value) return
  // Large full-screen canvases can saturate Chrome's GPU compositor at native
  // HiDPI scale. 1.25 keeps traces crisp without multiplying raster memory.
  const dpr = Math.min(window.devicePixelRatio || 1, 1.25)
  renderer?.postMessage({ type: 'resize', width: surface.value.clientWidth, height: surface.value.clientHeight, dpr })
}

/** WebSocket ArrayBuffer 转移至 Worker，不在主线程解析、拷贝或绘图。 */
function appendBinaryWaveform(buffer: ArrayBuffer) {
  if (!renderer) return false
  renderer.postMessage({ type: 'append', buffer }, [buffer])
  return true
}

function clampDataStart(value: number, duration = viewDuration.value) {
  if (!props.stream) return Math.max(dataStart.value, Math.min(dataEnd.value - duration, value))
  const pageDuration = props.windowDurationS ?? duration
  return clampPageViewportStart(value, duration, streamPageStart.value, pageDuration)
}
function zoom(event: WheelEvent) {
  event.preventDefault()
  const target = event.currentTarget as HTMLElement
  const ratio = Math.max(0, Math.min(1, (event.clientX - target.getBoundingClientRect().left - PLOT_LEFT_PX) / Math.max(1, target.clientWidth - PLOT_LEFT_PX - PLOT_RIGHT_PX)))
  const maximumDuration = props.stream ? (props.windowDurationS ?? viewDuration.value) : dataDuration.value
  const nextDuration = Math.max(.5, Math.min(maximumDuration, viewDuration.value * (event.deltaY > 0 ? 1.2 : .82)))
  const anchor = viewStart.value + viewDuration.value * ratio
  viewDuration.value = nextDuration
  viewStart.value = clampDataStart(anchor - nextDuration * ratio, nextDuration)
  sendViewport()
}

function beginDrag(event: PointerEvent) {
  if (selectingAnalysis.value) {
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect()
    selectionStartX.value = Math.max(PLOT_LEFT_PX, Math.min(rect.width - PLOT_RIGHT_PX, event.clientX - rect.left))
    selectionEndX.value = selectionStartX.value
    ;(event.currentTarget as HTMLElement).setPointerCapture(event.pointerId)
    return
  }
  plotDragging.value = true
  dragX = event.clientX
  dragStart = viewStart.value
  ;(event.currentTarget as HTMLElement).setPointerCapture(event.pointerId)
}
function moveDrag(event: PointerEvent) {
  if (selectionStartX.value !== null) {
    const rect = (event.currentTarget as HTMLElement).getBoundingClientRect()
    selectionEndX.value = Math.max(PLOT_LEFT_PX, Math.min(rect.width - PLOT_RIGHT_PX, event.clientX - rect.left))
    return
  }
  if (!plotDragging.value) return
  const target = event.currentTarget as HTMLElement
  viewStart.value = clampDataStart(dragStart - (event.clientX - dragX) / Math.max(1, target.clientWidth - PLOT_LEFT_PX) * viewDuration.value)
  sendViewport()
}
function endPlotInteraction(event: PointerEvent) {
  plotDragging.value = false
  if (selectionStartX.value === null || selectionEndX.value === null) return
  const width = Math.max(1, (event.currentTarget as HTMLElement).clientWidth - PLOT_LEFT_PX - PLOT_RIGHT_PX)
  const startRatio = (Math.min(selectionStartX.value, selectionEndX.value) - PLOT_LEFT_PX) / width
  const endRatio = (Math.max(selectionStartX.value, selectionEndX.value) - PLOT_LEFT_PX) / width
  const selectableEnd = props.stream ? Math.min(viewStart.value + viewDuration.value, props.positionS ?? viewStart.value) : (props.totalDurationS ?? Number.POSITIVE_INFINITY)
  const rawStart = Math.min(viewStart.value + startRatio * viewDuration.value, selectableEnd)
  const start = Math.round(Math.max(0, Math.min(rawStart, props.totalDurationS ?? rawStart)) * 1000) / 1000
  const rawEnd = Math.min(viewStart.value + endRatio * viewDuration.value, selectableEnd)
  const end = Math.round(Math.min(rawEnd, props.totalDurationS ?? rawEnd) * 1000) / 1000
  selectionStartX.value = null; selectionEndX.value = null
  if (end - start >= 4) { emit('analysisRangeSelected', start, end); selectingAnalysis.value = false }
}
function clampTimelineStart(value: number, duration = viewDuration.value) {
  return Math.max(0, Math.min(Math.max(0, totalS.value - duration), value))
}
function updateTimelinePointer(event: PointerEvent) {
  const target = timelineSurface.value
  if (!target) return 0
  const rect = target.getBoundingClientRect()
  const ratio = Math.max(0, Math.min(1, (event.clientX - rect.left) / Math.max(1, rect.width)))
  timelinePointerRatio.value = ratio
  timelineHoverS.value = totalS.value * ratio
  return timelineHoverS.value
}
function beginTimelineDrag(event: PointerEvent) {
  const pointerTime = updateTimelinePointer(event)
  timelineGrabOffsetS = viewDuration.value / 2
  pendingTimelineStart.value = clampTimelineStart(pointerTime - timelineGrabOffsetS)
  timelineDragging.value = true
  timelineSurface.value?.setPointerCapture(event.pointerId)
}
function beginTimelineWindowDrag(event: PointerEvent) {
  event.stopPropagation()
  const pointerTime = updateTimelinePointer(event)
  timelineGrabOffsetS = Math.max(0, Math.min(viewDuration.value, pointerTime - viewStart.value))
  pendingTimelineStart.value = viewStart.value
  timelineDragging.value = true
  timelineSurface.value?.setPointerCapture(event.pointerId)
}
function moveTimelineDrag(event: PointerEvent) {
  const pointerTime = updateTimelinePointer(event)
  if (!timelineDragging.value) return
  pendingTimelineStart.value = clampTimelineStart(pointerTime - timelineGrabOffsetS)
}
function endTimelineDrag() {
  if (!timelineDragging.value) return
  timelineDragging.value = false
  const start = pendingTimelineStart.value
  pendingTimelineStart.value = null
  if (start !== null) {
    // Commit the target optimistically so the overview does not jump back to
    // the old window while the parent is fetching the new waveform data.
    viewStart.value = clampTimelineStart(start)
    sendViewport()
    emit('windowRequested', viewStart.value)
  }
}
function leaveTimeline() { if (!timelineDragging.value) timelineHoverS.value = null }

function ensureRenderer() {
  if (renderer) return
  const element = canvas.value
  if (!element || !('transferControlToOffscreen' in element)) {
    if (element) emit('renderStats', '当前浏览器不支持 OffscreenCanvas，无法启用高性能波形引擎')
    return
  }
  renderer = new Worker(new URL('../workers/waveformRenderer.worker.ts', import.meta.url), { type: 'module' })
  renderer.onmessage = (event: MessageEvent<{ type: string; summary?: string; elapsedS?: number; viewStartS?: number }>) => {
    if (event.data.type === 'stats' && event.data.summary) emit('renderStats', event.data.summary)
    if (event.data.type === 'progress' && event.data.elapsedS !== undefined && event.data.viewStartS !== undefined) emit('progress', event.data.elapsedS, event.data.viewStartS)
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
    <div class="waveform-titlebar"><span class="section-title">EEG 波形预览</span><button type="button" class="waveform-select-range" :class="{ active: selectingAnalysis }" @click="selectingAnalysis = !selectingAnalysis">框选频谱区间</button><span class="waveform-hint">{{ formatAxisTime(viewStart) }} – {{ formatAxisTime(viewEnd) }} · 滚轮调整时间尺度，拖动平移</span></div>
    <div ref="surface" class="plot-surface waveform-canvas-surface" :class="{ dragging: plotDragging, selecting: selectingAnalysis }" @pointerdown="beginDrag" @pointermove="moveDrag" @pointerup="endPlotInteraction" @pointercancel="endPlotInteraction">
      <div v-if="!channelNames.length" class="empty-waveform-overlay">导入文件后显示波形</div>
      <canvas ref="canvas" class="waveform-canvas" aria-label="脑电波形"></canvas>
      <div class="analysis-selection" :style="selectionStyle"></div>
      <span v-for="label in labels" :key="label.name" class="channel-label-text" :style="{ color: label.color, top: label.top }">{{ label.name }}</span>
      <div class="scale-marker" aria-label="50 微伏垂直标尺"><span class="scale-marker-line" :style="{ height: referenceBarHeight }"></span><span>50 µV</span></div>
      <span class="scale-label-text">{{ sensitivity }} µV/mm</span>
    </div>
    <template v-if="channelNames.length">
      <div class="time-axis" aria-label="波形时间轴"><span v-for="tick in ticks" :key="tick.time" :style="{ left: `${(tick.time - viewStart) / viewDuration * 100}%` }">{{ tick.label }}</span></div>
      <div ref="timelineSurface" class="timeline-overview" :class="{ dragging: timelineDragging }" aria-label="点击或拖动时间轴浏览波形" @pointerdown.prevent="beginTimelineDrag" @pointermove="moveTimelineDrag" @pointerleave="leaveTimeline" @pointerup="endTimelineDrag" @pointercancel="endTimelineDrag">
        <span v-for="tick in overviewTicks" :key="tick.time" class="overview-tick" :style="{ left: `${tick.time / totalS * 100}%` }">{{ tick.label }}</span>
        <span v-for="event in events ?? []" :key="`event-${event.id}`" class="event-marker-line" :style="{ left: `${event.time_s / totalS * 100}%` }" :title="`${event.label} · ${event.time_s.toFixed(3)} s`"></span>
        <span class="timeline-playhead" :style="playbackHeadStyle" aria-hidden="true"></span>
        <div class="timeline-window" :style="timelineWindowStyle" @pointerdown.prevent="beginTimelineWindowDrag"><span class="timeline-grip" aria-hidden="true"></span></div>
        <output v-if="timelineTargetStart !== null" class="timeline-tooltip" :style="timelineTooltipStyle">目标 {{ formatAxisTime(timelineTargetStart) }}–{{ formatAxisTime(Math.min(totalS, timelineTargetStart + viewDuration)) }} · 指针 {{ formatAxisTime(timelineHoverS ?? timelineTargetStart) }}</output>
      </div>
    </template>
    <EventMarkerControls :markers="events ?? []" :current-time-s="positionS ?? viewStart" :total-duration-s="totalDurationS" @create="createEvent" @jump="emit('jumpEvent', $event)" @remove="emit('removeEvent', $event)" />
  </section>
</template>
