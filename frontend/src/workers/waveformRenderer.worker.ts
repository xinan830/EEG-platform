import { isRenderableWaveformValue, mapWaveformValueToY } from '../utils/waveformGeometry'
import { clampPageViewportStart, playbackPageStart } from '../utils/waveformViewport'

type Series = ArrayLike<number>
type StaticFrame = { elapsed: Series; channels: Record<string, Series> }

const COLORS = ['#ef5350', '#5b9bd5', '#62bd69', '#ad65c7', '#ff943f']
const PLOT_LEFT_PX = 74
const PLOT_RIGHT_PX = 12
const ERASE_GAP_SECONDS = 0.1
let canvas: OffscreenCanvas | null = null
let context: OffscreenCanvasRenderingContext2D | null = null
let width = 1
let height = 1
let dpr = 1
let names: string[] = []
let values: Float64Array[] = []
let elapsed: Float64Array = new Float64Array()
let sfreq = 1
let sweepStartS = 0
let sweepPointer = 0
let playbackPageStartS = 0
let pageDuration = 10
let viewStart = 0
let viewDuration = 10
let sensitivityUvPerMm = 7
let isStream = false
let latestElapsedS = 0
let lastProgressAt = 0
let statsStartedAt = performance.now()
let frames = 0
let accumulatedMs = 0
let slowFrames = 0

self.onmessage = (event: MessageEvent) => {
  const message = event.data as { type: string; [key: string]: unknown }
  if (message.type === 'canvas') initializeCanvas(message.canvas as OffscreenCanvas)
  else if (message.type === 'resize') resize(message.width as number, message.height as number, message.dpr as number)
  else if (message.type === 'stream') initializeStream(message as unknown as { sfreq: number; channelNames: string[]; startS: number; windowSeconds: number })
  else if (message.type === 'static') initializeStatic(message.frame as StaticFrame)
  else if (message.type === 'append') appendBinary(message.buffer as ArrayBuffer)
  else if (message.type === 'viewport') updateViewport(message.startS as number, message.durationS as number, message.pageDurationS as number, message.sensitivityUvPerMm as number)
}

function initializeCanvas(nextCanvas: OffscreenCanvas) {
  canvas = nextCanvas
  context = canvas.getContext('2d', { alpha: false, desynchronized: true })
}

function resize(nextWidth: number, nextHeight: number, nextDpr: number) {
  width = Math.max(1, nextWidth)
  height = Math.max(1, nextHeight)
  dpr = Math.max(1, nextDpr)
  if (canvas) {
    canvas.width = Math.round(width * dpr)
    canvas.height = Math.round(height * dpr)
  }
  draw()
}

function initializeStream(message: { sfreq: number; channelNames: string[]; startS: number; windowSeconds: number }) {
  sfreq = message.sfreq
  names = message.channelNames
  sweepStartS = message.startS
  pageDuration = message.windowSeconds
  playbackPageStartS = playbackPageStart(message.startS, pageDuration)
  viewStart = playbackPageStartS
  viewDuration = pageDuration
  sweepPointer = 0
  latestElapsedS = message.startS
  lastProgressAt = 0
  isStream = true
  // Screen duration is presentation state, not buffer policy. Sixty seconds
  // covers two maximum (30 s) screens while keeping memory bounded.
  const sampleCount = Math.max(1, Math.round(sfreq * 60))
  elapsed = new Float64Array(sampleCount)
  values = names.map(() => new Float64Array(sampleCount).fill(Number.NaN))
  updateElapsed()
  draw()
}

function initializeStatic(frame: StaticFrame) {
  names = Object.keys(frame.channels)
  elapsed = Float64Array.from(frame.elapsed)
  values = names.map((name) => Float64Array.from(frame.channels[name]))
  isStream = false
  if (elapsed.length) {
    viewStart = elapsed[0]
    viewDuration = Math.max(0.1, elapsed[elapsed.length - 1] - elapsed[0])
  }
  draw()
}

