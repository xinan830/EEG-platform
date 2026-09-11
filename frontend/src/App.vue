<script setup lang="ts">
import { onBeforeUnmount, ref, shallowRef } from 'vue'
import { getWaveformWindow } from './api/recordings'
import { controlWaveformPlayback, createWaveformPlayback, waveformPlaybackSocketUrl } from './api/waveformPlayback'
import FileImport from './components/FileImport.vue'
import ChannelSelectionDialog from './components/ChannelSelectionDialog.vue'
import DisplaySettingsPanel from './components/DisplaySettingsPanel.vue'
import DebugConsole from './components/DebugConsole.vue'
import ViewerToolbar from './components/ViewerToolbar.vue'
import WaveformPanel from './components/WaveformPanel.vue'
import type { Recording } from './types/recording'
import { isValidDisplaySettings } from './utils/displaySettings'
import { WaveformSweepBuffer } from './utils/waveformSweepBuffer'
import { chooseWaveformChannels } from './utils/displayChannels'
import { useDebugSample } from './composables/useDebugSample'
import { useChannelSelection } from './composables/useChannelSelection'
import { decodeWaveformBinary } from './utils/waveformBinary'
import { useDisplayControls } from './composables/useDisplayControls'

type WaveformValues = ArrayLike<number>
type Waveform = { elapsed_s: WaveformValues; channels: Record<string, WaveformValues> }
type WaveformMessage = { type: string; sfreq?: number; ch_names?: string[]; duration_s?: number; start_s?: number; elapsed_s?: number; detail?: string }
type WaveformPanelHandle = { appendBinaryWaveform: (buffer: ArrayBuffer) => boolean }
type StreamInfo = { sfreq: number; channelNames: string[]; startS: number } | null

const recording = ref<Recording | null>(null)
// 波形采样本身由 TypedArray 缓冲拥有；浅响应式只通知画布数据帧已推进。
const waveform = shallowRef<Waveform>({ elapsed_s: [], channels: {} })
const showStartup = ref(true)
const error = ref('')
const playing = ref(false)
const loading = ref(false)
const playbackPositionS = ref(0)
const totalDurationS = ref<number | undefined>()
const sfreq = ref<number | undefined>()
const windowStartS = ref(0)
const displayChannelNames = ref<string[]>([])
// Worker 消息必须是可结构化克隆的普通对象，流元数据不能被 Vue 深度代理。
const streamInfo = shallowRef<StreamInfo>(null)
const waveformPanel = ref<WaveformPanelHandle | null>(null)
let sessionId: string | null = null
let socket: WebSocket | null = null
let sweepBuffer: WaveformSweepBuffer | null = null
let reviewRequestId = 0
let packetStatsStartedAt = performance.now()
let receivedPackets = 0
let accumulatedPacketProcessingMs = 0
const displayControls = useDisplayControls({
  hasRecording: () => Boolean(recording.value),
  hasActivePlayback: () => Boolean(sessionId),
  restartPlayback: restartPlaybackFromBeginning,
  reloadFromStart: () => loadReviewWindow(0),
  rebuildEmptySweep: () => rebuildSweepBuffer(),
  showError: (message) => { error.value = message },
})
const displaySettings = displayControls.settings
const displayPreset = displayControls.preset
const debug = useDebugSample(recording, totalDurationS, displaySettings, displayChannelNames, error)
const debugSeconds = debug.seconds
const debugLoading = debug.loading
const debugSample = debug.sample
const inspectDebugSample = debug.inspect
const renderStats = ref('')
const transportStats = ref('')
const channelSelection = useChannelSelection(displayChannelNames, async () => {
  await stopPlayback()
  debug.reset()
  await loadReviewWindow(0)
})
// 模板只自动解包顶层 ref；嵌套在普通对象里的 ref 需先别名到顶层才能用于 v-if。
const isChannelDialogOpen = channelSelection.isChannelDialogOpen

function rebuildSweepBuffer(startS = playbackPositionS.value) {
  if (!sfreq.value || !displayChannelNames.value.length) return
  sweepBuffer = new WaveformSweepBuffer(
    sfreq.value,
    displayChannelNames.value,
    displaySettings.value.timebaseSeconds,
    startS,
  )
  clearWaveform(startS)
}

function clearWaveform(startS = 0) {
  sweepBuffer?.reset(startS)
  waveform.value = sweepBuffer?.frame() ?? { elapsed_s: [], channels: {} }
  windowStartS.value = startS
  playbackPositionS.value = startS
}

function closeSocket() {
  if (socket) {
    socket.onopen = null
    socket.onmessage = null
    socket.onerror = null
    socket.close()
  }
  socket = null
}

async function stopPlayback() {
  if (sessionId) {
    try { await controlWaveformPlayback(sessionId, 'stop') } catch { /* 会话可能已结束。 */ }
  }
  closeSocket()
  sessionId = null
  playing.value = false
}

