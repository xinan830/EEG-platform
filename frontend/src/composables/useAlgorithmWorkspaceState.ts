import { computed, ref } from 'vue'

export type DynamicAlgorithmDefinition = { id: string; label: string; unit: string }
export type DynamicAlgorithmSession = { channel: string; definitions: DynamicAlgorithmDefinition[]; windowS: number; displayRangeS: number }
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
      || previous.channel !== value.channel
      || previous.windowS !== value.windowS
      || previous.definitions.map((item) => item.id).join('|') !== value.definitions.map((item) => item.id).join('|')
    dynamicSession.value = value.enabled
      ? { channel: value.channel, definitions: value.definitions, windowS: value.windowS, displayRangeS: value.displayRangeS }
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
