<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import * as echarts from 'echarts/core'
import { LineChart } from 'echarts/charts'
import { GridComponent, MarkLineComponent, TooltipComponent } from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'

echarts.use([LineChart, GridComponent, MarkLineComponent, TooltipComponent, CanvasRenderer])
const props = defineProps<{ frequenciesHz: number[]; values: number[]; channel: string }>()
const element = ref<HTMLDivElement | null>(null)
let chart: echarts.ECharts | null = null
let resizeObserver: ResizeObserver | null = null
function render() {
  if (!chart) return
  chart.setOption({
    animation: false,
    grid: { left: 58, right: 20, top: 20, bottom: 40 },
    tooltip: { trigger: 'axis', valueFormatter: (value: unknown) => `${Number(value).toPrecision(5)} µV²/Hz` },
    xAxis: { type: 'value', min: 1, max: 30, name: 'Frequency (Hz)', nameLocation: 'middle', nameGap: 28, splitNumber: 6 },
    yAxis: { type: 'value', name: 'PSD (µV²/Hz)', nameLocation: 'middle', nameGap: 48, min: 0 },
    series: [{ name: props.channel, type: 'line', showSymbol: false, smooth: false, data: props.frequenciesHz.map((frequency, index) => [frequency, props.values[index] ?? null]), markLine: { silent: true, symbol: 'none', lineStyle: { color: '#94a3b8', type: 'dashed', width: 1 }, label: { color: '#64748b', formatter: '{c} Hz' }, data: [{ xAxis: 4 }, { xAxis: 8 }, { xAxis: 13 }] } }],
  }, true)
}
onMounted(() => { if (element.value) { chart = echarts.init(element.value); resizeObserver = new ResizeObserver(() => chart?.resize()); resizeObserver.observe(element.value); render() } })
watch(() => [props.frequenciesHz, props.values, props.channel], render, { deep: true })
onBeforeUnmount(() => { resizeObserver?.disconnect(); chart?.dispose(); chart = null })
</script>
<template><div ref="element" class="spectrum-psd-echart" aria-label="PSD 频谱图"></div></template>
