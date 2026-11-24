from __future__ import annotations

import math

from app.api.analysis_windows import _json_safe


def test_dynamic_window_json_projection_converts_unavailable_numbers_to_null() -> None:
    payload = {
        "result": {
            "arrays": {"tnk_faa": [float("nan")], "finite": [1.5]},
            "evidence": {"nested": (float("inf"), -2.0)},
        }
    }

    projected = _json_safe(payload)

    assert projected["result"]["arrays"]["tnk_faa"] == [None]
    assert projected["result"]["arrays"]["finite"] == [1.5]
    assert projected["result"]["evidence"]["nested"] == [None, -2.0]
    assert math.isnan(payload["result"]["arrays"]["tnk_faa"][0])
