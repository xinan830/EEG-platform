<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { listDefinitions, listDefinitionVersions } from '../api/algorithmDefinitions'
import { listOfficialAlgorithms, type OfficialAlgorithmCatalogItem } from '../api/officialAlgorithms'
import { createDefinitionMetricRun, getRun, type AnalysisRunResponse } from '../api/runs'
import type { AlgorithmDefinition, AlgorithmDefinitionVersion } from '../types/algorithmDefinition'
import type { Recording } from '../types/recording'
import type { DefinitionMetricResult } from './DefinitionMetricResultCard.vue'
import type { DynamicMetric } from './DefinitionMetricTrendChart.vue'
import { DYNAMIC_WINDOW_OPTIONS, MIN_DYNAMIC_METRIC_WINDOW_S, appendOrRetainDynamicMetric, dynamicMetricBootstrapRange, dynamicMetricCatchupRange, playbackMetricWindow, type DynamicWindowS } from '../utils/dynamicMetricPlayback'
import '../styles/algorithmDisplayWorkspace.css'

type Range = { start: number; end: number }
export type WorkspaceMetricRun = { status: string; result: DefinitionMetricResult | DynamicMetric | null; error?: string; run?: AnalysisRunResponse; definitionName?: string }
type DynamicSession = { enabled: boolean; channel: string; definitions: Array<{ id: string; label: string; unit: string }>; windowS: DynamicWindowS; displayRangeS: number }
const props = defineProps<{
  recording: Recording; activeRange?: Range | null; rangeStart: number; rangeEnd: number; channels: string[]; playbackPositionS?: number; playing?: boolean; dynamicActive?: boolean; playbackEpoch?: number; definitionsEpoch?: number
}>()
const emit = defineEmits<{ close: []; results: [runs: Record<string, WorkspaceMetricRun>]; dynamicSession: [session: DynamicSession] }>()
const definitions = ref<AlgorithmDefinition[]>([])
const officialAlgorithms = ref<OfficialAlgorithmCatalogItem[]>([])
const officialCatalogError = ref('')
const versions = ref<Record<string, AlgorithmDefinitionVersion>>({})
const selectedIds = ref<string[]>([])
const channel = ref(props.channels[0] ?? props.recording.channels[0] ?? '')
const mode = ref<'static' | 'dynamic'>('static')
const dynamicWindowS = ref<DynamicWindowS>(10)
const dynamicResultDisplayRangeS = ref(30)
const DYNAMIC_RESULT_DISPLAY_RANGE_OPTIONS = [10, 20, 30, 60] as const
const running = ref(false)
const loading = ref(false)
const message = ref('')
const runs = ref<Record<string, WorkspaceMetricRun>>({})
const lastDynamicRefreshS = ref<number | null>(null)
const staticStartS = ref(props.rangeStart)
const staticEndS = ref(props.rangeEnd)
const range = computed(() => props.activeRange ?? { start: props.rangeStart, end: props.rangeEnd })
const staticRangeDuration = computed(() => staticEndS.value - staticStartS.value)
const canRun = computed(() => selectedIds.value.length > 0 && Boolean(channel.value) && !loading.value && !running.value && (mode.value === 'dynamic' || staticRangeDuration.value >= 4))
const userDefinitions = computed(() => definitions.value.filter((item) => item.owner !== 'platform-official'))
function resultFrom(run: AnalysisRunResponse): DefinitionMetricResult | DynamicMetric | null {
  const metric = run.result_summary?.metric
  return metric && typeof metric === 'object' ? metric as DefinitionMetricResult | DynamicMetric : null
}
async function load() {
  loading.value = true
  try {
    const catalog = await listDefinitions()
    definitions.value = catalog
    const availableIds = new Set(catalog.filter((item) => item.owner !== 'platform-official').map((item) => item.definition_id))
    const removedIds = selectedIds.value.filter((id) => !availableIds.has(id))
    if (removedIds.length) {
      selectedIds.value = selectedIds.value.filter((id) => availableIds.has(id))
      const nextRuns = { ...runs.value }
      removedIds.forEach((id) => { delete nextRuns[id] })
      runs.value = nextRuns
      if (props.dynamicActive) emit('dynamicSession', dynamicSession(false))
      emit('results', { ...runs.value })
    }
    versions.value = {}
    await Promise.all(userDefinitions.value.map(async (item) => {
      const items = await listDefinitionVersions(item.definition_id)
      if (items[0]) versions.value[item.definition_id] = items[0]
    }))
  } catch (cause) { message.value = cause instanceof Error ? cause.message : '无法读取我的算法库' }
  finally { loading.value = false }
  await loadOfficialCatalog()
}
async function loadOfficialCatalog() {
  officialCatalogError.value = ''
  try { officialAlgorithms.value = await listOfficialAlgorithms() }
  catch { officialAlgorithms.value = []; officialCatalogError.value = '官方算法目录暂不可读取；我的算法不受影响。' }
}
function isDynamic(result: DefinitionMetricResult | DynamicMetric | null): result is DynamicMetric {
  return Boolean(result && Array.isArray((result as DynamicMetric).series))
}
function outputUnit(id: string): string {
  const version = versions.value[id]
  const outputId = version?.graph?.outputs?.[0]
  const metadata = outputId ? version?.outputs[outputId] : null
  return metadata && typeof metadata === 'object' && !Array.isArray(metadata) && typeof (metadata as Record<string, unknown>).unit === 'string'
    ? String((metadata as Record<string, unknown>).unit)
    : '未知单位'
}
function dynamicSession(enabled: boolean): DynamicSession {
  return {
    enabled,
    channel: channel.value,
    definitions: selectedIds.value.map((id) => ({ id, label: title(id), unit: outputUnit(id) })),
    windowS: dynamicWindowS.value,
    displayRangeS: dynamicResultDisplayRangeS.value,
  }
}
async function poll(runId: string, definitionId: string, append = false) {
  for (let attempt = 0; attempt < 120; attempt += 1) {
    const next = await getRun(runId)
    const result = resultFrom(next)
    const previous = runs.value[definitionId]?.result ?? null
    runs.value[definitionId] = {
      status: next.status,
      result: append && isDynamic(previous)
        ? appendOrRetainDynamicMetric(previous, isDynamic(result) ? result : null)
        : result,
      error: next.error?.message,
      run: next,
      definitionName: runs.value[definitionId]?.definitionName ?? title(definitionId),
    }
    if (['completed', 'gate_failed', 'failed', 'cancelled'].includes(next.status)) return
    await new Promise<void>((resolve) => window.setTimeout(resolve, 250))
  }
}
async function runSelected(startS: number, endS: number, dynamic: boolean, append = false) {
  if (!canRun.value) return
  running.value = true; message.value = ''
  if (!append) runs.value = {}
  try {
    await Promise.all(selectedIds.value.map(async (definitionId) => {
      const version = versions.value[definitionId]
      if (!version) { runs.value[definitionId] = { status: 'failed', result: null, error: '没有可运行的算法版本', definitionName: title(definitionId) }; return }
      try {
        const created = await createDefinitionMetricRun({ recordingId: props.recording.id, definitionId, definitionVersion: version.semver, channel: channel.value, startS, endS, mode: dynamic ? 'dynamic' : 'static', dynamicWindowS: dynamic ? dynamicWindowS.value : undefined })
        runs.value[definitionId] = { status: created.status, result: append ? (runs.value[definitionId]?.result ?? null) : resultFrom(created), run: created, definitionName: title(definitionId) }
        await poll(created.run_id, definitionId, append)
      } catch (cause) { runs.value[definitionId] = { status: 'failed', result: null, error: cause instanceof Error ? cause.message : '提交失败', definitionName: title(definitionId) } }
    }))
  } finally { running.value = false; emit('results', { ...runs.value }) }
}
async function runStatic() {
  emit('dynamicSession', dynamicSession(false))
  if (staticRangeDuration.value < 4) { message.value = '静态分析区间至少需要 4 秒。'; return }
  await runSelected(staticStartS.value, staticEndS.value, false)
}
async function startDynamic(closeAfterStart: boolean) {
  if (running.value) return
  const position = props.playbackPositionS
  const second = position === undefined ? null : Math.floor(position)
  lastDynamicRefreshS.value = second !== null && second >= MIN_DYNAMIC_METRIC_WINDOW_S ? second : null
  message.value = `已启用播放同步分析：${MIN_DYNAMIC_METRIC_WINDOW_S}–${dynamicWindowS.value - 1} 秒显示预热值；从 ${dynamicWindowS.value} 秒起，每整秒计算最近 ${dynamicWindowS.value} 秒的正式分析范围。`
  emit('dynamicSession', dynamicSession(true))
  const bootstrap = position === undefined ? null : dynamicMetricBootstrapRange(second ?? position, dynamicWindowS.value)
  if (bootstrap) {
    await runSelected(bootstrap.startS, bootstrap.endS, true)
    const latestPosition = props.playbackPositionS
    if (latestPosition !== undefined && Math.floor(latestPosition) > (second ?? -1)) await refreshDynamic(latestPosition)
  }
  if (closeAfterStart) emit('close')
}
async function enableDynamic() {
  await startDynamic(true)
}
async function refreshDynamic(position: number) {
  const second = Math.floor(position)
  const previousSecond = lastDynamicRefreshS.value
  if (previousSecond === second || running.value) return
  const window = previousSecond === null
    ? playbackMetricWindow(second, dynamicWindowS.value)
    : dynamicMetricCatchupRange(previousSecond, second, dynamicWindowS.value)
  if (!window) return
  lastDynamicRefreshS.value = second
  await runSelected(window.startS, window.endS, true, true)
  const latestPosition = props.playbackPositionS
  if (props.playing && props.dynamicActive && latestPosition !== undefined && Math.floor(latestPosition) > second) {
    await refreshDynamic(latestPosition)
  }
}
watch(() => [props.playing, props.playbackPositionS] as const, ([playing, position]) => {
  if (!playing || !props.dynamicActive || position === undefined || selectedIds.value.length === 0) return
  const second = Math.floor(position)
  void refreshDynamic(second)
})
watch(mode, (nextMode) => { if (nextMode !== 'dynamic') emit('dynamicSession', dynamicSession(false)) })
watch(dynamicWindowS, (nextWindowS, previousWindowS) => {
  if (nextWindowS !== previousWindowS && mode.value === 'dynamic' && props.dynamicActive) void startDynamic(false)
})
watch(dynamicResultDisplayRangeS, () => {
  if (mode.value === 'dynamic' && props.dynamicActive) emit('dynamicSession', dynamicSession(true))
})
watch(() => props.dynamicActive, (active) => { if (!active) lastDynamicRefreshS.value = null })
watch(() => props.playbackEpoch, () => {
  runs.value = {}
  lastDynamicRefreshS.value = null
  emit('results', {})
})
watch(() => props.definitionsEpoch, (next, previous) => {
  if (next !== previous) void load()
})
function useCurrentRange() { staticStartS.value = range.value.start; staticEndS.value = range.value.end }
function title(id: string) { return definitions.value.find((item) => item.definition_id === id)?.name ?? id }
function officialAvailability(item: OfficialAlgorithmCatalogItem) {
  return item.availability === 'shadow_validation' ? '工程验证中，暂不可运行' : '当前不可运行'
}
onMounted(load)
</script>
<template>
  <div class="algorithm-display-layer" @click.self="emit('close')">
    <section class="algorithm-display-workspace" aria-label="波形与算法">
      <header class="dialog-titlebar"><span class="app-glyph">◈</span><strong>波形与算法</strong><span class="definition-range">分析范围：{{ range.start.toFixed(3) }}–{{ range.end.toFixed(3) }} s</span><button class="dialog-close" title="关闭" aria-label="关闭" @click="emit('close')">×</button></header>
      <div class="algorithm-display-controls">
        <label>通道<select v-model="channel"><option v-for="item in props.channels.length ? props.channels : props.recording.channels" :key="item" :value="item">{{ item }}</option></select></label>
        <span class="algorithm-display-label">分析模式</span><div class="algorithm-display-segment"><button :class="{ active: mode === 'static' }" @click="mode = 'static'">静态分析</button><button :class="{ active: mode === 'dynamic' }" @click="mode = 'dynamic'">动态分析</button></div>
        <template v-if="mode === 'static'"><label>开始 <input v-model.number="staticStartS" type="number" min="0" step="0.001" /> s</label><label>结束 <input v-model.number="staticEndS" type="number" min="0" step="0.001" /> s</label><button type="button" @click="useCurrentRange">使用当前分析区间</button><button class="primary-action" :disabled="!canRun" @click="runStatic">{{ running ? '计算中…' : '计算此区间' }}</button></template>
        <template v-else><label>分析范围<select v-model.number="dynamicWindowS"><option v-for="windowS in DYNAMIC_WINDOW_OPTIONS" :key="windowS" :value="windowS">最近 {{ windowS }} s</option></select></label><label>结果展示范围<select v-model.number="dynamicResultDisplayRangeS"><option v-for="seconds in DYNAMIC_RESULT_DISPLAY_RANGE_OPTIONS" :key="seconds" :value="seconds">最近 {{ seconds }} s</option></select></label><span class="algorithm-display-range">每 1 s 更新</span><button class="primary-action" :disabled="!canRun" @click="enableDynamic">{{ props.dynamicActive ? '同步已启用' : '启用播放同步' }}</button></template>
      </div>
      <div class="algorithm-display-layout">
        <aside class="algorithm-display-sidebar">
          <h3>选择算法</h3>
          <p class="algorithm-display-help">可运行的我的算法可叠加到当前波形；官方算法会在完成执行器验证后开放运行。</p>
          <section v-if="officialAlgorithms.length" class="algorithm-definition-group" aria-label="官方内置算法">
            <h4>官方内置算法</h4>
            <label v-for="item in officialAlgorithms" :key="item.algorithm_id" :data-testid="`official-algorithm-${item.definition_id}`" class="algorithm-checkbox algorithm-checkbox-disabled">
              <input type="checkbox" disabled />
              <span>{{ item.display_name_zh }}（{{ item.abbreviation }}）</span><small>{{ item.purpose_zh }} · {{ officialAvailability(item) }}</small>
            </label>
          </section>
          <p v-else-if="officialCatalogError" class="definition-muted">{{ officialCatalogError }}</p>
          <section class="algorithm-definition-group" aria-label="我的算法">
            <h4>我的算法</h4>
            <label v-for="item in userDefinitions" :key="item.definition_id" class="algorithm-checkbox"><input v-model="selectedIds" type="checkbox" :value="item.definition_id" /> <span>{{ item.name }}</span></label>
            <p v-if="!loading && !userDefinitions.length" class="definition-muted">尚无我的算法</p>
          </section>
          <p v-if="mode === 'static' && staticRangeDuration < 4" class="algorithm-display-warning">静态分析范围至少需要 4 秒。</p><p v-else-if="mode === 'dynamic'" class="algorithm-display-warning">分析范围决定后端每次使用的 EEG：先在 {{ MIN_DYNAMIC_METRIC_WINDOW_S }} s 起显示预热值，达到 {{ dynamicWindowS }} s 后使用固定最近 {{ dynamicWindowS }} s。结果展示范围只改变趋势图横轴，不改变计算。</p>
        </aside>
        <main class="algorithm-display-main"><p class="algorithm-display-empty">勾选算法并运行后，结果会显示在主页面波形下方。</p></main>
      </div>
      <p v-if="message" class="definition-error">{{ message }}</p>
    </section>
  </div>
</template>
