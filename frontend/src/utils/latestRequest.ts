/** Keeps stale asynchronous responses from replacing newer UI state. */
export function createLatestRequestGuard() {
  let current = 0

  function begin() {
    current += 1
    return current
  }

  function isCurrent(requestId: number) {
    return requestId === current
  }

  return { begin, isCurrent }
}
