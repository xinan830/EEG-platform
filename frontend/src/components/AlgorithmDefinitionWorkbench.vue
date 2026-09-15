<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { ApiRequestError } from '../api/client'
import { cloneDefinition, compareDefinitionVersions, createDefinition, createDefinitionPreview, createDefinitionVersion, deleteDefinition as deleteDefinitionApi, getDefinitionCapabilities, listDefinitions, listDefinitionVersions, publishDefinitionVersion, validateDefinition } from '../api/algorithmDefinitions'
import { useDefinitionDraft } from '../composables/useDefinitionDraft'
import { DEFAULT_DRAFT, type AlgorithmDefinition, type AlgorithmDefinitionVersion, type DefinitionCapabilities, type Unit } from '../types/algorithmDefinition'
import type { Recording } from '../types/recording'
import { createLatestRequestGuard } from '../utils/latestRequest'
import { algorithmLabel } from '../utils/algorithmLabels'

const props = defineProps<{ recording: Recording; startS: number; endS: number }>()
const emit = defineEmits<{ close: [] }>()
const definitions = ref<AlgorithmDefinition[]>([])
const capabilities = ref<DefinitionCapabilities | null>(null)
const selectedId = ref<string | null>(null)
const versions = ref<AlgorithmDefinitionVersion[]>([])
const compareLeft = ref('')
const compareRight = ref('')
const compareResult = ref<{ same_digest: boolean; graph_changed: boolean; parameter_schema_changed: boolean } | null>(null)
const name = ref('研究算法草稿')
const description = ref('')
const loading = ref(false)
const actionMessage = ref('')
const validationError = ref('')
const formError = ref('')
const previewInputs = ref<Record<string, { value: number; unit: Unit }>>({ value: { value: 1, unit: 'ratio' } })
const previewRun = ref<Awaited<ReturnType<typeof createDefinitionPreview>> | null>(null)
const developerMode = ref(false)
const draftState = useDefinitionDraft(DEFAULT_DRAFT)
const selectionRequest = createLatestRequestGuard()
const selected = computed(() => definitions.value.find((item) => item.definition_id === selectedId.value) ?? null)
const activeVersion = computed(() => versions.value.find((item) => item.semver === draftState.draft.value.semver) ?? versions.value[0] ?? null)
const isCompositeOfficial = computed(() => selected.value?.owner === 'platform-official' && activeVersion.value?.quality_rules.execution_kind === 'official_composite_shadow_only')
const selectedLabel = computed(() => algorithmLabel(selected.value))
const inputNames = computed(() => Object.keys(draftState.draft.value.inputs))
const isUserDefinition = computed(() => Boolean(selected.value && selected.value.owner !== 'platform-official'))
const userFeatureLabel = (key: string) => {
  const metadata = draftState.draft.value.inputs[key]
  if (typeof metadata !== 'object' || metadata === null) return key
  const feature = (metadata as { feature?: unknown }).feature
  const labels: Record<string, string> = { delta_power: 'Delta 功率', theta_power: 'Theta 功率', alpha_power: 'Alpha 功率', beta_power: 'Beta 功率', delta_rbp: 'Delta 相对功率', theta_rbp: 'Theta 相对功率', alpha_rbp: 'Alpha 相对功率', beta_rbp: 'Beta 相对功率' }
  return labels[String(feature)] ?? key
}
const userFormula = computed(() => {
  if (!isUserDefinition.value) return ''
  const nodes = draftState.draft.value.graph.nodes
  const calculation = nodes.find((node) => ['divide', 'add', 'subtract', 'multiply'].includes(node.type))
  if (!calculation) return ''
  const inputKeys = inputNames.value
  const leftKey = calculation.inputs.left?.replace('$input.', '') ?? inputKeys[0] ?? 'A'
  const rightKey = calculation.inputs.right?.replace('$input.', '') ?? inputKeys[1] ?? 'B'
  const symbol: Record<string, string> = { divide: '÷', add: '+', subtract: '−', multiply: '×' }
  return `${userFeatureLabel(leftKey)} ${symbol[calculation.type] ?? calculation.type} ${userFeatureLabel(rightKey)}`
})
const readablePurpose = computed(() => isUserDefinition.value ? (selected.value?.description || '由已选择的基础指标组合生成的研究指标。') : selectedLabel.value.purpose)
const readableSteps = computed(() => isUserDefinition.value ? [`输入 A：${userFeatureLabel(inputNames.value[0] ?? 'A')}（后端基础指标）`, `输入 B：${userFeatureLabel(inputNames.value[1] ?? 'B')}（后端基础指标）`, `按照“${userFormula.value || '用户设定的运算'}”进行计算`, '结果由后端按当前录制、分析区间和质量门生成'] : selectedLabel.value.steps)
const readableResult = computed(() => {
  if (!isUserDefinition.value) return selectedLabel.value.result
  const output = Object.values(draftState.draft.value.outputs)[0]
  const unit = typeof output === 'object' && output !== null ? String((output as { unit?: unknown }).unit ?? '以定义为准') : '以定义为准'
  return `输出名称：${typeof output === 'object' && output !== null ? String((output as { label?: unknown }).label ?? selected.value?.name ?? '研究指标') : selected.value?.name ?? '研究指标'}；单位：${unit}。这是研究指标，不是临床结论。`
})
const userOutput = computed(() => {
  const output = Object.values(draftState.draft.value.outputs)[0]
  if (typeof output !== 'object' || output === null) return { label: selected.value?.name ?? '研究指标', unit: '以定义为准' }
  return {
    label: String((output as { label?: unknown }).label ?? selected.value?.name ?? '研究指标'),
    unit: String((output as { unit?: unknown }).unit ?? '以定义为准'),
  }
})
function definitionSummary(item: AlgorithmDefinition): string {
  const label = algorithmLabel(item)
  if (item.owner === 'platform-official') return `官方算法 · ${label.abbreviation || label.name}`
  return item.definition_id === selectedId.value && userFormula.value ? `用户算法 · ${userFormula.value}` : '用户算法 · 已保存公式'
}

