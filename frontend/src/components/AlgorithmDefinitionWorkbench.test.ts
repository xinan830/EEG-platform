// @vitest-environment jsdom
import { flushPromises, mount } from '@vue/test-utils'
import { ref } from 'vue'
import { expect, it, vi } from 'vitest'
import type { AlgorithmCatalogContext } from '../composables/useAlgorithmCatalog'
import AlgorithmDefinitionWorkbench from './AlgorithmDefinitionWorkbench.vue'

const catalog = {
  officialAlgorithms: ref([
    { algorithm_id: 'iapf', display_name_zh: '个体 Alpha 峰频率', abbreviation: 'IAPF',
      purpose_zh: '估计个体峰频', scientific_version: 'official-iapf-v2',
      supported_modes: ['static', 'dynamic'], is_runnable: true },
    { algorithm_id: 'brainbeat', display_name_zh: 'BrainBeat', abbreviation: 'BB',
      purpose_zh: '工程验证中', scientific_version: 'official-brainbeat-v1',
      supported_modes: [], is_runnable: false },
  ]),
  officialError: ref(''),
  refresh: vi.fn(async () => undefined),
} as unknown as AlgorithmCatalogContext

it('shows official algorithms without authoring controls or retired user entries', async () => {
  const wrapper = mount(AlgorithmDefinitionWorkbench, { props: { catalog } })
  await flushPromises()

  expect(wrapper.text()).toContain('个体 Alpha 峰频率')
  expect(wrapper.text()).toContain('official-iapf-v2')
  expect(wrapper.text()).not.toContain('我的算法')
  expect(wrapper.text()).not.toContain('新建草稿')
  expect(wrapper.text()).not.toContain('发布')
  expect(wrapper.text()).not.toContain('运行预览')
  expect(catalog.refresh).toHaveBeenCalled()

  await wrapper.findAll('.definition-list-item')[1].trigger('click')
  expect(wrapper.text()).toContain('暂不可运行')
})
