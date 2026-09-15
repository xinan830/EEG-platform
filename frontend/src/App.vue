<script setup lang="ts">
import { computed, onBeforeUnmount, ref, shallowRef } from 'vue'
import { getMontages, getWaveformWindow, type CustomMontageRow } from './api/recordings'
import { controlWaveformPlayback, createWaveformPlayback, waveformPlaybackSocketUrl } from './api/waveformPlayback'
import FileImport from './components/FileImport.vue'
import ChannelSelectionDialog from './components/ChannelSelectionDialog.vue'
import ChannelMapping from './components/ChannelMapping.vue'
import DisplaySettingsPanel from './components/DisplaySettingsPanel.vue'
import MontageSelector from './components/MontageSelector.vue'
import CustomMontageDialog from './components/CustomMontageDialog.vue'
import AlgorithmCheckDialog from './components/AlgorithmCheckDialog.vue'
import DebugConsole from './components/DebugConsole.vue'
import ViewerToolbar from './components/ViewerToolbar.vue'
import WaveformPanel from './components/WaveformPanel.vue'
import SpectrumPanel from './components/SpectrumPanel.vue'
import SpectrogramPanel from './components/SpectrogramPanel.vue'
import AlgorithmDefinitionWorkbench from './components/AlgorithmDefinitionWorkbench.vue'
import ResultsDrawer from './components/ResultsDrawer.vue'
import UserAlgorithmBuilder from './components/UserAlgorithmBuilder.vue'
import AlgorithmDisplayWorkspace from './components/AlgorithmDisplayWorkspace.vue'
import type { WorkspaceMetricRun } from './components/AlgorithmDisplayWorkspace.vue'
import DefinitionMetricResultCard, { type DefinitionMetricResult } from './components/DefinitionMetricResultCard.vue'
import DefinitionMetricTrendChart, { type DynamicMetric } from './components/DefinitionMetricTrendChart.vue'
import AlgorithmMetricDebugDialog from './components/AlgorithmMetricDebugDialog.vue'
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
import { useRecordingContext } from './composables/useRecordingContext'
import { useViewerContext } from './composables/useViewerContext'
import { useAnalysisTimeContext } from './composables/useAnalysisTimeContext'
import { useAlgorithmWorkspaceState } from './composables/useAlgorithmWorkspaceState'
import { useAlgorithmDebugWorkbench } from './composables/useAlgorithmDebugWorkbench'
import { useAlgorithmCatalog } from './composables/useAlgorithmCatalog'
import { playbackFilterPayload } from './utils/displayFilter'
import { pagedViewportStart } from './utils/waveformViewport'

type WaveformValues = ArrayLike<number>; type Waveform = { elapsed_s: WaveformValues; channels: Record<string, WaveformValues> }; type WaveformMessage = { type: string; sfreq?: number; ch_names?: string[]; duration_s?: number; start_s?: number; elapsed_s?: number; detail?: string }; type WaveformPanelHandle = { appendBinaryWaveform: (buffer: ArrayBuffer) => boolean }; type StreamInfo = { sfreq: number; channelNames: string[]; startS: number } | null

