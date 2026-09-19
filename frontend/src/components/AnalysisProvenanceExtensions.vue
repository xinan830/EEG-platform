<script setup lang="ts">
import type { AnalysisProvenanceExtension } from '../types/analysisProvenance'
import { unitDisplay } from '../utils/scientificDisplay'

const props = defineProps<{ extensions: AnalysisProvenanceExtension[]; definitionName: string }>()
const labels: Record<string, string> = { delta_power: 'Delta 功率', theta_power: 'Theta 功率', alpha_power: 'Alpha 功率', beta_power: 'Beta 功率', delta_rbp: 'Delta 相对功率', theta_rbp: 'Theta 相对功率', alpha_rbp: 'Alpha 相对功率', beta_rbp: 'Beta 相对功率' }
function record(value: unknown): Record<string, unknown> { return value && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : {} }
function number(value: unknown, digits = 4): string { return typeof value === 'number' && Number.isFinite(value) ? value.toFixed(digits) : '—' }
function ratio(value: unknown): string { return typeof value === 'number' && Number.isFinite(value) ? `${(value * 100).toFixed(2)}%` : '—' }
function inputLabel(value: unknown): string { const input = record(value); return labels[String(input.feature ?? '')] ?? String(input.feature ?? '输入') }
function range(value: unknown): string { const values = Array.isArray(value) ? value : []; return values.length === 2 && values.every((item) => typeof item === 'number') ? `[${number(values[0], 2)}, ${number(values[1], 2)}] Hz` : '—' }
function faaSource(data: Record<string, unknown>, side: 'left' | 'right', fallbackIndex: number): string {
  const source = record(data.source_channels)[side]
  if (typeof source === 'string' && source) return source
  const channels = Array.isArray(data.channels) ? data.channels : []
  return typeof channels[fallbackIndex] === 'string' ? channels[fallbackIndex] : '—'
}
</script>

<template>
  <template v-for="extension in props.extensions" :key="extension.kind">
    <section v-if="extension.kind === 'spectral_band_power'" class="algorithm-debug-section"><h3>频段积分</h3><p>频段功率和相对功率均为后端保存值。</p><div class="algorithm-debug-bands"><div v-for="band in ['delta', 'theta', 'alpha', 'beta']" :key="band"><strong>{{ band[0].toUpperCase() + band.slice(1) }}</strong><span>P = {{ number(record(extension.data.band_power)[band]) }} µV²</span><span>RBP = {{ ratio(record(extension.data.relative_band_power)[band]) }}</span></div></div></section>
    <section v-else-if="extension.kind === 'metric_inputs_output'" class="algorithm-debug-section"><h3>算法输入与输出</h3><div class="algorithm-debug-inputs"><div v-for="(input, key) in record(extension.data.inputs)" :key="key"><strong>{{ inputLabel(input) }}</strong><span>{{ number(record(input).value) }} {{ unitDisplay(record(input).unit) }}</span><small>{{ record(input).channel ?? '' }}</small></div></div><p class="algorithm-debug-output">{{ record(extension.data.output).label ?? props.definitionName }} = <strong>{{ number(record(extension.data.output).value) }}</strong> {{ unitDisplay(record(extension.data.output).unit) }}</p></section>
    <section v-else-if="extension.kind === 'algorithm_calculation'" class="algorithm-debug-section"><h3>实际计算明细</h3><p>以下字段为后端保存的实际公式输入，不使用基础频段参考值替代。</p><p class="algorithm-debug-output">公式：{{ extension.data.formula ?? '—' }}</p><div class="algorithm-debug-inputs"><div v-for="(input, index) in Array.isArray(extension.data.inputs) ? extension.data.inputs : []" :key="index"><strong>{{ record(input).label ?? '输入' }}</strong><span v-if="record(input).range_hz">{{ range(record(input).range_hz) }}</span><span v-else-if="record(input).text">{{ record(input).text }}</span><span v-else>{{ number(record(input).value) }} {{ unitDisplay(record(input).unit) }}</span></div></div></section>
    <section v-else-if="extension.kind === 'faa_paired_quality'" class="algorithm-debug-section"><h3>FAA 成对 Epoch 质量</h3><p>左来源：{{ faaSource(extension.data, 'left', 0) }} · 右来源：{{ faaSource(extension.data, 'right', 1) }} · Alpha：{{ range(extension.data.band) }}</p><div class="algorithm-debug-inputs"><div><strong>有效 Epoch</strong><span>{{ number(extension.data.clean_epochs, 0) }} / {{ number(extension.data.total_epochs, 0) }}</span></div><div><strong>质量比例</strong><span>{{ typeof extension.data.clean_ratio === 'number' ? `${(extension.data.clean_ratio * 100).toFixed(2)}%` : '—' }}</span></div><div><strong>最少有效 Epoch</strong><span>{{ number(record(extension.data.faa_contract).minimum_clean_epochs, 0) }}</span></div><div><strong>伪迹阈值</strong><span>{{ number(record(extension.data.faa_contract).artifact_peak_uv, 0) }} µV</span></div><div><strong>拒绝原因</strong><span>{{ extension.data.reason || '无' }}</span></div></div></section>
  </template>
</template>
