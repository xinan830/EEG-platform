<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { ApiRequestError } from '../api/client'
import { exportUrl, getResultView, listRunSummaries, type ResultView, type RunSummary } from '../api/results'
const props = defineProps<{ recordingId: string }>(); const emit = defineEmits<{ close: [] }>()
const runs = ref<RunSummary[]>([]); const selected = ref<ResultView | null>(null); const loading = ref(false); const error = ref('')
function describe(cause: unknown) { return cause instanceof ApiRequestError ? `${cause.code ?? 'REQUEST_FAILED'}: ${cause.message}` : '结果读取失败' }
async function load() { loading.value = true; error.value = ''; try { runs.value = await listRunSummaries(props.recordingId) } catch (cause) { error.value = describe(cause) } finally { loading.value = false } }
async function select(runId: string) { loading.value = true; error.value = ''; try { selected.value = await getResultView(runId) } catch (cause) { error.value = describe(cause) } finally { loading.value = false } }
onMounted(load)
</script>
<template><div class="modal-layer results-modal-layer" @click.self="emit('close')"><section class="results-drawer" aria-label="结果工作台"><header class="dialog-titlebar"><strong>结果工作台</strong><button class="dialog-close" aria-label="关闭" @click="emit('close')">×</button></header><div class="results-layout"><aside><button @click="load">刷新</button><button v-for="run in runs" :key="run.run_id" @click="select(run.run_id)"><strong>{{ run.analysis_type }}</strong><small>{{ run.status }} · {{ run.run_id.slice(0, 8) }}</small></button><span v-if="!loading && !runs.length">当前 Recording 暂无 Run</span></aside><main><p v-if="loading">正在读取后端结果...</p><p v-if="error" class="definition-error">{{ error }}</p><template v-if="selected"><h3>{{ selected.run.analysis_type }} · {{ selected.run.status }}</h3><p>算法：{{ selected.run.scientific_version }} · 实现：{{ selected.run.implementation_version }}</p><p>数据类别：算法输出；不包含临床结论。</p><p v-if="selected.run.error">质量/错误：{{ selected.run.error.code }} · {{ selected.run.error.message }}</p><p>Artifact：{{ selected.artifacts.length }} 个</p><a :href="exportUrl(selected.run.run_id)">导出可复现 ZIP</a></template><p v-else>选择一个后端 Run 查看其单位、质量状态和可复现导出。</p></main></div></section></div></template>
