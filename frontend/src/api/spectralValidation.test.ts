import { describe, expect, it, vi } from 'vitest'
import { validateSpectralReference } from './spectralValidation'

describe('validateSpectralReference', () => {
  it('submits only range and channel order to the backend', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ validation_id: 'v1', passed: true }) })
    vi.stubGlobal('fetch', fetchMock)
    await validateSpectralReference('rec 1', 10, 40, ['F3', 'Fz'])
    expect(fetchMock).toHaveBeenCalledWith(
      'http://127.0.0.1:8000/api/recordings/rec%201/validations/spectral-reference',
      expect.objectContaining({ method: 'POST', body: JSON.stringify({ start_s: 10, end_s: 40, channels: ['F3', 'Fz'] }) }),
    )
  })
})
