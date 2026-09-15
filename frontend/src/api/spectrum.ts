import type { SpectrumResponse } from '../types/spectrum'
import { request } from './client'
export async function getSpectrum(recordingId: string, startS: number, windowS: number, channels: string[]): Promise<SpectrumResponse> {
  const query = new URLSearchParams({ start_s: String(startS), window_s: String(windowS), channels: channels.join(',') })
  return request<SpectrumResponse>(`/api/recordings/${encodeURIComponent(recordingId)}/spectrum?${query}`)
}
export async function getConfiguredSpectrum(recordingId: string, config: Record<string, unknown>): Promise<SpectrumResponse> {
  return request<SpectrumResponse>(`/api/recordings/${encodeURIComponent(recordingId)}/spectrum/configured`, { method: 'POST', body: JSON.stringify(config) })
}
