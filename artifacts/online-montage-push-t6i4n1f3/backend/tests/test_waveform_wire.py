import struct

import numpy as np

from app.services.waveform_wire import (
    WAVEFORM_BINARY_ENCODING,
    encode_waveform_binary,
    waveform_binary_header_bytes,
)


def test_waveform_binary_uses_float64_time_and_interleaved_float32_uv_values():
    payload = encode_waveform_binary(1.25, np.array([[1.0, -2.0], [3.5, 4.0]], dtype=float))
    header_size = waveform_binary_header_bytes()

    assert WAVEFORM_BINARY_ENCODING == "float32-le-interleaved-v1"
    assert struct.unpack("<d", payload[:header_size]) == (1.25,)
    assert np.frombuffer(payload[header_size:], dtype="<f4").tolist() == [1.0, -2.0, 3.5, 4.0]
