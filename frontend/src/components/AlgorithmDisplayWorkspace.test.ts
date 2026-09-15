// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import { describe, expect, it, vi } from 'vitest'
import { listDefinitions } from '../api/algorithmDefinitions'
import { listOfficialAlgorithms } from '../api/officialAlgorithms'
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

vi.mock('../api/officialAlgorithms', () => ({
  listOfficialAlgorithms: vi.fn(async () => []),
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
    await flushPromises()
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

  it('refreshes the catalog after an algorithm is deleted and recreated', async () => {
    const list = vi.mocked(listDefinitions)
    list.mockReset()
    list.mockResolvedValueOnce([{
      definition_id: 'old-ratio', name: '旧 Theta/Beta', owner: 'local-user', status: 'testing',
      description: '', created_at: '', updated_at: '',
    }])
    list.mockResolvedValueOnce([{
      definition_id: 'new-ratio', name: '新 Theta/Beta', owner: 'local-user', status: 'testing',
      description: '', created_at: '', updated_at: '',
    }])
    const wrapper = mount(AlgorithmDisplayWorkspace, {
      props: {
        recording: { id: 'recording-1', channels: ['F3'] }, rangeStart: 0, rangeEnd: 30,
        channels: ['F3'], definitionsEpoch: 0,
      } as never,
    })
    await Promise.resolve()
    await nextTick()
    expect(wrapper.text()).toContain('旧 Theta/Beta')

    await wrapper.setProps({ definitionsEpoch: 1 })
    await Promise.resolve()
    await nextTick()

    expect(wrapper.text()).toContain('新 Theta/Beta')
    expect(wrapper.text()).not.toContain('旧 Theta/Beta')
  })

  it('shows official definitions as read-only while their executor is still in shadow validation', async () => {
    const list = vi.mocked(listDefinitions)
    list.mockReset()
    list.mockResolvedValueOnce([
      {
        definition_id: 'official-rbp', name: 'Official RBP', owner: 'platform-official', status: 'testing',
        description: 'Frozen official RBP contract; shadow migration only.', created_at: '', updated_at: '',
      },
      {
        definition_id: 'user-ratio', name: '我的 Theta/Beta', owner: 'local-user', status: 'testing',
        description: '', created_at: '', updated_at: '',
      },
    ])
    vi.mocked(listOfficialAlgorithms).mockResolvedValueOnce([{
      algorithm_id: 'rbp', display_name_zh: '相对频段功率', abbreviation: 'RBP', purpose_zh: '展示四个基础频段在总功率中的占比',
      scientific_version: 'offline-spectral-v3', implementation_identity: 'offline-spectral-v3', execution_kind: 'generic_research_primitives',
      availability: 'shadow_validation', is_runnable: false, required_channel_roles: [], supported_modes: [], definition_id: 'official-rbp', definition_version: '1.0.0',
    }])
    const wrapper = mount(AlgorithmDisplayWorkspace, {
      props: {
        recording: { id: 'recording-1', channels: ['F3'] }, rangeStart: 0, rangeEnd: 30,
        channels: ['F3'],
      } as never,
    })
    await flushPromises()
    await nextTick()

    expect(wrapper.text()).toContain('官方内置算法')
    expect(wrapper.text()).toContain('相对频段功率（RBP）')
    expect(wrapper.text()).toContain('工程验证中，暂不可运行')
    expect(wrapper.text()).toContain('我的算法')
    expect(wrapper.text()).toContain('我的 Theta/Beta')
    expect((wrapper.get('[data-testid="official-algorithm-official-rbp"] input').element as HTMLInputElement).disabled).toBe(true)
  })
})
