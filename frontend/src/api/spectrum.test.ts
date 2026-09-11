import { afterEach, expect, it, vi } from 'vitest'
import { getSpectrum } from './spectrum'

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
