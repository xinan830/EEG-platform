// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import { nextTick, ref } from 'vue'
import { describe, expect, it, vi } from 'vitest'
import type { AlgorithmCatalogContext } from '../composables/useAlgorithmCatalog'
import AlgorithmDisplayWorkspace from './AlgorithmDisplayWorkspace.vue'

const createAlgorithmRun = vi.fn()

vi.mock('../api/runs', () => ({
  createAlgorithmRun: (...args: unknown[]) => createAlgorithmRun(...args),
  getRun: vi.fn(async () => ({
    status: 'completed',
    result_summary: { metric: { mode: 'dynamic', output: { label: 'Theta/Beta 比值', unit: 'dimensionless' }, channel: 'F3', series: [{ time_s: 22, window_start_s: 12, window_end_s: 22, value: 1.5 }] } },
  })),
}))

const userRatio = { definition_id: 'theta-beta', name: 'Theta/Beta 比值', owner: 'local-user', status: 'testing' as const, description: '', created_at: '', updated_at: '' }
const ratioVersion = { semver: '1.0.0', graph: { outputs: [] }, outputs: {} }
const officialRbp = {
  algorithm_id: 'rbp', display_name_zh: '相对频段功率', abbreviation: 'RBP', purpose_zh: '展示四个基础频段在总功率中的占比',
  scientific_version: 'offline-spectral-v3', implementation_identity: 'offline-spectral-v3', execution_kind: 'generic_research_primitives',
  availability: 'shadow_validation' as const, is_runnable: false, required_channel_roles: [], supported_modes: [], definition_id: 'official-rbp', definition_version: '1.0.0',
}
const officialIapf = {
  ...officialRbp, algorithm_id: 'iapf', display_name_zh: '个体 Alpha 峰频', abbreviation: 'IAPF',
  availability: 'available' as const, is_runnable: true, definition_id: 'official-iapf', supported_modes: ['static', 'dynamic'],
}

function createCatalog(overrides: Partial<{ definitions: typeof userRatio[]; officialAlgorithms: typeof officialRbp[]; versions: Record<string, unknown[]> }> = {}) {
  const versions = overrides.versions ?? { 'theta-beta': [ratioVersion] }
  return {
    definitions: ref(overrides.definitions ?? [userRatio]), officialAlgorithms: ref(overrides.officialAlgorithms ?? []),
    versionsByDefinition: ref(versions), userError: ref(''), officialError: ref(''), loading: ref(false),
    refresh: vi.fn(async () => undefined),
    ensureVersions: vi.fn(async (id: string) => versions[id] ?? []),
    removeDefinitionVersionCache: vi.fn(),
  } as unknown as AlgorithmCatalogContext
}

describe('AlgorithmDisplayWorkspace', () => {
  it('restarts an active dynamic session when the selected window changes', async () => {
    createAlgorithmRun.mockResolvedValue({ run_id: 'run-1', status: 'queued' })
    const catalog = createCatalog()
    const wrapper = mount(AlgorithmDisplayWorkspace, {
      props: {
        recording: { id: 'recording-1', channels: ['F3'] }, rangeStart: 0, rangeEnd: 30,
        channels: ['F3'], playbackPositionS: 32, playing: false, dynamicActive: true, catalog,
      } as never,
    })
    await flushPromises()
    await nextTick()
    await wrapper.find('input[type="checkbox"]').setValue(true)
    await wrapper.findAll('button').find((button) => button.text() === '动态分析')!.trigger('click')

    await wrapper.findAll('select')[1].setValue('20')
    await Promise.resolve()
    await nextTick()

    expect(createAlgorithmRun).toHaveBeenCalledWith(expect.objectContaining({
      source: 'user',
      startS: 0,
      endS: 32,
      mode: 'dynamic',
      dynamicWindowS: 20,
    }))
  })

  it('clears prior dynamic results but retains selected settings for a new playback epoch', async () => {
    createAlgorithmRun.mockResolvedValue({ run_id: 'run-replay', status: 'queued' })
    const catalog = createCatalog()
    const wrapper = mount(AlgorithmDisplayWorkspace, {
      props: {
        recording: { id: 'recording-1', channels: ['F3'] }, rangeStart: 0, rangeEnd: 30,
        channels: ['F3'], playbackPositionS: 22, playing: false, dynamicActive: true, playbackEpoch: 0, catalog,
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

  it('renders a changed shared catalog without requesting a second local definition list', async () => {
    const catalog = createCatalog({ definitions: [{ ...userRatio, definition_id: 'old-ratio', name: '旧 Theta/Beta' }] })
    const wrapper = mount(AlgorithmDisplayWorkspace, {
      props: {
        recording: { id: 'recording-1', channels: ['F3'] }, rangeStart: 0, rangeEnd: 30,
        channels: ['F3'], catalog,
      } as never,
    })
    await flushPromises()
    await nextTick()
    expect(wrapper.text()).toContain('旧 Theta/Beta')

    catalog.definitions.value = [{ ...userRatio, definition_id: 'new-ratio', name: '新 Theta/Beta' }]
    await nextTick()

    expect(wrapper.text()).toContain('新 Theta/Beta')
    expect(wrapper.text()).not.toContain('旧 Theta/Beta')
    expect(catalog.refresh).toHaveBeenCalledTimes(1)
  })

  it('shows official definitions as read-only while their executor is still in shadow validation', async () => {
    const catalog = createCatalog({
      definitions: [{ ...userRatio, definition_id: 'user-ratio', name: '我的 Theta/Beta' }],
      officialAlgorithms: [officialRbp],
      versions: { 'user-ratio': [ratioVersion] },
    })
    const wrapper = mount(AlgorithmDisplayWorkspace, {
      props: {
        recording: { id: 'recording-1', channels: ['F3'] }, rangeStart: 0, rangeEnd: 30,
        channels: ['F3'], catalog,
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

  it('submits a runnable official IAPF run without treating it as a user definition', async () => {
    createAlgorithmRun.mockResolvedValue({ run_id: 'official-run', status: 'completed', result_summary: { metric: { output: { label: '个体 Alpha 峰频率', value: 10, unit: 'Hz', quality: { status: 'clean', reasons: [] } }, channel: 'F3', actual_range: { start_s: 0, end_s: 30 }, source_quality: {}, chart: { kind: 'none' } } } })
    const catalog = createCatalog({ officialAlgorithms: [officialIapf] as never })
    const wrapper = mount(AlgorithmDisplayWorkspace, { props: { recording: { id: 'recording-1', channels: ['F3'] }, rangeStart: 0, rangeEnd: 30, channels: ['F3'], catalog } as never })
    await flushPromises()
    await wrapper.get('[data-testid="official-algorithm-official-iapf"] input').setValue(true)
    await wrapper.findAll('button').find((button) => button.text() === '计算此区间')!.trigger('click')
    expect(createAlgorithmRun).toHaveBeenCalledWith(expect.objectContaining({ source: 'official', algorithmId: 'iapf', channel: 'F3', mode: 'static' }))
  })
})