const recordingContext = useRecordingContext()
const recording = recordingContext.recording
const totalDurationS = recordingContext.totalDurationS
const sfreq = recordingContext.sfreq
const sourceChannelNames = recordingContext.sourceChannelNames
const displayChannelNames = recordingContext.displayChannelNames
const viewerContext = useViewerContext()
const playing = viewerContext.playing
const loading = viewerContext.loading
const playbackPositionS = viewerContext.playbackPositionS
const windowStartS = viewerContext.windowStartS
const montageId = viewerContext.montageId
const montageOptions = viewerContext.montageOptions
const averageExclude = viewerContext.averageExclude
const customMontage = viewerContext.customMontage
const analysisTimeContext = useAnalysisTimeContext()
const spectrumSelection = analysisTimeContext.spectrumSelection
const activeAnalysisRange = analysisTimeContext.activeAnalysisRange
const algorithmWorkspaceState = useAlgorithmWorkspaceState<WorkspaceMetricRun>()
const algorithmCatalog = useAlgorithmCatalog()
const algorithmDisplayResults = algorithmWorkspaceState.results
const dynamicAlgorithmSession = algorithmWorkspaceState.dynamicSession
const dynamicPlaybackEpoch = algorithmWorkspaceState.playbackEpoch
const algorithmDisplayResultItems = algorithmWorkspaceState.resultItems
const {
  isOpen: algorithmDebugOpen,
  activeRun: activeAlgorithmDebugRun,
  definitionName: algorithmDebugDefinitionName,
  open: openAlgorithmDebugWorkbench,
  close: closeAlgorithmDebugWorkbench,
} = useAlgorithmDebugWorkbench(algorithmDisplayResults)
// 波形采样本身由 TypedArray 缓冲拥有；浅响应式只通知画布数据帧已推进。
const waveform = shallowRef<Waveform>({ elapsed_s: [], channels: {} })
const showStartup = ref(true)
const error = ref('')
function setActiveAnalysisRange(start: number, end: number, source = 'custom') { analysisTimeContext.commitStaticRange(start, end, source) }
function selectSpectrumRange(start: number, end: number) { analysisTimeContext.selectWaveformRange(start, end) }
function goToWorkflowSection(sectionId: string) {
  document.getElementById(sectionId)?.scrollIntoView({ behavior: 'smooth', block: 'start' })
}
const customMontageOpen = ref(false)
const channelMappingOpen = ref(false)
const algorithmWorkbenchOpen = ref(false)
const resultsOpen = ref(false)
const userAlgorithmBuilderOpen = ref(false)
const algorithmDisplayOpen = ref(false)
function isDynamicMetric(value: WorkspaceMetricRun['result']): value is DynamicMetric { return Boolean(value && Array.isArray((value as DynamicMetric).series)) }
function updateDynamicAlgorithmSession(value: { enabled: boolean; channel: string; definitions: Array<{ id: string; label: string; unit: string }>; windowS: number; displayRangeS: number }) {
  algorithmWorkspaceState.updateDynamicSession(value)
}
function clearAlgorithmResults() { algorithmWorkspaceState.clear() }
function saveChannelMapping(value: Recording) {
  recording.value = value
  channelMappingOpen.value = false
}
function openAlgorithmDebug(item: WorkspaceMetricRun, definitionId: string) {
  if (item.run) openAlgorithmDebugWorkbench(definitionId)
}
const developerMode = ref(false)
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
  applyTimebase: applyTimebaseChange,
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
  playing.value = false
  if (sessionId) {
    try { await controlWaveformPlayback(sessionId, 'stop') } catch { /* 会话可能已结束。 */ }
  }
  closeSocket()
  sessionId = null
}

async function applyTimebaseChange() {
  if (!recording.value || sessionId) return
  const duration = displaySettings.value.timebaseSeconds
  const pageStart = Math.floor(playbackPositionS.value / duration) * duration
  await loadReviewWindow(pageStart)
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

function handleWorkerProgress(positionS: number, visibleWindowStartS: number) {
  playbackPositionS.value = positionS
  windowStartS.value = visibleWindowStartS
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
      playing.value = false
      await controlWaveformPlayback(sessionId, 'pause')
    } else {
      playing.value = true
      await controlWaveformPlayback(sessionId, 'resume')
    }
  } catch (cause) {
    playing.value = !playing.value
    error.value = cause instanceof Error ? cause.message : '无法更新播放状态'
  }
}

