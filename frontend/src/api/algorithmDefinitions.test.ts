import { afterEach, expect, it, vi } from 'vitest'
import { getDefinition, listDefinitionVersions } from './algorithmDefinitions'

afterEach(() => vi.unstubAllGlobals())

it('requests historical definitions through read-only endpoints', async () => {
  const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ definition_id: 'old-definition' }), { status: 200 }))
  vi.stubGlobal('fetch', fetchMock)

  await getDefinition('old/definition')
  await listDefinitionVersions('old/definition')

  expect(new URL(fetchMock.mock.calls[0][0]).pathname).toBe('/api/algorithm-definitions/old%2Fdefinition')
  expect(new URL(fetchMock.mock.calls[1][0]).pathname).toBe('/api/algorithm-definitions/old%2Fdefinition/versions')
  expect(fetchMock.mock.calls.every((call) => !call[1]?.method || call[1].method === 'GET')).toBe(true)
})
