import { expect, it } from 'vitest'
import { currentAlgorithmDebugRun } from './algorithmDebugRun'

const runAt22 = { run_id: 'run-22', status: 'completed' }
const runAt23 = { run_id: 'run-23', status: 'completed' }

it('uses the newest completed run for the algorithm currently open in the debug workbench', () => {
  expect(currentAlgorithmDebugRun(
    { definitionId: 'theta-beta', fallbackRun: runAt22 },
    { 'theta-beta': { run: runAt23 } },
  )).toBe(runAt23)
})

it('retains the originally opened run when no newer result is available', () => {
  expect(currentAlgorithmDebugRun(
    { definitionId: 'theta-beta', fallbackRun: runAt22 },
    {},
  )).toBe(runAt22)
})
