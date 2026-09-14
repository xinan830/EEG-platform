import { request } from './client'

export interface SpectralReferenceValidation {
  validation_id: string; status: string; passed: boolean; point_count: number; passed_point_count: number
  max_absolute_error: number; max_relative_error: number; pass_rate: number
  evidence: { scope: string; unit: string; channels: string[]; frequencies_hz: number[]; actual_range: { start_s: number; end_s: number; duration_s: number } }
}

export function validateSpectralReference(recordingId: string, startS: number, endS: number, channels: string[]): Promise<SpectralReferenceValidation> {
  return request(`/api/recordings/${encodeURIComponent(recordingId)}/validations/spectral-reference`, {
    method: 'POST', body: JSON.stringify({ start_s: startS, end_s: endS, channels }),
  })
}