function displayError(cause: unknown) {
  if (cause instanceof ApiRequestError) return `${cause.code ?? 'REQUEST_FAILED'}: ${cause.message}`
  return cause instanceof Error ? cause.message : '操作失败'
}

function unitOf(metadata: unknown): Unit {
  const candidate = typeof metadata === 'object' && metadata !== null ? (metadata as { unit?: unknown }).unit : undefined
  return capabilities.value?.units.includes(candidate as Unit) ? candidate as Unit : 'ratio'
}

function syncPreviewInputs() {
  const next: Record<string, { value: number; unit: Unit }> = {}
  for (const key of inputNames.value) next[key] = previewInputs.value[key] ?? { value: 1, unit: unitOf(draftState.draft.value.inputs[key]) }
  previewInputs.value = next
}

function mutateDraft(change: () => void) {
  change()
  draftState.syncJson()
  syncPreviewInputs()
}

function newDraft() {
  selectedId.value = null
  versions.value = []
  name.value = '研究算法草稿'
  description.value = ''
  draftState.replace(DEFAULT_DRAFT)
  previewRun.value = null
  actionMessage.value = '已创建未保存草稿。'
  syncPreviewInputs()
}

async function loadDefinitions() {
  loading.value = true
  try {
    const [items, nextCapabilities] = await Promise.all([listDefinitions(), getDefinitionCapabilities()])
    definitions.value = items
    capabilities.value = nextCapabilities
    if (!selectedId.value && items.length) await selectDefinition(items[0].definition_id)
    syncPreviewInputs()
  } catch (cause) {
    validationError.value = displayError(cause)
  } finally { loading.value = false }
}

