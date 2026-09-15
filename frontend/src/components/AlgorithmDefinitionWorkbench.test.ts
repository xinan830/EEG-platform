// @vitest-environment jsdom
import { mount } from '@vue/test-utils'
import { nextTick, ref } from 'vue'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { AlgorithmCatalogContext } from '../composables/useAlgorithmCatalog'
import AlgorithmDefinitionWorkbench from './AlgorithmDefinitionWorkbench.vue'

const api = vi.hoisted(() => ({
  getDefinitionCapabilities: vi.fn(), cloneDefinition: vi.fn(), compareDefinitionVersions: vi.fn(), createDefinition: vi.fn(),
  createDefinitionPreview: vi.fn(), createDefinitionVersion: vi.fn(), deleteDefinition: vi.fn(), publishDefinitionVersion: vi.fn(), validateDefinition: vi.fn(),
}))

vi.mock('../api/algorithmDefinitions', () => api)

const officialDefinition = {
  definition_id: 'theta-beta', name: 'Official THETA_BETA', owner: 'platform-official', status: 'testing' as const,
  description: 'Persisted definition description', created_at: '2026-01-01T00:00:00Z', updated_at: '2026-01-01T00:00:00Z',
}
const officialVersion = {
  version_id: 'version-1', definition_id: 'theta-beta', state: 'published' as const, digest_sha256: 'digest', created_at: '2026-01-01T00:00:00Z', published_at: '2026-01-01T00:00:00Z',
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

function createCatalog() {
  const definitions = ref([officialDefinition, userDefinition])
  const versionsByDefinition = ref<Record<string, unknown[]>>({ 'theta-beta': [officialVersion], 'my-ratio': [userVersion] })
  return {
    definitions, officialAlgorithms: ref([]), versionsByDefinition, userError: ref(''), officialError: ref(''), loading: ref(false),
    refresh: vi.fn(async () => undefined),
    ensureVersions: vi.fn(async (id: string) => versionsByDefinition.value[id] ?? []),
    removeDefinitionVersionCache: vi.fn(),
  } as unknown as AlgorithmCatalogContext
}

function mountWorkbench(catalog = createCatalog()) {
  return { catalog, wrapper: mount(AlgorithmDefinitionWorkbench, { props: { recording: { id: 'recording-1' } as never, startS: 0, endS: 30, catalog } }) }
}

async function settle() {
  await Promise.resolve()
  await nextTick()
  await Promise.resolve()
  await nextTick()
}

afterEach(() => vi.unstubAllGlobals())

describe('AlgorithmDefinitionWorkbench', () => {
  it('renders its shared catalog rather than making a second definition-list request', async () => {
    api.getDefinitionCapabilities.mockResolvedValue({ nodes: ['output'], units: ['ratio'], official_execution: {} })
    const { catalog, wrapper } = mountWorkbench()
    await settle()

    expect(wrapper.findAll('.definition-list-item')).toHaveLength(2)
    expect(wrapper.text()).toContain('Theta/Beta 比值')
    expect(wrapper.text()).toContain('我的 Theta/Beta')
    expect(catalog.refresh).toHaveBeenCalledTimes(1)
  })

  it('shows a user-created formula in plain Chinese without developer details', async () => {
    api.getDefinitionCapabilities.mockResolvedValue({ nodes: ['output'], units: ['ratio'], official_execution: {} })
    const { wrapper } = mountWorkbench()
    await settle()
    await wrapper.findAll('.definition-list-item')[1].trigger('click')
    await settle()

    expect(wrapper.text()).toContain('Theta 功率 ÷ Beta 功率')
    expect(wrapper.text()).toContain('输入 A：Theta 功率')
    expect(wrapper.text()).toContain('输出名称：Theta/Beta 比值')
    expect(wrapper.text()).not.toContain('请在“开发者详情”中查看')
  })

  it('refreshes the shared catalog after deleting a private algorithm', async () => {
    api.getDefinitionCapabilities.mockResolvedValue({ nodes: ['output'], units: ['ratio'], official_execution: {} })
    api.deleteDefinition.mockResolvedValue(undefined)
    const confirmation = vi.fn(() => true)
    vi.stubGlobal('confirm', confirmation)
    const { catalog, wrapper } = mountWorkbench()
    vi.mocked(catalog.refresh).mockImplementation(async () => { catalog.definitions.value = [officialDefinition] })
    await settle()

    await wrapper.find('.definition-delete').trigger('click')
    await settle()

    expect(confirmation).toHaveBeenCalledWith(expect.stringContaining('历史分析结果会保留'))
    expect(api.deleteDefinition).toHaveBeenCalledWith('my-ratio')
    expect(catalog.removeDefinitionVersionCache).toHaveBeenCalledWith('my-ratio')
    expect(catalog.refresh).toHaveBeenCalled()
    expect(wrapper.text()).not.toContain('我的 Theta/Beta')
  })
})
