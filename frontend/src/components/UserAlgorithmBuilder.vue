<script setup lang="ts">
import { computed, ref } from 'vue'
import { createDefinition, createDefinitionVersion, validateDefinition } from '../api/algorithmDefinitions'
import type { DefinitionDraft, Unit } from '../types/algorithmDefinition'

const emit = defineEmits<{ close: []; saved: [string] }>()
const name = ref('Theta/Beta 比值')
const description = ref('由用户选择的基础频段功率计算研究指标。')
const leftFeature = ref('theta_power')
const rightFeature = ref('beta_power')
const operator = ref('/')
const outputName = ref('theta_beta_ratio')
const loading = ref(false)
const message = ref('')
const error = ref('')

const features = [
  { id: 'delta_power', label: 'Delta 功率', band: '1–4 Hz', unit: 'uV^2' as Unit },
  { id: 'theta_power', label: 'Theta 功率', band: '4–8 Hz', unit: 'uV^2' as Unit },
  { id: 'alpha_power', label: 'Alpha 功率', band: '8–13 Hz', unit: 'uV^2' as Unit },
  { id: 'beta_power', label: 'Beta 功率', band: '13–30 Hz', unit: 'uV^2' as Unit },
  { id: 'delta_rbp', label: 'Delta 相对功率', band: '1–4 Hz', unit: 'ratio' as Unit },
  { id: 'theta_rbp', label: 'Theta 相对功率', band: '4–8 Hz', unit: 'ratio' as Unit },
  { id: 'alpha_rbp', label: 'Alpha 相对功率', band: '8–13 Hz', unit: 'ratio' as Unit },
  { id: 'beta_rbp', label: 'Beta 相对功率', band: '13–30 Hz', unit: 'ratio' as Unit },
]
const left = computed(() => features.find((item) => item.id === leftFeature.value) ?? features[1])
const right = computed(() => features.find((item) => item.id === rightFeature.value) ?? features[3])
const formula = computed(() => `${left.value.label} ${operator.value === '/' ? '÷' : operator.value} ${right.value.label}`)

function draft(): DefinitionDraft {
  const operation = operator.value === '/' ? 'divide' : operator.value === '+' ? 'add' : operator.value === '-' ? 'subtract' : 'multiply'
  const outputUnit: Unit = operator.value === '/' && left.value.unit === right.value.unit ? 'dimensionless' : left.value.unit
  return {
    semver: '1.0.0',
    graph: { nodes: [{ id: 'calculation', type: operation, inputs: { left: '$input.input_left', right: '$input.input_right' }, parameters: {} }, { id: 'output', type: 'output', inputs: { source: 'calculation' }, parameters: {} }], outputs: ['output'] },
    parameter_schema: { type: 'object', additionalProperties: false },
    inputs: { input_left: { type: 'Scalar', unit: left.value.unit, feature: left.value.id }, input_right: { type: 'Scalar', unit: right.value.unit, feature: right.value.id } },
    outputs: { output: { type: 'Scalar', unit: outputUnit, label: outputName.value } }, units: { input: left.value.unit, output: outputUnit }, quality_rules: { source: 'backend_spectral_feature', unavailable_output: 'null' }, references: ['offline-spectral-v3'],
  }
}

async function save() {
  loading.value = true; error.value = ''; message.value = ''
  try {
    const value = draft()
    await validateDefinition(value)
    const definition = await createDefinition(name.value.trim(), description.value.trim())
    await createDefinitionVersion(definition.definition_id, value)
    message.value = '算法已保存为 1.0.0 版本。'
    emit('saved', definition.definition_id)
  } catch (cause) { error.value = cause instanceof Error ? cause.message : '保存失败' } finally { loading.value = false }
}
</script>

<template>
  <div class="modal-layer user-builder-layer" @click.self="emit('close')">
    <section class="user-builder" aria-label="创建算法">
      <header class="dialog-titlebar"><strong>创建研究算法</strong><span>选择基础指标并编排公式</span><button class="dialog-close" aria-label="关闭" @click="emit('close')">×</button></header>
      <main class="user-builder-body">
        <p class="user-builder-intro">基础指标由后端按当前录制、通道、分析区间和 offline-spectral-v3 计算；这里仅保存公式，不在浏览器重新计算 EEG。</p>
        <label>算法名称<input v-model="name" placeholder="例如：Theta/Beta 比值" /></label>
        <label>说明<textarea v-model="description" /></label>
        <div class="builder-grid">
          <label>输入 A<select v-model="leftFeature"><option v-for="item in features" :key="item.id" :value="item.id">{{ item.label }} · {{ item.band }}</option></select><small>{{ left.unit }}</small></label>
          <label>输入 B<select v-model="rightFeature"><option v-for="item in features" :key="item.id" :value="item.id">{{ item.label }} · {{ item.band }}</option></select><small>{{ right.unit }}</small></label>
        </div>
        <label>计算方式<select v-model="operator"><option value="/">A ÷ B</option><option value="+">A + B</option><option value="-">A − B</option><option value="*">A × B</option></select></label>
        <div class="builder-formula"><span>当前公式</span><strong>{{ formula }}</strong></div>
        <label>输出名称<input v-model="outputName" placeholder="例如：Theta/Beta 比值" /></label>
        <p v-if="message" class="definition-message">{{ message }}</p><p v-if="error" class="definition-error">{{ error }}</p>
      </main>
      <footer class="user-builder-actions"><button @click="emit('close')">取消</button><button class="primary-action" :disabled="loading || !name.trim() || !outputName.trim()" @click="save">{{ loading ? '保存中...' : '验证并保存' }}</button></footer>
    </section>
  </div>
</template>
