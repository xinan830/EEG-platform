/** CSS 像素的标准换算值；临床使用前仍应按实际显示器完成物理标定。 */
export const CSS_PIXELS_PER_MM = 96 / 25.4

export function mapWaveformValueToY(
  valueUv: number,
  laneCenterY: number,
  _laneHeight: number,
  sensitivityUvPerMm: number,
): number {
  // 灵敏度是 µV/mm：数值越小，给定振幅在屏幕上的偏移越大。
  // 不按通道高度裁剪，避免把高幅伪迹或尖波伪装成正常低幅活动。
  const displacement = valueUv / sensitivityUvPerMm * CSS_PIXELS_PER_MM
  return laneCenterY - displacement
}

export function isRenderableWaveformValue(value: number): boolean {
  return Number.isFinite(value)
}
