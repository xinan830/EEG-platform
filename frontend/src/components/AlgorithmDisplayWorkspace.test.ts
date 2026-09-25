// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import { ref } from 'vue'
import { beforeEach, expect, it, vi } from 'vitest'
import type { AlgorithmCatalogContext } from '../composables/useAlgorithmCatalog'
import AlgorithmDisplayWorkspace from './AlgorithmDisplayWorkspace.vue'

const createAlgorithmRun = vi.fn()
vi.mock('../api/runs', () => ({
  createAlgorithmRun: (...args: unknown[]) => createAlgorithmRun(...args),
  getRun: vi.fn(async () => ({ status: 'completed', result_summary: { metric: { mode: 'dynamic', series: [] } } })),
}))

const iapf = {
  algorithm_id: 'iapf', display_name_zh: '个体 Alpha 峰频', abbreviation: 'IAPF', purpose_zh: '个体峰频',
  scientific_version: 'official-iapf-v2', availability: 'available', is_runnable: true,
  supported_modes: ['static', 'dynamic'], definition_id: 'official-iapf', output_unit: 'Hz',
}

function createCatalog(items: Array<typeof iapf> = [iapf]) {
  return {
    officialAlgorithms: ref(items), algorithms: ref(items.map((item) => ({ source: 'official', id: item.algorithm_id,
      version: item.scientific_version, parameters: [], dynamic_policy: {
        minimum_window_s: 4, window_options_s: [5, 10, 20, 30], default_window_s: 10,
        refresh_step_s: 1, allow_warmup: true,
      },
    }))),
    officialError: ref(''), refresh: vi.fn(async () => undefined),
  } as unknown as AlgorithmCatalogContext
}

function mountWorkspace(catalog = createCatalog()) {
  return mount(AlgorithmDisplayWorkspace, { props: {
    recording: { id: 'recording-1', channels: ['F3'] }, rangeStart: 0, rangeEnd: 30,
    channels: ['F3'], playbackPositionS: 10, playing: false, catalog,
  } as never })
}

beforeEach(() => createAlgorithmRun.mockReset().mockResolvedValue({ run_id: 'run-1', status: 'completed' }))

it('offers only official algorithms and disables unavailable modules', async () => {
  const catalog = createCatalog([{ ...iapf, algorithm_id: 'brainbeat', display_name_zh: 'BrainBeat',
    definition_id: 'official-brainbeat', availability: 'shadow_validation', is_runnable: false }])
  const wrapper = mountWorkspace(catalog)
  await flushPromises()

  expect(wrapper.text()).toContain('官方内置算法')
  expect(wrapper.text()).not.toContain('我的算法')
  expect((wrapper.get('[data-testid="official-algorithm-official-brainbeat"] input').element as HTMLInputElement).disabled).toBe(true)
})

it('submits a static official Run without a user Definition identity', async () => {
  const wrapper = mountWorkspace()
  await flushPromises()
  await wrapper.get('[data-testid="official-algorithm-official-iapf"] input').setValue(true)
  await wrapper.findAll('button').find((button) => button.text() === '计算此区间')!.trigger('click')

  expect(createAlgorithmRun).toHaveBeenCalledWith(expect.objectContaining({
    source: 'official', algorithmId: 'iapf', scientificVersion: 'official-iapf-v2',
    channel: 'F3', mode: 'static',
  }))
})

it('uses the backend dynamic policy for an official Run', async () => {
  const wrapper = mountWorkspace()
  await flushPromises()
  await wrapper.get('[data-testid="official-algorithm-official-iapf"] input').setValue(true)
  const card = wrapper.get('[data-testid="algorithm-config-official:iapf"]')
  await card.findAll('button').find((button) => button.text() === '动态')!.trigger('click')
  await wrapper.findAll('button').find((button) => button.text() === '启用播放同步')!.trigger('click')

  expect(createAlgorithmRun).toHaveBeenCalledWith(expect.objectContaining({
    source: 'official', algorithmId: 'iapf', dynamicWindowS: 10, refreshStepS: 1,
  }))
})
