<script setup lang="ts">
import type { AnalysisProvenance, AnalysisTimeRange } from '../types/analysisProvenance'

const props = defineProps<{ provenance?: AnalysisProvenance }>()

function number(value: unknown, digits = 3): string {
  return typeof value === 'number' && Number.isFinite(value) ? value.toFixed(digits) : '—'
}

function range(value: AnalysisTimeRange | null | undefined): string {
  return value ? `${number(value.start_s)}–${number(value.end_s)} s` : '—'
}

function recordText(value: unknown): string {
  if (typeof value === 'string' && value) return value
  if (!value || typeof value !== 'object' || Array.isArray(value)) return '—'
  const record = value as Record<string, unknown>
  if (typeof record.mode === 'string') return record.mode
  if (Array.isArray(record.bandpass_hz) && record.bandpass_hz.length === 2) return `${record.bandpass_hz[0]}–${record.bandpass_hz[1]} Hz`
  return Object.keys(record).length ? JSON.stringify(record) : '—'
}

function welchText(): string {
  const welch = props.provenance?.welch
  if (!welch) return '—'
  const window = welch.window.toLowerCase() === 'hann' ? 'Hann' : welch.window
  return `${number(welch.segment_s, 0)} s ${window}，${number(welch.overlap_fraction * 100, 0)}% overlap（${number(welch.step_s, 0)} s 步进）`
}

function frequencyText(): string {
  const frequency = props.provenance?.frequency
  return frequency ? `${number(frequency.low_hz, 0)}–${number(frequency.high_hz, 0)} Hz（${frequency.point_count} 个频率点）` : '—'
}

function qualityText(): string {
  const quality = props.provenance?.quality
  if (!quality) return '—'
  const clean = quality.clean_segments
  const total = quality.total_segments
  return typeof clean === 'number' && typeof total === 'number' ? `${clean}/${total} clean` : String(quality.status ?? '—')
}
</script>

<template>
  <section class="analysis-provenance-panel algorithm-debug-section">
    <h3>分析追溯信息</h3>
    <p>只读展示后端 Run 已保存的证据；前端不重新计算 EEG、PSD、频段功率或算法公式。</p>
    <dl>
      <dt>模式</dt><dd>{{ provenance?.mode === 'dynamic' ? '动态算法' : provenance?.mode === 'static' ? '静态算法' : provenance?.mode ?? '—' }}</dd>
      <dt>请求分析区间</dt><dd>{{ range(provenance?.requested_range) }}</dd>
      <dt>实际分析区间</dt><dd>{{ range(provenance?.actual_range) }}</dd>
      <dt>通道</dt><dd>{{ provenance?.channel ?? '—' }}</dd>
      <dt>算法版本</dt><dd>{{ provenance?.definition_version ?? '—' }}</dd>
      <dt>频谱算法</dt><dd>{{ provenance?.scientific_algorithm_version ?? '—' }}</dd>
      <dt>实现版本</dt><dd>{{ provenance?.implementation_version ?? '—' }}</dd>
      <dt>配置指纹</dt><dd><code>{{ provenance?.config_sha256 ?? '—' }}</code></dd>
      <dt>参考方式</dt><dd>{{ recordText(provenance?.analysis_reference) }}</dd>
      <dt>采样率</dt><dd>{{ provenance?.sfreq_hz == null ? '—' : `${provenance.sfreq_hz} Hz` }}</dd>
      <dt>预处理</dt><dd>{{ recordText(provenance?.filter) }}</dd>
      <dt>Welch</dt><dd>{{ welchText() }}</dd>
      <dt>频率范围</dt><dd>{{ frequencyText() }}</dd>
      <dt>质量门</dt><dd>{{ qualityText() }}</dd>
    </dl>
  </section>
</template>
