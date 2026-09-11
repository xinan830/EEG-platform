<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { saveMapping } from '../api/recordings'
import type { ChannelMapping, Recording } from '../types/recording'

const props = defineProps<{ recording: Recording }>()
const emit = defineEmits<{ saved: [recording: Recording] }>()
const busy = ref(false)
const error = ref('')
const mapping = reactive<ChannelMapping>({ fz: '', pz: '', oz: '', f3: null, f4: null })
const channels = computed(() => props.recording.channels)

watch(() => props.recording, (recording) => {
  const current = recording.mapping
  mapping.fz = current?.fz ?? channels.value.find((name) => name.toUpperCase() === 'FZ') ?? ''
  mapping.pz = current?.pz ?? channels.value.find((name) => name.toUpperCase() === 'PZ') ?? ''
  mapping.oz = current?.oz ?? channels.value.find((name) => name.toUpperCase() === 'OZ') ?? ''
  mapping.f3 = current?.f3 ?? channels.value.find((name) => name.toUpperCase() === 'F3') ?? null
  mapping.f4 = current?.f4 ?? channels.value.find((name) => name.toUpperCase() === 'F4') ?? null
}, { immediate: true })

async function submit() {
  busy.value = true
  error.value = ''
  try {
    emit('saved', await saveMapping(props.recording.id, { ...mapping }))
  } catch (cause) {
    error.value = cause instanceof Error ? cause.message : '保存映射失败'
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <section class="panel">
    <p class="eyebrow">第二步</p>
    <h2>确认分析通道</h2>
    <p>Fz、Pz、Oz 为必填。F3 与 F4 同时选择时才计算额叶平衡（FAA）。</p>
    <div class="mapping-grid">
      <label v-for="key in ['fz', 'pz', 'oz', 'f3', 'f4']" :key="key">
        {{ key.toUpperCase() }}<span v-if="['fz','pz','oz'].includes(key)"> *</span>
        <select v-model="mapping[key as keyof ChannelMapping]">
          <option :value="null">未选择</option>
          <option v-for="channel in channels" :key="channel" :value="channel">{{ channel }}</option>
        </select>
      </label>
    </div>
    <button class="primary" :disabled="busy" @click="submit">{{ busy ? '正在保存…' : '保存映射并分析' }}</button>
    <p v-if="error" class="error">{{ error }}</p>
  </section>
</template>
