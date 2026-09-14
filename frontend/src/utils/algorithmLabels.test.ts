import { describe, expect, it } from 'vitest'
import { algorithmLabel } from './algorithmLabels'

describe('algorithmLabel', () => {
  it('localizes official display names without changing their persisted identity', () => {
    const label = algorithmLabel({ name: 'Official FAA', owner: 'platform-official' } as never)
    expect(label.name).toBe('额叶 Alpha 不对称性')
    expect(label.abbreviation).toBe('FAA')
  })
})
