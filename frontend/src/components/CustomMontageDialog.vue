<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import type { CustomMontageRow, CustomMontageTerm } from '../api/recordings'

const props = defineProps<{ channels: string[]; rows: CustomMontageRow[] }>()
const emit = defineEmits<{ cancel: []; apply: [rows: CustomMontageRow[]] }>()
function defaultTerms(): CustomMontageTerm[] { return [{ channel: props.channels[0] ?? '', weight: 1 }, { channel: props.channels[1] ?? '', weight: -1 }] }
function newRow(): CustomMontageRow { return { name: '', terms: defaultTerms() } }
const draft = ref<CustomMontageRow[]>(props.rows.length ? props.rows.map((row) => ({ name: row.name, terms: row.terms.map((term) => ({ ...term })) })) : [newRow()])
const validationError = computed(() => {
  if (!draft.value.length) return '至少需要一条自定义导联。'
  const names = new Set<string>()
  for (const [index, row] of draft.value.entries()) {
    const name = row.name.trim(); if (!name) return `第 ${index + 1} 条导联需要填写显示名称。`
    if (names.has(name.toLocaleLowerCase())) return `导联名称不能重复：${name}`
    if (!row.terms.length) return `导联 ${name} 至少需要一个计算项。`
    const channels = new Set<string>()
    for (const term of row.terms) {
      if (!term.channel) return `导联 ${name} 存在未选择通道的计算项。`
      if (!Number.isFinite(Number(term.weight)) || Number(term.weight) === 0) return `导联 ${name} 的权重必须是非零数字。`
      if (channels.has(term.channel)) return `导联 ${name} 不能重复使用通道 ${term.channel}。`
      channels.add(term.channel)
    }
    names.add(name.toLocaleLowerCase())
  }
  return ''
})
function addRow() { draft.value.push(newRow()) }
function removeRow(index: number) { draft.value.splice(index, 1) }
function addTerm(row: CustomMontageRow) { row.terms.push({ channel: props.channels[0] ?? '', weight: -1 }) }
function removeTerm(row: CustomMontageRow, index: number) { row.terms.splice(index, 1) }
function formula(row: CustomMontageRow) { return row.terms.map((term, index) => { const weight = Number(term.weight); const sign = weight < 0 ? '-' : index ? '+' : ''; const magnitude = Math.abs(weight); return `${sign} ${magnitude === 1 ? '' : `${magnitude}*`}${term.channel}`.trim() }).join(' ') }
function apply() { if (!validationError.value) emit('apply', draft.value.map((row) => ({ name: row.name.trim(), terms: row.terms.map((term) => ({ channel: term.channel, weight: Number(term.weight) })) }))) }
function closeOnEscape(event: KeyboardEvent) { if (event.key === 'Escape') emit('cancel') }
onMounted(() => window.addEventListener('keydown', closeOnEscape)); onBeforeUnmount(() => window.removeEventListener('keydown', closeOnEscape))
</script>

<template>
  <section class="custom-montage-dialog" role="dialog" aria-modal="true" aria-labelledby="custom-montage-title" @click.stop>
    <header class="dialog-titlebar"><span class="app-glyph">▣</span><strong id="custom-montage-title">自定义 Montage</strong><button type="button" class="dialog-close" aria-label="关闭自定义 Montage" @click="emit('cancel')">×</button></header>
    <div class="custom-montage-body"><p class="custom-montage-help">用“通道 × 权重”定义线性导联；双极导联使用 +1 和 -1。</p>
      <section v-for="(row, rowIndex) in draft" :key="rowIndex" class="custom-montage-channel">
        <div class="custom-montage-name"><label>显示名称 <input v-model="row.name" maxlength="64" :aria-label="`第 ${rowIndex + 1} 条导联显示名称`"></label><code>{{ formula(row) || '尚未定义公式' }}</code><button type="button" class="montage-remove" :aria-label="`删除第 ${rowIndex + 1} 条导联`" title="删除导联" @click="removeRow(rowIndex)">×</button></div>
        <div v-for="(term, termIndex) in row.terms" :key="termIndex" class="custom-montage-term"><label>通道 <select v-model="term.channel"><option v-for="channel in channels" :key="channel" :value="channel">{{ channel }}</option></select></label><label>权重 <input v-model.number="term.weight" type="number" step="0.1"></label><button type="button" class="montage-term-remove" :aria-label="`删除第 ${termIndex + 1} 个计算项`" title="删除计算项" @click="removeTerm(row, termIndex)">×</button></div>
        <button type="button" class="montage-add-term" :disabled="row.terms.length >= 32" @click="addTerm(row)">+ 计算项</button>
      </section>
      <button type="button" class="montage-add" :disabled="draft.length >= 32" @click="addRow">+ 新增显示导联</button><p v-if="validationError" class="channel-selection-error">{{ validationError }}</p>
    </div>
    <footer class="channel-dialog-footer"><button type="button" @click="emit('cancel')">取消</button><button type="button" class="channel-confirm" :disabled="Boolean(validationError)" @click="apply">应用并从头显示</button></footer>
  </section>
</template>
