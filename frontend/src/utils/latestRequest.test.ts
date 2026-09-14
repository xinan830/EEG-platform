import { expect, it } from 'vitest'
import { createLatestRequestGuard } from './latestRequest'

it('rejects a stale response after a newer request starts', () => {
  const guard = createLatestRequestGuard()
  const first = guard.begin()
  const second = guard.begin()

  expect(guard.isCurrent(first)).toBe(false)
  expect(guard.isCurrent(second)).toBe(true)
})
