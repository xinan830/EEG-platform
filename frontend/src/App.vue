<script setup lang="ts">
import { onBeforeUnmount, ref, shallowRef } from 'vue'
import { getMontages, getWaveformWindow, type CustomMontageRow, type MontageOption } from './api/recordings'
import { controlWaveformPlayback, createWaveformPlayback, waveformPlaybackSocketUrl } from './api/waveformPlayback'
import FileImport from './components/FileImport.vue'
import ChannelSelectionDialog from './components/ChannelSelectionDialog.vue'
import DisplaySettingsPanel from './components/DisplaySettingsPanel.vue'
import MontageSelector from './components/MontageSelector.vue'
import CustomMontageDialog from './components/CustomMontageDialog.vue'
import AlgorithmCheckDialog from './components/AlgorithmCheckDialog.vue'
import DebugConsole from './components/DebugConsole.vue'
import ViewerToolbar from './components/ViewerToolbar.vue'
import WaveformPanel from './components/WaveformPanel.vue'
import SpectrumPanel from './components/SpectrumPanel.vue'
import type { Recording } from './types/recording'
import { isValidDisplaySettings } from './utils/displaySettings'
import { WaveformSweepBuffer } from './utils/waveformSweepBuffer'
import { chooseWaveformChannels } from './utils/displayChannels'
import { useDebugSample } from './composables/useDebugSample'
import { useChannelSelection } from './composables/useChannelSelection'
import { decodeWaveformBinary } from './utils/waveformBinary'
import { useDisplayControls } from './composables/useDisplayControls'
import { useAlgorithmCheck } from './composables/useAlgorithmCheck'
import { useEventMarkers } from './composables/useEventMarkers'
import { playbackFilterPayload } from './utils/displayFilter'

