// @vitest-environment jsdom
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import { afterEach, describe, expect, it, vi } from 'vitest'
import AlgorithmDefinitionWorkbench from './AlgorithmDefinitionWorkbench.vue'

const officialDefinition = {
  definition_id: 'theta-beta', name: 'Official THETA_BETA', owner: 'platform-official', status: 'testing',
  description: 'Persisted definition description', created_at: '2026-01-01T00:00:00Z', updated_at: '2026-01-01T00:00:00Z',
}

const officialVersion = {
  version_id: 'version-1', definition_id: 'theta-beta', state: 'published', digest_sha256: 'digest', created_at: '2026-01-01T00:00:00Z', published_at: '2026-01-01T00:00:00Z',
  semver: '1.0.0', graph: { nodes: [{ id: 'out', type: 'output', inputs: {}, parameters: {} }], outputs: ['out'] },
  parameter_schema: {}, inputs: { official_result: { unit: 'ratio' } }, outputs: { out: { unit: 'ratio' } }, units: {},
  quality_rules: { execution_kind: 'official_composite_shadow_only' }, references: [],
}

const userDefinition = { ...officialDefinition, definition_id: 'my-ratio', name: '我的 Theta/Beta', owner: 'local-user', description: '我的用户算法' }
const userVersion = {
  ...officialVersion, definition_id: 'my-ratio', version_id: 'my-version',
  graph: { nodes: [{ id: 'calculation', type: 'divide', inputs: { left: '$input.input_left', right: '$input.input_right' }, parameters: {} }, { id: 'output', type: 'output', inputs: { source: 'calculation' }, parameters: {} }], outputs: ['output'] },
  inputs: { input_left: { feature: 'theta_power', unit: 'uV^2' }, input_right: { feature: 'beta_power', unit: 'uV^2' } },
  outputs: { output: { label: 'Theta/Beta 比值', unit: 'dimensionless' } }, quality_rules: {},
}

vi.mock('../api/algorithmDefinitions', () => ({
  listDefinitions: vi.fn(async () => [officialDefinition, userDefinition]),
  getDefinitionCapabilities: vi.fn(async () => ({ nodes: ['output'], units: ['ratio'], official_execution: {} })),
  listDefinitionVersions: vi.fn(async (id: string) => [id === 'my-ratio' ? userVersion : officialVersion]),
  cloneDefinition: vi.fn(), compareDefinitionVersions: vi.fn(), createDefinition: vi.fn(), createDefinitionPreview: vi.fn(),
  createDefinitionVersion: vi.fn(), deleteDefinition: vi.fn(async () => undefined), publishDefinitionVersion: vi.fn(), validateDefinition: vi.fn(),
}))

afterEach(() => vi.unstubAllGlobals())

describe('AlgorithmDefinitionWorkbench', () => {
  it('hides technical editor fields until developer details are explicitly opened', async () => {
    const wrapper = mount(AlgorithmDefinitionWorkbench, { props: { recording: { id: 'recording-1' } as never, startS: 0, endS: 30 } })
    await Promise.resolve()
    await nextTick()
    await Promise.resolve()
    await nextTick()

    expect(wrapper.text()).toContain('后端如何计算')
    expect(wrapper.text()).toContain('无单位比值')
    expect(wrapper.text()).not.toContain('高级 JSON')

    await wrapper.get('.definition-mode').trigger('click')
    expect(wrapper.text()).toContain('高级 JSON')
    expect(wrapper.text()).toContain('official_result')
  })

  it('shows a user-created formula in plain Chinese without developer details', async () => {
    const wrapper = mount(AlgorithmDefinitionWorkbench, { props: { recording: { id: 'recording-1' } as never, startS: 0, endS: 30 } })
    await Promise.resolve()
    await nextTick()
    await Promise.resolve()
    await nextTick()

    await wrapper.findAll('.definition-list-item')[1].trigger('click')
    await Promise.resolve()
    await nextTick()

    expect(wrapper.text()).toContain('Theta 功率 ÷ Beta 功率')
    expect(wrapper.text()).toContain('输入 A：Theta 功率')
    expect(wrapper.text()).toContain('输出名称：Theta/Beta 比值')
    expect(wrapper.text()).not.toContain('请在“开发者详情”中查看')
  })

  it('offers deletion only for private algorithms and removes them through the backend after confirmation', async () => {
    const api = await import('../api/algorithmDefinitions')
    const confirmation = vi.fn(() => true)
    vi.stubGlobal('confirm', confirmation)
    const wrapper = mount(AlgorithmDefinitionWorkbench, { props: { recording: { id: 'recording-1' } as never, startS: 0, endS: 30 } })
    await Promise.resolve()
    await nextTick()
    await Promise.resolve()
    await nextTick()

    expect(wrapper.findAll('.definition-delete')).toHaveLength(1)
    expect(wrapper.findAll('.definition-delete')[0].attributes('aria-label')).toContain('我的 Theta/Beta')
    await wrapper.find('.definition-delete').trigger('click')
    await Promise.resolve()
    await nextTick()

    expect(confirmation).toHaveBeenCalledOnce()
    expect(confirmation).toHaveBeenCalledWith(expect.stringContaining('历史分析结果会保留'))
    expect(confirmation).toHaveBeenCalledWith(expect.stringContaining('未完成的分析任务将被取消'))
    expect(api.deleteDefinition).toHaveBeenCalledWith('my-ratio')
    expect(wrapper.text()).not.toContain('我的 Theta/Beta')
  })
})
