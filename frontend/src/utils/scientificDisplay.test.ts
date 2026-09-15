import { expect, it } from 'vitest'
import { formatScientificValue, unitDisplay } from './scientificDisplay'

it('uses one canonical display label for scientific units', () => {
  expect(unitDisplay('uV^2')).toBe('µV²')
  expect(unitDisplay('uV^2/Hz')).toBe('µV²/Hz')
  expect(unitDisplay('dB re 1 uV^2/Hz')).toBe('dB re 1 µV²/Hz')
  expect(unitDisplay('ratio')).toBe('比值')
})

it('does not guess unknown units or display non-finite values as numbers', () => {
  expect(unitDisplay('backend-unit-v2')).toBe('backend-unit-v2')
  expect(formatScientificValue(Number.NaN, 'uV^2')).toBe('—')
  expect(formatScientificValue(2.5, 'uV^2', 2)).toBe('2.50 µV²')
})
