<script setup lang="ts">
import type { DynamicWindowS } from '../utils/dynamicMetricPlayback'
import { DYNAMIC_WINDOW_OPTIONS, MIN_DYNAMIC_METRIC_WINDOW_S } from '../utils/dynamicMetricPlayback'

type Settings = { channel: string; mode: 'static' | 'dynamic'; startS: number; endS: number; windowS: DynamicWindowS; displayRangeS: number }
const props = defineProps<{ id: string; settings: Settings; channels: string[]; running: boolean }>()
const emit = defineEmits<{ update: [patch: Partial<Settings>]; currentRange: []; runStatic: [] }>()
const DISPLAY_WINDOWS = [10, 20, 30, 60] as const
</script>
<template>
  <div class="algorithm-config-card" :data-testid="`algorithm-config-${id}`">
    <label>通道<select :value="settings.channel" @change="emit('update', { channel: ($event.target as HTMLSelectElement).value })"><option v-for="name in channels" :key="name" :value="name">{{ name }}</option></select></label>
    <div class="algorithm-display-segment"><button :class="{ active: settings.mode === 'static' }" @click="emit('update', { mode: 'static' })">静态</button><button :class="{ active: settings.mode === 'dynamic' }" @click="emit('update', { mode: 'dynamic' })">动态</button></div>
    <template v-if="settings.mode === 'static'"><label>开始 <input :value="settings.startS" type="number" min="0" step="0.001" @input="emit('update', { startS: Number(($event.target as HTMLInputElement).value) })" /> s</label><label>结束 <input :value="settings.endS" type="number" min="0" step="0.001" @input="emit('update', { endS: Number(($event.target as HTMLInputElement).value) })" /> s</label><button @click="emit('currentRange')">使用当前范围</button><button class="primary-action" :disabled="running" @click="emit('runStatic')">{{ running ? '计算中…' : '计算此区间' }}</button></template>
    <template v-else><label>分析窗口<select :value="settings.windowS" @change="emit('update', { windowS: Number(($event.target as HTMLSelectElement).value) as DynamicWindowS })"><option v-for="seconds in DYNAMIC_WINDOW_OPTIONS" :key="seconds" :value="seconds">最近 {{ seconds }} s</option></select></label><label>结果展示<select :value="settings.displayRangeS" @change="emit('update', { displayRangeS: Number(($event.target as HTMLSelectElement).value) })"><option v-for="seconds in DISPLAY_WINDOWS" :key="seconds" :value="seconds">最近 {{ seconds }} s</option></select></label><small>每 1 s 更新；{{ MIN_DYNAMIC_METRIC_WINDOW_S }} s 起可出现预热值。</small></template>
  </div>
</template>
