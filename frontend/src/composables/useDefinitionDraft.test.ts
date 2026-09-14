import { expect, it } from 'vitest'
import { useDefinitionDraft } from './useDefinitionDraft'

it('retains the last valid draft while advanced JSON is invalid', () => {
  const state = useDefinitionDraft()
  const before = state.draft.value.semver

  expect(state.applyJson('{ invalid')).toBe(false)

  expect(state.draft.value.semver).toBe(before)
  expect(state.jsonError.value).toContain('JSON')
})

it('applies a valid JSON document to the shared form draft', () => {
  const state = useDefinitionDraft()
  const next = { ...state.draft.value, semver: '0.2.0' }

  expect(state.applyJson(JSON.stringify(next))).toBe(true)

  expect(state.draft.value.semver).toBe('0.2.0')
  expect(state.jsonError.value).toBe('')
})

it('retains the last valid draft when JSON omits form-required fields', () => {
  const state = useDefinitionDraft()
  const before = state.draft.value.semver

  expect(state.applyJson(JSON.stringify({ semver: '9.0.0', graph: { nodes: [], outputs: [] } }))).toBe(false)

  expect(state.draft.value.semver).toBe(before)
  expect(state.jsonError.value).toContain('草稿必须包含')
})
