import { request } from './client'
import type { AlgorithmDefinition, AlgorithmDefinitionVersion, DefinitionCapabilities, DefinitionDraft, PreviewRun, Unit } from '../types/algorithmDefinition'

export function listDefinitions(): Promise<AlgorithmDefinition[]> {
  return request('/api/algorithm-definitions')
}

/** Remove a private definition only after the server has checked its provenance. */
export function deleteDefinition(definitionId: string): Promise<void> {
  return request(`/api/algorithm-definitions/${encodeURIComponent(definitionId)}`, { method: 'DELETE' })
}

export function getDefinitionCapabilities(): Promise<DefinitionCapabilities> {
  return request('/api/algorithm-definitions/capabilities')
}

export function listDefinitionVersions(definitionId: string): Promise<AlgorithmDefinitionVersion[]> {
  return request(`/api/algorithm-definitions/${encodeURIComponent(definitionId)}/versions`)
}

export function validateDefinition(draft: DefinitionDraft): Promise<{ valid: true; node_order: string[] }> {
  return request('/api/algorithm-definitions/validate', { method: 'POST', body: JSON.stringify({ draft }) })
}

export function createDefinition(name: string, description: string): Promise<AlgorithmDefinition> {
  return request('/api/algorithm-definitions', { method: 'POST', body: JSON.stringify({ name, description }) })
}

export function createDefinitionVersion(definitionId: string, draft: DefinitionDraft): Promise<AlgorithmDefinitionVersion> {
  return request(`/api/algorithm-definitions/${encodeURIComponent(definitionId)}/versions`, { method: 'POST', body: JSON.stringify(draft) })
}

export function publishDefinitionVersion(definitionId: string, semver: string): Promise<AlgorithmDefinitionVersion> {
  return request(`/api/algorithm-definitions/${encodeURIComponent(definitionId)}/versions/${encodeURIComponent(semver)}/publish`, { method: 'POST' })
}

export function cloneDefinition(definitionId: string, name?: string): Promise<AlgorithmDefinition> {
  return request(`/api/algorithm-definitions/${encodeURIComponent(definitionId)}/clone`, { method: 'POST', body: JSON.stringify({ name }) })
}

export function compareDefinitionVersions(definitionId: string, left: string, right: string): Promise<{ same_digest: boolean; graph_changed: boolean; parameter_schema_changed: boolean }> {
  return request(`/api/algorithm-definitions/${encodeURIComponent(definitionId)}/versions/${encodeURIComponent(left)}/compare/${encodeURIComponent(right)}`)
}

export function createDefinitionPreview(recordingId: string, startS: number, endS: number, draft: DefinitionDraft, inputs: Record<string, { value: number; unit: Unit }>): Promise<PreviewRun> {
  return request('/api/algorithm-definitions/preview-run', { method: 'POST', body: JSON.stringify({ recording_id: recordingId, time: { start_s: startS, end_s: endS }, draft, inputs }) })
}
