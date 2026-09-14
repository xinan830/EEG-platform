<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{
  recording: boolean
  loading: boolean
  playing: boolean
  positionS: number
  windowStartS: number
  screenDurationS: number
  totalDurationS?: number
  sfreq?: number
}>()

const emit = defineEmits<{ open: []; channels: []; algorithms: []; results: []; toggle: []; replay: []; previousScreen: []; nextScreen: [] }>()
const previousDisabled = computed(() => props.loading || props.windowStartS <= 0)
const nextDisabled = computed(() => props.loading || props.totalDurationS === undefined || props.windowStartS >= Math.max(0, props.totalDurationS - props.screenDurationS))
</script>

<template>
  <div class="viewer-toolbar">
    <button class="open-file" @click="emit('open')">{{ recording ? '打开其他文件' : '选择 BDF/EDF 文件' }}</button>
    <template v-if="recording">
      <button :disabled="loading" @click="emit('channels')">通道设置</button>
      <button :disabled="loading" @click="emit('algorithms')">算法定义</button>
      <button :disabled="loading" @click="emit('results')">结果工作台</button>
      <button class="screen-nav-button" :disabled="previousDisabled" title="上一屏" aria-label="上一屏" @click="emit('previousScreen')">◀ 上一屏</button>
      <button class="playback-button" :disabled="loading" @click="emit('toggle')">{{ playing ? '暂停' : '播放' }}</button>
      <button class="screen-nav-button" :disabled="nextDisabled" title="下一屏" aria-label="下一屏" @click="emit('nextScreen')">下一屏 ▶</button>
      <button class="replay-button" :disabled="loading" @click="emit('replay')">重播</button>
      <span class="file-summary">{{ positionS.toFixed(1) }} / {{ totalDurationS ?? '--' }} s　采样率：{{ sfreq ?? '--' }} Hz</span>
    </template>
    <span v-else class="file-summary">导入 BDF 或 EDF 文件后即可查看原始波形</span>
  </div>
</template>
