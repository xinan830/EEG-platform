import type { SpectrogramResponse } from '../types/spectrogram'
import { request } from './client'
export async function getSpectrogram(recordingId: string, startS: number, windowS: number, channels: string[]): Promise<SpectrogramResponse> {
  const query = new URLSearchParams({ start_s: String(startS), window_s: String(windowS), channels: channels.join(',') })
  return request<SpectrogramResponse>(`/api/recordings/${encodeURIComponent(recordingId)}/spectrogram?${query}`)
}

export async function getConfiguredSpectrogram(recordingId: string, config: Record<string, unknown>): Promise<SpectrogramResponse> {
  return request<SpectrogramResponse>(`/api/recordings/${encodeURIComponent(recordingId)}/spectrogram/configured`, {
    method: 'POST', body: JSON.stringify(config),
  })
}
