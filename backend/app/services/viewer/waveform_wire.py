"""连续波形 WebSocket 的紧凑二进制帧协议。"""

from __future__ import annotations

import struct

import numpy as np


WAVEFORM_BINARY_ENCODING = "float32-le-interleaved-v1"
_ELAPSED_HEADER_FORMAT = "<d"
_ELAPSED_HEADER_BYTES = struct.calcsize(_ELAPSED_HEADER_FORMAT)


def encode_waveform_binary(elapsed_s: float, samples_uv: np.ndarray) -> bytes:
    """编码 `float64 elapsed_s` 加 `(samples, channels)` 的交错 float32 微伏值。"""
    values = np.ascontiguousarray(samples_uv, dtype="<f4")
    if values.ndim != 2:
        raise ValueError("波形二进制帧必须是 (samples, channels) 矩阵")
    return struct.pack(_ELAPSED_HEADER_FORMAT, float(elapsed_s)) + values.tobytes()


def waveform_binary_header_bytes() -> int:
    """供协议测试引用，避免前后端在魔数上产生不一致。"""
    return _ELAPSED_HEADER_BYTES
