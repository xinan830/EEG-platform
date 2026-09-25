import { ref } from 'vue'
import { listOfficialAlgorithms, type OfficialAlgorithmCatalogItem } from '../api/officialAlgorithms'
import { listAlgorithms, type AlgorithmCatalogItem } from '../api/algorithms'

/** Shared directory state only; scientific result arrays remain backend-owned. */
export function useAlgorithmCatalog() {
  const officialAlgorithms = ref<OfficialAlgorithmCatalogItem[]>([])
  const algorithms = ref<AlgorithmCatalogItem[]>([])
  const officialError = ref('')
  const loading = ref(false)

  async function refresh() {
    loading.value = true
    officialError.value = ''
    try {
      const [officialResult, catalogResult] = await Promise.allSettled([listOfficialAlgorithms(), listAlgorithms()])
      if (officialResult.status === 'fulfilled') officialAlgorithms.value = officialResult.value
      else officialError.value = '官方算法目录暂不可读取。'
      if (catalogResult.status === 'fulfilled') algorithms.value = catalogResult.value
    } finally {
      loading.value = false
    }
  }

  return { officialAlgorithms, algorithms, officialError, loading, refresh }
}

export type AlgorithmCatalogContext = ReturnType<typeof useAlgorithmCatalog>
