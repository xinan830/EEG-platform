<script setup lang="ts">
import type { MontageOption } from '../api/recordings'

const props = defineProps<{ modelValue: string; options: MontageOption[]; channels: string[]; excludedChannels: string[] }>()
const emit = defineEmits<{ change: [value: string]; 'edit-custom': []; 'update-excluded': [channels: string[]] }>()

function choose(event: Event) {
  const value = (event.target as HTMLSelectElement).value
  const option = props.options.find((item) => item.id === value)
  if (!option?.available) return
  if (value === 'custom_bipolar') emit('edit-custom')
  else emit('change', value)
}

function toggleExcluded(channel: string) {
  const next = props.excludedChannels.includes(channel)
    ? props.excludedChannels.filter((item) => item !== channel)
    : [...props.excludedChannels, channel]
  emit('update-excluded', next)
}
</script>

<template>
  <label class="montage-selector">导联
    <select :value="modelValue" aria-label="导联方案" @change="choose">
      <option v-for="option in options" :key="option.id" :value="option.id" :disabled="!option.available">
        {{ option.label }}{{ option.available ? '' : '（缺少通道）' }}
      </option>
    </select>
  </label>
  <button v-if="modelValue === 'custom_bipolar'" type="button" class="settings-reset" @click="emit('edit-custom')">编辑导联</button>
  <details v-if="modelValue === 'average'" class="average-reference-options">
    <summary>{{ excludedChannels.length ? `配置平均参考（自定义排除 ${excludedChannels.length} 个）` : 'AVG-All（全部有效通道）' }}</summary>
    <div class="average-channel-grid">
      <label v-for="channel in channels" :key="channel"><input type="checkbox" :checked="excludedChannels.includes(channel)" @change="toggleExcluded(channel)">{{ channel }}</label>
    </div>
  </details>
</template>
