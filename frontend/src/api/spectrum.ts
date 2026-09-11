import type { SpectrumResponse } from '../types/spectrum'
const API_BASE = import.meta.env.VITE_API_BASE ?? 'http://127.0.0.1:8000'
export async function getSpectrum(recordingId: string, startS: number, windowS: number, channels: string[]): Promise<SpectrumResponse> {
  const query = new URLSearchParams({ start_s: String(startS), window_s: String(windowS), channels: channels.join(',') })
  const response = await fetch(`${API_BASE}/api/recordings/${encodeURIComponent(recordingId)}/spectrum?${query}`)
  if (!response.ok) throw new Error('无法读取频谱分析结果')
  return await response.json() as SpectrumResponse
}
