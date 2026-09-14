export interface SpectrogramResponse {
  recording_id: string
  window_start_s: number
  window_duration_s: number
  sfreq_hz?: number
  channels: string[]
  times_s: number[]
  frequencies_hz: number[]
  power: Record<string, number[][]>
  power_linear?: Record<string, number[][]>
  power_db?: Record<string, number[][]>
  band_power_timeseries?: Record<string, Record<'delta' | 'theta' | 'alpha' | 'beta', number[]>>
  custom_band_power_timeseries?: Record<string, number[]>
  custom_band?: { low_hz: number; high_hz: number; unit: string; integration: string; frequency_resolution_hz: number | null; frequency_points_hz: number[]; algorithm_version: string }
  units: string
  power_db_units?: string
  power_linear_units?: string
  band_power_timeseries_units?: string
  analysis_algorithm_version?: string
  spectrogram_contract_version?: string
  algorithm_version: string
  segment_s: number
  step_s: number
  baseline_algorithm_version?: string
  requested_config?: Record<string, unknown>
  execution_config?: Record<string, unknown>
  analysis_config_hash?: string
  requested_start_s?: number
  requested_end_s?: number
  requested_window_s?: number
  actual_start_s?: number
  actual_end_s?: number
  actual_duration_s?: number
  warmup?: boolean
  time_bins?: number
  frequency_bins?: number
  matrix_shape?: [number, number]
  first_center_s?: number | null
  last_center_s?: number | null
  quality?: { windows: Array<{ center_s: number; start_s: number; end_s: number; status: 'clean' | 'bad'; reason: string | null; peak_uv: number | null }>; clean_windows: number; total_windows: number; bad_windows: number }
  single_window_psd?: Record<string, number[]>
}
