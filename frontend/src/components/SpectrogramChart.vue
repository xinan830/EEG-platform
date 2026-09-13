<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import * as echarts from 'echarts/core'
import { HeatmapChart } from 'echarts/charts'
import { GridComponent, MarkAreaComponent, MarkLineComponent, TooltipComponent, VisualMapComponent } from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'
import type { SpectrogramResponse } from '../types/spectrogram'

echarts.use([HeatmapChart, GridComponent, MarkAreaComponent, MarkLineComponent, TooltipComponent, VisualMapComponent, CanvasRenderer])
const props = withDefaults(defineProps<{ result: SpectrogramResponse; channel: string; frequencyRange?: { min: number; max: number } }>(), { frequencyRange: () => ({ min: 1, max: 30 }) })
const element = ref<HTMLDivElement | null>(null)
let chart: echarts.ECharts | null = null
let resizeObserver: ResizeObserver | null = null
function quantile(values: number[], fraction: number) { const sorted = [...values].sort((a, b) => a - b); return sorted[Math.min(sorted.length - 1, Math.max(0, Math.floor((sorted.length - 1) * fraction)))] ?? 0 }
function nearestIndex(values: number[], target: number) { return values.reduce((best, value, index) => Math.abs(value - target) < Math.abs(values[best] - target) ? index : best, 0) }
function render() {
  if (!chart) return
  const matrix = props.result.power_db?.[props.channel] ?? []
  const frequencyIndexes = frequenciesInRange(props.result.frequencies_hz, props.frequencyRange)
  const frequencies = frequencyIndexes.map((index) => props.result.frequencies_hz[index])
  const times = props.result.times_s
  const values = matrix.flat().filter((value) => Number.isFinite(value))
  const min = quantile(values, .05); const max = quantile(values, .95)
  const badIndexes = new Set((props.result.quality?.windows ?? []).flatMap((window, index) => window.status === 'bad' ? [index] : []))
  const points: Array<[number, number, number]> = []
  matrix.forEach((row, timeIndex) => frequencyIndexes.forEach((sourceIndex, frequencyIndex) => {
    const value = row[sourceIndex]
    points.push([timeIndex, frequencyIndex, badIndexes.has(timeIndex) || !Number.isFinite(value) ? min - 1 : value])
  }))
  const qualityByIndex = props.result.quality?.windows ?? []
  chart.setOption({
    animation: false,
    grid: { left: 72, right: 92, top: 24, bottom: 48 },
    tooltip: { formatter: (raw: unknown) => { const item = Array.isArray(raw) ? raw[0] : raw as { data: [number, number, number]; seriesName?: string }; const [timeIndex, frequencyIndex, value] = item.data; const center = times[timeIndex] ?? 0; const half = props.result.segment_s / 2; const quality = qualityByIndex[timeIndex]; const rejected = item.seriesName === '质量门拒绝' || quality?.status === 'bad'; const reason = quality?.reason ? ` · 原因：${quality.reason}` : ''; return `${props.channel}<br>中心：${center.toFixed(3)} s<br>窗口：${(center - half).toFixed(3)}–${(center + half).toFixed(3)} s<br>频率：${frequencies[frequencyIndex]?.toFixed(2)} Hz<br>${rejected ? `质量门拒绝${reason}${quality?.peak_uv != null ? ` · 峰值 ${quality.peak_uv.toFixed(1)} µV` : ''}` : `功率：${value.toFixed(3)} dB re 1 µV²/Hz`}` } },
    xAxis: { type: 'category', data: times.map((value) => value.toFixed(1)), name: '时间中心 (s)', nameLocation: 'middle', nameGap: 30, axisLabel: { formatter: (value: string) => { const time = Number(value); return Math.abs(time / 5 - Math.round(time / 5)) < .02 ? `${Math.round(time)}` : '' } } },
    yAxis: { type: 'category', data: frequencies.map((value) => value.toFixed(2)), name: '频率 (Hz)', nameLocation: 'middle', nameGap: 54, axisLabel: { formatter: (value: string) => [1, 4, 8, 13, 20, 30].some((tick) => Math.abs(Number(value) - tick) < .01) ? String(Number(value)) : '' } },
    visualMap: { seriesIndex: 0, min, max: Math.max(min + 1e-9, max), precision: 1, calculable: false, orient: 'vertical', right: 0, top: 'center', inRange: { color: ['#081a70', '#2d30dc', '#5aa0dc', '#ff8a22'] }, outOfRange: { color: ['#94a3b8'] }, text: ['高功率', '低功率'], formatter: (value: number) => `${value.toFixed(1)} dB` },
    series: [{ name: '功率', type: 'heatmap', data: points, progressive: 0, emphasis: { itemStyle: { borderColor: '#fff', borderWidth: 1 } }, markLine: props.frequencyRange.min === 1 && props.frequencyRange.max === 30 ? { silent: true, symbol: 'none', lineStyle: { color: 'rgba(255,255,255,.75)', width: 1 }, label: { color: '#334155', backgroundColor: 'rgba(255,255,255,.8)', padding: [1, 3] }, data: [{ name: 'Delta / Theta · 4 Hz', yAxis: nearestIndex(frequencies, 4) }, { name: 'Theta / Alpha · 8 Hz', yAxis: nearestIndex(frequencies, 8) }, { name: 'Alpha / Beta · 13 Hz', yAxis: nearestIndex(frequencies, 13) }] } : undefined }],
  }, true)
}
function frequenciesInRange(values: number[], range: { min: number; max: number }) { const indexes = values.map((value, index) => ({ value, index })).filter(({ value }) => value >= range.min && value <= range.max).map(({ index }) => index); return indexes.length ? indexes : values.map((_, index) => index) }
onMounted(() => { if (element.value) { chart = echarts.init(element.value); resizeObserver = new ResizeObserver(() => chart?.resize()); resizeObserver.observe(element.value); render() } })
watch(() => [props.result, props.channel, props.frequencyRange], render, { deep: true })
onBeforeUnmount(() => { resizeObserver?.disconnect(); chart?.dispose(); chart = null })
</script>
<template><div ref="element" class="spectrogram-echart" aria-label="功率时频图"></div></template>
