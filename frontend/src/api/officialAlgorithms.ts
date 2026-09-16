import { listAlgorithms } from './algorithms'

export type OfficialAlgorithmCatalogItem = {
  algorithm_id: string
  display_name_zh: string
  abbreviation: string
  purpose_zh: string
  scientific_version: string
  implementation_identity: string
  execution_kind: string
  availability: 'shadow_validation' | 'available' | 'deprecated'
  is_runnable: boolean
  required_channel_roles: string[]
  supported_modes: string[]
  output_unit: string
  definition_id: string
  definition_version: string
}

export function listOfficialAlgorithms(): Promise<OfficialAlgorithmCatalogItem[]> {
  return listAlgorithms().then((items) => items.filter((item) => item.source === 'official').map((item) => ({
    algorithm_id: item.id,
    display_name_zh: item.display_name_zh,
    abbreviation: item.abbreviation,
    purpose_zh: item.description,
    scientific_version: item.version,
    implementation_identity: item.version,
    execution_kind: 'algorithm_runtime',
    availability: (item.availability === 'available' ? 'available' : 'deprecated') as OfficialAlgorithmCatalogItem['availability'],
    is_runnable: item.is_runnable,
    required_channel_roles: [],
    supported_modes: item.modes,
    output_unit: typeof item.output.unit === 'string' ? item.output.unit : '未知单位',
    definition_id: item.id,
    definition_version: item.version,
  })))
}
