import type { SpectrogramResponse } from '../types/spectrogram'
const API_BASE = import.meta.env.VITE_API_BASE ?? 'http://127.0.0.1:8000'
export async function getSpectrogram(recordingId: string, startS: number, windowS: number, channels: string[]): Promise<SpectrogramResponse> {
  const query = new URLSearchParams({ start_s: String(startS), window_s: String(windowS), channels: channels.join(',') })
  const response = await fetch(`${API_BASE}/api/recordings/${encodeURIComponent(recordingId)}/spectrogram?${query}`)
  if (!response.ok) throw new Error('无法读取时频图')
  return await response.json() as SpectrogramResponse
}
