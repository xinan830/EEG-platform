<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import * as echarts from 'echarts/core'
import { LineChart } from 'echarts/charts'
import { GridComponent, TooltipComponent, MarkAreaComponent } from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'
import { metricTrendTimeAxis, metricTrendValueAxis } from '../utils/metricTrendAxis'

export type MetricPoint = { time_s: number; window_start_s: number; window_end_s: number; value: number | null; quality?: { status?: string; reasons?: string[] } }
export type DynamicMetric = {
  output: { label: string; unit: string }
  channel: string
  series: MetricPoint[]
  dynamic_contract?: { window_s: number; step_s: number; alignment?: string }
  chart?: { y_axis?: { label?: string; unit?: string } }
}

export type DynamicMetricPending = { label: string; unit: string; channel: string; windowS: number }

echarts.use([LineChart, GridComponent, TooltipComponent, MarkAreaComponent, CanvasRenderer])
const props = defineProps<{ result: DynamicMetric | null; pending?: DynamicMetricPending }>()
const element = ref<HTMLDivElement | null>(null)
let chart: echarts.ECharts | null = null
let resizeObserver: ResizeObserver | null = null
const displayed = computed<DynamicMetric>(() => props.result ?? {
  output: { label: props.pending?.label ?? '算法指标', unit: props.pending?.unit ?? 'dimensionless' },
  channel: props.pending?.channel ?? '',
  dynamic_contract: { window_s: props.pending?.windowS ?? 10, step_s: 1, alignment: 'window_end' },
  series: [],
})
const latestPoint = computed(() => [...(displayed.value.series ?? [])].reverse().find((point) => point.value !== null) ?? null)
const firstWindowText = computed(() => `等待第一个完整窗口：0.000–${(displayed.value.dynamic_contract?.window_s ?? 10).toFixed(3)} s`)

function render() {
  if (!chart) return
  const result = displayed.value
  const points = result.series ?? []
  const timeAxis = metricTrendTimeAxis(points.map((point) => point.time_s), result.dynamic_contract?.window_s ?? 10)
  const values = points.map((point) => [point.time_s, point.value] as [number, number | null])
  const valueAxis = metricTrendValueAxis(points.map((point) => point.value))
  const badAreas = points.filter((point) => point.value === null || point.quality?.status === 'bad').map((point) => [
    { xAxis: point.window_start_s }, { xAxis: point.window_end_s },
  ])
  chart.setOption({
    animation: false,
    grid: { left: 62, right: 18, top: 10, bottom: 36 },
    tooltip: {
      trigger: 'axis',
      formatter: (params: unknown) => {
        const item = Array.isArray(params) ? params[0] as { data?: [number, number | null] } : params as { data?: [number, number | null] }
        const time = item?.data?.[0]
        const point = points.find((candidate) => Math.abs(candidate.time_s - Number(time)) < 1e-7)
        if (!point) return ''
        const value = point.value === null ? '不可用' : `${point.value} ${result.output.unit}`
        const quality = point.quality?.status === 'bad' ? ` · 质量门拒绝${point.quality.reasons?.length ? `（${point.quality.reasons.join('、')}）` : ''}` : ' · Clean'
        return `时间：${point.time_s.toFixed(3)} s<br/>窗口：${point.window_start_s.toFixed(3)}–${point.window_end_s.toFixed(3)} s<br/>${result.output.label}：${value}${quality}`
      },
    },
    xAxis: {
      type: 'value',
      name: '窗口结束时间 (s)',
      nameLocation: 'middle',
      nameGap: 24,
      min: timeAxis.min,
      max: timeAxis.max,
      scale: true,
      splitNumber: 3,
    },
    yAxis: {
      type: 'value',
      min: valueAxis.min,
      max: valueAxis.max,
      scale: true,
      splitNumber: 3,
      axisLabel: { formatter: (value: number) => Number(value).toPrecision(5) },
    },
    series: [{ type: 'line', name: result.output.label, data: values, showSymbol: points.length <= 1, symbolSize: 8, connectNulls: false, lineStyle: { width: 2, color: '#2878bd' }, itemStyle: { color: '#2878bd' }, markArea: { silent: true, itemStyle: { color: 'rgba(190, 198, 205, .24)' }, data: badAreas } }],
  }, true)
}
onMounted(() => { if (element.value) { chart = echarts.init(element.value); resizeObserver = new ResizeObserver(() => chart?.resize()); resizeObserver.observe(element.value); render() } })
watch(displayed, render, { deep: true })
onBeforeUnmount(() => { resizeObserver?.disconnect(); chart?.dispose(); chart = null })
</script>
<template>
  <section class="definition-metric-trend-wrap" aria-label="动态算法趋势图">
    <header class="definition-metric-trend-meta">
      <span>{{ displayed.output.label }}（{{ displayed.output.unit }}）<template v-if="displayed.dynamic_contract"> · 最近 {{ displayed.dynamic_contract.window_s }} s · 每 {{ displayed.dynamic_contract.step_s }} s</template></span>
      <strong v-if="latestPoint">当前：{{ latestPoint.value }} {{ displayed.output.unit }}</strong>
      <span v-else>当前：等待完整窗口</span>
    </header>
    <p v-if="!result" class="definition-metric-trend-pending">{{ firstWindowText }}；获得完整 EEG 前不会生成算法值。</p>
    <div ref="element" class="definition-metric-trend"></div>
  </section>
</template>