function handleWaveformMessage(message: WaveformMessage) {
  if (message.type === 'info' && message.sfreq && message.ch_names && message.duration_s !== undefined) {
    sfreq.value = message.sfreq
    totalDurationS.value = message.duration_s
    displayChannelNames.value = message.ch_names
    streamInfo.value = { sfreq: message.sfreq, channelNames: [...message.ch_names], startS: message.start_s ?? 0 }
    rebuildSweepBuffer(message.start_s ?? 0)
    return
  }
  if (message.type === 'reset') {
    if (streamInfo.value) streamInfo.value = { ...streamInfo.value, startS: message.start_s ?? 0 }
    clearWaveform(message.start_s ?? 0)
    return
  }
  if (message.type === 'completed') {
    playing.value = false
    sessionId = null
    closeSocket()
    return
  }
  if (message.type === 'error') {
    playing.value = false
    error.value = message.detail ?? '波形回放失败'
    sessionId = null
    closeSocket()
  }
}

function handleWaveformBinary(buffer: ArrayBuffer) {
  if (waveformPanel.value?.appendBinaryWaveform(buffer)) return
  if (!sweepBuffer || !displayChannelNames.value.length) return
  const processingStartedAt = performance.now()
  const chunk = decodeWaveformBinary(buffer, displayChannelNames.value.length)
  sweepBuffer.pushInterleaved(chunk.values)
  const frame = sweepBuffer.frame()
  waveform.value = frame
  windowStartS.value = frame.sweepStartS
  playbackPositionS.value = chunk.elapsedS
  receivedPackets += 1
  accumulatedPacketProcessingMs += performance.now() - processingStartedAt
  const elapsedMs = performance.now() - packetStatsStartedAt
  if (elapsedMs >= 1000) {
    transportStats.value = `数据 ${Math.round(receivedPackets * 1000 / elapsedMs)} 包/s · 解码入缓冲 ${(accumulatedPacketProcessingMs / receivedPackets).toFixed(2)} ms/包`
    packetStatsStartedAt = performance.now()
    receivedPackets = 0
    accumulatedPacketProcessingMs = 0
  }
}

function handleWorkerProgress(positionS: number, workerWindowStartS: number) {
  playbackPositionS.value = positionS
  windowStartS.value = workerWindowStartS
}

async function restartPlaybackFromBeginning() {
  if (!recording.value || !sessionId) return
  debug.reset()
  clearWaveform(0)
  playing.value = true
  try {
    await controlWaveformPlayback(sessionId, 'set_filters', {
      low_cut_hz: displaySettings.value.lowCutHz,
      high_cut_hz: displaySettings.value.highCutHz,
      notch_hz: displaySettings.value.notchHz,
      position_s: 0,
    })
  } catch (cause) {
    playing.value = false
    error.value = cause instanceof Error ? cause.message : '无法从头应用波形设置'
  }
}

async function loadReviewWindow(startS = 0) {
  const current = recording.value
  if (!current) return
  if (!isValidDisplaySettings(displaySettings.value)) {
    error.value = '低切必须小于高切'
    return
  }
  const requestId = ++reviewRequestId
  streamInfo.value = null
  debug.reset()
  loading.value = true
  error.value = ''
  try {
    const payload = await getWaveformWindow(current.id, {
      startS,
      windowS: displaySettings.value.timebaseSeconds,
      lowCutHz: displaySettings.value.lowCutHz,
      highCutHz: displaySettings.value.highCutHz,
      notchHz: displaySettings.value.notchHz,
      reference: displaySettings.value.reference,
      channels: displayChannelNames.value.length ? displayChannelNames.value : chooseWaveformChannels(current.channels),
    })
    if (requestId !== reviewRequestId) return
    sfreq.value = payload.sfreq
    totalDurationS.value = payload.duration_s
    displayChannelNames.value = Object.keys(payload.channels)
    waveform.value = { elapsed_s: payload.elapsed_s, channels: payload.channels }
    windowStartS.value = payload.window_start_s
    playbackPositionS.value = payload.window_start_s
  } catch (cause) {
    if (requestId === reviewRequestId) error.value = cause instanceof Error ? cause.message : '无法读取波形窗口'
  } finally {
    if (requestId === reviewRequestId) loading.value = false
  }
}

async function startPlayback(positionS = 0) {
  if (!recording.value || loading.value) return
  loading.value = true
  error.value = ''
  try {
    const created = await createWaveformPlayback(recording.value.id, displayChannelNames.value)
    sessionId = created.session_id
    socket = new WebSocket(waveformPlaybackSocketUrl(created.websocket_url))
    socket.binaryType = 'arraybuffer'
    socket.onopen = async () => {
      if (!sessionId) return
      // 会话创建时已固定通道，避免先渲染默认通道再切换的首帧竞态。
      await controlWaveformPlayback(sessionId, 'set_filters', {
        low_cut_hz: displaySettings.value.lowCutHz,
        high_cut_hz: displaySettings.value.highCutHz,
        notch_hz: displaySettings.value.notchHz,
        position_s: positionS,
      })
    }
    socket.onmessage = (event) => {
      if (event.data instanceof ArrayBuffer) {
        handleWaveformBinary(event.data)
        return
      }
      handleWaveformMessage(JSON.parse(event.data) as WaveformMessage)
    }
    socket.onerror = () => {
      playing.value = false
      error.value = '波形数据连接中断'
    }
  } catch (cause) {
    playing.value = false
    sessionId = null
    error.value = cause instanceof Error ? cause.message : '无法启动波形回放'
  } finally {
    loading.value = false
  }
}

