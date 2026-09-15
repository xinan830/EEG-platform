import { ref } from 'vue'
import type { CustomMontageRow, MontageOption } from '../api/recordings'

/** Playback and waveform presentation state. It never owns offline analysis ranges. */
export function useViewerContext() {
  const playing = ref(false)
  const loading = ref(false)
  const playbackPositionS = ref(0)
  const windowStartS = ref(0)
  const montageId = ref('original')
  const montageOptions = ref<MontageOption[]>([])
  const averageExclude = ref<string[]>([])
  const customMontage = ref<CustomMontageRow[]>([])

  function reset() {
    playing.value = false
    loading.value = false
    playbackPositionS.value = 0
    windowStartS.value = 0
    montageId.value = 'original'
    montageOptions.value = []
    averageExclude.value = []
    customMontage.value = []
  }

  return {
    playing, loading, playbackPositionS, windowStartS,
    montageId, montageOptions, averageExclude, customMontage, reset,
  }
}
