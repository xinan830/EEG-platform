<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { listDefinitions, listDefinitionVersions } from '../api/algorithmDefinitions'
import { createDefinitionMetricRun, getRun, type AnalysisRunResponse } from '../api/runs'
import type { AlgorithmDefinition, AlgorithmDefinitionVersion } from '../types/algorithmDefinition'
import type { Recording } from '../types/recording'
import type { DefinitionMetricResult } from './DefinitionMetricResultCard.vue'
import type { DynamicMetric } from './DefinitionMetricTrendChart.vue'
import '../styles/algorithmDisplayWorkspace.css'

type Range = { start: number; end: number }
export type WorkspaceMetricRun = { status: string; result: DefinitionMetricResult | DynamicMetric | null; error?: string }
const props = defineProps<{
  recording: Recording; activeRange?: Range | null; rangeStart: number; rangeEnd: number; channels: string[]; playbackPositionS?: number; playing?: boolean
}>()
const emit = defineEmits<{ close: []; results: [runs: Record<string, WorkspaceMetricRun>] }>()
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
const range = computed(() => props.activeRange ?? { start: props.rangeStart, end: props.rangeEnd })
const rangeDuration = computed(() => range.value.end - range.value.start)
const canRun = computed(() => selectedIds.value.length > 0 && Boolean(channel.value) && rangeDuration.value >= (mode.value === 'dynamic' ? 10 : 4) && !running.value)
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
async function poll(runId: string, definitionId: string) {
  for (let attempt = 0; attempt < 120; attempt += 1) {
    const next = await getRun(runId)
    runs.value[definitionId] = { status: next.status, result: resultFrom(next), error: next.error?.message }
    if (['completed', 'gate_failed', 'failed', 'cancelled'].includes(next.status)) return
    await new Promise<void>((resolve) => window.setTimeout(resolve, 250))
  }
}
async function runSelected(overrideEnd?: number) {
  if (!canRun.value) return
  running.value = true; message.value = ''; runs.value = {}
  try {
    await Promise.all(selectedIds.value.map(async (definitionId) => {
      const version = versions.value[definitionId]
      if (!version) { runs.value[definitionId] = { status: 'failed', result: null, error: '没有可运行的算法版本' }; return }
      try {
        const endS = overrideEnd ?? range.value.end
        const startS = overrideEnd === undefined ? range.value.start : Math.max(0, range.value.start)
        if (endS - startS < (mode.value === 'dynamic' ? 10 : 4)) return
        const created = await createDefinitionMetricRun({ recordingId: props.recording.id, definitionId, definitionVersion: version.semver, channel: channel.value, startS, endS, mode: mode.value })
        runs.value[definitionId] = { status: created.status, result: resultFrom(created) }
        await poll(created.run_id, definitionId)
      } catch (cause) { runs.value[definitionId] = { status: 'failed', result: null, error: cause instanceof Error ? cause.message : '提交失败' } }
    }))
  } finally { running.value = false; emit('results', { ...runs.value }) }
}
watch(() => [props.playing, props.playbackPositionS, mode.value] as const, ([playing, position, currentMode]) => {
  if (!playing || currentMode !== 'dynamic' || position === undefined || running.value || selectedIds.value.length === 0) return
  const second = Math.floor(position)
  if (lastDynamicRefreshS.value === second) return
  const start = range.value.start
  if (position - start < 10) return
  lastDynamicRefreshS.value = second
  void runSelected(position)
})
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
        <span class="algorithm-display-range">分析区间：{{ range.start.toFixed(3) }}–{{ range.end.toFixed(3) }} s · {{ mode === 'dynamic' ? '最近 10 s / 每 1 s' : '当前区间一次计算' }}</span>
        <button class="primary-action" :disabled="!canRun" @click="() => runSelected()">{{ running ? '计算中…' : '运行已选算法' }}</button>
      </div>
      <div class="algorithm-display-layout">
        <aside class="algorithm-display-sidebar"><h3>选择算法</h3><p class="algorithm-display-help">勾选要叠加到当前波形的用户算法。</p><label v-for="item in userDefinitions" :key="item.definition_id" class="algorithm-checkbox"><input v-model="selectedIds" type="checkbox" :value="item.definition_id" /> <span>{{ item.name }}</span></label><p v-if="!loading && !userDefinitions.length" class="definition-muted">尚无用户算法</p><p v-if="mode === 'dynamic' && rangeDuration < 10" class="algorithm-display-warning">动态分析至少需要 10 秒区间。</p><p v-else-if="mode === 'static' && rangeDuration < 4" class="algorithm-display-warning">静态分析至少需要 4 秒区间。</p></aside>
        <main class="algorithm-display-main"><p class="algorithm-display-empty">勾选算法并运行后，结果会显示在主页面波形下方。</p></main>
      </div>
      <p v-if="message" class="definition-error">{{ message }}</p>
    </section>
  </div>
</template>
