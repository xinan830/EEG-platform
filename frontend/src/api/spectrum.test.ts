import { afterEach, expect, it, vi } from 'vitest'
import { ApiRequestError } from './client'
import { getConfiguredSpectrum, getSpectrum } from './spectrum'
import { getConfiguredSpectrogram } from './spectrogram'

afterEach(() => vi.unstubAllGlobals())

it('sends absolute start, fixed window and requested channel order', async () => {
  const fetchMock = vi.fn().mockResolvedValue(new Response('{}', { status: 200 }))
  vi.stubGlobal('fetch', fetchMock)

  await getSpectrum('rec/1', 14.6, 30, ['Oz', 'Fz', 'Pz'])

  const url = new URL(fetchMock.mock.calls[0][0])
  expect(url.pathname).toBe('/api/recordings/rec%2F1/spectrum')
  expect(url.searchParams.get('start_s')).toBe('14.6')
  expect(url.searchParams.get('window_s')).toBe('30')
  expect(url.searchParams.get('channels')).toBe('Oz,Fz,Pz')
})

it('posts the complete configurable analysis request', async () => {
  const fetchMock = vi.fn().mockResolvedValue(new Response('{}', { status: 200 }))
  vi.stubGlobal('fetch', fetchMock)
  const config = { mode: 'static', channels: ['F3'], time: { start_s: 10, end_s: 40 } }
  await getConfiguredSpectrum('r1', config)
  expect(fetchMock.mock.calls[0][1].method).toBe('POST')
  expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toEqual(config)
})

it('posts configured spectrogram requests through the common structured error path', async () => {
  const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ code: 'SPECTROGRAM_REQUEST_INVALID', message: '文件实际结束时间为 7.634 s', request_id: 'spectrogram-42' }), { status: 422 }))
  vi.stubGlobal('fetch', fetchMock)
  const config = { mode: 'spectrogram', channels: ['F3'], time: { start_s: 1, end_s: 7.635 }, dynamic_window_s: 10, refresh_step_s: 1 }
  await expect(getConfiguredSpectrogram('r/1', config)).rejects.toMatchObject({
    name: 'ApiRequestError', code: 'SPECTROGRAM_REQUEST_INVALID', requestId: 'spectrogram-42', message: '文件实际结束时间为 7.634 s',
  } satisfies Partial<ApiRequestError>)
  expect(fetchMock.mock.calls[0][1].method).toBe('POST')
  expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toEqual(config)
})
