// @vitest-environment jsdom
import { mount } from '@vue/test-utils'
import { nextTick } from 'vue'
import { describe, expect, it, vi } from 'vitest'
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

vi.mock('../api/algorithmDefinitions', () => ({
  listDefinitions: vi.fn(async () => [officialDefinition]),
  getDefinitionCapabilities: vi.fn(async () => ({ nodes: ['output'], units: ['ratio'], official_execution: {} })),
  listDefinitionVersions: vi.fn(async () => [officialVersion]),
  cloneDefinition: vi.fn(), compareDefinitionVersions: vi.fn(), createDefinition: vi.fn(), createDefinitionPreview: vi.fn(),
  createDefinitionVersion: vi.fn(), publishDefinitionVersion: vi.fn(), validateDefinition: vi.fn(),
}))

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
})
