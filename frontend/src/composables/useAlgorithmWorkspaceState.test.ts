import { expect, it } from 'vitest'
import { useAlgorithmWorkspaceState } from './useAlgorithmWorkspaceState'

type TestRun = { status: string; result: null }

it('preserves result history when only the dynamic result display range changes', () => {
  const state = useAlgorithmWorkspaceState<TestRun>()
  state.updateDynamicSession({
    enabled: true, channel: 'Fz', windowS: 10, displayRangeS: 30,
    definitions: [{ id: 'ratio', label: 'Theta/Beta 比值', unit: 'dimensionless' }],
  })
  state.results.value = { ratio: { status: 'completed', result: null } }
  state.updateDynamicSession({
    enabled: true, channel: 'Fz', windowS: 10, displayRangeS: 60,
    definitions: [{ id: 'ratio', label: 'Theta/Beta 比值', unit: 'dimensionless' }],
  })

  expect(state.results.value).toHaveProperty('ratio')
  expect(state.dynamicSession.value?.displayRangeS).toBe(60)
})

it('clears dynamic results and advances playback epoch on replay', () => {
  const state = useAlgorithmWorkspaceState<TestRun>()
  state.results.value = { ratio: { status: 'completed', result: null } }
  state.updateDynamicSession({ enabled: true, channel: 'Fz', windowS: 10, displayRangeS: 30, definitions: [] })

  state.resetForReplay()

  expect(state.results.value).toEqual({})
  expect(state.playbackEpoch.value).toBe(1)
})
