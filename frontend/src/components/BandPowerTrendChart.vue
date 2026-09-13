<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, watch } from 'vue'
import * as echarts from 'echarts/core'
import { LineChart } from 'echarts/charts'
import { GridComponent, LegendComponent, MarkAreaComponent, TooltipComponent } from 'echarts/components'
import { CanvasRenderer } from 'echarts/renderers'
import type { SpectrogramResponse } from '../types/spectrogram'

echarts.use([LineChart, GridComponent, LegendComponent, MarkAreaComponent, TooltipComponent, CanvasRenderer])
type TrendBand = 'delta' | 'theta' | 'alpha' | 'beta' | 'all' | 'custom'
const props = withDefaults(defineProps<{ result: SpectrogramResponse; channel: string; band?: TrendBand }>(), { band: 'all' })
const element = ref<HTMLDivElement | null>(null)
let chart: echarts.ECharts | null = null
let resizeObserver: ResizeObserver | null = null
const bands = ['delta', 'theta', 'alpha', 'beta'] as const
const labels = { delta: 'Delta 1–4 Hz', theta: 'Theta 4–8 Hz', alpha: 'Alpha 8–13 Hz', beta: 'Beta 13–30 Hz' }
const colors = { delta: '#64748b', theta: '#2f80ed', alpha: '#27ae60', beta: '#e67e22' }
function render() {
  if (!chart) return
  const times = props.result.times_s
  const firstTime = times[0] ?? 0
  const lastTime = times.at(-1) ?? firstTime
  const rejectedAreas = (props.result.quality?.windows ?? []).filter((window) => window.status === 'bad').map((window) => ([
    { name: '已拒绝', xAxis: window.start_s },
    { xAxis: window.end_s },
  ]))
  const visibleBands = props.band === 'all' ? bands : props.band === 'custom' ? [] : [props.band]
  const series = visibleBands.map((band) => ({ name: labels[band], type: 'line', showSymbol: false, smooth: false, connectNulls: false, data: (props.result.band_power_timeseries?.[props.channel]?.[band] ?? []).map((value, index) => [props.result.times_s[index], Number.isFinite(value) ? value : null]), lineStyle: { color: colors[band], width: 2 }, itemStyle: { color: colors[band] } }))
  if (props.band === 'custom' && props.result.custom_band) {
    const band = props.result.custom_band
    series.push({ name: `自定义 ${band.low_hz}–${band.high_hz} Hz`, type: 'line', showSymbol: false, smooth: false, connectNulls: false, data: (props.result.custom_band_power_timeseries?.[props.channel] ?? []).map((value, index) => [props.result.times_s[index], Number.isFinite(value) ? value : null]), lineStyle: { color: '#7c3aed', width: 2 }, itemStyle: { color: '#7c3aed' } })
  }
  if (series[0]) (series[0] as Record<string, unknown>).markArea = { silent: true, itemStyle: { color: 'rgba(148,163,184,.22)' }, label: { show: true, color: '#475569', formatter: '质量门拒绝' }, data: rejectedAreas }
  chart.setOption({ animation: false, grid: { left: 62, right: 18, top: 36, bottom: 42 }, legend: { top: 0 }, tooltip: { trigger: 'axis', valueFormatter: (value: unknown) => `${Number(value).toPrecision(5)} µV²` }, xAxis: { type: 'value', min: firstTime, max: Math.max(firstTime + 1, lastTime), name: '时间中心 (s)', nameLocation: 'middle', nameGap: 28 }, yAxis: { type: 'value', name: '频段功率 (µV²)', nameLocation: 'middle', nameGap: 48, min: 0 }, series }, true)
}
onMounted(() => { if (element.value) { chart = echarts.init(element.value); resizeObserver = new ResizeObserver(() => chart?.resize()); resizeObserver.observe(element.value); render() } })
watch(() => [props.result, props.channel, props.band], render, { deep: true })
onBeforeUnmount(() => { resizeObserver?.disconnect(); chart?.dispose(); chart = null })
</script>
<template><div ref="element" class="band-power-trend-echart" aria-label="频段功率趋势图"></div></template>
