<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import * as echarts from 'echarts/core'
import { LineChart } from 'echarts/charts'
import { GridComponent, TooltipComponent, MarkAreaComponent } from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'

export type MetricPoint = { time_s: number; window_start_s: number; window_end_s: number; value: number | null; quality?: { status?: string; reasons?: string[] } }
export type DynamicMetric = {
  output: { label: string; unit: string }
  channel: string
  series: MetricPoint[]
  chart?: { y_axis?: { label?: string; unit?: string } }
}

echarts.use([LineChart, GridComponent, TooltipComponent, MarkAreaComponent, CanvasRenderer])
const props = defineProps<{ result: DynamicMetric }>()
const element = ref<HTMLDivElement | null>(null)
let chart: echarts.ECharts | null = null
let resizeObserver: ResizeObserver | null = null

function render() {
  if (!chart) return
  const points = props.result.series ?? []
  const values = points.map((point) => [point.time_s, point.value] as [number, number | null])
  const badAreas = points.filter((point) => point.value === null || point.quality?.status === 'bad').map((point) => [
    { xAxis: point.window_start_s }, { xAxis: point.window_end_s },
  ])
  chart.setOption({
    animation: false,
    grid: { left: 68, right: 18, top: 18, bottom: 48 },
    tooltip: {
      trigger: 'axis',
      formatter: (params: unknown) => {
        const item = Array.isArray(params) ? params[0] as { data?: [number, number | null] } : params as { data?: [number, number | null] }
        const time = item?.data?.[0]
        const point = points.find((candidate) => Math.abs(candidate.time_s - Number(time)) < 1e-7)
        if (!point) return ''
        const value = point.value === null ? '不可用' : `${point.value} ${props.result.output.unit}`
        const quality = point.quality?.status === 'bad' ? ` · 质量门拒绝${point.quality.reasons?.length ? `（${point.quality.reasons.join('、')}）` : ''}` : ' · Clean'
        return `时间：${point.time_s.toFixed(3)} s<br/>窗口：${point.window_start_s.toFixed(3)}–${point.window_end_s.toFixed(3)} s<br/>${props.result.output.label}：${value}${quality}`
      },
    },
    xAxis: { type: 'value', name: '窗口结束时间 (s)', nameLocation: 'middle', nameGap: 30 },
    yAxis: { type: 'value', name: `${props.result.output.label} (${props.result.output.unit})`, nameLocation: 'middle', nameGap: 48 },
    series: [{ type: 'line', name: props.result.output.label, data: values, showSymbol: points.length <= 1, symbolSize: 8, connectNulls: false, lineStyle: { width: 2, color: '#2878bd' }, itemStyle: { color: '#2878bd' }, markArea: { silent: true, itemStyle: { color: 'rgba(190, 198, 205, .24)' }, data: badAreas } }],
  }, true)
}
onMounted(() => { if (element.value) { chart = echarts.init(element.value); resizeObserver = new ResizeObserver(() => chart?.resize()); resizeObserver.observe(element.value); render() } })
watch(() => props.result, render, { deep: true })
onBeforeUnmount(() => { resizeObserver?.disconnect(); chart?.dispose(); chart = null })
</script>
<template><div ref="element" class="definition-metric-trend" aria-label="动态算法趋势图"></div></template>
