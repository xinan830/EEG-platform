<script setup lang="ts">
import type { AnalysisProvenanceExtension } from '../types/analysisProvenance'

const props = defineProps<{ extensions: AnalysisProvenanceExtension[]; definitionName: string }>()
const labels: Record<string, string> = { delta_power: 'Delta 功率', theta_power: 'Theta 功率', alpha_power: 'Alpha 功率', beta_power: 'Beta 功率', delta_rbp: 'Delta 相对功率', theta_rbp: 'Theta 相对功率', alpha_rbp: 'Alpha 相对功率', beta_rbp: 'Beta 相对功率' }
function record(value: unknown): Record<string, unknown> { return value && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : {} }
function number(value: unknown, digits = 4): string { return typeof value === 'number' && Number.isFinite(value) ? value.toFixed(digits) : '—' }
function ratio(value: unknown): string { return typeof value === 'number' && Number.isFinite(value) ? `${(value * 100).toFixed(2)}%` : '—' }
function inputLabel(value: unknown): string { const input = record(value); return labels[String(input.feature ?? '')] ?? String(input.feature ?? '输入') }
</script>

<template>
  <template v-for="extension in props.extensions" :key="extension.kind">
    <section v-if="extension.kind === 'spectral_band_power'" class="algorithm-debug-section"><h3>频段积分</h3><p>频段功率和相对功率均为后端保存值。</p><div class="algorithm-debug-bands"><div v-for="band in ['delta', 'theta', 'alpha', 'beta']" :key="band"><strong>{{ band[0].toUpperCase() + band.slice(1) }}</strong><span>P = {{ number(record(extension.data.band_power)[band]) }} µV²</span><span>RBP = {{ ratio(record(extension.data.relative_band_power)[band]) }}</span></div></div></section>
    <section v-else-if="extension.kind === 'metric_inputs_output'" class="algorithm-debug-section"><h3>算法输入与输出</h3><div class="algorithm-debug-inputs"><div v-for="(input, key) in record(extension.data.inputs)" :key="key"><strong>{{ inputLabel(input) }}</strong><span>{{ number(record(input).value) }} {{ record(input).unit ?? '' }}</span><small>{{ record(input).channel ?? '' }}</small></div></div><p class="algorithm-debug-output">{{ record(extension.data.output).label ?? props.definitionName }} = <strong>{{ number(record(extension.data.output).value) }}</strong> {{ record(extension.data.output).unit ?? '—' }}</p></section>
  </template>
</template>
