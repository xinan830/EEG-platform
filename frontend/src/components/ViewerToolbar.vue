<script setup lang="ts">
defineProps<{
  recording: boolean
  loading: boolean
  playing: boolean
  positionS: number
  totalDurationS?: number
  sfreq?: number
}>()

const emit = defineEmits<{ open: []; channels: []; toggle: []; replay: [] }>()
</script>

<template>
  <div class="viewer-toolbar">
    <button class="open-file" @click="emit('open')">{{ recording ? '打开其他文件' : '选择 BDF/EDF 文件' }}</button>
    <template v-if="recording">
      <button :disabled="loading" @click="emit('channels')">通道设置</button>
      <button class="playback-button" :disabled="loading" @click="emit('toggle')">{{ playing ? '暂停' : '播放' }}</button>
      <button class="replay-button" :disabled="loading" @click="emit('replay')">重播</button>
      <span class="file-summary">{{ positionS.toFixed(1) }} / {{ totalDurationS ?? '--' }} s　采样率：{{ sfreq ?? '--' }} Hz</span>
    </template>
    <span v-else class="file-summary">导入 BDF 或 EDF 文件后即可查看原始波形</span>
  </div>
</template>
