import { expect, it } from 'vitest'
import { ref } from 'vue'
import { useAlgorithmDebugWorkbench } from './useAlgorithmDebugWorkbench'

type TestRun = { run_id: string; status: string }
type TestResult = { run?: TestRun; definitionName?: string }

it('keeps an open workbench attached to the latest workspace result for its algorithm', () => {
  const results = ref<Record<string, TestResult>>({
    ratio: { run: { run_id: 'run-22', status: 'completed' }, definitionName: 'Theta/Beta 比值' },
  })
  const workbench = useAlgorithmDebugWorkbench(results)

  workbench.open('ratio')
  results.value = {
    ratio: { run: { run_id: 'run-23', status: 'completed' }, definitionName: 'Theta/Beta 比值' },
  }

  expect(workbench.activeRun.value?.run_id).toBe('run-23')
  expect(workbench.definitionName.value).toBe('Theta/Beta 比值')
})

it('closes the workbench when its selected algorithm no longer has a completed Run', () => {
  const results = ref<Record<string, TestResult>>({
    ratio: { run: { run_id: 'run-22', status: 'completed' }, definitionName: 'Theta/Beta 比值' },
  })
  const workbench = useAlgorithmDebugWorkbench(results)

  workbench.open('ratio')
  results.value = {}

  expect(workbench.activeRun.value).toBeNull()
  expect(workbench.isOpen.value).toBe(false)
})

it('uses the terminal gate-failed Run instead of retaining older evidence', () => {
  const results = ref<Record<string, TestResult>>({
    ratio: { run: { run_id: 'run-22', status: 'completed' }, definitionName: 'Theta/Beta 比值' },
  })
  const workbench = useAlgorithmDebugWorkbench(results)

  workbench.open('ratio')
  results.value = {
    ratio: { run: { run_id: 'run-23', status: 'gate_failed' }, definitionName: 'Theta/Beta 比值' },
  }

  expect(workbench.activeRun.value?.run_id).toBe('run-23')
})
