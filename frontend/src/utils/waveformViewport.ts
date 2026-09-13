export interface Viewport {
  start: number
  duration: number
}

interface ZoomViewportInput extends Viewport {
  total: number
  anchorRatio: number
  deltaY: number
}

interface MoveViewportInput extends Viewport {
  total: number
  deltaX: number
  width: number
}

const MIN_VIEW_DURATION_S = 2

export function clampViewportStart(start: number, duration: number, total: number): number {
  return Math.max(0, Math.min(Math.max(0, total - duration), start))
}

/** Playback stays on fixed pages and changes page only at exact boundaries. */
export function playbackPageStart(position: number, duration: number, total?: number): number {
  const requested = Math.floor((Math.max(0, position) + 1e-9) / duration) * duration
  if (total === undefined) return requested
  const lastPage = Math.max(0, Math.floor(Math.max(0, total - Number.EPSILON) / duration) * duration)
  return Math.min(requested, lastPage)
}

export function clampPageViewportStart(
  start: number,
  viewDuration: number,
  pageStart: number,
  pageDuration: number,
): number {
  const maximum = pageStart + Math.max(0, pageDuration - viewDuration)
  return Math.max(pageStart, Math.min(maximum, start))
}

/** Previous/next navigation always lands on fixed page boundaries. */
export function pagedViewportStart(
  currentStart: number,
  direction: -1 | 1,
  duration: number,
  total: number,
): number {
  const lastPage = Math.max(0, Math.floor(Math.max(0, total - Number.EPSILON) / duration))
  const pagePosition = currentStart / duration
  const requestedPage = direction === 1
    ? Math.floor(pagePosition + Number.EPSILON) + 1
    : Math.ceil(pagePosition - Number.EPSILON) - 1
  const targetPage = Math.max(0, Math.min(lastPage, requestedPage))
  return targetPage * duration
}

export function zoomViewport({ start, duration, total, anchorRatio, deltaY }: ZoomViewportInput): Viewport {
  const safeTotal = Math.max(MIN_VIEW_DURATION_S, total)
  const safeRatio = Math.max(0, Math.min(1, anchorRatio))
  const nextDuration = Math.max(
    MIN_VIEW_DURATION_S,
    Math.min(safeTotal, duration * (deltaY > 0 ? 1.2 : 0.82)),
  )
  const anchorTime = start + duration * safeRatio
  return {
    duration: nextDuration,
    start: clampViewportStart(anchorTime - nextDuration * safeRatio, nextDuration, safeTotal),
  }
}

export function moveViewportByPixels({ start, duration, total, deltaX, width }: MoveViewportInput): Viewport {
  const nextStart = start - deltaX / Math.max(1, width) * duration
  return {
    duration,
    start: clampViewportStart(nextStart, duration, total),
  }
}
