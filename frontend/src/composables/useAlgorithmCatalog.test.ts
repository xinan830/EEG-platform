import { beforeEach, expect, it, vi } from 'vitest'
import { useAlgorithmCatalog } from './useAlgorithmCatalog'

const api = vi.hoisted(() => ({
  listDefinitions: vi.fn(),
  listDefinitionVersions: vi.fn(),
  listOfficialAlgorithms: vi.fn(),
}))

vi.mock('../api/algorithmDefinitions', () => ({ listDefinitions: api.listDefinitions, listDefinitionVersions: api.listDefinitionVersions }))
vi.mock('../api/officialAlgorithms', () => ({ listOfficialAlgorithms: api.listOfficialAlgorithms }))

const ratioDefinition = {
  definition_id: 'ratio', name: 'Theta/Beta 比值', owner: 'user-private', status: 'testing' as const,
  description: '', created_at: '2026-09-15T00:00:00Z', updated_at: '2026-09-15T00:00:00Z',
}
const publishedRatioV1 = {
  version_id: 'ratio-v1', definition_id: 'ratio', semver: '1.0.0', state: 'published' as const,
  digest_sha256: 'digest', created_at: '2026-09-15T00:00:00Z', published_at: '2026-09-15T00:00:00Z',
  graph: { nodes: [], outputs: [] }, parameter_schema: {}, inputs: {}, outputs: {}, units: {}, quality_rules: {}, references: [],
}
const officialRbp = {
  algorithm_id: 'official-rbp', display_name_zh: '相对频段功率', abbreviation: 'RBP', purpose_zh: '功率占比',
  scientific_version: '1.0.0', implementation_identity: 'test', execution_kind: 'generic_research_primitives',
  availability: 'shadow_validation' as const, is_runnable: false, required_channel_roles: [], supported_modes: ['static'],
  definition_id: 'official-rbp-definition', definition_version: '1.0.0',
}

beforeEach(() => {
  api.listDefinitions.mockReset().mockResolvedValue([ratioDefinition])
  api.listDefinitionVersions.mockReset().mockResolvedValue([publishedRatioV1])
  api.listOfficialAlgorithms.mockReset().mockResolvedValue([officialRbp])
})

it('refreshes user and official catalogs independently', async () => {
  const catalog = useAlgorithmCatalog()

  await catalog.refresh()

  expect(catalog.definitions.value.map((item) => item.definition_id)).toEqual(['ratio'])
  expect(catalog.officialAlgorithms.value.map((item) => item.algorithm_id)).toEqual(['official-rbp'])
  expect(catalog.userError.value).toBe('')
  expect(catalog.officialError.value).toBe('')
})

it('preserves user definitions when the official catalog refresh fails', async () => {
  const catalog = useAlgorithmCatalog()
  await catalog.refresh()
  api.listOfficialAlgorithms.mockRejectedValueOnce(new Error('catalog unavailable'))

  await catalog.refresh()

  expect(catalog.definitions.value.map((item) => item.definition_id)).toEqual(['ratio'])
  expect(catalog.officialAlgorithms.value.map((item) => item.algorithm_id)).toEqual(['official-rbp'])
  expect(catalog.officialError.value).toBe('官方算法目录暂不可读取；我的算法不受影响。')
})

it('caches versions by definition id until the caller explicitly invalidates them', async () => {
  const catalog = useAlgorithmCatalog()

  await catalog.ensureVersions('ratio')
  await catalog.ensureVersions('ratio')
  catalog.removeDefinitionVersionCache('ratio')
  await catalog.ensureVersions('ratio')

  expect(api.listDefinitionVersions).toHaveBeenCalledTimes(2)
  expect(catalog.versionsByDefinition.value.ratio[0].semver).toBe('1.0.0')
})
