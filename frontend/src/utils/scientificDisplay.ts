const DISPLAY_UNITS: Record<string, string> = {
  V: 'V', uV: 'µV', 'V^2': 'V²', 'uV^2': 'µV²', 'V^2/Hz': 'V²/Hz', 'uV^2/Hz': 'µV²/Hz',
  Hz: 'Hz', s: 's', ratio: '比值', percent: '%', dimensionless: '无量纲',
  'dB re 1 uV^2/Hz': 'dB re 1 µV²/Hz',
}

export function unitDisplay(unit: unknown): string {
  return typeof unit === 'string' ? DISPLAY_UNITS[unit] ?? unit : '—'
}

export function formatScientificValue(value: unknown, unit: unknown, digits = 4): string {
  if (typeof value !== 'number' || !Number.isFinite(value)) return '—'
  const label = unitDisplay(unit)
  return label === '—' ? value.toFixed(digits) : `${value.toFixed(digits)} ${label}`
}