async function togglePlayback() {
  if (!recording.value || loading.value) return
  if (!sessionId) {
    playing.value = true
    await startPlayback(playbackPositionS.value)
    return
  }
  try {
    if (playing.value) {
      await controlWaveformPlayback(sessionId, 'pause')
      playing.value = false
    } else {
      await controlWaveformPlayback(sessionId, 'resume')
      playing.value = true
    }
  } catch (cause) {
    error.value = cause instanceof Error ? cause.message : '无法更新播放状态'
  }
}

async function replay() {
  if (!recording.value) return
  if (!sessionId) {
    playing.value = true
    await startPlayback(0)
    return
  }
  clearWaveform(0)
  playing.value = true
  try {
    await controlWaveformPlayback(sessionId, 'restart')
  } catch (cause) {
    error.value = cause instanceof Error ? cause.message : '无法重播波形'
  }
}

async function seekWindow(startS: number) {
  if (!recording.value) return
  await stopPlayback()
  await loadReviewWindow(startS)
}

async function onImported(value: Recording) {
  await stopPlayback()
  recording.value = value
  debug.reset()
  sweepBuffer = null
  streamInfo.value = null
  displayChannelNames.value = chooseWaveformChannels(value.channels)
  waveform.value = { elapsed_s: [], channels: {} }
  totalDurationS.value = value.duration_s ?? undefined
  sfreq.value = value.sfreq ?? undefined
  playbackPositionS.value = 0
  windowStartS.value = 0
  showStartup.value = false
  // 导入后先选通道再进入阅图；确认或取消都会从文件 0 秒读取（见 useChannelSelection）。
  channelSelection.openInitialChannelDialog()
}

async function newSession() {
  await stopPlayback()
  recording.value = null
  debug.reset()
  sweepBuffer = null
  streamInfo.value = null
  displayChannelNames.value = []
  waveform.value = { elapsed_s: [], channels: {} }
  totalDurationS.value = undefined
  sfreq.value = undefined
  playbackPositionS.value = 0
  windowStartS.value = 0
  error.value = ''
  showStartup.value = true
}

onBeforeUnmount(() => {
  if (sessionId) void controlWaveformPlayback(sessionId, 'stop')
  closeSocket()
})
</script>

<template>
  <main class="desktop-app">
    <header class="app-header"><span class="brand-mark">▣</span><span>脑电文件波形查看器</span><span class="header-file">{{ recording?.original_name ? `- [${recording.original_name}]` : '' }}</span></header>
    <div class="app-body focused-body"><section class="main-column">
      <ViewerToolbar :recording="Boolean(recording)" :loading="loading" :playing="playing" :position-s="playbackPositionS" :total-duration-s="totalDurationS" :sfreq="sfreq" @open="newSession" @channels="channelSelection.openChannelDialog" @toggle="togglePlayback" @replay="replay" />
      <DisplaySettingsPanel v-if="recording" :settings="displaySettings" :preset="displayPreset" :channel-names="displayChannelNames" @change="displayControls.change" @reset="displayControls.restoreDefaults" />
      <DebugConsole v-if="recording" :seconds="debugSeconds" :loading="debugLoading" :sample="debugSample" :render-stats="renderStats" :transport-stats="transportStats" @update-seconds="debugSeconds = $event" @inspect="inspectDebugSample" />
      <WaveformPanel
        ref="waveformPanel"
        :waveform="waveform"
        :stream="streamInfo"
        :position-s="playbackPositionS"
        :total-duration-s="totalDurationS"
        :window-start-s="windowStartS"
        :window-duration-s="displaySettings.timebaseSeconds"
        :sensitivity-uv-per-mm="displaySettings.sensitivityUvPerMm"
        @window-requested="seekWindow"
        @render-stats="renderStats = $event"
        @progress="handleWorkerProgress"
      />
    </section></div>
    <div v-if="showStartup" class="modal-layer"><FileImport @imported="onImported" /></div>
    <div v-if="recording && isChannelDialogOpen" class="modal-layer channel-modal-layer" @click.self="channelSelection.closeChannelDialog">
      <ChannelSelectionDialog :channels="recording.channels" :selected-channels="displayChannelNames" :on-cancel="channelSelection.closeChannelDialog" :on-confirm="channelSelection.applyChannels" />
    </div>
    <div v-if="error" class="error-toast">{{ error }}</div>
  </main>
</template>
