import { computed, ref } from 'vue'

export type DynamicAlgorithmDefinition = { id: string; label: string; unit: string; channel: string; windowS: number; stepS: number; minimumWindowS: number; allowWarmup: boolean; displayRangeS: number }
export type DynamicAlgorithmSession = { definitions: DynamicAlgorithmDefinition[] }
export type DynamicSessionUpdate = DynamicAlgorithmSession & { enabled: boolean }

/** Run-result index and dynamic session state. Numeric results remain backend-owned. */
export function useAlgorithmWorkspaceState<T extends { result: unknown }>() {
  const results = ref<Record<string, T>>({})
  const dynamicSession = ref<DynamicAlgorithmSession | null>(null)
  const playbackEpoch = ref(0)
  const resultItems = computed(() => Object.entries(results.value).map(([id, item]) => ({ id, ...item })))

  function updateDynamicSession(value: DynamicSessionUpdate) {
    const previous = dynamicSession.value
    const changedCalculation = !previous
      || JSON.stringify(previous.definitions.map(({ id, channel, windowS, stepS, minimumWindowS, allowWarmup }) => ({ id, channel, windowS, stepS, minimumWindowS, allowWarmup })))
        !== JSON.stringify(value.definitions.map(({ id, channel, windowS, stepS, minimumWindowS, allowWarmup }) => ({ id, channel, windowS, stepS, minimumWindowS, allowWarmup })))
    dynamicSession.value = value.enabled
      ? { definitions: value.definitions }
      : null
    if (value.enabled && changedCalculation) results.value = {}
  }

  function clear() {
    results.value = {}
    dynamicSession.value = null
  }

  function resetForReplay() {
    if (!dynamicSession.value) return
    results.value = {}
    playbackEpoch.value += 1
  }

  return { results, dynamicSession, playbackEpoch, resultItems, updateDynamicSession, clear, resetForReplay }
}
