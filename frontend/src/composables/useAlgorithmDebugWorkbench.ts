import { computed, ref, type Ref } from 'vue'

type RunWithStatus = { status: string }
type ResultEntry<T extends RunWithStatus> = { run?: T; definitionName?: string }
type Selection<T extends RunWithStatus> = { definitionId: string; fallbackRun: T; fallbackName: string }

function hasFinalDebugEvidence(status: string) {
  return status === 'completed' || status === 'gate_failed'
}

/**
 * Owns only the user's open/close intent for the read-only algorithm workbench.
 * Recording time, channel and scientific evidence remain owned by the viewer,
 * workspace result and backend Run respectively.
 */
export function useAlgorithmDebugWorkbench<T extends RunWithStatus>(results: Ref<Record<string, ResultEntry<T>>>) {
  const selection = ref<Selection<T> | null>(null) as Ref<Selection<T> | null>
  const currentEntry = computed(() => selection.value ? results.value[selection.value.definitionId] : undefined)
  const activeRun = computed<T | null>(() => {
    const chosen = selection.value
    if (!chosen || !currentEntry.value) return null
    // A replacement Run may be queued while the previous completed evidence is
    // still the only truthful material that can be shown in the debug panel.
    return (currentEntry.value.run && hasFinalDebugEvidence(currentEntry.value.run.status)
      ? currentEntry.value.run
      : chosen.fallbackRun) as T
  })
  const definitionName = computed(() => currentEntry.value?.definitionName ?? selection.value?.fallbackName ?? '')
  const isOpen = computed(() => Boolean(selection.value && activeRun.value))

  function open(definitionId: string) {
    const entry = results.value[definitionId]
    if (!entry?.run) return
    selection.value = {
      definitionId,
      fallbackRun: entry.run,
      fallbackName: entry.definitionName ?? definitionId,
    }
  }

  function close() { selection.value = null }

  return { activeRun, definitionName, isOpen, open, close }
}
