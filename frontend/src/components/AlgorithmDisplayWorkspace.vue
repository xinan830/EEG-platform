<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { listDefinitions, listDefinitionVersions } from '../api/algorithmDefinitions'
import { createDefinitionMetricRun, getRun, type AnalysisRunResponse } from '../api/runs'
import type { AlgorithmDefinition, AlgorithmDefinitionVersion } from '../types/algorithmDefinition'
import type { Recording } from '../types/recording'
import type { DefinitionMetricResult } from './DefinitionMetricResultCard.vue'
import type { DynamicMetric } from './DefinitionMetricTrendChart.vue'
import { appendDynamicMetricPoint, dynamicMetricBootstrapRange, dynamicMetricCatchupRange, playbackMetricWindow } from '../utils/dynamicMetricPlayback'
import '../styles/algorithmDisplayWorkspace.css'

type Range = { start: number; end: number }
export type WorkspaceMetricRun = { status: string; result: DefinitionMetricResult | DynamicMetric | null; error?: string }
const props = defineProps<{
  recording: Recording; activeRange?: Range | null; rangeStart: number; rangeEnd: number; channels: string[]; playbackPositionS?: number; playing?: boolean; dynamicActive?: boolean
}>()
const emit = defineEmits<{ close: []; results: [runs: Record<string, WorkspaceMetricRun>]; dynamicSession: [session: { enabled: boolean; channel: string; definitionCount: number }] }>()
const definitions = ref<AlgorithmDefinition[]>([])
const versions = ref<Record<string, AlgorithmDefinitionVersion>>({})
const selectedIds = ref<string[]>([])
const channel = ref(props.channels[0] ?? props.recording.channels[0] ?? '')
const mode = ref<'static' | 'dynamic'>('static')
const running = ref(false)
const loading = ref(false)
const message = ref('')
const runs = ref<Record<string, WorkspaceMetricRun>>({})
const lastDynamicRefreshS = ref<number | null>(null)
const staticStartS = ref(props.rangeStart)
const staticEndS = ref(props.rangeEnd)
const range = computed(() => props.activeRange ?? { start: props.rangeStart, end: props.rangeEnd })
const staticRangeDuration = computed(() => staticEndS.value - staticStartS.value)
const canRun = computed(() => selectedIds.value.length > 0 && Boolean(channel.value) && !running.value && (mode.value === 'dynamic' || staticRangeDuration.value >= 4))
const userDefinitions = computed(() => definitions.value.filter((item) => item.owner !== 'platform-official'))
function resultFrom(run: AnalysisRunResponse): DefinitionMetricResult | DynamicMetric | null {
  const metric = run.result_summary?.metric
  return metric && typeof metric === 'object' ? metric as DefinitionMetricResult | DynamicMetric : null
}
async function load() {
  loading.value = true
  try {
    definitions.value = await listDefinitions()
    await Promise.all(userDefinitions.value.map(async (item) => {
      const items = await listDefinitionVersions(item.definition_id)
      if (items[0]) versions.value[item.definition_id] = items[0]
    }))
  } catch (cause) { message.value = cause instanceof Error ? cause.message : '无法读取算法库' }
  finally { loading.value = false }
}
function isDynamic(result: DefinitionMetricResult | DynamicMetric | null): result is DynamicMetric {
  return Boolean(result && Array.isArray((result as DynamicMetric).series))
}
async function poll(runId: string, definitionId: string, append = false) {
  for (let attempt = 0; attempt < 120; attempt += 1) {
    const next = await getRun(runId)
    const result = resultFrom(next)
    const previous = runs.value[definitionId]?.result ?? null
    runs.value[definitionId] = {
      status: next.status,
      result: append && isDynamic(previous) && isDynamic(result) ? appendDynamicMetricPoint(previous, result) : result,
      error: next.error?.message,
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
      if (!version) { runs.value[definitionId] = { status: 'failed', result: null, error: '没有可运行的算法版本' }; return }
      try {
        const created = await createDefinitionMetricRun({ recordingId: props.recording.id, definitionId, definitionVersion: version.semver, channel: channel.value, startS, endS, mode: dynamic ? 'dynamic' : 'static' })
        runs.value[definitionId] = { status: created.status, result: append ? (runs.value[definitionId]?.result ?? null) : resultFrom(created) }
        await poll(created.run_id, definitionId, append)
      } catch (cause) { runs.value[definitionId] = { status: 'failed', result: null, error: cause instanceof Error ? cause.message : '提交失败' } }
    }))
  } finally { running.value = false; emit('results', { ...runs.value }) }
}
async function runStatic() {
  emit('dynamicSession', { enabled: false, channel: channel.value, definitionCount: 0 })
  if (staticRangeDuration.value < 4) { message.value = '静态分析区间至少需要 4 秒。'; return }
  await runSelected(staticStartS.value, staticEndS.value, false)
}
async function enableDynamic() {
  const position = props.playbackPositionS
  const second = position === undefined ? null : Math.floor(position)
  lastDynamicRefreshS.value = second !== null && second >= 10 ? second : null
  message.value = '已启用播放同步分析：播放到 10 秒后，每整秒计算最近 10 秒。'
  emit('dynamicSession', { enabled: true, channel: channel.value, definitionCount: selectedIds.value.length })
  const bootstrap = position === undefined ? null : dynamicMetricBootstrapRange(second ?? position)
  if (bootstrap) {
    await runSelected(bootstrap.startS, bootstrap.endS, true)
    const latestPosition = props.playbackPositionS
    if (latestPosition !== undefined && Math.floor(latestPosition) > (second ?? -1)) await refreshDynamic(latestPosition)
  }
  emit('close')
}
async function refreshDynamic(position: number) {
  const second = Math.floor(position)
  const previousSecond = lastDynamicRefreshS.value
  if (previousSecond === second || running.value) return
  const window = previousSecond === null ? playbackMetricWindow(second) : dynamicMetricCatchupRange(previousSecond, second)
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
watch(mode, (nextMode) => { if (nextMode !== 'dynamic') emit('dynamicSession', { enabled: false, channel: channel.value, definitionCount: 0 }) })
watch(() => props.dynamicActive, (active) => { if (!active) lastDynamicRefreshS.value = null })
function useCurrentRange() { staticStartS.value = range.value.start; staticEndS.value = range.value.end }
function title(id: string) { return definitions.value.find((item) => item.definition_id === id)?.name ?? id }
onMounted(load)
</script>
<template>
  <div class="algorithm-display-layer" @click.self="emit('close')">
    <section class="algorithm-display-workspace" aria-label="波形与算法">
      <header class="dialog-titlebar"><span class="app-glyph">◈</span><strong>波形与算法</strong><span class="definition-range">{{ range.start.toFixed(3) }}–{{ range.end.toFixed(3) }} s</span><button class="dialog-close" title="关闭" aria-label="关闭" @click="emit('close')">×</button></header>
      <div class="algorithm-display-controls">
        <label>通道<select v-model="channel"><option v-for="item in props.channels.length ? props.channels : props.recording.channels" :key="item" :value="item">{{ item }}</option></select></label>
        <span class="algorithm-display-label">分析模式</span><div class="algorithm-display-segment"><button :class="{ active: mode === 'static' }" @click="mode = 'static'">静态分析</button><button :class="{ active: mode === 'dynamic' }" @click="mode = 'dynamic'">动态分析</button></div>
        <template v-if="mode === 'static'"><label>开始 <input v-model.number="staticStartS" type="number" min="0" step="0.001" /> s</label><label>结束 <input v-model.number="staticEndS" type="number" min="0" step="0.001" /> s</label><button type="button" @click="useCurrentRange">使用当前分析区间</button><button class="primary-action" :disabled="!canRun" @click="runStatic">{{ running ? '计算中…' : '计算此区间' }}</button></template>
        <template v-else><span class="algorithm-display-range">播放同步：最近 10 s · 每 1 s 更新</span><button class="primary-action" :disabled="!canRun" @click="enableDynamic">{{ props.dynamicActive ? '同步已启用' : '启用播放同步' }}</button></template>
      </div>
      <div class="algorithm-display-layout">
        <aside class="algorithm-display-sidebar"><h3>选择算法</h3><p class="algorithm-display-help">勾选要叠加到当前波形的用户算法。</p><label v-for="item in userDefinitions" :key="item.definition_id" class="algorithm-checkbox"><input v-model="selectedIds" type="checkbox" :value="item.definition_id" /> <span>{{ item.name }}</span></label><p v-if="!loading && !userDefinitions.length" class="definition-muted">尚无用户算法</p><p v-if="mode === 'static' && staticRangeDuration < 4" class="algorithm-display-warning">静态分析区间至少需要 4 秒。</p><p v-else-if="mode === 'dynamic'" class="algorithm-display-warning">动态模式启动时补算最近 30 秒的真实窗口；之后每秒追加一个真实结果点。</p></aside>
        <main class="algorithm-display-main"><p class="algorithm-display-empty">勾选算法并运行后，结果会显示在主页面波形下方。</p></main>
      </div>
      <p v-if="message" class="definition-error">{{ message }}</p>
    </section>
  </div>
</template>
