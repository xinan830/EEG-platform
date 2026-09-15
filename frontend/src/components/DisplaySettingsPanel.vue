<script setup lang="ts">
import type { DisplaySettings } from '../utils/displaySettings'

const props = defineProps<{
  settings: DisplaySettings
  preset: 'general' | 'original' | 'custom'
  channelNames: string[]
}>()

const emit = defineEmits<{
  change: [kind: 'timebase' | 'sensitivity' | 'filter' | 'baseline' | 'reference' | 'preset', value?: number | string | boolean | null]
  reset: []
  'algorithm-check': []
}>()
</script>

<template>
  <div class="display-settings" aria-label="波形显示设置">
    <div class="display-settings-heading"><strong>波形阅图设置</strong><span>仅影响当前波形显示，不改变频谱或时频分析算法</span></div>
    <label>时间基
      <select :value="settings.timebaseSeconds" @change="emit('change', 'timebase', Number(($event.target as HTMLSelectElement).value))">
        <option :value="5">5 秒/屏</option><option :value="10">10 秒/屏</option><option :value="15">15 秒/屏</option><option :value="30">30 秒/屏</option>
      </select>
    </label>
    <label>灵敏度：
      <select :value="settings.sensitivityUvPerMm" @change="emit('change', 'sensitivity', Number(($event.target as HTMLSelectElement).value))">
        <option :value="2">2 µV/mm</option><option :value="3">3 µV/mm</option><option :value="5">5 µV/mm</option><option :value="7">7 µV/mm</option><option :value="10">10 µV/mm</option><option :value="15">15 µV/mm</option><option :value="20">20 µV/mm</option>
      </select>
    </label>
    <label>低切（高通）
      <select :value="settings.lowCutHz" @change="emit('change', 'filter', Number(($event.target as HTMLSelectElement).value))">
        <option :value="0.1">0.1 Hz</option><option :value="0.3">0.3 Hz</option><option :value="0.5">0.5 Hz</option><option :value="1">1 Hz</option><option :value="2">2 Hz</option>
      </select>
    </label>
    <label>高切（低通）
      <select :value="settings.highCutHz" @change="emit('change', 'filter', Number(($event.target as HTMLSelectElement).value))">
        <option :value="15">15 Hz</option><option :value="30">30 Hz</option><option :value="35">35 Hz</option><option :value="70">70 Hz</option><option :value="100">100 Hz</option>
      </select>
    </label>
    <label>陷波
      <select :value="settings.notchHz ?? ''" @change="emit('change', 'filter', ($event.target as HTMLSelectElement).value === '' ? null : Number(($event.target as HTMLSelectElement).value))">
        <option value="">关闭</option><option :value="50">50 Hz</option><option :value="60">60 Hz</option>
      </select>
    </label>
    <label class="baseline-toggle"><input type="checkbox" :checked="settings.baselineStabilization" @change="emit('change', 'baseline', ($event.target as HTMLInputElement).checked)">基线稳定</label>
    <label>参考
      <select :value="settings.reference" @change="emit('change', 'reference', ($event.target as HTMLSelectElement).value)">
        <option value="original">原始记录（不重参考）</option><option value="average">平均参考</option>
        <option v-for="name in channelNames" :key="`ref-${name}`" :value="name">{{ name }} 参考</option>
      </select>
    </label>
    <button class="settings-reset" @click="emit('reset')">恢复默认</button>
    <button class="settings-reset" @click="emit('algorithm-check')">算法检验</button>
    <span class="settings-status">波形显示范围：{{ settings.timebaseSeconds }} 秒/屏 · {{ settings.sensitivityUvPerMm }} µV/mm · {{ settings.lowCutHz }}–{{ settings.highCutHz }} Hz · 陷波：{{ settings.notchHz ? `${settings.notchHz} Hz` : '关闭' }} · 基线稳定：{{ settings.baselineStabilization ? '开启' : '关闭' }}</span>
  </div>
</template>
