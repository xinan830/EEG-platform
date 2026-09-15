import { ref } from 'vue'
import type { Recording } from '../types/recording'

/** File identity and imported metadata shared by every workspace surface. */
export function useRecordingContext() {
  const recording = ref<Recording | null>(null)
  const totalDurationS = ref<number | undefined>()
  const sfreq = ref<number | undefined>()
  const sourceChannelNames = ref<string[]>([])
  const displayChannelNames = ref<string[]>([])

  function begin(next: Recording, channels: string[]) {
    recording.value = next
    totalDurationS.value = next.duration_s ?? undefined
    sfreq.value = next.sfreq ?? undefined
    sourceChannelNames.value = [...channels]
    displayChannelNames.value = [...channels]
  }

  function reset() {
    recording.value = null
    totalDurationS.value = undefined
    sfreq.value = undefined
    sourceChannelNames.value = []
    displayChannelNames.value = []
  }

  return { recording, totalDurationS, sfreq, sourceChannelNames, displayChannelNames, begin, reset }
}
