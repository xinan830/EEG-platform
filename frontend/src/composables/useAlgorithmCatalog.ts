import { ref } from 'vue'
import { listDefinitions, listDefinitionVersions } from '../api/algorithmDefinitions'
import { listOfficialAlgorithms, type OfficialAlgorithmCatalogItem } from '../api/officialAlgorithms'
import type { AlgorithmDefinition, AlgorithmDefinitionVersion } from '../types/algorithmDefinition'

/** Shared directory state only; scientific result arrays remain backend-owned. */
export function useAlgorithmCatalog() {
  const definitions = ref<AlgorithmDefinition[]>([])
  const officialAlgorithms = ref<OfficialAlgorithmCatalogItem[]>([])
  const versionsByDefinition = ref<Record<string, AlgorithmDefinitionVersion[]>>({})
  const userError = ref('')
  const officialError = ref('')
  const loading = ref(false)

  async function refresh() {
    loading.value = true
    userError.value = ''
    officialError.value = ''
    try {
      const [userResult, officialResult] = await Promise.allSettled([listDefinitions(), listOfficialAlgorithms()])
      if (userResult.status === 'fulfilled') definitions.value = userResult.value
      else userError.value = '我的算法目录暂不可读取。'
      if (officialResult.status === 'fulfilled') officialAlgorithms.value = officialResult.value
      else officialError.value = '官方算法目录暂不可读取；我的算法不受影响。'
    } finally {
      loading.value = false
    }
  }

  async function ensureVersions(definitionId: string, force = false): Promise<AlgorithmDefinitionVersion[]> {
    if (!force && versionsByDefinition.value[definitionId]) return versionsByDefinition.value[definitionId]
    const versions = await listDefinitionVersions(definitionId)
    versionsByDefinition.value = { ...versionsByDefinition.value, [definitionId]: versions }
    return versions
  }

  function removeDefinitionVersionCache(definitionId: string) {
    const next = { ...versionsByDefinition.value }
    delete next[definitionId]
    versionsByDefinition.value = next
  }

  return { definitions, officialAlgorithms, versionsByDefinition, userError, officialError, loading, refresh, ensureVersions, removeDefinitionVersionCache }
}

export type AlgorithmCatalogContext = ReturnType<typeof useAlgorithmCatalog>
