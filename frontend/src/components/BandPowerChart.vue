<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import * as echarts from 'echarts/core'
import { BarChart } from 'echarts/charts'
import { GridComponent, LegendComponent, TooltipComponent } from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'
import type { SpectrumBand } from '../types/spectrum'

echarts.use([BarChart, GridComponent, LegendComponent, TooltipComponent, CanvasRenderer])
const props = defineProps<{ channels: string[]; relativeBandPower: Record<string, Record<SpectrumBand, number>> }>()
const bands: SpectrumBand[] = ['delta', 'theta', 'alpha', 'beta']
const labels: Record<SpectrumBand, string> = { delta: 'Delta', theta: 'Theta', alpha: 'Alpha', beta: 'Beta' }
const colors = ['#64748b', '#2f80ed', '#27ae60', '#e67e22']
const element = ref<HTMLDivElement | null>(null)
let chart: echarts.ECharts | null = null
let resizeObserver: ResizeObserver | null = null
function render() {
  if (!chart) return
  chart.setOption({
    animation: false,
    grid: { left: 48, right: 18, top: 32, bottom: 28 },
    tooltip: { trigger: 'axis', axisPointer: { type: 'shadow' }, valueFormatter: (value: unknown) => `${(Number(value) * 100).toFixed(1)}%` },
    legend: { top: 0, data: bands.map((band) => labels[band]) },
    xAxis: { type: 'value', min: 0, max: 1, axisLabel: { formatter: (value: number) => `${Math.round(value * 100)}%` } },
    yAxis: { type: 'category', data: props.channels },
    series: bands.map((band, index) => ({ name: labels[band], type: 'bar', stack: 'total', data: props.channels.map((channel) => props.relativeBandPower[channel]?.[band] ?? 0), itemStyle: { color: colors[index] } })),
  }, true)
}
onMounted(() => { if (element.value) { chart = echarts.init(element.value); resizeObserver = new ResizeObserver(() => chart?.resize()); resizeObserver.observe(element.value); render() } })
watch(() => [props.channels, props.relativeBandPower], render, { deep: true })
onBeforeUnmount(() => { resizeObserver?.disconnect(); chart?.dispose(); chart = null })
</script>
<template><div ref="element" class="band-power-echart" aria-label="各通道频段占比图"></div></template>