async function selectDefinition(id: string) {
  const requestId = selectionRequest.begin()
  selectedId.value = id
  loading.value = true
  validationError.value = ''
  compareResult.value = null
  actionMessage.value = ''
  previewRun.value = null
  try {
    const nextVersions = await listDefinitionVersions(id)
    if (!selectionRequest.isCurrent(requestId)) return
    versions.value = nextVersions
    const latest = nextVersions[0]
    const item = definitions.value.find((entry) => entry.definition_id === id)
    if (item) { const label = algorithmLabel(item); name.value = label.name; description.value = label.purpose }
    if (latest) draftState.replace(latest)
    compareLeft.value = latest?.semver ?? ''
    compareRight.value = versions.value[1]?.semver ?? latest?.semver ?? ''
    syncPreviewInputs()
  } catch (cause) {
    if (selectionRequest.isCurrent(requestId)) validationError.value = displayError(cause)
  } finally {
    if (selectionRequest.isCurrent(requestId)) loading.value = false
  }
}

async function validate() {
  if (!draftState.isJsonValid.value) return
  loading.value = true
  validationError.value = ''
  try {
    const result = await validateDefinition(draftState.draft.value)
    actionMessage.value = `后端校验通过 · 节点顺序：${result.node_order.join(' -> ')}`
    return true
  } catch (cause) {
    validationError.value = displayError(cause)
    return false
  } finally { loading.value = false }
}

async function saveVersion() {
  if (isCompositeOfficial.value || !(await validate())) return
  loading.value = true
  try {
    let definitionId = selectedId.value
    if (!definitionId) {
      const created = await createDefinition(name.value.trim() || '未命名研究算法', description.value)
      definitions.value = [created, ...definitions.value]
      definitionId = created.definition_id
      selectedId.value = definitionId
    }
    const version = await createDefinitionVersion(definitionId, draftState.draft.value)
    versions.value = [version, ...versions.value.filter((item) => item.semver !== version.semver)]
    actionMessage.value = `已保存 ${version.semver} 草稿版本。`
  } catch (cause) {
    validationError.value = displayError(cause)
  } finally { loading.value = false }
}

async function publish() {
  if (!selectedId.value || isCompositeOfficial.value || !(await validate())) return
  loading.value = true
  try {
    const version = await publishDefinitionVersion(selectedId.value, draftState.draft.value.semver)
    versions.value = versions.value.map((item) => item.semver === version.semver ? version : item)
    actionMessage.value = `已发布不可变版本 ${version.semver}。`
  } catch (cause) {
    validationError.value = displayError(cause)
  } finally { loading.value = false }
}

async function clone() {
  if (!selectedId.value) return
  loading.value = true
  try {
    const created = await cloneDefinition(selectedId.value, `${name.value} 副本`)
    definitions.value = [created, ...definitions.value]
    await selectDefinition(created.definition_id)
    actionMessage.value = '已创建独立副本，原定义保持不变。'
  } catch (cause) {
    validationError.value = displayError(cause)
  } finally { loading.value = false }
}

async function deleteSavedDefinition(item: AlgorithmDefinition) {
  if (item.owner === 'platform-official' || loading.value) return
  const confirmed = window.confirm(`删除“${algorithmLabel(item).name}”及其所有版本？\n\n历史分析结果会保留当时已保存的数值、参数与频谱证据，但不能再从历史结果打开该算法定义。未完成的分析任务将被取消。`)
  if (!confirmed) return
  loading.value = true
  validationError.value = ''
  actionMessage.value = ''
  try {
    await deleteDefinitionApi(item.definition_id)
    definitions.value = definitions.value.filter((entry) => entry.definition_id !== item.definition_id)
    if (selectedId.value === item.definition_id) {
      selectedId.value = null
      versions.value = []
      draftState.replace(DEFAULT_DRAFT)
      const next = definitions.value[0]
      if (next) await selectDefinition(next.definition_id)
    }
    actionMessage.value = `已删除“${algorithmLabel(item).name}”。`
  } catch (cause) {
    validationError.value = displayError(cause)
  } finally { loading.value = false }
}

async function compare() {
  if (!selectedId.value || !compareLeft.value || !compareRight.value) return
  loading.value = true
  try { compareResult.value = await compareDefinitionVersions(selectedId.value, compareLeft.value, compareRight.value) } catch (cause) { validationError.value = displayError(cause) } finally { loading.value = false }
}

