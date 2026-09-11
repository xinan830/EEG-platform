export const WAVEFORM_BINARY_ENCODING = 'float32-le-interleaved-v1'

const HEADER_BYTES = Float64Array.BYTES_PER_ELEMENT

export interface BinaryWaveformChunk {
  elapsedS: number
  values: Float32Array
}

/** 解析后端发送的 `float64 elapsed_s + interleaved float32 µV` 波形帧。 */
export function decodeWaveformBinary(buffer: ArrayBuffer, channelCount: number): BinaryWaveformChunk {
  if (!Number.isInteger(channelCount) || channelCount < 1) {
    throw new Error('无法解析缺少通道元数据的二进制波形')
  }
  if (buffer.byteLength < HEADER_BYTES || (buffer.byteLength - HEADER_BYTES) % Float32Array.BYTES_PER_ELEMENT !== 0) {
    throw new Error('二进制波形帧长度无效')
  }
  const values = new Float32Array(buffer, HEADER_BYTES)
  if (values.length % channelCount !== 0) {
    throw new Error('二进制波形帧与通道数不匹配')
  }
  return { elapsedS: new DataView(buffer).getFloat64(0, true), values }
}
