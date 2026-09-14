import { afterEach, expect, it, vi } from 'vitest'
import { createDefinitionPreview } from './algorithmDefinitions'
import { DEFAULT_DRAFT } from '../types/algorithmDefinition'

afterEach(() => vi.unstubAllGlobals())

it('posts explicit scalar preview inputs and absolute recording range', async () => {
  const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ run_id: 'preview-1', status: 'completed', is_preview: true }), { status: 201 }))
  vi.stubGlobal('fetch', fetchMock)

  await createDefinitionPreview('recording/1', 10, 14, DEFAULT_DRAFT, { value: { value: 2, unit: 'ratio' } })

  expect(new URL(fetchMock.mock.calls[0][0]).pathname).toBe('/api/algorithm-definitions/preview-run')
  expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toMatchObject({
    recording_id: 'recording/1', time: { start_s: 10, end_s: 14 }, inputs: { value: { value: 2, unit: 'ratio' } },
  })
})
