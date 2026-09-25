import { afterEach, expect, it, vi } from 'vitest'
import { createAlgorithmRun } from './runs'

afterEach(() => vi.unstubAllGlobals())

it('sends the locked dynamic window contract only for official dynamic runs', async () => {
  const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ run_id: 'run-2', status: 'queued' }), { status: 202 }))
  vi.stubGlobal('fetch', fetchMock)

  await createAlgorithmRun({ source: 'official', recordingId: 'recording-1', algorithmId: 'iapf', scientificVersion: 'official-iapf-v2', channel: 'F3', startS: 10, endS: 40, mode: 'dynamic' })

  expect(JSON.parse(fetchMock.mock.calls[0][1].body).config).toEqual({
    algorithm_id: 'iapf', scientific_version: 'official-iapf-v2', channel: 'F3', time: { start_s: 10, end_s: 40 }, mode: 'dynamic', dynamic_window_s: 10, refresh_step_s: 1,
  })
})

it('creates an official IAPF run without a user definition identity', async () => {
  const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ run_id: 'run-3', status: 'queued' }), { status: 202 }))
  vi.stubGlobal('fetch', fetchMock)

  await createAlgorithmRun({ source: 'official', recordingId: 'recording-1', algorithmId: 'iapf', scientificVersion: 'official-iapf-v2', channel: 'Fz', startS: 0, endS: 30 })

  expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toEqual({
    recording_id: 'recording-1', analysis_type: 'official_algorithm',
    config: { algorithm_id: 'iapf', scientific_version: 'official-iapf-v2', channel: 'Fz', time: { start_s: 0, end_s: 30 }, mode: 'static' },
  })
})

it('creates official Theta/Beta without inventing a semantic channel mapping in the browser', async () => {
  const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ run_id: 'run-4', status: 'queued' }), { status: 202 }))
  vi.stubGlobal('fetch', fetchMock)

  await createAlgorithmRun({ source: 'official', recordingId: 'recording-1', algorithmId: 'theta_beta', scientificVersion: 'official-theta-beta-v2', channel: 'F3', startS: 0, endS: 30 })

  expect(JSON.parse(fetchMock.mock.calls[0][1].body)).toEqual({
    recording_id: 'recording-1', analysis_type: 'official_algorithm',
    config: { algorithm_id: 'theta_beta', scientific_version: 'official-theta-beta-v2', channel: 'F3', time: { start_s: 0, end_s: 30 }, mode: 'static' },
  })
})

it('sends both explicit FAA source channels without inferring a mapping', async () => {
  const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ run_id: 'run-5', status: 'queued' }), { status: 202 }))
  vi.stubGlobal('fetch', fetchMock)

  await createAlgorithmRun({ source: 'official', recordingId: 'recording-1', algorithmId: 'faa', scientificVersion: 'official-faa-v1', channel: 'F3', f4Channel: 'F4', startS: 0, endS: 30 })

  expect(JSON.parse(fetchMock.mock.calls[0][1].body).config).toEqual({ algorithm_id: 'faa', scientific_version: 'official-faa-v1', channel: 'F3', f4_channel: 'F4', time: { start_s: 0, end_s: 30 }, mode: 'static' })
})
