import { normalizeApiError } from './client'

const message = normalizeApiError({ detail: 'Fz、Pz、Oz 映射不能重复' })
if (message !== 'Fz、Pz、Oz 映射不能重复') {
  throw new Error('API 错误消息未被保留')
}
