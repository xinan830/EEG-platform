<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import type { AnalysisRunResponse } from '../api/runs'

type UnknownRecord = Record<string, unknown>
type InputValue = { feature?: string; value?: number | null; unit?: string; channel?: string }
type Evidence = {
  sfreq_hz?: number; analysis_reference?: unknown; algorithm_version?: string
  filter_contract?: UnknownRecord; welch_contract?: UnknownRecord; frequencies_hz?: number[]
  psd_uV2_per_hz?: number[]; band_power?: Record<string, number>; relative_band_power?: Record<string, number>; quality?: UnknownRecord
}
type DynamicPoint = { time_s: number; window_start_s: number; window_end_s: number; value: number | null; quality?: UnknownRecord; inputs?: Record<string, InputValue>; spectral_evidence?: Evidence }

const props = defineProps<{ run: AnalysisRunResponse; definitionName: string }>()
const emit = defineEmits<{ close: [] }>()
const selectedEndS = ref<number | null>(null)
const showRawPsd = ref(false)
const featureLabels: Record<string, string> = {
  delta_power: 'Delta 功率', theta_power: 'Theta 功率', alpha_power: 'Alpha 功率', beta_power: 'Beta 功率',
  delta_rbp: 'Delta 相对功率', theta_rbp: 'Theta 相对功率', alpha_rbp: 'Alpha 相对功率', beta_rbp: 'Beta 相对功率',
}
function asRecord(value: unknown): UnknownRecord { return value && typeof value === 'object' && !Array.isArray(value) ? value as UnknownRecord : {} }
const metric = computed(() => asRecord(props.run.result_summary?.metric))
const points = computed<DynamicPoint[]>(() => Array.isArray(metric.value.series) ? metric.value.series as DynamicPoint[] : [])
watch(points, (items) => { if (items.length && !items.some((item) => item.window_end_s === selectedEndS.value)) selectedEndS.value = items.at(-1)?.window_end_s ?? null }, { immediate: true })
const point = computed(() => points.value.find((item) => item.window_end_s === selectedEndS.value) ?? points.value.at(-1) ?? null)
const isDynamic = computed(() => points.value.length > 0)
const actualRange = computed(() => {
  if (point.value) return { start_s: point.value.window_start_s, end_s: point.value.window_end_s }
  const range = asRecord(metric.value.actual_range)
  return { start_s: Number(range.start_s ?? props.run.actual_range?.start_s ?? 0), end_s: Number(range.end_s ?? props.run.actual_range?.end_s ?? 0) }
})
const evidence = computed<Evidence>(() => (point.value?.spectral_evidence ?? metric.value.spectral_evidence ?? {}) as Evidence)
const inputs = computed(() => Object.entries((point.value?.inputs ?? metric.value.inputs ?? {}) as Record<string, InputValue>))
const quality = computed(() => point.value?.quality ?? evidence.value.quality ?? metric.value.source_quality ?? {})
const output = computed(() => asRecord(metric.value.output))
const outputValue = computed(() => point.value?.value ?? output.value.value ?? null)
const rawRows = computed(() => (evidence.value.frequencies_hz ?? []).map((frequency, index) => ({ frequency, psd: evidence.value.psd_uV2_per_hz?.[index] ?? null })))
function number(value: unknown, digits = 4): string { return typeof value === 'number' && Number.isFinite(value) ? value.toFixed(digits) : '—' }
function ratio(value: unknown): string { return typeof value === 'number' && Number.isFinite(value) ? `${(value * 100).toFixed(2)}%` : '—' }
function inputLabel(input: InputValue): string { return featureLabels[input.feature ?? ''] ?? input.feature ?? '输入' }
function welchText(): string {
  const contract = evidence.value.welch_contract ?? {}
  const seconds = Number(contract.welch_segment_s ?? 4)
  const overlap = Number(contract.welch_segment_overlap ?? 0.5)
  const overlapPercent = overlap <= 1 ? overlap * 100 : overlap
  const window = String(contract.welch_window ?? 'hann')
  return `${seconds} s ${window.toLowerCase() === 'hann' ? 'Hann' : window}，${overlapPercent}% overlap（${(seconds * overlapPercent / 100).toFixed(0)} s 步进）`
}
</script>