async function preview() {
  if (isCompositeOfficial.value || !(await validate())) return
  loading.value = true
  try {
    previewRun.value = await createDefinitionPreview(props.recording.id, props.startS, props.endS, draftState.draft.value, previewInputs.value)
    actionMessage.value = `预览 Run ${previewRun.value.run_id.slice(0, 8)} 已保存，仅用于模拟。`
  } catch (cause) {
    validationError.value = displayError(cause)
  } finally { loading.value = false }
}

function addNode() {
  const fallback = capabilities.value?.nodes[0] ?? 'output'
  mutateDraft(() => draftState.draft.value.graph.nodes.push({ id: `node_${draftState.draft.value.graph.nodes.length + 1}`, type: fallback, inputs: {}, parameters: {} }))
}

function removeNode(index: number) {
  mutateDraft(() => {
    const [removed] = draftState.draft.value.graph.nodes.splice(index, 1)
    draftState.draft.value.graph.outputs = draftState.draft.value.graph.outputs.filter((item) => item !== removed?.id)
  })
}

function applyNodeJson(node: { inputs: Record<string, string>; parameters: Record<string, unknown> }, field: 'inputs' | 'parameters', value: string) {
  try {
    const parsed = JSON.parse(value)
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) throw new Error('必须是对象')
    node[field] = parsed as never
    formError.value = ''
    draftState.syncJson()
  } catch (cause) { formError.value = `节点 ${field}：${cause instanceof Error ? cause.message : 'JSON 无效'}` }
}

function applyQualityJson(value: string) {
  try {
    const parsed = JSON.parse(value)
    if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) throw new Error('必须是对象')
    draftState.draft.value.quality_rules = parsed as Record<string, unknown>
    formError.value = ''
    draftState.syncJson()
  } catch (cause) { formError.value = `质量规则：${cause instanceof Error ? cause.message : 'JSON 无效'}` }
}

function eventValue(event: Event): string {
  return (event.target as HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement).value
}

watch(() => draftState.draft.value.inputs, syncPreviewInputs, { deep: true })
onMounted(loadDefinitions)
</script>

