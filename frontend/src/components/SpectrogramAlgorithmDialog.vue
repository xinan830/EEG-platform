<script setup lang="ts">
import type { SpectrogramResponse } from '../types/spectrogram'

const props = defineProps<{ result: SpectrogramResponse; channel: string }>()
const emit = defineEmits<{ close: [] }>()
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
        <template v-if="result.quality?.bad_windows">
          <h3>坏窗明细</h3>
          <div class="algorithm-channel-table"><div class="algorithm-channel-head"><span>窗口</span><span>中心</span><span>原因</span></div><div v-for="item in result.quality.windows.filter((window) => window.status === 'bad')" :key="`${item.start_s}-${item.end_s}`" class="algorithm-channel-row"><span>{{ item.start_s.toFixed(3) }}–{{ item.end_s.toFixed(3) }} s</span><span>{{ item.center_s.toFixed(3) }} s</span><span>{{ item.reason ?? 'unknown' }}</span></div></div>
        </template>
      </div>
      <footer class="channel-dialog-footer"><button type="button" @click="emit('close')">关闭</button></footer>
    </section>
  </div>
</template>
