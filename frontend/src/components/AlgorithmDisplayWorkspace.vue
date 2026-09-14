<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { listDefinitions, listDefinitionVersions } from '../api/algorithmDefinitions'
import { createDefinitionMetricRun, getRun, type AnalysisRunResponse } from '../api/runs'
import type { AlgorithmDefinition, AlgorithmDefinitionVersion } from '../types/algorithmDefinition'
import type { Recording } from '../types/recording'
import WaveformPanel from './WaveformPanel.vue'
import DefinitionMetricResultCard, { type DefinitionMetricResult } from './DefinitionMetricResultCard.vue'
import DefinitionMetricTrendChart, { type DynamicMetric } from './DefinitionMetricTrendChart.vue'
import '../styles/algorithmDisplayWorkspace.css'

type Waveform = { elapsed_s: ArrayLike<number>; channels: Record<string, ArrayLike<number>> }
type StreamInfo = { sfreq: number; channelNames: string[]; startS: number } | null
type Range = { start: number; end: number }
type WorkspaceRun = { status: string; result: DefinitionMetricResult | DynamicMetric | null; error?: string }
const props = defineProps<{
  recording: Recording; waveform: Waveform; stream: StreamInfo; positionS: number; playing: boolean; totalDurationS?: number; windowStartS: number; windowDurationS: number; sensitivityUvPerMm: number; activeRange?: Range | null; channels: string[]
}>()
const emit = defineEmits<{ close: []; windowRequested: [startS: number] }>()
const definitions = ref<AlgorithmDefinition[]>([])
const versions = ref<Record<string, AlgorithmDefinitionVersion>>({})
const selectedIds = ref<string[]>([])
const channel = ref(props.channels[0] ?? props.recording.channels[0] ?? '')
const mode = ref<'static' | 'dynamic'>('static')
const running = ref(false)
const loading = ref(false)
const message = ref('')
const runs = ref<Record<string, WorkspaceRun>>({})
const range = computed(() => props.activeRange ?? { start: props.windowStartS, end: Math.min(props.totalDurationS ?? props.windowStartS + props.windowDurationS, props.windowStartS + props.windowDurationS) })
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
async function runSelected() {
  if (!canRun.value) return
  running.value = true; message.value = ''; runs.value = {}
  try {
    await Promise.all(selectedIds.value.map(async (definitionId) => {
      const version = versions.value[definitionId]
      if (!version) { runs.value[definitionId] = { status: 'failed', result: null, error: '没有可运行的算法版本' }; return }
      try {
        const created = await createDefinitionMetricRun({ recordingId: props.recording.id, definitionId, definitionVersion: version.semver, channel: channel.value, startS: range.value.start, endS: range.value.end, mode: mode.value })
        runs.value[definitionId] = { status: created.status, result: resultFrom(created) }
        await poll(created.run_id, definitionId)
      } catch (cause) { runs.value[definitionId] = { status: 'failed', result: null, error: cause instanceof Error ? cause.message : '提交失败' } }
    }))
  } finally { running.value = false }
}
function isDynamic(result: DefinitionMetricResult | DynamicMetric | null): result is DynamicMetric { return Boolean(result && 'series' in result) }
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
        <button class="primary-action" :disabled="!canRun" @click="runSelected">{{ running ? '计算中…' : '运行已选算法' }}</button>
      </div>
      <div class="algorithm-display-layout">
        <aside class="algorithm-display-sidebar"><h3>选择算法</h3><p class="algorithm-display-help">勾选要叠加到当前波形的用户算法。</p><label v-for="item in userDefinitions" :key="item.definition_id" class="algorithm-checkbox"><input v-model="selectedIds" type="checkbox" :value="item.definition_id" /> <span>{{ item.name }}</span></label><p v-if="!loading && !userDefinitions.length" class="definition-muted">尚无用户算法</p><p v-if="mode === 'dynamic' && rangeDuration < 10" class="algorithm-display-warning">动态分析至少需要 10 秒区间。</p><p v-else-if="mode === 'static' && rangeDuration < 4" class="algorithm-display-warning">静态分析至少需要 4 秒区间。</p></aside>
        <main class="algorithm-display-main">
          <div class="algorithm-display-waveform"><WaveformPanel :waveform="props.waveform" :stream="props.stream" :position-s="props.positionS" :playing="props.playing" :total-duration-s="props.totalDurationS" :window-start-s="props.windowStartS" :window-duration-s="props.windowDurationS" :sensitivity-uv-per-mm="props.sensitivityUvPerMm" @window-requested="(start) => emit('windowRequested', start)" /></div>
          <section class="algorithm-display-results"><header><h3>算法结果</h3><span v-if="selectedIds.length">已选 {{ selectedIds.length }} 个</span></header><p v-if="!selectedIds.length" class="algorithm-display-empty">先勾选左侧算法，再运行分析。</p><article v-for="id in selectedIds" :key="id" class="algorithm-display-result"><h4>{{ title(id) }} <small v-if="runs[id]">· {{ runs[id].status }}</small></h4><p v-if="runs[id]?.error" class="algorithm-display-error">{{ runs[id]?.error }}</p><DefinitionMetricResultCard v-if="runs[id]?.result && !isDynamic(runs[id].result)" :result="runs[id].result" /><DefinitionMetricTrendChart v-if="runs[id]?.result && isDynamic(runs[id].result)" :result="runs[id].result" /><p v-if="runs[id]?.result && isDynamic(runs[id].result)" class="algorithm-display-meta">动态窗口：10 s · 步长：1 s · 横轴：窗口结束时间 (s) · 数值与质量来自后端</p></article></section>
        </main>
      </div>
      <p v-if="message" class="definition-error">{{ message }}</p>
    </section>
  </div>
</template>