<template>
  <div class="modal-layer algorithm-metric-debug-layer" @click.self="emit('close')">
    <section class="algorithm-metric-debug-dialog" role="dialog" aria-modal="true" aria-label="算法调试台">
      <header class="dialog-titlebar"><strong>算法调试台 · {{ definitionName }}</strong><button type="button" class="dialog-close" aria-label="关闭算法调试台" @click="emit('close')">×</button></header>
      <main class="algorithm-metric-debug-body">
        <p class="algorithm-debug-note">只读展示后端 Run 已保存的结果与证据；前端不重新计算 EEG、PSD、频段功率或公式。</p>
        <label v-if="isDynamic" class="algorithm-debug-window">动态窗口结束时间<select v-model.number="selectedEndS"><option v-for="item in points" :key="item.window_end_s" :value="item.window_end_s">{{ item.window_end_s.toFixed(3) }} s（{{ item.window_start_s.toFixed(3) }}–{{ item.window_end_s.toFixed(3)}} s）</option></select></label>
        <section class="algorithm-debug-section"><h3>本次窗口</h3><dl><dt>模式</dt><dd>{{ isDynamic ? '动态算法' : '静态算法' }}</dd><dt>实际分析区间</dt><dd>{{ actualRange.start_s.toFixed(3) }}–{{ actualRange.end_s.toFixed(3) }} s</dd><dt>通道</dt><dd>{{ metric.channel ?? '' }}</dd><dt v-if="isDynamic">刷新步长</dt><dd v-if="isDynamic">{{ asRecord(metric.dynamic_contract).step_s ?? 1 }} s</dd></dl></section>
        <section class="algorithm-debug-section"><h3>运行与频谱契约</h3><dl><dt>算法版本</dt><dd>{{ run.definition_version ?? '—' }}</dd><dt>频谱算法</dt><dd>{{ evidence.algorithm_version ?? run.scientific_version ?? '—' }}</dd><dt>配置指纹</dt><dd><code>{{ run.config_sha256 ?? '—' }}</code></dd><dt>参考方式</dt><dd>{{ evidence.analysis_reference ?? run.reference?.mode ?? '—' }}</dd><dt>采样率</dt><dd>{{ evidence.sfreq_hz ?? '—' }} Hz</dd><dt>Welch</dt><dd>{{ welchText() }}</dd></dl></section>
        <section class="algorithm-debug-section"><h3>频段积分 · {{ metric.channel ?? '' }} 计算明细</h3><p>频段功率、相对功率和算法输入均为后端保存值。</p><div class="algorithm-debug-bands"><div v-for="band in ['delta', 'theta', 'alpha', 'beta']" :key="band"><strong>{{ band[0].toUpperCase() + band.slice(1) }}</strong><span>P = {{ number(evidence.band_power?.[band]) }} µV²</span><span>RBP = {{ ratio(evidence.relative_band_power?.[band]) }}</span></div></div></section>
        <section class="algorithm-debug-section"><h3>算法输入与输出</h3><div class="algorithm-debug-inputs"><div v-for="([key, input]) in inputs" :key="key"><strong>{{ inputLabel(input) }}</strong><span>{{ number(input.value) }} {{ input.unit }}</span><small>{{ input.channel }}</small></div></div><p class="algorithm-debug-output">{{ output.label ?? definitionName }} = <strong>{{ number(outputValue) }}</strong> {{ output.unit ?? '—' }}</p></section>
        <section class="algorithm-debug-section"><h3>质量门</h3><p>状态：{{ asRecord(quality).status ?? 'clean' }} · {{ asRecord(quality).clean_segments ?? evidence.quality?.clean_segments ?? '—' }}/{{ asRecord(quality).total_segments ?? evidence.quality?.total_segments ?? '—' }} clean</p></section>
        <section class="algorithm-debug-section"><button type="button" @click="showRawPsd = !showRawPsd">{{ showRawPsd ? '收起' : '展开' }}原始 PSD 点（{{ rawRows.length }} 个频率点）</button><div v-if="showRawPsd" class="algorithm-debug-psd"><div v-for="row in rawRows" :key="row.frequency"><span>{{ row.frequency.toFixed(2) }} Hz</span><span>{{ number(row.psd, 8) }} µV²/Hz</span></div></div></section>
      </main>
      <footer class="channel-dialog-footer"><button type="button" @click="emit('close')">关闭</button></footer>
    </section>
  </div>
</template>
