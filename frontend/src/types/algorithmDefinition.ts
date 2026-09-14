export type Unit = 'V' | 'uV' | 'V^2' | 'uV^2' | 'V^2/Hz' | 'uV^2/Hz' | 'Hz' | 's' | 'ratio' | 'percent' | 'dimensionless' | 'dB re 1 uV^2/Hz'

export interface DefinitionDraft {
  semver: string
  graph: { nodes: DefinitionNode[]; outputs: string[] }
  parameter_schema: Record<string, unknown>
  inputs: Record<string, unknown>
  outputs: Record<string, unknown>
  units: Record<string, unknown>
  quality_rules: Record<string, unknown>
  references: string[]
}

export interface DefinitionNode {
  id: string
  type: string
  inputs: Record<string, string>
  parameters: Record<string, unknown>
}

export interface AlgorithmDefinition {
  definition_id: string
  name: string
  owner: string
  status: 'draft' | 'testing' | 'validated_engineering' | 'research_use' | 'deprecated' | 'archived'
  description: string
  created_at: string
  updated_at: string
}

export interface AlgorithmDefinitionVersion extends DefinitionDraft {
  version_id: string
  definition_id: string
  state: 'draft' | 'published'
  digest_sha256: string
  created_at: string
  published_at: string | null
}

export interface DefinitionCapabilities {
  nodes: string[]
  units: Unit[]
  official_execution: Record<string, 'generic_research_primitives' | 'official_composite_shadow_only'>
}

export interface PreviewRun {
  run_id: string
  status: 'completed' | 'gate_failed' | 'failed'
  is_preview: true
  requested_range: { start_s: number; end_s: number }
  actual_range: { start_s: number; end_s: number } | null
  result_summary: {
    preview: true
    outputs: Record<string, { value: number | null; unit: Unit; quality: { status: 'clean' | 'bad'; reasons: string[] } }>
  } | null
  error: { code: string; message: string } | null
}

export const DEFAULT_DRAFT: DefinitionDraft = {
  semver: '0.1.0',
  graph: { nodes: [{ id: 'out', type: 'output', inputs: { source: '$input.value' }, parameters: {} }], outputs: ['out'] },
  parameter_schema: { type: 'object', additionalProperties: false },
  inputs: { value: { type: 'Scalar', unit: 'ratio' } },
  outputs: { out: { type: 'Scalar', unit: 'ratio' } },
  units: { input: 'ratio', output: 'ratio' },
  quality_rules: {},
  references: [],
}