function appendBinary(buffer: ArrayBuffer) {
  if (!isStream || !names.length || buffer.byteLength < 8) return
  const elapsedS = new DataView(buffer).getFloat64(0, true)
  const chunk = new Float32Array(buffer, 8)
  if (chunk.length % names.length) return
  const packetStartS = elapsedS - chunk.length / names.length / sfreq
  let chunkSample = 0
  const sampleCount = chunk.length / names.length
  while (chunkSample < sampleCount) {
    const writable = Math.min(sampleCount - chunkSample, elapsed.length - sweepPointer)
    writeChunk(chunk, chunkSample, writable)
    sweepPointer += writable
    chunkSample += writable
    if (sweepPointer === elapsed.length) compactStreamBuffer()
  }
  latestElapsedS = elapsedS
  const nextPageStart = playbackPageStart(elapsedS, pageDuration)
  const pageChanged = Math.abs(nextPageStart - playbackPageStartS) >= 1 / sfreq
  if (pageChanged) {
    // Finish the old page before changing its time label. The next page keeps
    // these pixels only as a visual overwrite background.
    drawStreamRange(packetStartS, Math.min(elapsedS, nextPageStart))
    const pageOffset = clampPageViewportStart(viewStart, viewDuration, playbackPageStartS, pageDuration) - playbackPageStartS
    playbackPageStartS = nextPageStart
    viewStart = playbackPageStartS + pageOffset
    clearStreamGap(playbackPageStartS)
  } else {
    clearStreamGap(elapsedS)
    drawStreamRange(packetStartS, elapsedS)
  }
  const now = performance.now()
  if (pageChanged || now - lastProgressAt >= 100) {
    lastProgressAt = now
    self.postMessage({ type: 'progress', elapsedS, viewStartS: playbackPageStartS })
  }
}

function updateViewport(startS: number, durationS: number, nextPageDurationS: number, nextSensitivityUvPerMm: number) {
  const pageDurationChanged = Math.abs(pageDuration - nextPageDurationS) >= 1 / sfreq
  pageDuration = Math.max(0.1, nextPageDurationS)
  viewDuration = Math.max(0.1, Math.min(pageDuration, durationS))
  sensitivityUvPerMm = nextSensitivityUvPerMm
  if (isStream && elapsed.length) {
    if (pageDurationChanged) playbackPageStartS = playbackPageStart(latestElapsedS, pageDuration)
    viewStart = clampPageViewportStart(pageDurationChanged ? playbackPageStartS : startS, viewDuration, playbackPageStartS, pageDuration)
  } else {
    viewStart = startS
  }
  draw()
}

function updateElapsed() {
  for (let index = 0; index < elapsed.length; index += 1) elapsed[index] = sweepStartS + index / sfreq
}

/** Write only real samples received from the server; future slots stay NaN. */
function writeChunk(chunk: Float32Array, chunkStart: number, count: number) {
  for (let sample = 0; sample < count; sample += 1) {
    const source = (chunkStart + sample) * names.length
    for (let channel = 0; channel < names.length; channel += 1) values[channel][sweepPointer + sample] = chunk[source + channel]
  }
}

function compactStreamBuffer() {
  const keepSamples = Math.max(1, Math.floor(elapsed.length / 2))
  for (const series of values) {
    series.copyWithin(0, keepSamples)
    series.fill(Number.NaN, keepSamples)
  }
  sweepPointer = keepSamples
  sweepStartS += keepSamples / sfreq
  updateElapsed()
}

function clearStreamGap(frontierS: number) {
  if (!context || !names.length) return
  if (frontierS < viewStart - 1 / sfreq || frontierS > viewStart + viewDuration + 1 / sfreq) return
  const startRatio = Math.max(0, Math.min(1, (frontierS - viewStart) / viewDuration))
  const endRatio = Math.max(startRatio, Math.min(1, startRatio + ERASE_GAP_SECONDS / viewDuration))
  const plotWidth = Math.max(1, width - PLOT_LEFT_PX - PLOT_RIGHT_PX)
  const x = PLOT_LEFT_PX + startRatio * plotWidth
  const gapWidth = Math.max(1, (endRatio - startRatio) * plotWidth + 1)
  const laneHeight = height / (names.length + 1)
  context.setTransform(dpr, 0, 0, dpr, 0, 0)
  context.fillStyle = '#fff'
  context.fillRect(x, 0, gapWidth, height)
  context.strokeStyle = '#e6e6e6'
  for (let channelIndex = 0; channelIndex < names.length; channelIndex += 1) {
    const centerY = laneHeight * (channelIndex + 1)
    context.beginPath()
    context.moveTo(x, centerY)
    context.lineTo(x + gapWidth, centerY)
    context.stroke()
  }
}

function drawStreamRange(packetStartS: number, packetEndS: number) {
  if (!context || !names.length) return
  const visibleStart = Math.max(viewStart, packetStartS)
  const visibleEnd = Math.min(viewStart + viewDuration, packetEndS)
  if (visibleEnd <= visibleStart) return
  const startedAt = performance.now()
  const plotWidth = Math.max(1, width - PLOT_LEFT_PX - PLOT_RIGHT_PX)
  const laneHeight = height / (names.length + 1)
  const pageStartIndex = lowerBound(viewStart)
  const start = Math.max(pageStartIndex, lowerBound(visibleStart) - 1)
  const end = lowerBound(visibleEnd + Number.EPSILON)
  let points = 0
  names.forEach((name, channelIndex) => {
    context!.strokeStyle = COLORS[channelIndex % COLORS.length]
    context!.beginPath()
    points += drawRaw(values[channelIndex], start, end, laneHeight * (channelIndex + 1), laneHeight, plotWidth)
    context!.stroke()
  })
  recordDraw(points, startedAt)
}

