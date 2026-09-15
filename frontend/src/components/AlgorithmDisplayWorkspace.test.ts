// @vitest-environment jsdom
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import { describe, expect, it, vi } from 'vitest'
import AlgorithmDisplayWorkspace from './AlgorithmDisplayWorkspace.vue'

const createDefinitionMetricRun = vi.fn()

vi.mock('../api/algorithmDefinitions', () => ({
  listDefinitions: vi.fn(async () => [{
    definition_id: 'theta-beta', name: 'Theta/Beta 比值', owner: 'local-user', status: 'testing',
  }]),
  listDefinitionVersions: vi.fn(async () => [{ semver: '1.0.0' }]),
}))

vi.mock('../api/runs', () => ({
  createDefinitionMetricRun: (...args: unknown[]) => createDefinitionMetricRun(...args),
  getRun: vi.fn(async () => ({
    status: 'completed',
    result_summary: { metric: { mode: 'dynamic', output: { label: 'Theta/Beta 比值', unit: 'dimensionless' }, channel: 'F3', series: [{ time_s: 22, window_start_s: 12, window_end_s: 22, value: 1.5 }] } },
  })),
}))

describe('AlgorithmDisplayWorkspace', () => {
  it('restarts an active dynamic session when the selected window changes', async () => {
    createDefinitionMetricRun.mockResolvedValue({ run_id: 'run-1', status: 'queued' })
    const wrapper = mount(AlgorithmDisplayWorkspace, {
      props: {
        recording: { id: 'recording-1', channels: ['F3'] }, rangeStart: 0, rangeEnd: 30,
        channels: ['F3'], playbackPositionS: 32, playing: false, dynamicActive: true,
      } as never,
    })
    await Promise.resolve()
    await nextTick()
    await wrapper.find('input[type="checkbox"]').setValue(true)
    await wrapper.findAll('button').find((button) => button.text() === '动态分析')!.trigger('click')

    await wrapper.findAll('select')[1].setValue('20')
    await Promise.resolve()
    await nextTick()

    expect(createDefinitionMetricRun).toHaveBeenCalledWith(expect.objectContaining({
      startS: 0,
      endS: 32,
      mode: 'dynamic',
      dynamicWindowS: 20,
    }))
  })

  it('clears prior dynamic results but retains selected settings for a new playback epoch', async () => {
    createDefinitionMetricRun.mockResolvedValue({ run_id: 'run-replay', status: 'queued' })
    const wrapper = mount(AlgorithmDisplayWorkspace, {
      props: {
        recording: { id: 'recording-1', channels: ['F3'] }, rangeStart: 0, rangeEnd: 30,
        channels: ['F3'], playbackPositionS: 22, playing: false, dynamicActive: true, playbackEpoch: 0,
      } as never,
    })
    await Promise.resolve()
    await nextTick()
    await wrapper.find('input[type="checkbox"]').setValue(true)
    await wrapper.findAll('button').find((button) => button.text() === '动态分析')!.trigger('click')
    await wrapper.findAll('button').find((button) => button.text() === '同步已启用')!.trigger('click')
    await Promise.resolve()
    await nextTick()

    await wrapper.setProps({ playbackEpoch: 1, playbackPositionS: 0 })

    expect(wrapper.emitted('results')?.at(-1)?.[0]).toEqual({})
    expect((wrapper.find('input[type="checkbox"]').element as HTMLInputElement).checked).toBe(true)
    expect((wrapper.findAll('select')[1].element as HTMLSelectElement).value).toBe('10')
  })
})
