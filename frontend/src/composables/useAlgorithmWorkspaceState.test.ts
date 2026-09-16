import { expect, it } from 'vitest'
import { useAlgorithmWorkspaceState } from './useAlgorithmWorkspaceState'

type TestRun = { status: string; result: null }

it('preserves result history when only the dynamic result display range changes', () => {
  const state = useAlgorithmWorkspaceState<TestRun>()
  state.updateDynamicSession({
    enabled: true,
    definitions: [{ id: 'ratio', label: 'Theta/Beta 比值', unit: 'dimensionless', channel: 'Fz', windowS: 10, stepS: 1, minimumWindowS: 4, allowWarmup: true, displayRangeS: 30 }],
  })
  state.results.value = { ratio: { status: 'completed', result: null } }
  state.updateDynamicSession({
    enabled: true,
    definitions: [{ id: 'ratio', label: 'Theta/Beta 比值', unit: 'dimensionless', channel: 'Fz', windowS: 10, stepS: 1, minimumWindowS: 4, allowWarmup: true, displayRangeS: 60 }],
  })

  expect(state.results.value).toHaveProperty('ratio')
  expect(state.dynamicSession.value?.definitions[0].displayRangeS).toBe(60)
})

it('clears dynamic results and advances playback epoch on replay', () => {
  const state = useAlgorithmWorkspaceState<TestRun>()
  state.results.value = { ratio: { status: 'completed', result: null } }
  state.updateDynamicSession({ enabled: true, definitions: [] })

  state.resetForReplay()

  expect(state.results.value).toEqual({})
  expect(state.playbackEpoch.value).toBe(1)
})
