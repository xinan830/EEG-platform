<script setup lang="ts">
import { computed } from 'vue'
import { unitDisplay } from '../utils/scientificDisplay'

type Input = { feature: string; value: number | null; unit: string; channel: string }
export type DefinitionMetricResult = {
  output: { label: string; value: number | null; unit: string; quality: { status: string; reasons: string[] } }
  inputs?: Record<string, Input>
  channel: string
  actual_range: { start_s: number; end_s: number }
  source_quality?: { clean_segments?: number; total_segments?: number }
  chart: { kind: string; y_axis?: { unit?: string } }
}

const props = defineProps<{ result: DefinitionMetricResult }>()
const inputs = computed(() => Object.values(props.result.inputs ?? {}))
const roleValues = computed(() => props.result.chart.kind === 'official_role_values'
  ? ((props.result.chart as { values?: Array<{ role: string; value: number | null }> }).values ?? []) : [])
const largestInput = computed(() => Math.max(1, ...inputs.value.map((item) => item.value ?? 0)))
const inputLabels: Record<string, string> = {
  delta_power: 'Delta 功率', theta_power: 'Theta 功率', alpha_power: 'Alpha 功率', beta_power: 'Beta 功率',
  delta_rbp: 'Delta 相对功率', theta_rbp: 'Theta 相对功率', alpha_rbp: 'Alpha 相对功率', beta_rbp: 'Beta 相对功率',
}
function label(input: Input) { return inputLabels[input.feature] ?? input.feature }
</script>

<template>
  <section class="definition-metric-result" aria-label="算法运行结果">
    <p class="algorithm-explainer-kicker">本次算法结果</p>
    <div class="definition-metric-value"><span>{{ result.output.label }}</span><strong>{{ result.output.value ?? '不可用' }}</strong><em>{{ unitDisplay(result.output.unit) }}</em></div>
    <p>通道：{{ result.channel }} · 分析区间：{{ result.actual_range.start_s.toFixed(3) }}–{{ result.actual_range.end_s.toFixed(3) }} s · 输出质量：{{ result.output.quality.status }}</p>
    <p v-if="result.source_quality?.clean_segments !== undefined">PSD 质量：{{ result.source_quality.clean_segments }}/{{ result.source_quality.total_segments }} clean</p>
    <div v-if="result.chart.kind === 'input_comparison'" data-testid="metric-input-chart" class="metric-input-chart">
      <strong>输入功率对比</strong><small>横轴：输入指标 · 纵轴：{{ unitDisplay(result.chart.y_axis?.unit) }}</small>
      <div v-for="input in inputs" :key="input.feature" class="metric-input-bar-row"><span>{{ label(input) }}</span><i><b :style="{ width: `${((input.value ?? 0) / largestInput) * 100}%` }" /></i><em>{{ input.value ?? '不可用' }} {{ unitDisplay(input.unit) }}</em></div>
    </div>
    <div v-if="roleValues.length" data-testid="official-role-values" class="metric-input-chart">
      <strong>逻辑通道结果</strong><small>Fz、Pz、Oz 是后端保存的通道映射角色，未做前端计算或平均。</small>
      <div v-for="item in roleValues" :key="item.role" class="metric-input-bar-row"><span>{{ item.role }}</span><em>{{ item.value ?? '不可用' }} {{ unitDisplay(result.output.unit) }}</em></div>
    </div>
    <p class="algorithm-explainer-note">数值、单位、质量和图表数据均来自后端 Run；前端不重新计算 EEG。</p>
  </section>
</template>
