"""Optional TFLite model boundary for the ANT-derived TNK metrics."""

from __future__ import annotations

import hashlib
import threading
from pathlib import Path
from typing import Any

import numpy as np


class LegacyModelError(RuntimeError):
    pass


def _interpreter(contents: bytes) -> Any:
    try:
        from ai_edge_litert.interpreter import Interpreter, OpResolverType
    except ImportError as exc:
        raise LegacyModelError("ai_edge_litert 未安装，神经网络指标不可用") from exc
    return Interpreter(
        model_content=contents,
        num_threads=1,
        experimental_op_resolver_type=OpResolverType.BUILTIN_WITHOUT_DEFAULT_DELEGATES,
    )


class TfliteModel:
    def __init__(self, path: str | Path, sha256: str, input_shape: tuple[int, ...], output_shape: tuple[int, ...]) -> None:
        self.path = Path(path)
        contents = self.path.read_bytes()
        actual = hashlib.sha256(contents).hexdigest()
        if actual != sha256:
            raise LegacyModelError(f"模型文件校验失败: expected={sha256}, actual={actual}")
        interpreter = _interpreter(contents)
        interpreter.allocate_tensors()
        inputs, outputs = interpreter.get_input_details(), interpreter.get_output_details()
        if (len(inputs) != 1 or len(outputs) != 1
                or tuple(inputs[0]["shape"]) != input_shape
                or tuple(outputs[0]["shape"]) != output_shape
                or inputs[0]["dtype"] != np.float32
                or outputs[0]["dtype"] != np.float32):
            raise LegacyModelError("模型输入输出不符合冻结 shape/dtype 约定")
        self._interpreter = interpreter
        self._input = inputs[0]["index"]
        self._output = outputs[0]["index"]
        # The interpreter is a reusable inference handle, not algorithm state.
        # Replay checkpoints must copy filters/history/smoothing state without
        # trying to pickle or clone the native interpreter. A process can still
        # share this handle across isolated stream states; serialize invocation
        # because the native tensor arena is mutable during predict().
        self._predict_lock = threading.RLock()
        self.input_shape = input_shape
        self.output_shape = output_shape

    def predict(self, window: np.ndarray) -> np.ndarray:
        values = np.asarray(window, dtype=np.float32)
        if values.shape != (self.input_shape[2],):
            raise LegacyModelError(f"模型输入长度必须为 {self.input_shape[2]}")
        if not np.all(np.isfinite(values)):
            raise LegacyModelError("模型输入包含非有限数值")
        with self._predict_lock:
            self._interpreter.set_tensor(self._input, values.reshape(self.input_shape))
            self._interpreter.invoke()
            result = np.asarray(self._interpreter.get_tensor(self._output)[0], dtype=np.float32)
        if result.shape != self.output_shape[1:] or not np.all(np.isfinite(result)):
            raise LegacyModelError("模型输出无效")
        return result

    def __deepcopy__(self, memo: dict[int, Any]):
        """Share the immutable model handle while replay state is copied."""
        memo[id(self)] = self
        return self


class CrimsonModel(TfliteModel):
    SHA256 = "17cf291169f0e11f7868f7784332bbc4fbef483e363fd8bb0f67c3eedd82f644"

    def __init__(self, path: str | Path) -> None:
        super().__init__(path, self.SHA256, (1, 1, 1250, 1), (1, 3))


class ZenliteModel(TfliteModel):
    SHA256 = "19fb19f948a2f86b59d01ce6ba2ca20a6034feb9941307184ee9ddbfe9517203"

    def __init__(self, path: str | Path) -> None:
        super().__init__(path, self.SHA256, (1, 1, 1200, 1), (1, 2))

