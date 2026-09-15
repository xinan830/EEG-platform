/**
 * Keep an open debug workbench attached to the latest completed Run for the
 * selected algorithm. The fallback preserves the view while a replacement Run
 * is still queued or the result list is temporarily unavailable.
 */
export function currentAlgorithmDebugRun<T>(
  selection: { definitionId: string; fallbackRun: T },
  results: Record<string, { run?: T }>,
): T {
  return results[selection.definitionId]?.run ?? selection.fallbackRun
}
