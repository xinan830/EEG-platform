<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { createAlgorithmRun, getRun, type AnalysisRunResponse } from '../api/runs'
import type { OfficialAlgorithmCatalogItem } from '../api/officialAlgorithms'
import type { AlgorithmDefinition, AlgorithmDefinitionVersion } from '../types/algorithmDefinition'
import type { AlgorithmCatalogContext } from '../composables/useAlgorithmCatalog'
import type { AlgorithmParameter } from '../api/algorithms'
import type { Recording } from '../types/recording'
import type { DefinitionMetricResult } from './DefinitionMetricResultCard.vue'
import type { DynamicMetric } from './DefinitionMetricTrendChart.vue'
import AlgorithmConfigCard from './AlgorithmConfigCard.vue'
import { DYNAMIC_WINDOW_OPTIONS, MIN_DYNAMIC_METRIC_WINDOW_S, appendOrRetainDynamicMetric, dynamicMetricBootstrapRange, dynamicMetricCatchupRange, playbackMetricWindow, type DynamicWindowS } from '../utils/dynamicMetricPlayback'
import '../styles/algorithmDisplayWorkspace.css'

type Range = { start: number; end: number }
type Settings = { channel: string; mode: 'static' | 'dynamic'; startS: number; endS: number; windowS: DynamicWindowS; displayRangeS: number }
type DynamicDefinition = { id: string; label: string; unit: string; channel: string; windowS: DynamicWindowS; displayRangeS: number }
export type WorkspaceMetricRun = { status: string; result: DefinitionMetricResult | DynamicMetric | null; error?: string; run?: AnalysisRunResponse; definitionName?: string }
type DynamicSession = { enabled: boolean; definitions: DynamicDefinition[] }
const props = defineProps<{ recording: Recording; activeRange?: Range | null; rangeStart: number; rangeEnd: number; channels: string[]; playbackPositionS?: number; playing?: boolean; dynamicActive?: boolean; playbackEpoch?: number; catalog: AlgorithmCatalogContext }>()
const emit = defineEmits<{ close: []; results: [runs: Record<string, WorkspaceMetricRun>]; dynamicSession: [session: DynamicSession] }>()
const definitions = computed<AlgorithmDefinition[]>(() => props.catalog.definitions.value)
const official = computed(() => props.catalog.officialAlgorithms.value)
const versions = computed<Record<string, AlgorithmDefinitionVersion>>(() => Object.fromEntries(Object.entries(props.catalog.versionsByDefinition.value).flatMap(([id, list]) => list[0] ? [[id, list[0]]] : [])))
const selectedUsers = ref<string[]>([]); const selectedOfficial = ref<string[]>([])
const settings = ref<Record<string, Settings>>({}); const refreshSeconds = ref<Record<string, number | null>>({}); const running = ref<string[]>([])
const loading = ref(false); const message = ref(''); const runs = ref<Record<string, WorkspaceMetricRun>>({})
const DISPLAY_WINDOWS = [10, 20, 30, 60] as const
const range = computed(() => props.activeRange ?? { start: props.rangeStart, end: props.rangeEnd })
const channels = computed(() => props.channels.length ? props.channels : props.recording.channels)
const keys = computed(() => [...selectedUsers.value, ...selectedOfficial.value.map((id) => `official:${id}`)])
const users = computed(() => definitions.value.filter((item) => item.owner === 'local-user'))
function defaults(): Settings { return { channel: channels.value[0] ?? '', mode: 'static', startS: range.value.start, endS: range.value.end, windowS: 10, displayRangeS: 30 } }
function config(key: string): Settings { if (!settings.value[key]) settings.value = { ...settings.value, [key]: defaults() }; return settings.value[key] }
function setConfig(key: string, patch: Partial<Settings>) { settings.value = { ...settings.value, [key]: { ...config(key), ...patch } } }
function isRunning(key: string) { return running.value.includes(key) }
function setRunning(key: string, value: boolean) { running.value = value ? [...new Set([...running.value, key])] : running.value.filter((item) => item !== key) }
function isDynamic(value: DefinitionMetricResult | DynamicMetric | null): value is DynamicMetric { return Boolean(value && Array.isArray((value as DynamicMetric).series)) }
function title(key: string) { return key.startsWith('official:') ? official.value.find((item) => item.algorithm_id === key.slice(9))?.display_name_zh ?? key : users.value.find((item) => item.definition_id === key)?.name ?? key }
function unit(key: string): string { if (key.startsWith('official:')) return official.value.find((item) => item.algorithm_id === key.slice(9))?.output_unit ?? '未知单位'; const version = versions.value[key]; const outputId = version?.graph?.outputs?.[0]; const value = outputId ? version?.outputs[outputId] : null; return value && typeof value === 'object' && !Array.isArray(value) && typeof (value as Record<string, unknown>).unit === 'string' ? String((value as Record<string, unknown>).unit) : '未知单位' }
function availability(item: OfficialAlgorithmCatalogItem) { return item.is_runnable ? '可运行' : item.availability === 'shadow_validation' ? '工程验证中，暂不可运行' : '当前不可运行' }
function labelsFor(key: string) {
  const source = key.startsWith('official:') ? 'official' : 'user'; const id = key.startsWith('official:') ? key.slice(9) : key
  const catalog = props.catalog as AlgorithmCatalogContext & { algorithms?: { value: Array<{ source: string; id: string; parameters: AlgorithmParameter[] | Record<string, unknown> }> } }
  const fields = catalog.algorithms?.value.find((item) => item.source === source && item.id === id)?.parameters
  const labels: Record<string, string> = Array.isArray(fields) ? Object.fromEntries(fields.map((item) => [item.key, item.label_zh])) : {}
  return { channel: labels.channel, startS: labels.start_s, endS: labels.end_s, windowS: labels.window_s }
}
function session(enabled: boolean): DynamicSession { return { enabled, definitions: keys.value.filter((key) => config(key).mode === 'dynamic').map((id) => ({ id, label: title(id), unit: unit(id), channel: config(id).channel, windowS: config(id).windowS, displayRangeS: config(id).displayRangeS })) } }
function resultOf(run: AnalysisRunResponse): DefinitionMetricResult | DynamicMetric | null { const metric = run.result_summary?.metric; return metric && typeof metric === 'object' ? metric as DefinitionMetricResult | DynamicMetric : null }
async function load() { loading.value = true; try { await props.catalog.refresh(); await Promise.all(users.value.map((item) => props.catalog.ensureVersions(item.definition_id))) } catch (error) { message.value = error instanceof Error ? error.message : '无法读取算法目录' } finally { loading.value = false } }
async function poll(runId: string, key: string, append: boolean) { for (let attempt = 0; attempt < 120; attempt += 1) { const next = await getRun(runId); const result = resultOf(next); const previous = runs.value[key]?.result ?? null; runs.value[key] = { status: next.status, result: append && isDynamic(previous) ? appendOrRetainDynamicMetric(previous, isDynamic(result) ? result : null) : result, error: next.error?.message, run: next, definitionName: title(key) }; if (['completed', 'gate_failed', 'failed', 'cancelled'].includes(next.status)) return; await new Promise<void>((resolve) => window.setTimeout(resolve, 250)) } }
async function submit(key: string, startS: number, endS: number, dynamic: boolean, append = false) {
  const value = config(key); if (!value.channel || isRunning(key)) return
  if (!dynamic && endS - startS < 4) { message.value = `${title(key)} 的静态分析区间至少需要 4 秒。`; return }
  setRunning(key, true); message.value = ''
  try {
    const officialId = key.startsWith('official:') ? key.slice(9) : null
    const request = officialId ? { source: 'official' as const, recordingId: props.recording.id, algorithmId: officialId, channel: value.channel, startS, endS, mode: dynamic ? 'dynamic' as const : 'static' as const, dynamicWindowS: dynamic ? value.windowS : undefined } : userRequest(key, value, startS, endS, dynamic)
    const created = await createAlgorithmRun(request)
    runs.value[key] = { status: created.status, result: append ? (runs.value[key]?.result ?? null) : resultOf(created), run: created, definitionName: title(key) }; await poll(created.run_id, key, append)
  } catch (error) { runs.value[key] = { status: 'failed', result: null, error: error instanceof Error ? error.message : '提交失败', definitionName: title(key) } }
  finally { setRunning(key, false); emit('results', { ...runs.value }) }
}
function userRequest(key: string, value: Settings, startS: number, endS: number, dynamic: boolean) { const version = versions.value[key]; if (!version) throw new Error('没有可运行的算法版本'); return { source: 'user' as const, recordingId: props.recording.id, definitionId: key, definitionVersion: version.semver, channel: value.channel, startS, endS, mode: dynamic ? 'dynamic' as const : 'static' as const, dynamicWindowS: dynamic ? value.windowS : undefined } }
async function runStatic(key: string) { const value = config(key); await submit(key, value.startS, value.endS, false) }
async function startDynamic() { const dynamicKeys = keys.value.filter((key) => config(key).mode === 'dynamic'); if (!dynamicKeys.length) { message.value = '请先将至少一个已勾选算法设为动态分析。'; return } if (props.playbackPositionS === undefined) { message.value = '等待波形播放位置后再启用动态分析。'; return }; const second = Math.floor(props.playbackPositionS); dynamicKeys.forEach((key) => { refreshSeconds.value = { ...refreshSeconds.value, [key]: second >= MIN_DYNAMIC_METRIC_WINDOW_S ? second : null } }); emit('dynamicSession', session(true)); await Promise.all(dynamicKeys.map(async (key) => { const window = dynamicMetricBootstrapRange(second, config(key).windowS); if (window) await submit(key, window.startS, window.endS, true) })) }
async function refreshDynamic(position: number) { const second = Math.floor(position); await Promise.all(keys.value.filter((key) => config(key).mode === 'dynamic').map(async (key) => { if (isRunning(key) || refreshSeconds.value[key] === second) return; const previous = refreshSeconds.value[key] ?? null; const window = previous === null ? playbackMetricWindow(second, config(key).windowS) : dynamicMetricCatchupRange(previous, second, config(key).windowS); if (!window) return; refreshSeconds.value = { ...refreshSeconds.value, [key]: second }; await submit(key, window.startS, window.endS, true, true) })) }
function currentRange(key: string) { setConfig(key, { startS: range.value.start, endS: range.value.end }) }
watch(() => [props.playing, props.playbackPositionS] as const, ([playing, position]) => { if (playing && props.dynamicActive && position !== undefined) void refreshDynamic(position) })
watch(() => props.playbackEpoch, () => { runs.value = {}; refreshSeconds.value = {}; emit('results', {}) })
watch(keys, (next) => { const allowed = new Set(next); const cleaned = Object.fromEntries(Object.entries(settings.value).filter(([key]) => allowed.has(key))); next.forEach((key) => { if (!cleaned[key]) cleaned[key] = defaults() }); settings.value = cleaned; if (props.dynamicActive && !next.some((key) => cleaned[key].mode === 'dynamic')) emit('dynamicSession', session(false)) })
watch(users, (next) => { const allowed = new Set(next.map((item) => item.definition_id)); selectedUsers.value = selectedUsers.value.filter((id) => allowed.has(id)); void Promise.all(next.map((item) => props.catalog.ensureVersions(item.definition_id))) }, { immediate: true })
onMounted(load)
</script>
<template>
  <div class="algorithm-display-layer" @click.self="emit('close')">
    <section class="algorithm-display-workspace" aria-label="波形与算法">
      <header class="dialog-titlebar"><span class="app-glyph">◈</span><strong>波形与算法</strong><span class="definition-range">默认范围：{{ range.start.toFixed(3) }}–{{ range.end.toFixed(3) }} s</span><button class="dialog-close" title="关闭" aria-label="关闭" @click="emit('close')">×</button></header>
      <div class="algorithm-display-controls"><span>每个算法独立设置通道、分析模式与时间参数。</span><button class="primary-action" :disabled="!keys.some((key) => config(key).mode === 'dynamic') || running.length > 0" @click="startDynamic">{{ props.dynamicActive ? '更新播放同步' : '启用播放同步' }}</button></div>
      <div class="algorithm-display-layout"><aside class="algorithm-display-sidebar">
        <h3>选择算法</h3><p class="algorithm-display-help">分析范围决定后端读取的真实 EEG；结果展示范围只影响趋势图横轴。</p>
        <section v-if="official.length" class="algorithm-definition-group" aria-label="官方内置算法"><h4>官方内置算法</h4>
          <template v-for="item in official" :key="item.algorithm_id"><label :data-testid="`official-algorithm-${item.definition_id}`" :class="['algorithm-checkbox', { 'algorithm-checkbox-disabled': !item.is_runnable }]"><input v-model="selectedOfficial" type="checkbox" :value="item.algorithm_id" :disabled="!item.is_runnable" /><span>{{ item.display_name_zh }}（{{ item.abbreviation }}）</span><small>{{ item.purpose_zh }} · {{ availability(item) }}</small></label><AlgorithmConfigCard v-if="selectedOfficial.includes(item.algorithm_id)" :id="`official:${item.algorithm_id}`" :settings="config(`official:${item.algorithm_id}`)" :labels="labelsFor(`official:${item.algorithm_id}`)" :channels="channels" :running="isRunning(`official:${item.algorithm_id}`)" @update="setConfig(`official:${item.algorithm_id}`, $event)" @current-range="currentRange(`official:${item.algorithm_id}`)" @run-static="runStatic(`official:${item.algorithm_id}`)" /></template>
        </section><p v-else-if="props.catalog.officialError.value" class="definition-muted">{{ props.catalog.officialError.value }}</p>
        <section class="algorithm-definition-group" aria-label="我的算法"><h4>我的算法</h4>
          <template v-for="item in users" :key="item.definition_id"><label class="algorithm-checkbox"><input v-model="selectedUsers" type="checkbox" :value="item.definition_id" /><span>{{ item.name }}</span></label><AlgorithmConfigCard v-if="selectedUsers.includes(item.definition_id)" :id="item.definition_id" :settings="config(item.definition_id)" :labels="labelsFor(item.definition_id)" :channels="channels" :running="isRunning(item.definition_id)" @update="setConfig(item.definition_id, $event)" @current-range="currentRange(item.definition_id)" @run-static="runStatic(item.definition_id)" /></template><p v-if="!loading && !users.length" class="definition-muted">尚无我的算法</p>
        </section>
      </aside></div><p v-if="message" class="definition-error">{{ message }}</p>
    </section>
  </div>
</template>
