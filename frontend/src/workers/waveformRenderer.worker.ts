import { isRenderableWaveformValue, mapWaveformValueToY } from '../utils/waveformGeometry'

type Series = ArrayLike<number>
type StaticFrame = { elapsed: Series; channels: Record<string, Series> }

const COLORS = ['#ef5350', '#5b9bd5', '#62bd69', '#ad65c7', '#ff943f']
const PLOT_LEFT_PX = 74
const PLOT_RIGHT_PX = 12
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
let viewStart = 0
let viewDuration = 10
let sensitivityUvPerMm = 7
let isStream = false
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
  else if (message.type === 'viewport') updateViewport(message.startS as number, message.durationS as number, message.sensitivityUvPerMm as number)
}

function initializeCanvas(nextCanvas: OffscreenCanvas) {
  canvas = nextCanvas
  context = canvas.getContext('2d')
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
  viewStart = message.startS
  viewDuration = message.windowSeconds
  sweepPointer = 0
  isStream = true
  const sampleCount = Math.max(1, Math.round(sfreq * message.windowSeconds))
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
  let chunkSample = 0
  const sampleCount = chunk.length / names.length
  while (chunkSample < sampleCount) {
    const startPointer = sweepPointer
    const writable = Math.min(sampleCount - chunkSample, elapsed.length - sweepPointer)
    writeChunk(chunk, chunkSample, writable)
    sweepPointer += writable
    chunkSample += writable
    clearSweepGap()
    if (isWholeSweepView()) drawSweepSegment(startPointer, sweepPointer)
    else draw()
    if (sweepPointer === elapsed.length) resetSweep()
  }
  self.postMessage({ type: 'progress', elapsedS, sweepStartS })
}

function updateViewport(startS: number, durationS: number, nextSensitivityUvPerMm: number) {
  viewDuration = Math.max(0.1, durationS)
  sensitivityUvPerMm = nextSensitivityUvPerMm
  if (isStream && elapsed.length) {
    const streamEnd = sweepStartS + elapsed.length / sfreq
    viewStart = Math.max(sweepStartS, Math.min(streamEnd - viewDuration, startS))
  } else {
    viewStart = startS
  }
  draw()
}

function updateElapsed() {
  for (let index = 0; index < elapsed.length; index += 1) elapsed[index] = sweepStartS + index / sfreq
}

function clearSweepGap() {
  const gap = Math.max(1, Math.floor(sfreq * 0.2))
  for (let offset = 0; offset < gap; offset += 1) {
    const index = (sweepPointer + offset) % elapsed.length
    for (const channel of values) channel[index] = Number.NaN
  }
}

/** 默认扫屏只清除游标后的空白带，再补画本批采样，避免整屏 21 通道重复重绘。 */
function writeChunk(chunk: Float32Array, chunkStart: number, count: number) {
  for (let sample = 0; sample < count; sample += 1) {
    const source = (chunkStart + sample) * names.length
    for (let channel = 0; channel < names.length; channel += 1) values[channel][sweepPointer + sample] = chunk[source + channel]
  }
}

function isWholeSweepView() {
  return Math.abs(viewStart - sweepStartS) < 1 / sfreq && Math.abs(viewDuration - elapsed.length / sfreq) < 1 / sfreq
}

function drawSweepSegment(start: number, end: number) {
  if (!context || end <= start) return
  const startedAt = performance.now()
  const plotWidth = Math.max(1, width - PLOT_LEFT_PX - PLOT_RIGHT_PX)
  const laneHeight = height / (names.length + 1)
  clearSweepColumns(end, plotWidth, laneHeight)
  let points = 0
  names.forEach((name, channelIndex) => {
    context!.strokeStyle = COLORS[channelIndex % COLORS.length]
    context!.beginPath()
    points += drawSweepRaw(values[channelIndex], Math.max(0, start - 1), end, channelIndex, laneHeight, plotWidth)
    context!.stroke()
  })
  recordDraw(points, startedAt)
}

function clearSweepColumns(pointer: number, plotWidth: number, laneHeight: number) {
  if (!context) return
  const gapSamples = Math.max(1, Math.floor(sfreq * 0.2))
  const start = pointer % elapsed.length
  const end = start + gapSamples
  const clearRange = (from: number, to: number) => {
    const x = PLOT_LEFT_PX + from / elapsed.length * plotWidth
    const range = Math.max(1, (to - from) / elapsed.length * plotWidth + 1)
    context!.clearRect(x, 0, range, height)
    context!.strokeStyle = '#e6e6e6'
    for (let index = 0; index < names.length; index += 1) {
      const centerY = laneHeight * (index + 1)
      context!.beginPath()
      context!.moveTo(x, centerY)
      context!.lineTo(x + range, centerY)
      context!.stroke()
    }
  }
  if (end <= elapsed.length) clearRange(start, end)
  else { clearRange(start, elapsed.length); clearRange(0, end - elapsed.length) }
}

function drawSweepRaw(series: Float64Array, start: number, end: number, channelIndex: number, laneHeight: number, plotWidth: number) {
  let started = false
  let points = 0
  const centerY = laneHeight * (channelIndex + 1)
  for (let index = start; index < end; index += 1) {
    if (!isRenderableWaveformValue(series[index])) { started = false; continue }
    const x = PLOT_LEFT_PX + index / elapsed.length * plotWidth
    const y = mapWaveformValueToY(series[index], centerY, laneHeight, sensitivityUvPerMm)
    if (started) context!.lineTo(x, y); else { context!.moveTo(x, y); started = true }
    points += 1
  }
  return points
}

function resetSweep() {
  sweepPointer = 0
  sweepStartS += elapsed.length / sfreq
  updateElapsed()
  viewStart = sweepStartS
  draw()
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
  if (!context || !names.length) return
  const startedAt = performance.now()
  context.setTransform(dpr, 0, 0, dpr, 0, 0)
  context.clearRect(0, 0, width, height)
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
