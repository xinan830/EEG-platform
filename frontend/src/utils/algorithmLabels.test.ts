import { describe, expect, it } from 'vitest'
import { algorithmLabel } from './algorithmLabels'

describe('algorithmLabel', () => {
  it('localizes official display names without changing their persisted identity', () => {
    const label = algorithmLabel({ name: 'Official FAA', owner: 'platform-official' } as never)
    expect(label.name).toBe('额叶 Alpha 不对称性')
    expect(label.abbreviation).toBe('FAA')
  })

  it('provides readable calculation and result explanations for official algorithms', () => {
    const label = algorithmLabel({ name: 'Official THETA_BETA', owner: 'platform-official' } as never)
    expect(label.steps).toHaveLength(4)
    expect(label.steps.join(' ')).toContain('IAPF')
    expect(label.result).toContain('无单位比值')
  })
})
