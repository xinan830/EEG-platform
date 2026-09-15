<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { ApiRequestError } from '../api/client'
import { exportUrl, getResultView, listRunSummaries, type ResultView, type RunSummary } from '../api/results'
import { validateSpectralReference, type SpectralReferenceValidation } from '../api/spectralValidation'
import DefinitionMetricResultCard from './DefinitionMetricResultCard.vue'
import DefinitionMetricTrendChart from './DefinitionMetricTrendChart.vue'
import type { DefinitionMetricResult } from './DefinitionMetricResultCard.vue'
import type { DynamicMetric } from './DefinitionMetricTrendChart.vue'
import { definitionMetricFromRun, isDynamicMetricResult, resultRunLabel } from '../utils/resultMetric'

const props = defineProps<{ recordingId: string; startS: number; endS: number; channels: string[] }>()
const emit = defineEmits<{ close: [] }>()
const runs = ref<RunSummary[]>([])
const selected = ref<ResultView | null>(null)
const loading = ref(false)
const error = ref('')
const validation = ref<SpectralReferenceValidation | null>(null)
const validating = ref(false)
const selectedMetric = computed(() => definitionMetricFromRun(selected.value?.run))
const selectedStaticMetric = computed<DefinitionMetricResult | null>(() => {
  const metric = selectedMetric.value
  return metric && !isDynamicMetricResult(metric) ? metric : null
})
const selectedDynamicMetric = computed<DynamicMetric | null>(() => {
  const metric = selectedMetric.value
  return metric && isDynamicMetricResult(metric) ? metric : null
})

function describe(cause: unknown) {
  return cause instanceof ApiRequestError ? `${cause.code ?? 'REQUEST_FAILED'}: ${cause.message}` : '结果读取失败'
}

async function load() {
  loading.value = true
  error.value = ''
  try { runs.value = await listRunSummaries(props.recordingId) } catch (cause) { error.value = describe(cause) } finally { loading.value = false }
}

async function select(runId: string) {
  loading.value = true
  error.value = ''
  try { selected.value = await getResultView(runId) } catch (cause) { error.value = describe(cause) } finally { loading.value = false }
}

async function validate() {
  validating.value = true
  error.value = ''
  validation.value = null
  try { validation.value = await validateSpectralReference(props.recordingId, props.startS, props.endS, props.channels) } catch (cause) { error.value = describe(cause) } finally { validating.value = false }
}

onMounted(load)
</script>
<template>
  <div class="modal-layer results-modal-layer" @click.self="emit('close')">
    <section class="results-drawer" aria-label="结果工作台">
      <header class="dialog-titlebar"><strong>结果工作台</strong><button class="dialog-close" aria-label="关闭" @click="emit('close')">×</button></header>
      <div class="results-layout">
        <aside>
          <button type="button" @click="load">刷新</button>
          <button v-for="run in runs" :key="run.run_id" type="button" :class="{ active: selected?.run.run_id === run.run_id }" @click="select(run.run_id)">
            <strong>{{ resultRunLabel(run) }}</strong><small>{{ run.status }} · {{ run.run_id.slice(0, 8) }}</small>
          </button>
          <span v-if="!loading && !runs.length">当前 Recording 暂无分析结果</span>
        </aside>
        <main>
          <section class="result-validation">
            <h3>独立 PSD 参考校验</h3>
            <p>{{ startS.toFixed(3) }}–{{ endS.toFixed(3) }} s · {{ channels.join(', ') }}</p>
            <button type="button" :disabled="validating || endS - startS < 4 || !channels.length" @click="validate">{{ validating ? '正在校验…' : '运行独立校验' }}</button>
            <p v-if="validation"><strong>{{ validation.passed ? 'PASS' : 'FAIL' }}</strong> · {{ validation.passed_point_count }}/{{ validation.point_count }} 点 · 最大绝对误差 {{ validation.max_absolute_error }} · 最大相对误差 {{ validation.max_relative_error }}</p>
            <p v-if="validation">{{ validation.evidence.unit }} · {{ validation.evidence.frequencies_hz.length }} 个频点 · 后端工程校验，不表示临床结论。</p>
          </section>
          <p v-if="loading">正在读取后端结果...</p>
          <p v-if="error" class="definition-error">{{ error }}</p>
          <template v-if="selected">
            <section class="result-summary">
              <h3>{{ resultRunLabel(selected.run) }} · {{ selected.run.status }}</h3>
              <p>算法版本：{{ selected.run.scientific_version }} · 实现：{{ selected.run.implementation_version }}</p>
              <p v-if="selected.run.error">质量/错误：{{ selected.run.error.code }} · {{ selected.run.error.message }}</p>
            </section>
            <DefinitionMetricResultCard v-if="selectedStaticMetric" :result="selectedStaticMetric" />
            <DefinitionMetricTrendChart v-else-if="selectedDynamicMetric" :result="selectedDynamicMetric" />
            <p v-else-if="selected.run.analysis_type === 'definition_metric' && selected.run.status === 'completed'">本次算法 Run 没有可呈现的后端指标数据。</p>
            <p v-else-if="selected.run.analysis_type === 'definition_metric'">算法正在运行或未通过质量门；结果生成后会显示后端返回的数值与图表。</p>
            <p>数据类别：算法输出；不包含临床结论。Artifact：{{ selected.artifacts.length }} 个</p>
            <a :href="exportUrl(selected.run.run_id, validation?.validation_id)">导出可复现 ZIP</a>
          </template>
          <p v-else>选择一个后端 Run 查看其数值、单位、质量状态和可复现导出。</p>
        </main>
      </div>
    </section>
  </div>
</template>
