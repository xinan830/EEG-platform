<script setup lang="ts">
import { ref, watch } from 'vue'
import type { EventMarker } from '../api/recordings'

const props = defineProps<{ markers: EventMarker[]; currentTimeS: number; totalDurationS?: number }>()
const emit = defineEmits<{ create: [timeS: number, label: string, durationS: number | null]; jump: [timeS: number]; remove: [markerId: string] }>()
const timeS = ref(0)
const label = ref('')
const durationS = ref<number | null>(null)

watch(() => props.currentTimeS, (value) => { timeS.value = Number(value.toFixed(3)) }, { immediate: true })
function submit() {
  if (!label.value.trim()) return
  emit('create', Math.max(0, timeS.value), label.value, durationS.value)
  label.value = ''
  durationS.value = null
}
</script>

<template>
  <div class="event-marker-controls" aria-label="人工事件标记">
    <strong>事件标记</strong>
    <input v-model.number="timeS" type="number" min="0" step="0.001" aria-label="事件绝对时间" placeholder="时间（秒）">
    <input v-model="label" maxlength="120" aria-label="事件标签" placeholder="事件标签" @keyup.enter="submit">
    <input v-model.number="durationS" type="number" min="0" step="0.1" aria-label="事件持续时间" placeholder="持续秒数">
    <button type="button" @click="submit">添加事件</button>
    <span v-if="!markers.length" class="event-marker-empty">暂无人工事件</span>
    <span v-for="marker in markers" :key="marker.id" class="event-marker-chip">
      <button type="button" class="event-jump" @click="emit('jump', marker.time_s)">{{ marker.label }} · {{ marker.time_s.toFixed(3) }}s</button>
      <button type="button" class="event-remove" aria-label="删除事件" @click="emit('remove', marker.id)">×</button>
    </span>
  </div>
</template>