function lowerBound(target: number) {
  let low = 0
  let high = elapsed.length
  while (low < high) {
    const middle = Math.floor((low + high) / 2)
    if (elapsed[middle] < target) low = middle + 1
    else high = middle
  }
  return low
}

function draw() {
  if (!context) return
  const startedAt = performance.now()
  context.setTransform(dpr, 0, 0, dpr, 0, 0)
  context.fillStyle = '#fff'
  context.fillRect(0, 0, width, height)
  if (!names.length) return
  const plotWidth = Math.max(1, width - PLOT_LEFT_PX - PLOT_RIGHT_PX)
  const laneHeight = height / (names.length + 1)
  const visibleEnd = viewStart + viewDuration
  const start = lowerBound(viewStart)
  const end = lowerBound(visibleEnd + Number.EPSILON)
  let points = 0
  names.forEach((name, channelIndex) => {
    const centerY = laneHeight * (channelIndex + 1)
    context!.strokeStyle = '#e6e6e6'
    context!.beginPath()
    context!.moveTo(PLOT_LEFT_PX, centerY)
    context!.lineTo(width - PLOT_RIGHT_PX, centerY)
    context!.stroke()
    context!.strokeStyle = COLORS[channelIndex % COLORS.length]
    context!.beginPath()
    points += drawTrace(values[channelIndex], start, end, centerY, laneHeight, plotWidth)
    context!.stroke()
  })
  recordDraw(points, startedAt)
}

function recordDraw(points: number, startedAt: number) {
  frames += 1
  const drawMs = performance.now() - startedAt
  accumulatedMs += drawMs
  if (drawMs > 16.7) slowFrames += 1
  const elapsedMs = performance.now() - statsStartedAt
  if (elapsedMs >= 1000) {
    self.postMessage({ type: 'stats', summary: `渲染 ${Math.round(frames * 1000 / elapsedMs)} FPS · ${(accumulatedMs / frames).toFixed(1)} ms/帧 · ${points.toLocaleString()} 点 · 慢帧 ${slowFrames}` })
    statsStartedAt = performance.now()
    frames = 0
    accumulatedMs = 0
    slowFrames = 0
  }
}

function drawTrace(series: Float64Array, start: number, end: number, centerY: number, laneHeight: number, plotWidth: number) {
  const samplesPerPixel = (end - start) / plotWidth
  if (samplesPerPixel <= 1.5) return drawRaw(series, start, end, centerY, laneHeight, plotWidth)
  let started = false
  let points = 0
  for (let column = 0; column < Math.ceil(plotWidth); column += 1) {
    const first = start + Math.floor(column * samplesPerPixel)
    const last = Math.min(end, start + Math.floor((column + 1) * samplesPerPixel))
    let min = Number.POSITIVE_INFINITY
    let max = Number.NEGATIVE_INFINITY
    let minIndex = -1
    let maxIndex = -1
    for (let index = first; index < last; index += 1) {
      if (!isRenderableWaveformValue(series[index])) continue
      const y = mapWaveformValueToY(series[index], centerY, laneHeight, sensitivityUvPerMm)
      if (y < min) { min = y; minIndex = index }
      if (y > max) { max = y; maxIndex = index }
    }
    if (!Number.isFinite(min)) { started = false; continue }
    const x = PLOT_LEFT_PX + (column + .5) / Math.ceil(plotWidth) * plotWidth
    const point = (y: number) => { if (started) context!.lineTo(x, y); else { context!.moveTo(x, y); started = true }; points += 1 }
    if (minIndex <= maxIndex) { point(min); if (minIndex !== maxIndex) point(max) }
    else { point(max); point(min) }
  }
  return points
}

function drawRaw(series: Float64Array, start: number, end: number, centerY: number, laneHeight: number, plotWidth: number) {
  let started = false
  let points = 0
  for (let index = start; index < end; index += 1) {
    if (!isRenderableWaveformValue(series[index])) { started = false; continue }
    const x = PLOT_LEFT_PX + (elapsed[index] - viewStart) / viewDuration * plotWidth
    const y = mapWaveformValueToY(series[index], centerY, laneHeight, sensitivityUvPerMm)
    if (started) context!.lineTo(x, y); else { context!.moveTo(x, y); started = true }
    points += 1
  }
  return points
}