<template>
  <div class="modal-layer definition-modal-layer" @click.self="emit('close')">
    <section class="definition-workbench" aria-label="算法定义工作台">
      <header class="dialog-titlebar"><span class="app-glyph">◫</span><strong>{{ developerMode ? '算法定义工作台 · 开发者详情' : '算法说明' }}</strong><span class="definition-range">预览范围 {{ startS.toFixed(3) }}-{{ endS.toFixed(3) }} s</span><button class="definition-mode" @click="developerMode = !developerMode">{{ developerMode ? '返回简洁版' : '开发者详情' }}</button><button class="dialog-close" title="关闭" aria-label="关闭" @click="emit('close')">×</button></header>
      <div class="definition-layout">
        <aside class="definition-sidebar">
          <template v-if="developerMode"><button @click="newDraft">新建草稿</button><button :disabled="!selectedId || loading" @click="clone">克隆</button></template>
          <p>算法库</p>
          <div v-for="item in definitions" :key="item.definition_id" class="definition-list-entry">
            <button class="definition-list-item" :class="{ selected: item.definition_id === selectedId }" @click="selectDefinition(item.definition_id)">
              <strong>{{ algorithmLabel(item).name }}</strong><small>{{ definitionSummary(item) }}</small>
            </button>
            <button v-if="item.owner !== 'platform-official'" class="definition-delete" :disabled="loading" :aria-label="`删除 ${algorithmLabel(item).name}`" title="删除此用户算法" @click.stop="deleteSavedDefinition(item)">删除</button>
          </div>
          <span v-if="!definitions.length && !loading" class="definition-muted">尚无保存的算法</span>
        </aside>
        <main class="definition-editor">
          <section v-if="!developerMode" class="algorithm-explainer">
            <template v-if="selected">
              <p class="algorithm-explainer-kicker">{{ selected.owner === 'platform-official' ? '官方算法说明' : '研究算法说明' }}</p>
              <h2>{{ selectedLabel.name }}<span v-if="selectedLabel.abbreviation"> · {{ selectedLabel.abbreviation }}</span></h2>
              <p class="algorithm-explainer-purpose">{{ readablePurpose }}</p>
              <section v-if="isUserDefinition" class="algorithm-formula-card">
                <h3>你保存的计算方式</h3>
                <div class="algorithm-formula-flow">
                  <span><small>输入 A</small>{{ userFeatureLabel(inputNames[0] ?? 'A') }}</span>
                  <b>{{ userFormula.includes('÷') ? '÷' : userFormula.includes('×') ? '×' : userFormula.includes('−') ? '−' : '+' }}</b>
                  <span><small>输入 B</small>{{ userFeatureLabel(inputNames[1] ?? 'B') }}</span>
                  <b>=</b>
                  <span><small>输出</small>{{ userOutput.label }}<em>{{ userOutput.unit }}</em></span>
                </div>
                <p>公式：{{ userFormula || '用户设定的研究计算' }}</p>
              </section>
              <section>
                <h3>{{ isUserDefinition ? '这个算法做什么' : '后端如何计算' }}</h3>
                <ol><li v-for="step in readableSteps" :key="step">{{ step }}</li></ol>
              </section>
              <section>
                <h3>结果如何理解</h3>
                <p>{{ readableResult }}</p>
              </section>
              <p class="algorithm-explainer-note">实际数值请在频谱分析、时频图或结果工作台查看。本页只解释已保存的算法定义，不在前端重新计算 EEG。</p>
            </template>
            <p v-else class="definition-muted">正在读取算法说明...</p>
          </section>
          <template v-else>
          <div class="definition-actions"><button :disabled="loading || !draftState.isJsonValid.value" @click="validate">校验</button><button :disabled="loading || isCompositeOfficial" @click="saveVersion">保存版本</button><button :disabled="loading || !selectedId || isCompositeOfficial" @click="publish">发布</button><button :disabled="loading || isCompositeOfficial" @click="preview">运行预览</button><span v-if="loading">处理中...</span></div>
          <p v-if="isCompositeOfficial" class="definition-warning">这是官方复合定义：可检查和克隆，但通用图执行器不会伪造其运行语义。</p>
          <p v-if="selected?.owner === 'platform-official'" class="definition-message">{{ selectedLabel.name }}（{{ selectedLabel.abbreviation }}）：{{ selectedLabel.purpose }}</p>
          <p v-if="actionMessage" class="definition-message">{{ actionMessage }}</p><p v-if="validationError || formError || draftState.jsonError.value" class="definition-error">{{ validationError || formError || draftState.jsonError.value }}</p>
          <div class="definition-grid">
            <label>名称<input v-model="name" :disabled="isCompositeOfficial" /></label><label>版本<input v-model="draftState.draft.value.semver" :disabled="isCompositeOfficial" @change="draftState.syncJson" /></label>
            <label class="wide">说明<textarea v-model="description" :disabled="isCompositeOfficial" /></label>
          </div>
          <section class="definition-section"><h3>输入与输出</h3><div class="definition-grid"><label>输入名称<input :value="inputNames[0] ?? ''" :disabled="isCompositeOfficial" @change="mutateDraft(() => { const old = inputNames[0]; const next = eventValue($event); if (old && next) { draftState.draft.value.inputs[next] = draftState.draft.value.inputs[old]; delete draftState.draft.value.inputs[old]; for (const node of draftState.draft.value.graph.nodes) for (const [key, source] of Object.entries(node.inputs)) if (source === '$input.' + old) node.inputs[key] = '$input.' + next } })" /></label><label>输入单位<select :value="unitOf(draftState.draft.value.inputs[inputNames[0] ?? ''])" :disabled="isCompositeOfficial" @change="mutateDraft(() => { const key = inputNames[0]; if (key) draftState.draft.value.inputs[key] = { ...(draftState.draft.value.inputs[key] as object), unit: eventValue($event) } })"><option v-for="unit in capabilities?.units ?? []" :key="unit" :value="unit">{{ unit }}</option></select></label><label>输出名称<input :value="draftState.draft.value.graph.outputs[0] ?? ''" :disabled="isCompositeOfficial" @change="mutateDraft(() => { const next = eventValue($event); if (next && draftState.draft.value.graph.outputs.length) draftState.draft.value.graph.outputs[0] = next })" /></label><label>输出单位<select :value="unitOf(Object.values(draftState.draft.value.outputs)[0])" :disabled="isCompositeOfficial" @change="mutateDraft(() => { const key = Object.keys(draftState.draft.value.outputs)[0]; if (key) draftState.draft.value.outputs[key] = { ...(draftState.draft.value.outputs[key] as object), unit: eventValue($event) } })"><option v-for="unit in capabilities?.units ?? []" :key="unit" :value="unit">{{ unit }}</option></select></label></div></section>
          <section class="definition-section"><h3>预处理、窗口与指标节点</h3><div v-for="(node, index) in draftState.draft.value.graph.nodes" :key="`${node.id}-${index}`" class="definition-node"><input v-model="node.id" :disabled="isCompositeOfficial" @change="draftState.syncJson" /><select v-model="node.type" :disabled="isCompositeOfficial" @change="draftState.syncJson"><option v-for="kind in capabilities?.nodes ?? []" :key="kind" :value="kind">{{ kind }}</option></select><textarea :value="JSON.stringify(node.inputs)" :disabled="isCompositeOfficial" title="输入绑定 JSON" @change="applyNodeJson(node, 'inputs', eventValue($event))" /><textarea :value="JSON.stringify(node.parameters)" :disabled="isCompositeOfficial" title="参数 JSON" @change="applyNodeJson(node, 'parameters', eventValue($event))" /><button :disabled="isCompositeOfficial" title="删除节点" aria-label="删除节点" @click="removeNode(index)">×</button></div><button :disabled="isCompositeOfficial" @click="addNode">添加节点</button></section>
          <section class="definition-section"><h3>质量规则与引用</h3><div class="definition-grid"><label class="wide">质量规则 JSON<textarea :value="JSON.stringify(draftState.draft.value.quality_rules, null, 2)" :disabled="isCompositeOfficial" @change="applyQualityJson(eventValue($event))" /></label><label class="wide">引用（每行一项）<textarea :value="draftState.draft.value.references.join('\n')" :disabled="isCompositeOfficial" @change="mutateDraft(() => { draftState.draft.value.references = eventValue($event).split('\n').map((item: string) => item.trim()).filter(Boolean) })" /></label></div></section>
          <section class="definition-section"><h3>高级 JSON</h3><textarea class="definition-json" :value="draftState.jsonText.value" :disabled="isCompositeOfficial" @input="draftState.applyJson(eventValue($event))" /></section>
          <section class="definition-section"><h3>版本比较</h3><div class="definition-compare"><select v-model="compareLeft"><option v-for="item in versions" :key="item.semver" :value="item.semver">{{ item.semver }}</option></select><select v-model="compareRight"><option v-for="item in versions" :key="item.semver" :value="item.semver">{{ item.semver }}</option></select><button :disabled="!selectedId || loading" @click="compare">比较</button><span v-if="compareResult">摘要相同：{{ compareResult.same_digest ? '是' : '否' }} · 图变更：{{ compareResult.graph_changed ? '是' : '否' }} · 参数变更：{{ compareResult.parameter_schema_changed ? '是' : '否' }}</span></div></section>
          <section class="definition-section"><h3>标量模拟预览</h3><div class="definition-preview-inputs"><label v-for="key in inputNames" :key="key">{{ key }}<input v-model.number="previewInputs[key].value" type="number" step="any" :disabled="isCompositeOfficial" /><select v-model="previewInputs[key].unit" :disabled="isCompositeOfficial"><option v-for="unit in capabilities?.units ?? []" :key="unit" :value="unit">{{ unit }}</option></select></label></div><div v-if="previewRun" class="definition-preview-result"><strong>{{ previewRun.status === 'completed' ? '预览完成' : '预览不可用' }}</strong><span v-for="(output, key) in previewRun.result_summary?.outputs" :key="key">{{ key }} = {{ output.value ?? '不可用' }} {{ output.unit }}</span></div></section>
          </template>
        </main>
      </div>
    </section>
  </div>
</template>
