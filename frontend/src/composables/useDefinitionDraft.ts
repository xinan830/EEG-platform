import { computed, ref } from 'vue'
import { DEFAULT_DRAFT, type DefinitionDraft } from '../types/algorithmDefinition'

function copyDraft(value: DefinitionDraft): DefinitionDraft {
  return JSON.parse(JSON.stringify(value)) as DefinitionDraft
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isDraftShape(value: unknown): value is DefinitionDraft {
  if (!isObject(value) || typeof value.semver !== 'string' || !isObject(value.graph)) return false
  return Array.isArray(value.graph.nodes)
    && Array.isArray(value.graph.outputs)
    && isObject(value.parameter_schema)
    && isObject(value.inputs)
    && isObject(value.outputs)
    && isObject(value.units)
    && isObject(value.quality_rules)
    && Array.isArray(value.references)
}

export function useDefinitionDraft(initial = DEFAULT_DRAFT) {
  const draft = ref<DefinitionDraft>(copyDraft(initial))
  const jsonText = ref(JSON.stringify(draft.value, null, 2))
  const jsonError = ref('')
  const isJsonValid = computed(() => !jsonError.value)

  function syncJson() {
    jsonText.value = JSON.stringify(draft.value, null, 2)
    jsonError.value = ''
  }

  function replace(value: DefinitionDraft) {
    draft.value = copyDraft(value)
    syncJson()
  }

  function applyJson(value: string) {
    jsonText.value = value
    try {
      const parsed = JSON.parse(value) as unknown
      if (!isDraftShape(parsed)) throw new Error('草稿必须包含 semver、graph、输入、输出、单位、质量规则和引用')
      draft.value = parsed
      jsonError.value = ''
      return true
    } catch (cause) {
      jsonError.value = cause instanceof Error ? cause.message : 'JSON 格式无效'
      return false
    }
  }

  return { draft, jsonText, jsonError, isJsonValid, syncJson, replace, applyJson }
}
