import { ref } from 'vue'

export type AnalysisRangeSource = 'custom' | 'current_30s' | 'current_screen' | 'selection' | 'dynamic'
export type AnalysisRange = { start: number; end: number; source: AnalysisRangeSource | string }
export type WaveformSelection = { start: number; end: number }

/**
 * Holds user time intent only. A waveform selection is a draft; a committed
 * analysis range is the static backend input. Neither changes playback pages.
 */
export function useAnalysisTimeContext() {
  const spectrumSelection = ref<WaveformSelection | null>(null)
  const activeAnalysisRange = ref<AnalysisRange | null>(null)

  function selectWaveformRange(start: number, end: number) {
    spectrumSelection.value = { start, end }
  }

  function commitStaticRange(start: number, end: number, source: AnalysisRangeSource | string = 'custom') {
    activeAnalysisRange.value = { start, end, source }
  }

  function initializeForRecording(durationS: number | null | undefined) {
    spectrumSelection.value = null
    activeAnalysisRange.value = durationS !== undefined && durationS !== null && durationS >= 4
      ? { start: 0, end: Math.min(30, durationS), source: 'current_30s' }
      : null
  }

  function reset() {
    spectrumSelection.value = null
    activeAnalysisRange.value = null
  }

  return { spectrumSelection, activeAnalysisRange, selectWaveformRange, commitStaticRange, initializeForRecording, reset }
}
