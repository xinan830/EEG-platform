<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'

const props = defineProps<{
  channels: string[]
  selectedChannels: string[]
  onCancel: () => void
  onConfirm: (channels: string[]) => void
}>()

const selected = ref(new Set(props.selectedChannels))
const selectedCount = computed(() => props.channels.filter((name) => selected.value.has(name)).length)

function toggle(channel: string) {
  const next = new Set(selected.value)
  if (next.has(channel)) next.delete(channel)
  else next.add(channel)
  selected.value = next
}

function selectAll() {
  selected.value = new Set(props.channels)
}

function clearSelection() {
  selected.value = new Set()
}

function confirm() {
  // 按文件通道顺序返回，保证每次选择后的波形轨道与数据列稳定对应。
  props.onConfirm(props.channels.filter((name) => selected.value.has(name)))
}

function closeOnEscape(event: KeyboardEvent) {
  if (event.key === 'Escape') props.onCancel()
}

onMounted(() => window.addEventListener('keydown', closeOnEscape))
onBeforeUnmount(() => window.removeEventListener('keydown', closeOnEscape))
</script>

<template>
  <section class="channel-dialog" role="dialog" aria-modal="true" aria-labelledby="channel-dialog-title" @click.stop>
    <header class="dialog-titlebar">
      <span class="app-glyph">▣</span>
      <strong id="channel-dialog-title">通道设置</strong>
      <button type="button" class="dialog-close" aria-label="关闭通道设置" @click.stop="onCancel">×</button>
    </header>
    <div class="channel-dialog-body">
      <div class="channel-dialog-actions">
        <span>已选择 {{ selectedCount }} / {{ channels.length }} 个通道</span>
        <button type="button" @click="selectAll">全选</button>
        <button type="button" @click="clearSelection">清空</button>
      </div>
      <div class="channel-grid" aria-label="可显示通道">
        <label v-for="channel in channels" :key="channel" class="channel-option">
          <input type="checkbox" :checked="selected.has(channel)" @change="toggle(channel)">
          <span>{{ channel }}</span>
        </label>
      </div>
      <p v-if="!selectedCount" class="channel-selection-error">至少选择一个通道。</p>
    </div>
    <footer class="channel-dialog-footer">
      <button type="button" @click="onCancel">取消</button>
      <button type="button" class="channel-confirm" :disabled="!selectedCount" @click="confirm">应用并从头显示</button>
    </footer>
  </section>
</template>
