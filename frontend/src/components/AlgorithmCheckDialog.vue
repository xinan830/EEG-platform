<script setup lang="ts">
import type { AlgorithmCheck } from '../api/recordings'

defineProps<{ loading: boolean; seconds: number; result: AlgorithmCheck | null }>()
const emit = defineEmits<{ close: []; inspect: [seconds: number] }>()

function inspect(event: Event) {
  emit('inspect', Number((event.target as HTMLInputElement).value))
}
</script>

<template>
  <div class="modal-layer algorithm-modal-layer" @click.self="emit('close')">
    <section class="algorithm-dialog" role="dialog" aria-modal="true" aria-labelledby="algorithm-dialog-title" @click.stop>
      <header class="dialog-titlebar"><strong id="algorithm-dialog-title">算法检验</strong><button type="button" class="dialog-close" aria-label="关闭算法检验" @click="emit('close')">×</button></header>
      <div class="algorithm-body">
        <label>文件绝对时间（秒）<input type="number" min="0" step="0.001" :value="seconds" @change="inspect"></label>
        <button type="button" :disabled="loading" @click="emit('inspect', seconds)">{{ loading ? '读取中…' : '读取该点' }}</button>
        <p v-if="result" class="algorithm-meta">{{ result.time_s.toFixed(3) }} s · 导联：{{ result.montage_label }}{{ result.montage === 'average' ? ` · 平均参考 ${result.average_participants} 通道` : '' }} · {{ result.settings.low_cut_hz }}–{{ result.settings.high_cut_hz }} Hz · 陷波 {{ result.settings.notch_hz ?? '关闭' }}</p>
        <div v-if="result" class="algorithm-table">
          <div v-for="(value, name) in result.values_uv" :key="name" class="algorithm-row"><strong>{{ name }}</strong><code>{{ result.formulas[name] }}</code><span>{{ value == null ? '—' : `${value.toFixed(4)} µV` }}</span></div>
        </div>
        <p v-else class="algorithm-empty">输入时间后读取一个处理后的导联采样点。</p>
      </div>
      <footer class="channel-dialog-footer"><button type="button" @click="emit('close')">关闭</button></footer>
    </section>
  </div>
</template>
