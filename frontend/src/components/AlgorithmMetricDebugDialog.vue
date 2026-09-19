<script setup lang="ts">
import { computed, ref } from 'vue'
import type { AnalysisRunResponse } from '../api/runs'
import AnalysisProvenancePanel from './AnalysisProvenancePanel.vue'
import AnalysisProvenanceExtensions from './AnalysisProvenanceExtensions.vue'

type UnknownRecord = Record<string, unknown>
type Evidence = { frequencies_hz?: number[]; psd_uV2_per_hz?: number[] }
type DynamicPoint = { spectral_evidence?: Evidence }
const props = defineProps<{ run: AnalysisRunResponse; definitionName: string; playbackPositionS?: number }>()
const emit = defineEmits<{ close: [] }>()
const showRawPsd = ref(false)
function asRecord(value: unknown): UnknownRecord { return value && typeof value === 'object' && !Array.isArray(value) ? value as UnknownRecord : {} }
const metric = computed(() => asRecord(props.run.result_summary?.metric))
const points = computed<DynamicPoint[]>(() => Array.isArray(metric.value.series) ? metric.value.series as DynamicPoint[] : [])
const latestPoint = computed(() => points.value.at(-1))
const evidence = computed<Evidence>(() => (latestPoint.value?.spectral_evidence ?? metric.value.spectral_evidence ?? {}) as Evidence)
const rawRows = computed(() => (evidence.value.frequencies_hz ?? []).map((frequency, index) => ({ frequency, psd: evidence.value.psd_uV2_per_hz?.[index] ?? null })))
const isDynamic = computed(() => props.run.analysis_provenance?.mode === 'dynamic')
function number(value: unknown, digits = 4): string { return typeof value === 'number' && Number.isFinite(value) ? value.toFixed(digits) : '—' }
</script>

<template>
  <div class="modal-layer algorithm-metric-debug-layer" @click.self="emit('close')">
    <section class="algorithm-metric-debug-dialog" role="dialog" aria-modal="true" aria-label="算法调试台">
      <header class="dialog-titlebar"><strong>算法调试台 · {{ definitionName }}</strong><button type="button" class="dialog-close" aria-label="关闭算法调试台" @click="emit('close')">×</button></header>
      <main class="algorithm-metric-debug-body">
        <p class="algorithm-debug-note">只读展示后端 Run 已保存的结果与证据；前端不重新计算 EEG、PSD、频段功率或公式。动态调试台跟随当前最新完成的结果，播放位置只用于说明进度，不替代实际分析区间。</p>
        <AnalysisProvenancePanel :provenance="run.analysis_provenance" />
        <section v-if="isDynamic" class="algorithm-debug-section"><h3>播放上下文</h3><dl><dt>波形播放位置</dt><dd>{{ props.playbackPositionS === undefined ? '—' : `${props.playbackPositionS.toFixed(3)} s` }}</dd></dl></section>
        <AnalysisProvenanceExtensions :extensions="run.analysis_provenance?.extensions ?? []" :definition-name="definitionName" />
        <section v-if="rawRows.length" class="algorithm-debug-section"><button type="button" @click="showRawPsd = !showRawPsd">{{ showRawPsd ? '收起' : '展开' }}原始 PSD 点（{{ rawRows.length }} 个频率点）</button><div v-if="showRawPsd" class="algorithm-debug-psd"><div v-for="row in rawRows" :key="row.frequency"><span>{{ row.frequency.toFixed(2) }} Hz</span><span>{{ number(row.psd, 8) }} µV²/Hz</span></div></div></section>
      </main>
      <footer class="channel-dialog-footer"><button type="button" @click="emit('close')">关闭</button></footer>
    </section>
  </div>
</template>
