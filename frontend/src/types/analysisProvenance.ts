export interface AnalysisTimeRange {
  start_s: number
  end_s: number
}

export interface AnalysisProvenanceWelch {
  segment_s: number
  window: string
  overlap_fraction: number | null
  step_s: number
}

export interface AnalysisProvenanceFrequency {
  label_zh?: string
  low_hz: number | null
  high_hz: number | null
  point_count: number
  resolution_hz?: number | null
}

export interface AnalysisProvenanceMethod {
  kind: string
  label_zh: string
  detail_zh: string
}

export interface AnalysisProvenanceExtension {
  kind: 'spectral_band_power' | 'metric_inputs_output' | 'algorithm_calculation' | 'faa_paired_quality'
  data: Record<string, unknown>
}

export interface AnalysisProvenance {
  contract_version: 'analysis-provenance-v1'
  status: string
  analysis_type: string
  definition_version: string | null
  scientific_algorithm_version: string | null
  implementation_version: string
  config_sha256: string
  mode: string
  requested_range: AnalysisTimeRange | null
  actual_range: AnalysisTimeRange | null
  channel: string | null
  channel_mapping: Record<string, unknown>
  analysis_reference: unknown
  sfreq_hz: number | null
  filter: Record<string, unknown> | null
  preprocessing?: { kind: string; label_zh: string } | null
  method?: AnalysisProvenanceMethod | null
  welch: AnalysisProvenanceWelch | null
  frequency: AnalysisProvenanceFrequency | null
  quality_label_zh?: string | null
  quality: Record<string, unknown> | null
  extensions: AnalysisProvenanceExtension[]
}
