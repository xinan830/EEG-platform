<script setup lang="ts">
import { computed, reactive, ref, watch } from 'vue'
import { saveMapping } from '../api/recordings'
import type { ChannelMapping, Recording } from '../types/recording'

const props = defineProps<{ recording: Recording }>()
const emit = defineEmits<{ saved: [recording: Recording]; close: [] }>()
const busy = ref(false)
const error = ref('')
const mapping = reactive<ChannelMapping>({ fz: '', pz: '', oz: '', f3: null, f4: null })
const channels = computed(() => props.recording.channels)
const ozSuggestion = computed(() => !props.recording.mapping && !channels.value.some((name) => name.trim().toUpperCase() === 'OZ')
  ? channels.value.find((name) => name.trim().toUpperCase() === 'O2') ?? null
  : null)

watch(() => props.recording, (recording) => {
  const current = recording.mapping
  mapping.fz = current?.fz ?? channels.value.find((name) => name.toUpperCase() === 'FZ') ?? ''
  mapping.pz = current?.pz ?? channels.value.find((name) => name.toUpperCase() === 'PZ') ?? ''
  mapping.oz = current?.oz ?? channels.value.find((name) => name.toUpperCase() === 'OZ') ?? ozSuggestion.value ?? ''
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
  <section class="channel-mapping-dialog" role="dialog" aria-modal="true" aria-labelledby="channel-mapping-title" @click.stop>
    <header class="dialog-titlebar"><span class="app-glyph">◈</span><strong id="channel-mapping-title">通道映射</strong><button type="button" class="dialog-close" aria-label="关闭通道映射" @click="emit('close')">×</button></header>
    <div class="channel-mapping-body">
      <p>阅图和基础频谱可直接使用原始通道。这里只保存需要空间角色的算法映射。</p>
      <p v-if="recording.mapping" class="channel-mapping-status">当前已保存：Fz → {{ recording.mapping.fz }}，Pz → {{ recording.mapping.pz }}，Oz → {{ recording.mapping.oz }}</p>
      <p v-else-if="ozSuggestion" class="channel-mapping-suggestion">未找到名称为 Oz 的通道；已建议将 {{ ozSuggestion }} 用作 Oz。请确认后保存。</p>
      <p v-else class="channel-mapping-status">未保存空间角色映射。Fz、Pz、Oz 为官方 Theta/Beta 的必填角色。</p>
      <div class="mapping-grid">
        <label v-for="key in ['fz', 'pz', 'oz', 'f3', 'f4']" :key="key">
          {{ key.toUpperCase() }}<span v-if="['fz','pz','oz'].includes(key)"> *</span>
          <select v-model="mapping[key as keyof ChannelMapping]">
            <option :value="['fz', 'pz', 'oz'].includes(key) ? '' : null">未选择</option>
            <option v-for="channel in channels" :key="channel" :value="channel">{{ channel }}</option>
          </select>
        </label>
      </div>
    </div>
    <footer class="channel-dialog-footer"><button type="button" @click="emit('close')">取消</button><button type="button" class="channel-confirm" :disabled="busy || !mapping.fz || !mapping.pz || !mapping.oz" @click="submit">{{ busy ? '正在保存…' : '确认并保存映射' }}</button></footer>
    <p v-if="error" class="error channel-mapping-error">{{ error }}</p>
  </section>
</template>
