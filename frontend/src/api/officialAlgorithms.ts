import { request } from './client'

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
  definition_id: string
  definition_version: string
}

export function listOfficialAlgorithms(): Promise<OfficialAlgorithmCatalogItem[]> {
  return request<{ algorithms: OfficialAlgorithmCatalogItem[] }>('/api/official-algorithms')
    .then((response) => response.algorithms)
}