async function replay() {
  if (!recording.value) return
  algorithmWorkspaceState.resetForReplay()
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

async function moveScreen(direction: -1 | 1) {
  const duration = displaySettings.value.timebaseSeconds
  const target = pagedViewportStart(windowStartS.value, direction, duration, totalDurationS.value ?? duration)
  if (Math.abs(target - windowStartS.value) < 1e-9) return
  await seekWindow(target)
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
  viewerContext.reset()
  recordingContext.begin(value, chooseWaveformChannels(value.channels))
  analysisTimeContext.initializeForRecording(value.duration_s)
  algorithmWorkspaceState.clear()
  debug.reset(); sweepBuffer = null; streamInfo.value = null; customMontageOpen.value = false
  const importGeneration = fileGeneration
  try {
    const availableMontages = await getMontages(value.id)
    if (importGeneration !== fileGeneration) return
    montageOptions.value = availableMontages.montages
  } catch {
    if (importGeneration !== fileGeneration) return
    montageOptions.value = [{ id: 'original', label: '原始记录（不重参考）', available: true, channels: value.channels, missing: [] }]
  }
  waveform.value = { elapsed_s: [], channels: {} }
  showStartup.value = false
  // 导入后先选通道再进入阅图；确认或取消都会从文件 0 秒读取（见 useChannelSelection）。
  channelSelection.openInitialChannelDialog()
}

async function newSession() {
  fileGeneration += 1
  reviewRequestId += 1
  await stopPlayback()
  recordingContext.reset()
  viewerContext.reset()
  analysisTimeContext.reset()
  algorithmWorkspaceState.clear()
  debug.reset(); sweepBuffer = null; streamInfo.value = null; customMontageOpen.value = false
  waveform.value = { elapsed_s: [], channels: {} }; error.value = ''
  showStartup.value = true
}

onBeforeUnmount(() => {
  if (sessionId) void controlWaveformPlayback(sessionId, 'stop')
  closeSocket()
})
</script>

<template>
  <main class="desktop-app">
    <header class="app-header"><div class="app-brand"><span class="brand-mark">▣</span><span>脑电科研工作台</span><span class="header-file">{{ recording?.original_name ? `· ${recording.original_name}` : '' }}</span></div><button class="app-mode-toggle" :class="{ active: developerMode }" @click="developerMode = !developerMode">{{ developerMode ? '退出开发者模式' : '开发者模式' }}</button></header>
    <div class="app-body focused-body"><section class="main-column">
      <nav v-if="recording" class="workflow-nav" aria-label="科研工作流">
        <button type="button" @click="goToWorkflowSection('waveform-section')">1 波形查看</button><button type="button" @click="goToWorkflowSection('spectrum-section')">2 频谱分析</button><button type="button" @click="goToWorkflowSection('spectrogram-section')">3 时频分析</button><button type="button" @click="channelMappingOpen = true">通道映射</button><button type="button" @click="algorithmDisplayOpen = true">波形与算法</button><button type="button" @click="algorithmWorkbenchOpen = true">算法库</button><button type="button" @click="userAlgorithmBuilderOpen = true">创建算法</button><button type="button" @click="resultsOpen = true">结果</button>
      </nav>
      <ViewerToolbar :recording="Boolean(recording)" :loading="loading" :playing="playing" :position-s="playbackPositionS" :window-start-s="windowStartS" :screen-duration-s="displaySettings.timebaseSeconds" :total-duration-s="totalDurationS" :sfreq="sfreq" @open="newSession" @channels="channelSelection.openChannelDialog" @algorithms="algorithmWorkbenchOpen = true" @results="resultsOpen = true" @previous-screen="moveScreen(-1)" @toggle="togglePlayback" @next-screen="moveScreen(1)" @replay="replay" />
      <section v-if="recording" id="waveform-section" class="workflow-section waveform-workflow-section">
      <header class="workflow-section-heading"><div><p>数据与阅图</p><h1>波形查看</h1></div><span>通道、页宽和播放仅影响阅图</span></header>
      <DisplaySettingsPanel v-if="recording" :settings="displaySettings" :preset="displayPreset" :channel-names="sourceChannelNames" @change="handleDisplayChange" @reset="handleDisplayReset" @algorithm-check="algorithmCheck.show" />
      <div v-if="recording && montageOptions.length" class="montage-bar"><MontageSelector :model-value="montageId" :options="montageOptions" :channels="recording.channels" :excluded-channels="averageExclude" @change="changeMontage" @edit-custom="customMontageOpen = true" @update-excluded="changeAverageExclude" /><span class="montage-status">{{ montageOptions.find((item) => item.id === montageId)?.label }}{{ montageId === 'average' ? (averageExclude.length ? ` · 自定义排除 ${averageExclude.length} 个` : ' · AVG-All') : montageId === 'custom_bipolar' ? ` · ${customMontage.length} 条导联` : '' }}</span></div>
      <DebugConsole v-if="developerMode" :seconds="debugSeconds" :loading="debugLoading" :sample="debugSample" :render-stats="renderStats" :transport-stats="transportStats" @update-seconds="debugSeconds = $event" @inspect="inspectDebugSample" />
      <section v-if="recording && (algorithmDisplayResultItems.length || dynamicAlgorithmSession)" class="algorithm-results-on-main"><header><h2>{{ dynamicAlgorithmSession ? '播放同步算法趋势' : '当前波形算法结果' }}</h2><button type="button" @click="clearAlgorithmResults">{{ dynamicAlgorithmSession ? '停止并清除' : '清除结果' }}</button></header><article v-if="dynamicAlgorithmSession && !algorithmDisplayResultItems.length" v-for="definition in dynamicAlgorithmSession.definitions" :key="definition.id"><DefinitionMetricTrendChart :result="null" :pending="{ label: definition.label, unit: definition.unit, channel: dynamicAlgorithmSession.channel, windowS: dynamicAlgorithmSession.windowS }" :display-range-s="dynamicAlgorithmSession.displayRangeS" /></article><article v-for="item in algorithmDisplayResultItems" :key="item.id"><div class="algorithm-result-tools"><button v-if="item.run" type="button" @click="openAlgorithmDebug(item, item.id)">算法调试台</button></div><p v-if="item.error" class="algorithm-display-error">{{ item.error }}</p><DefinitionMetricResultCard v-if="item.result && !isDynamicMetric(item.result)" :result="item.result as DefinitionMetricResult" /><DefinitionMetricTrendChart v-if="item.result && isDynamicMetric(item.result)" :result="item.result" :display-range-s="dynamicAlgorithmSession?.displayRangeS" /></article></section>
      <WaveformPanel
        ref="waveformPanel"
        :waveform="waveform"
        :stream="streamInfo"
        :position-s="playbackPositionS"
        :playing="playing"
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
        @analysis-range-selected="selectSpectrumRange"
      />
      </section>
      <section v-if="recording" id="spectrum-section" class="workflow-section"><header class="workflow-section-heading"><div><p>稳定定量分析</p><h1>频谱分析</h1></div><span>选择通道和分析区间，查看 PSD 与频段功率</span></header><SpectrumPanel :recording-id="recording.id" :start-s="windowStartS" :position-s="playbackPositionS" :playing="playing" :total-duration-s="totalDurationS" :screen-duration-s="displaySettings.timebaseSeconds" :selected-range="spectrumSelection" :channels="sourceChannelNames" @active-range-change="setActiveAnalysisRange" /></section>
      <section v-if="recording" id="spectrogram-section" class="workflow-section"><header class="workflow-section-heading"><div><p>时间变化</p><h1>时频分析</h1></div><span>使用已提交的分析区间观察频段随时间的变化</span></header><SpectrogramPanel :recording-id="recording.id" :start-s="windowStartS" :duration-s="totalDurationS" :position-s="playbackPositionS" :playing="playing" :active-range="activeAnalysisRange" :channels="sourceChannelNames" /></section>
    </section></div>
    <div v-if="showStartup" class="modal-layer"><FileImport @imported="onImported" /></div>
    <div v-if="recording && isChannelDialogOpen" class="modal-layer channel-modal-layer" @click.self="channelSelection.closeChannelDialog">
      <ChannelSelectionDialog :channels="recording.channels" :selected-channels="sourceChannelNames" :on-cancel="channelSelection.closeChannelDialog" :on-confirm="channelSelection.applyChannels" />
    </div>
    <div v-if="recording && channelMappingOpen" class="modal-layer channel-mapping-layer" @click.self="channelMappingOpen = false">
      <ChannelMapping :recording="recording" @close="channelMappingOpen = false" @saved="saveChannelMapping" />
    </div>
    <div v-if="recording && customMontageOpen" class="modal-layer custom-montage-layer" @click.self="customMontageOpen = false">
      <CustomMontageDialog :channels="recording.channels" :rows="customMontage" @cancel="customMontageOpen = false" @apply="applyCustomMontage" />
    </div>
    <AlgorithmCheckDialog v-if="algorithmOpen" :loading="algorithmLoading" :seconds="algorithmSeconds" :result="algorithmResult" @close="algorithmCheck.close" @inspect="algorithmCheck.inspect" />
    <AlgorithmDefinitionWorkbench v-if="recording && algorithmWorkbenchOpen" :recording="recording" :start-s="activeAnalysisRange?.start ?? windowStartS" :end-s="activeAnalysisRange?.end ?? Math.min((totalDurationS ?? windowStartS + displaySettings.timebaseSeconds), windowStartS + displaySettings.timebaseSeconds)" :catalog="algorithmCatalog" @close="algorithmWorkbenchOpen = false" />
    <AlgorithmDisplayWorkspace v-show="recording && algorithmDisplayOpen" v-if="recording" :recording="recording" :range-start="activeAnalysisRange?.start ?? windowStartS" :range-end="activeAnalysisRange?.end ?? Math.min(totalDurationS ?? (windowStartS + displaySettings.timebaseSeconds), windowStartS + displaySettings.timebaseSeconds)" :active-range="activeAnalysisRange" :channels="sourceChannelNames" :playback-position-s="playbackPositionS" :playing="playing" :dynamic-active="Boolean(dynamicAlgorithmSession)" :playback-epoch="dynamicPlaybackEpoch" :catalog="algorithmCatalog" @close="algorithmDisplayOpen = false" @results="algorithmDisplayResults = $event" @dynamic-session="updateDynamicAlgorithmSession" />
    <AlgorithmMetricDebugDialog v-if="algorithmDebugOpen && activeAlgorithmDebugRun" :run="activeAlgorithmDebugRun" :definition-name="algorithmDebugDefinitionName" :playback-position-s="playbackPositionS" @close="closeAlgorithmDebugWorkbench" />
    <UserAlgorithmBuilder v-if="recording && userAlgorithmBuilderOpen" @close="userAlgorithmBuilderOpen = false" @saved="userAlgorithmBuilderOpen = false; void algorithmCatalog.refresh()" />
    <ResultsDrawer v-if="recording && resultsOpen" :recording-id="recording.id" :start-s="activeAnalysisRange?.start ?? windowStartS" :end-s="activeAnalysisRange?.end ?? Math.min(totalDurationS ?? windowStartS + displaySettings.timebaseSeconds, windowStartS + Math.max(4, displaySettings.timebaseSeconds))" :channels="sourceChannelNames" @close="resultsOpen = false" />
    <div v-if="error" class="error-toast">{{ error }}</div>
  </main>
</template>
