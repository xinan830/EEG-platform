<script setup lang="ts">
import { computed, ref } from 'vue'
import type { AnalysisResult } from '../types/analysis'
import type { Recording } from '../types/recording'
import ScorePanel from '../components/ScorePanel.vue'
import WaveformPanel from '../components/WaveformPanel.vue'

const props = defineProps<{ recording: Recording; analysis: AnalysisResult }>()
const positionS = ref(0)
const speed = ref<1 | 2 | 4>(1)
const playing = ref(false)
let timer: number | undefined
const visibleMetrics = computed(() => props.analysis.metrics.filter((point) => point.elapsed_s <= positionS.value))

function toggle() {
  playing.value = !playing.value
  if (!playing.value) { window.clearInterval(timer); return }
  timer = window.setInterval(() => {
    positionS.value = Math.min(props.analysis.duration_s, positionS.value + speed.value * 0.25)
    if (positionS.value >= props.analysis.duration_s) toggle()
  }, 250)
}

function restart() { positionS.value = 0; playing.value = false; window.clearInterval(timer) }
</script>

<template>
  <main class="workbench">
    <header class="workbench-header">
      <div><p class="eyebrow">ANALYSIS WORKBENCH</p><h1>{{ recording.original_name }}</h1></div>
      <div class="controls"><button @click="toggle">{{ playing ? '暂停' : '播放' }}</button><button @click="restart">重播</button><button v-for="item in [1,2,4]" :key="item" :class="{ active: speed === item }" @click="speed = item as 1|2|4">{{ item }}x</button></div>
    </header>
    <p class="playback">数据时间 {{ positionS.toFixed(1) }} / {{ analysis.duration_s.toFixed(1) }} s · 倍速只改变显示节奏，不改变分析结果。</p>
    <WaveformPanel :waveform="analysis.waveform" :mapping="recording.mapping" :position-s="positionS" :events="analysis.events" />
    <div class="workbench-grid"><ScorePanel :analysis="analysis" /><section class="panel"><h2>实时 IAPF 尝试</h2><ul class="metric-list"><li v-for="point in analysis.iapf_attempts" :key="point.elapsed_s">{{ point.elapsed_s.toFixed(0) }}s · {{ point.iapf?.toFixed(2) ?? '无有效值' }} Hz · 质量 {{ (point.signal_quality * 100).toFixed(0) }}%</li></ul></section></div>
    <section class="panel"><h2>指标趋势</h2><p v-if="!visibleMetrics.length">播放后将在此显示每秒数据时间上的指标。</p><table v-else><thead><tr><th>时间</th><th>放松度</th><th>空间分布</th><th>脑负荷</th></tr></thead><tbody><tr v-for="point in visibleMetrics.slice(-12)" :key="point.elapsed_s"><td>{{ point.elapsed_s.toFixed(0) }}s</td><td>{{ point.relaxation.toFixed(4) }}</td><td>{{ point.spatial_distribution.toFixed(4) }}</td><td>{{ point.brainbeat?.toFixed(4) ?? '--' }}</td></tr></tbody></table></section>
  </main>
</template>
