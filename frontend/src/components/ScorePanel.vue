<script setup lang="ts">
import { computed } from 'vue'
import type { AnalysisResult } from '../types/analysis'

const props = defineProps<{ analysis: AnalysisResult }>()
const last = computed(() => props.analysis.metrics[props.analysis.metrics.length - 1])
const score = computed(() => {
  if (!last.value) return null
  return Math.round(Math.max(0, Math.min(100, last.value.relaxation * 50 + last.value.spatial_distribution * 50)))
})
</script>

<template>
  <section class="score-panel panel">
    <p class="eyebrow">综合冥想指数</p>
    <strong>{{ score ?? '--' }}</strong>
    <span>/ 100</span>
    <dl>
      <div><dt>锁定 IAPF</dt><dd>{{ analysis.locked_iapf?.toFixed(2) ?? '未锁定' }} Hz</dd></div>
      <div><dt>分析时长</dt><dd>{{ analysis.duration_s.toFixed(1) }} s</dd></div>
      <div><dt>最后脑负荷</dt><dd>{{ last?.brainbeat?.toFixed(3) ?? '--' }}</dd></div>
    </dl>
  </section>
</template>
