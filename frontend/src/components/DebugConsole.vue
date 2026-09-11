<script setup lang="ts">
defineProps<{
  seconds: number
  loading: boolean
  sample: { time: number; values: Record<string, number> } | null
  renderStats?: string
  transportStats?: string
}>()

const emit = defineEmits<{ updateSeconds: [value: number]; inspect: [] }>()
</script>

<template>
  <div class="debug-console" aria-label="波形调试台">
    <strong>调试台</strong>
    <label>文件绝对时间（秒）
      <input :value="seconds" type="number" min="0" step="0.1" @input="emit('updateSeconds', Number(($event.target as HTMLInputElement).value))" @keyup.enter="emit('inspect')" />
    </label>
    <button :disabled="loading" @click="emit('inspect')">{{ loading ? '读取中…' : '读取该点 µV' }}</button>
    <span v-if="sample" class="debug-time">文件绝对时间 {{ sample.time.toFixed(3) }} s</span>
    <span v-if="renderStats" class="debug-render-stats">{{ renderStats }}</span>
    <span v-if="transportStats" class="debug-transport-stats">{{ transportStats }}</span>
    <div v-if="sample" class="debug-values">
      <span v-for="(value, name) in sample.values" :key="name"><b>{{ name }}</b> {{ Number.isFinite(value) ? value.toFixed(3) : 'NaN' }} µV</span>
    </div>
  </div>
</template>