type WaveformValues = ArrayLike<number>; type Waveform = { elapsed_s: WaveformValues; channels: Record<string, WaveformValues> }; type WaveformMessage = { type: string; sfreq?: number; ch_names?: string[]; duration_s?: number; start_s?: number; elapsed_s?: number; detail?: string }; type WaveformPanelHandle = { appendBinaryWaveform: (buffer: ArrayBuffer) => boolean }; type StreamInfo = { sfreq: number; channelNames: string[]; startS: number } | null

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
const displayChannelNames = ref<string[]>([]); const sourceChannelNames = ref<string[]>([])
const montageId = ref('original'); const montageOptions = ref<MontageOption[]>([]); const averageExclude = ref<string[]>([])
const customMontage = ref<CustomMontageRow[]>([]); const customMontageOpen = ref(false)
// Worker 消息必须是可结构化克隆的普通对象，流元数据不能被 Vue 深度代理。
const streamInfo = shallowRef<StreamInfo>(null)
const waveformPanel = ref<WaveformPanelHandle | null>(null)
let sessionId: string | null = null
let socket: WebSocket | null = null
let sweepBuffer: WaveformSweepBuffer | null = null
let reviewRequestId = 0
let fileGeneration = 0
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
const debug = useDebugSample(recording, totalDurationS, displaySettings, sourceChannelNames, montageId, customMontage, error)
const algorithmCheck = useAlgorithmCheck(recording, displaySettings, sourceChannelNames, montageId, averageExclude, customMontage, error)
const algorithmOpen = algorithmCheck.open; const algorithmLoading = algorithmCheck.loading; const algorithmSeconds = algorithmCheck.seconds; const algorithmResult = algorithmCheck.result
const debugSeconds = debug.seconds
const debugLoading = debug.loading
const debugSample = debug.sample
const inspectDebugSample = debug.inspect
const renderStats = ref('')
const transportStats = ref('')
const eventMarkersState = useEventMarkers(recording, error)
const eventMarkers = eventMarkersState.markers
const channelSelection = useChannelSelection(sourceChannelNames, async () => {
  await stopPlayback()
  debug.reset()
  await loadReviewWindow(0)
})
// 模板只自动解包顶层 ref；嵌套在普通对象里的 ref 需先别名到顶层才能用于 v-if。
const isChannelDialogOpen = channelSelection.isChannelDialogOpen
async function changeAverageExclude(channels: string[]) { averageExclude.value = channels; await stopPlayback(); await loadReviewWindow(0) }
async function applyCustomMontage(rows: CustomMontageRow[]) {
  customMontage.value = rows; customMontageOpen.value = false; montageId.value = 'custom_bipolar'
  await stopPlayback(); await loadReviewWindow(0)
}
function handleDisplayChange(kind: 'timebase' | 'sensitivity' | 'filter' | 'baseline' | 'reference' | 'preset', value?: number | string | boolean | null) {
  if (kind === 'reference') {
    const reference = String(value ?? 'original'); montageId.value = reference === 'original' || reference === 'average' ? reference : `reference:${reference}`
    if (reference !== 'original' && reference !== 'average' && !montageOptions.value.some((item) => item.id === montageId.value)) montageOptions.value = [...montageOptions.value, { id: montageId.value, label: `${reference} 参考`, available: true, channels: [], missing: [] }]
  }
  displayControls.change(kind, value)
}
function handleDisplayReset() {
  montageId.value = 'original'
  displayControls.restoreDefaults()
}
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
    await controlWaveformPlayback(sessionId, 'set_filters', { ...playbackFilterPayload(displaySettings.value), position_s: 0 })
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
      baselineStabilization: displaySettings.value.baselineStabilization,
      reference: displaySettings.value.reference,
      montage: montageId.value,
      averageExclude: averageExclude.value,
      customMontage: customMontage.value,
      channels: sourceChannelNames.value.length ? sourceChannelNames.value : chooseWaveformChannels(current.channels),
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
    const created = await createWaveformPlayback(recording.value.id, sourceChannelNames.value, montageId.value, averageExclude.value, customMontage.value)
    sessionId = created.session_id
    socket = new WebSocket(waveformPlaybackSocketUrl(created.websocket_url))
    socket.binaryType = 'arraybuffer'
    socket.onopen = async () => {
      if (!sessionId) return
      // 会话创建时已固定通道，避免先渲染默认通道再切换的首帧竞态。
      await controlWaveformPlayback(sessionId, 'set_filters', { ...playbackFilterPayload(displaySettings.value), position_s: positionS })
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

async function changeMontage(value: string) {
  if (value === montageId.value) return
  montageId.value = value
  await stopPlayback()
  await loadReviewWindow(0)
}

async function onImported(value: Recording) {
  // 新文件开始后，旧文件的异步窗口/导联请求不得再写回界面。
  fileGeneration += 1
  reviewRequestId += 1
  await stopPlayback()
  recording.value = value; debug.reset(); sweepBuffer = null; streamInfo.value = null
  displayChannelNames.value = chooseWaveformChannels(value.channels); sourceChannelNames.value = [...displayChannelNames.value]; montageId.value = 'original'; averageExclude.value = []; customMontage.value = []; customMontageOpen.value = false
  const importGeneration = fileGeneration
  try {
    const availableMontages = await getMontages(value.id)
    if (importGeneration !== fileGeneration) return
    montageOptions.value = availableMontages.montages
  } catch {
    if (importGeneration !== fileGeneration) return
    montageOptions.value = [{ id: 'original', label: '原始记录（不重参考）', available: true, channels: value.channels, missing: [] }]
  }
  waveform.value = { elapsed_s: [], channels: {} }; totalDurationS.value = value.duration_s ?? undefined; sfreq.value = value.sfreq ?? undefined
  playbackPositionS.value = 0; windowStartS.value = 0
  showStartup.value = false
  // 导入后先选通道再进入阅图；确认或取消都会从文件 0 秒读取（见 useChannelSelection）。
  channelSelection.openInitialChannelDialog()
}

async function newSession() {
  fileGeneration += 1
  reviewRequestId += 1
  await stopPlayback()
  recording.value = null; debug.reset(); sweepBuffer = null; streamInfo.value = null
  displayChannelNames.value = []; sourceChannelNames.value = []; montageId.value = 'original'; montageOptions.value = []; averageExclude.value = []; customMontage.value = []; customMontageOpen.value = false
  waveform.value = { elapsed_s: [], channels: {} }; totalDurationS.value = undefined; sfreq.value = undefined
  playbackPositionS.value = 0; windowStartS.value = 0; error.value = ''; loading.value = false
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
      <DisplaySettingsPanel v-if="recording" :settings="displaySettings" :preset="displayPreset" :channel-names="sourceChannelNames" @change="handleDisplayChange" @reset="handleDisplayReset" @algorithm-check="algorithmCheck.show" />
      <div v-if="recording && montageOptions.length" class="montage-bar"><MontageSelector :model-value="montageId" :options="montageOptions" :channels="recording.channels" :excluded-channels="averageExclude" @change="changeMontage" @edit-custom="customMontageOpen = true" @update-excluded="changeAverageExclude" /><span class="montage-status">{{ montageOptions.find((item) => item.id === montageId)?.label }}{{ montageId === 'average' ? (averageExclude.length ? ` · 自定义排除 ${averageExclude.length} 个` : ' · AVG-All') : montageId === 'custom_bipolar' ? ` · ${customMontage.length} 条导联` : '' }}</span></div>
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
        :events="eventMarkers"
        @window-requested="seekWindow"
        @render-stats="renderStats = $event"
        @progress="handleWorkerProgress"
        @create-event="eventMarkersState.create"
        @jump-event="seekWindow"
        @remove-event="eventMarkersState.remove"
      />
      <SpectrumPanel v-if="recording" :recording-id="recording.id" :start-s="windowStartS" :channels="sourceChannelNames" />
    </section></div>
    <div v-if="showStartup" class="modal-layer"><FileImport @imported="onImported" /></div>
    <div v-if="recording && isChannelDialogOpen" class="modal-layer channel-modal-layer" @click.self="channelSelection.closeChannelDialog">
      <ChannelSelectionDialog :channels="recording.channels" :selected-channels="sourceChannelNames" :on-cancel="channelSelection.closeChannelDialog" :on-confirm="channelSelection.applyChannels" />
    </div>
    <div v-if="recording && customMontageOpen" class="modal-layer custom-montage-layer" @click.self="customMontageOpen = false">
      <CustomMontageDialog :channels="recording.channels" :rows="customMontage" @cancel="customMontageOpen = false" @apply="applyCustomMontage" />
    </div>
    <AlgorithmCheckDialog v-if="algorithmOpen" :loading="algorithmLoading" :seconds="algorithmSeconds" :result="algorithmResult" @close="algorithmCheck.close" @inspect="algorithmCheck.inspect" />
    <div v-if="error" class="error-toast">{{ error }}</div>
  </main>
</template>
