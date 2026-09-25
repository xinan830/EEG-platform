import { beforeEach, expect, it, vi } from 'vitest'
import { useAlgorithmCatalog } from './useAlgorithmCatalog'

const api = vi.hoisted(() => ({ listOfficialAlgorithms: vi.fn(), listAlgorithms: vi.fn() }))
vi.mock('../api/officialAlgorithms', () => ({ listOfficialAlgorithms: api.listOfficialAlgorithms }))
vi.mock('../api/algorithms', () => ({ listAlgorithms: api.listAlgorithms }))

const official = {
  algorithm_id: 'iapf', display_name_zh: '个体 Alpha 峰频率', is_runnable: true,
}

beforeEach(() => {
  api.listOfficialAlgorithms.mockReset().mockResolvedValue([official])
  api.listAlgorithms.mockReset().mockResolvedValue([{ source: 'official', id: 'iapf' }])
})

it('loads only official algorithm catalogs', async () => {
  const catalog = useAlgorithmCatalog()
  await catalog.refresh()

  expect(catalog.officialAlgorithms.value.map((item) => item.algorithm_id)).toEqual(['iapf'])
  expect(catalog.algorithms.value.map((item) => item.id)).toEqual(['iapf'])
  expect(catalog.officialError.value).toBe('')
})

it('keeps the last official catalog but reports a refresh failure', async () => {
  const catalog = useAlgorithmCatalog()
  await catalog.refresh()
  api.listOfficialAlgorithms.mockRejectedValueOnce(new Error('unavailable'))

  await catalog.refresh()

  expect(catalog.officialAlgorithms.value.map((item) => item.algorithm_id)).toEqual(['iapf'])
  expect(catalog.officialError.value).toBe('官方算法目录暂不可读取。')
})
