import { expect, it } from 'vitest'
import { useRecordingContext } from './useRecordingContext'

const recording = {
  id: 'recording-1', original_name: 'sample.bdf', stored_name: 'sample.bdf', extension: '.bdf' as const,
  created_at: '2026-09-15T00:00:00Z', sfreq: 500, duration_s: 120, channels: ['F3', 'Fz', 'Pz'], mapping: null,
}

it('owns imported recording metadata and resets it as one context', () => {
  const state = useRecordingContext()

  state.begin(recording, ['F3', 'Fz'])

  expect(state.recording.value?.id).toBe('recording-1')
  expect(state.totalDurationS.value).toBe(120)
  expect(state.sfreq.value).toBe(500)
  expect(state.sourceChannelNames.value).toEqual(['F3', 'Fz'])
  expect(state.displayChannelNames.value).toEqual(['F3', 'Fz'])

  state.reset()

  expect(state.recording.value).toBeNull()
  expect(state.totalDurationS.value).toBeUndefined()
  expect(state.sourceChannelNames.value).toEqual([])
})
