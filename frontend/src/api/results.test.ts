import { afterEach, expect, it, vi } from 'vitest'
import { getStructuredPreview } from './results'

afterEach(() => vi.unstubAllGlobals())

it('requests a bounded backend structured preview without frontend calculation', async () => {
  const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ run_id: 'run-1', arrays: {}, axes: {} }), { status: 200 }))
  vi.stubGlobal('fetch', fetchMock)

  await getStructuredPreview('run/1', 1234)

  const url = new URL(fetchMock.mock.calls[0][0])
  expect(url.pathname).toBe('/api/runs/run%2F1/structured-preview')
  expect(url.searchParams.get('max_cells')).toBe('1234')
})
