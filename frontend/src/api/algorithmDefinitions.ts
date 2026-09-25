import { request } from './client'
import type { AlgorithmDefinition, AlgorithmDefinitionVersion } from '../types/algorithmDefinition'

/** Historical Definitions are readable but cannot be edited or executed. */
export function listDefinitions(): Promise<AlgorithmDefinition[]> {
  return request('/api/algorithm-definitions')
}

export function getDefinition(definitionId: string): Promise<AlgorithmDefinition> {
  return request(`/api/algorithm-definitions/${encodeURIComponent(definitionId)}`)
}

export function listDefinitionVersions(definitionId: string): Promise<AlgorithmDefinitionVersion[]> {
  return request(`/api/algorithm-definitions/${encodeURIComponent(definitionId)}/versions`)
}

export function compareDefinitionVersions(definitionId: string, left: string, right: string): Promise<{ same_digest: boolean; graph_changed: boolean; parameter_schema_changed: boolean }> {
  return request(`/api/algorithm-definitions/${encodeURIComponent(definitionId)}/versions/${encodeURIComponent(left)}/compare/${encodeURIComponent(right)}`)
}
