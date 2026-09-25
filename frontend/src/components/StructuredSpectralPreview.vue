<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import * as echarts from 'echarts/core'
import { HeatmapChart, LineChart } from 'echarts/charts'
import { GridComponent, TooltipComponent, VisualMapComponent } from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'
import type { StructuredPreview } from '../api/results'

echarts.use([HeatmapChart, LineChart, GridComponent, TooltipComponent, VisualMapComponent, CanvasRenderer])
const props = defineProps<{ preview: StructuredPreview }>()
const element = ref<HTMLDivElement | null>(null)
let chart: echarts.ECharts | null = null
let resizeObserver: ResizeObserver | null = null
const selectedWindow = ref(0)
const frequencies = computed(() => props.preview.axes.frequency_hz ?? [])
const windows = computed(() => props.preview.windows ?? [])
const power = computed(() => props.preview.arrays.power_db ?? props.preview.arrays.psd ?? [])
const windowLabel = computed(() => { const item = windows.value[selectedWindow.value]; return item ? `${item.start_s.toFixed(3)}–${item.end_s.toFixed(3)} s · ${item.state}` : '无窗口信息' })
function asRows(value: unknown): unknown[] { return Array.isArray(value) ? value : [] }
function render() {
  if (!chart) return
  const matrix = asRows(power.value)
  const kind = props.preview.output.kind
  if (kind === 'frequency_series') {
    const row = Array.isArray(matrix[0]) ? matrix[selectedWindow.value] as unknown[] : matrix
    chart.setOption({ animation: false, grid: { left: 62, right: 20, top: 24, bottom: 42 }, tooltip: { trigger: 'axis' }, xAxis: { type: 'value', name: '频率 (Hz)', min: frequencies.value[0], max: frequencies.value.at(-1), nameLocation: 'middle', nameGap: 28 }, yAxis: { type: 'value', name: props.preview.array_metadata.psd?.unit ?? '功率', nameLocation: 'middle', nameGap: 48 }, series: [{ type: 'line', showSymbol: false, data: frequencies.value.map((frequency, index) => { const value = row[index]; return [frequency, typeof value === 'number' && Number.isFinite(value) ? value : null] }) }] }, true)
    return
  }
  const frame = Array.isArray(matrix[0]) && Array.isArray(matrix[0]?.[0]) ? matrix[selectedWindow.value] as unknown[][] : matrix as unknown[][]
  const times = props.preview.axes.time_center_s ?? []
  const values: Array<[number, number, number | null]> = []
  frame.forEach((row, timeIndex) => frequencies.value.forEach((_frequency, frequencyIndex) => values.push([timeIndex, frequencyIndex, typeof row[frequencyIndex] === 'number' && Number.isFinite(row[frequencyIndex] as number) ? row[frequencyIndex] as number : null])))
  chart.setOption({ animation: false, grid: { left: 62, right: 80, top: 24, bottom: 42 }, tooltip: { trigger: 'item' }, xAxis: { type: 'category', data: times.map(value => value.toFixed(2)), name: '时间中心 (s)', nameLocation: 'middle', nameGap: 28 }, yAxis: { type: 'category', data: frequencies.value.map(value => value.toFixed(2)), name: '频率 (Hz)', nameLocation: 'middle', nameGap: 48 }, visualMap: { min: -120, max: 20, calculable: false, right: 0, top: 'center' }, series: [{ type: 'heatmap', data: values }] }, true)
}
function previous() { selectedWindow.value = Math.max(0, selectedWindow.value - 1) }
function next() { selectedWindow.value = Math.min(Math.max(0, windows.value.length - 1), selectedWindow.value + 1) }
watch(() => props.preview, () => { selectedWindow.value = 0; render() }, { deep: true })
watch(selectedWindow, render)
onMounted(() => { if (element.value) { chart = echarts.init(element.value); resizeObserver = new ResizeObserver(() => chart?.resize()); resizeObserver.observe(element.value); render() } })
onBeforeUnmount(() => { resizeObserver?.disconnect(); chart?.dispose(); chart = null })
</script>
<template>
  <section class="structured-spectral-preview" aria-label="结构化频谱结果">
    <div class="structured-spectral-toolbar"><span>{{ preview.output.label }} · {{ preview.output.kind === 'frequency_series' ? '频率序列' : '时频矩阵' }}</span><button type="button" :disabled="selectedWindow <= 0" @click="previous">上一窗口</button><span>{{ windowLabel }}</span><button type="button" :disabled="selectedWindow >= windows.length - 1" @click="next">下一窗口</button></div>
    <p class="structured-spectral-meta">状态：{{ preview.quality }} · 窗口统计：{{ Object.entries(preview.window_state_counts).map(([key, value]) => `${key} ${value}`).join('，') }} · 结果由后端计算，未包含临床解释。</p>
    <div ref="element" class="structured-spectral-chart"></div>
  </section>
</template>
