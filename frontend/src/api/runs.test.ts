import { afterEach, expect, it, vi } from 'vitest'
import { createDefinitionMetricRun, createOfficialAlgorithmRun } from './runs'

afterEach(() => vi.unstubAllGlobals())

it('creates a normal definition metric run with the selected channel and range', async () => {
  const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ run_id: 'run-1', status: 'queued' }), { status: 202 }))
  vi.stubGlobal('fetch', fetchMock)

  await createDefinitionMetricRun({ recordingId: 'recording-1', definitionId: 'definition-1', definitionVersion: '1.0.0', channel: 'F3', startS: 10, endS: 40 })

  expect(new URL(fetchMock.mock.calls[0][0]).pathname).toBe('/api/runs')
  expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toEqual({
    recording_id: 'recording-1', analysis_type: 'definition_metric', definition_id: 'definition-1', definition_version: '1.0.0',
    config: { channel: 'F3', time: { start_s: 10, end_s: 40 } },
  })
})

it('sends the locked dynamic window contract only for dynamic runs', async () => {
  const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ run_id: 'run-2', status: 'queued' }), { status: 202 }))
  vi.stubGlobal('fetch', fetchMock)

  await createDefinitionMetricRun({ recordingId: 'recording-1', definitionId: 'definition-1', definitionVersion: '1.0.0', channel: 'F3', startS: 10, endS: 40, mode: 'dynamic' })

  expect(JSON.parse(fetchMock.mock.calls[0][1].body).config).toEqual({
    channel: 'F3', time: { start_s: 10, end_s: 40 }, mode: 'dynamic', dynamic_window_s: 10, refresh_step_s: 1,
  })
})

it('creates an official IAPF run without a user definition identity', async () => {
  const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ run_id: 'run-3', status: 'queued' }), { status: 202 }))
  vi.stubGlobal('fetch', fetchMock)

  await createOfficialAlgorithmRun({ recordingId: 'recording-1', algorithmId: 'iapf', channel: 'Fz', startS: 0, endS: 30 })

  expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toEqual({
    recording_id: 'recording-1', analysis_type: 'official_algorithm',
    config: { algorithm_id: 'iapf', channel: 'Fz', time: { start_s: 0, end_s: 30 }, mode: 'static' },
  })
})

it('creates official Theta/Beta without inventing a semantic channel mapping in the browser', async () => {
  const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ run_id: 'run-4', status: 'queued' }), { status: 202 }))
  vi.stubGlobal('fetch', fetchMock)

  await createOfficialAlgorithmRun({ recordingId: 'recording-1', algorithmId: 'theta_beta', channel: 'F3', startS: 0, endS: 30 })

  expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toEqual({
    recording_id: 'recording-1', analysis_type: 'official_algorithm',
    config: { algorithm_id: 'theta_beta', channel: 'F3', time: { start_s: 0, end_s: 30 }, mode: 'static' },
  })
})
