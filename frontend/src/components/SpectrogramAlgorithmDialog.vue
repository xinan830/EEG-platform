<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { getConfiguredSpectrum } from '../api/spectrum'
import type { SpectrogramResponse } from '../types/spectrogram'

const props = defineProps<{ result: SpectrogramResponse; channel: string; recordingId?: string }>()
const emit = defineEmits<{ close: [] }>()
const selectedCenter = ref(props.result.times_s[0] ?? 0)
const staticResult = ref<Awaited<ReturnType<typeof getConfiguredSpectrum>> | null>(null)
const comparisonError = ref('')
const comparisonLoading = ref(false)
const rowIndex = computed(() => {
  let best = 0
  let distance = Number.POSITIVE_INFINITY
  props.result.times_s.forEach((value, index) => { const next = Math.abs(value - selectedCenter.value); if (next < distance) { best = index; distance = next } })
  return best
})
const windowInfo = computed(() => props.result.quality?.windows[rowIndex.value] ?? {
  center_s: selectedCenter.value, start_s: selectedCenter.value - 2, end_s: selectedCenter.value + 2,
})
const spectrogramPsd = computed(() => props.result.power_linear?.[props.channel]?.[rowIndex.value] ?? props.result.power[props.channel]?.[rowIndex.value] ?? [])
const comparison = computed(() => {
  const left = spectrogramPsd.value
  const right = staticResult.value?.psd[props.channel] ?? []
  if (!left.length || !right.length || left.length !== right.length) return null
  let maxAbs = 0; let maxRel = 0; let consistent = true
  left.forEach((value, index) => {
    const other = right[index]
    const absolute = Math.abs(value - other)
    const relative = absolute / Math.max(Math.abs(value), Math.abs(other), 1e-20)
    maxAbs = Math.max(maxAbs, absolute); maxRel = Math.max(maxRel, relative)
    if (absolute > 1e-9 && relative > 1e-7) consistent = false
  })
  return { maxAbs, maxRel, consistent }
})
async function loadStaticWindow() {
  if (!props.recordingId || !props.result.times_s.length) return
  comparisonLoading.value = true; comparisonError.value = ''
  try {
    const start = Math.max(0, windowInfo.value.start_s)
    staticResult.value = await getConfiguredSpectrum(props.recordingId, {
      mode: 'static', channels: [props.channel], time: { start_s: start, end_s: start + 4 }, dynamic_window_s: 10, refresh_step_s: 1,
    })
  } catch (cause) { staticResult.value = null; comparisonError.value = cause instanceof Error ? cause.message : '单窗口 PSD 读取失败' }
  finally { comparisonLoading.value = false }
}
watch(() => [props.channel, selectedCenter.value, props.result.recording_id], loadStaticWindow, { immediate: true })
</script>

