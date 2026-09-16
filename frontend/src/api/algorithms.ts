import { request } from './client'

export type AlgorithmParameter = {
  key: string
  label_zh: string
  value_type: 'string' | 'number' | 'integer' | 'boolean' | 'enum'
  required: boolean
  default: unknown
  unit?: string | null
  affects_science: boolean
  visibility: 'user' | 'advanced' | 'developer'
  options?: Array<{ value: string | number | boolean; label_zh: string }>
  description_zh?: string
}

export type AlgorithmCatalogItem = {
  source: 'official' | 'user'
  id: string
  version: string
  display_name_zh: string
  abbreviation: string
  description: string
  parameters: AlgorithmParameter[] | Record<string, unknown>
  modes: string[]
  output: Record<string, unknown>
  availability: string
  is_runnable: boolean
}

export function listAlgorithms(): Promise<AlgorithmCatalogItem[]> {
  return request<{ algorithms: AlgorithmCatalogItem[] }>('/api/algorithms').then((response) => response.algorithms)
}
