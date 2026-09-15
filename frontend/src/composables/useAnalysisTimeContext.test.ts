import { expect, it } from 'vitest'
import { useAnalysisTimeContext } from './useAnalysisTimeContext'

it('keeps waveform selection and committed static analysis range as distinct state', () => {
  const state = useAnalysisTimeContext()

  state.initializeForRecording(75)
  state.selectWaveformRange(12, 22)
  state.commitStaticRange(30, 60, 'custom')

  expect(state.spectrumSelection.value).toEqual({ start: 12, end: 22 })
  expect(state.activeAnalysisRange.value).toEqual({ start: 30, end: 60, source: 'custom' })

  state.reset()
  expect(state.spectrumSelection.value).toBeNull()
  expect(state.activeAnalysisRange.value).toBeNull()
})
