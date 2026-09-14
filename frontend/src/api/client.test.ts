import { afterEach, expect, it, vi } from 'vitest'
import { ApiRequestError, request } from './client'

afterEach(() => vi.unstubAllGlobals())

it('retains the backend error code and request ID for UI state handling', async () => {
  vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({
    code: 'UNIT_MISMATCH', message: '单位不兼容', request_id: 'request-42',
  }), { status: 422 })))

  await expect(request('/api/example')).rejects.toMatchObject<ApiRequestError>({
    name: 'ApiRequestError', code: 'UNIT_MISMATCH', requestId: 'request-42', message: '单位不兼容',
  })
})
