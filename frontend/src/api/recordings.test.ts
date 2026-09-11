import { afterEach, describe, expect, it, vi } from 'vitest'
import { getAlgorithmCheck, getWaveformWindow } from './recordings'

afterEach(() => vi.unstubAllGlobals())

function mockResponse() {
  const fetchMock = vi.fn().mockResolvedValue(new Response('{}', { status: 200, headers: { 'Content-Type': 'application/json' } }))
  vi.stubGlobal('fetch', fetchMock)
  return fetchMock
}

const rows = [{ name: 'Left', terms: [{ channel: 'F3', weight: 1 }, { channel: 'Cz', weight: -1 }] }]

describe('自定义 Montage API 参数', () => {
  it('静态窗口携带结构化定义', async () => {
    const fetchMock = mockResponse()
    await getWaveformWindow('rec-1', { montage: 'custom_bipolar', customMontage: rows })
    const url = new URL(fetchMock.mock.calls[0][0])
    expect(url.searchParams.get('montage')).toBe('custom_bipolar')
    expect(JSON.parse(url.searchParams.get('custom_montage') ?? '')).toEqual(rows)
  })

  it('算法检验携带同一份结构化定义', async () => {
    const fetchMock = mockResponse()
    await getAlgorithmCheck('rec-1', { timeS: 5, montage: 'custom_bipolar', customMontage: rows })
    const url = new URL(fetchMock.mock.calls[0][0])
    expect(JSON.parse(url.searchParams.get('custom_montage') ?? '')).toEqual(rows)
  })
})