<template>
  <div class="modal-layer algorithm-modal-layer" @click.self="emit('close')">
    <section class="algorithm-dialog spectrum-check-dialog" role="dialog" aria-modal="true" aria-labelledby="spectrogram-check-title" @click.stop>
      <header class="dialog-titlebar"><strong id="spectrogram-check-title">时频图算法校验</strong><button type="button" class="dialog-close" aria-label="关闭时频图算法校验" @click="emit('close')">×</button></header>
      <div class="algorithm-body">
        <p class="algorithm-meta">只读展示后端返回的时频结果，前端不执行 FFT、PSD 或 dB 换算。当前通道：{{ channel }}</p>
        <div class="spectrum-check-grid">
          <span>请求区间</span><strong>{{ result.requested_start_s?.toFixed(3) ?? '—' }}–{{ result.requested_end_s?.toFixed(3) ?? '—' }} s</strong>
          <span>实际区间</span><strong>{{ (result.actual_start_s ?? result.window_start_s).toFixed(3) }}–{{ (result.actual_end_s ?? result.window_start_s + result.window_duration_s).toFixed(3) }} s</strong>
          <span>接口版本</span><strong>{{ result.algorithm_version }}</strong>
          <span>分析算法版本</span><strong>{{ result.analysis_algorithm_version ?? result.baseline_algorithm_version ?? '—' }}</strong>
          <span>时频契约版本</span><strong>{{ result.spectrogram_contract_version ?? '—' }}</strong>
          <span>配置指纹</span><strong>{{ result.analysis_config_hash ?? '—' }}</strong>
          <span>时频窗</span><strong>{{ result.segment_s }} s Hann</strong>
          <span>时间步长</span><strong>{{ result.step_s }} s</strong>
          <span>时间轴语义</span><strong>窗口中心</strong>
          <span>时间点</span><strong>{{ result.time_bins ?? result.times_s.length }}</strong>
          <span>第一个中心</span><strong>{{ result.first_center_s?.toFixed(3) ?? result.times_s[0]?.toFixed(3) ?? '—' }} s</strong>
          <span>最后一个中心</span><strong>{{ result.last_center_s?.toFixed(3) ?? result.times_s.at(-1)?.toFixed(3) ?? '—' }} s</strong>
          <span>频率点</span><strong>{{ result.frequency_bins ?? result.frequencies_hz.length }}</strong>
          <span>矩阵方向</span><strong>time × frequency</strong>
          <span>矩阵形状</span><strong>{{ result.matrix_shape?.join(' × ') ?? `${result.times_s.length} × ${result.frequencies_hz.length}` }}</strong>
          <span>线性功率单位</span><strong>{{ result.units }}</strong>
          <span>显示功率单位</span><strong>{{ result.power_db_units ?? '—' }}</strong>
          <span>质量窗</span><strong>{{ result.quality?.clean_windows ?? '—' }}/{{ result.quality?.total_windows ?? '—' }} clean，坏窗 {{ result.quality?.bad_windows ?? '—' }}</strong>
          <template v-if="result.custom_band">
            <span>自定义频段</span><strong>{{ result.custom_band.low_hz }}–{{ result.custom_band.high_hz }} Hz</strong>
            <span>趋势计算</span><strong>后端线性 PSD 梯形积分（边界线性插值）</strong>
            <span>趋势单位</span><strong>{{ result.custom_band.unit }}</strong>
            <span>频率分辨率</span><strong>{{ result.custom_band.frequency_resolution_hz ?? '—' }} Hz</strong>
            <span>参与频点</span><strong>{{ result.custom_band.frequency_points_hz.join(' / ') }} Hz</strong>
            <span>自定义趋势版本</span><strong>{{ result.custom_band.algorithm_version }}</strong>
          </template>
        </div>
        <h3>单窗口调试数据</h3>
        <div class="algorithm-debug-controls"><label>时间中心 <select v-model.number="selectedCenter"><option v-for="center in result.times_s" :key="center" :value="center">{{ center.toFixed(3) }} s</option></select></label><span>窗口范围：{{ windowInfo.start_s.toFixed(3) }}–{{ windowInfo.end_s.toFixed(3) }} s</span><span>频率点：{{ result.frequencies_hz.length }}</span></div>
        <div class="algorithm-psd-table" role="table" aria-label="时频图单窗口线性 PSD"><div class="algorithm-channel-head"><span>频率 Hz</span><span>线性 PSD（{{ result.power_linear_units ?? result.units }}）</span></div><div v-for="(frequency, index) in result.frequencies_hz" :key="frequency" class="algorithm-channel-row"><span>{{ frequency.toFixed(2) }}</span><code>{{ spectrogramPsd[index]?.toExponential(10) ?? 'NaN' }}</code></div></div>
        <h3>与静态 PSD 单窗口逐点比对</h3>
        <p class="algorithm-meta">spectrogram row @ {{ selectedCenter.toFixed(3) }} s vs static PSD @ {{ windowInfo.start_s.toFixed(3) }}–{{ windowInfo.end_s.toFixed(3) }} s。静态 PSD 由后端单独请求，前端只做误差展示。</p>
        <p v-if="comparisonLoading" class="algorithm-meta">正在读取静态单窗口 PSD…</p><p v-else-if="comparisonError" class="spectrum-error">{{ comparisonError }}</p><div v-else-if="comparison" class="algorithm-comparison"><span>最大绝对误差：{{ comparison.maxAbs.toExponential(6) }} {{ result.power_linear_units ?? result.units }}</span><span>最大相对误差：{{ (comparison.maxRel * 100).toExponential(6) }}%</span><strong :class="comparison.consistent ? 'comparison-pass' : 'comparison-fail'">{{ comparison.consistent ? 'PASS · 逐点一致（浮点容差内）' : 'FAIL · 存在超出浮点容差的差异' }}</strong></div><p v-else class="algorithm-meta">暂无可比对的静态单窗口结果。</p>
        <template v-if="result.quality?.bad_windows">
          <h3>坏窗明细</h3>
          <div class="algorithm-channel-table"><div class="algorithm-channel-head"><span>窗口</span><span>中心</span><span>原因</span></div><div v-for="item in result.quality.windows.filter((window) => window.status === 'bad')" :key="`${item.start_s}-${item.end_s}`" class="algorithm-channel-row"><span>{{ item.start_s.toFixed(3) }}–{{ item.end_s.toFixed(3) }} s</span><span>{{ item.center_s.toFixed(3) }} s</span><span>{{ item.reason ?? 'unknown' }}</span></div></div>
        </template>
      </div>
      <footer class="channel-dialog-footer"><button type="button" @click="emit('close')">关闭</button></footer>
    </section>
  </div>
</template>
