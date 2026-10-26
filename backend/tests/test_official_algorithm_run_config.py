from __future__ import annotations

import pytest
from pydantic import ValidationError

from app.models.official_algorithm_run import OfficialAlgorithmRunConfig


def test_official_run_config_accepts_one_tnk_group_mapping():
    algorithm_id = "tnk_biofeedback"
    role_fields = {"tnk_role_mapping": {"Fz": "Fz", "Pz": "Pz", "Oz": "Oz", "F3": "F3", "F4": "F4"}}
    config = OfficialAlgorithmRunConfig.model_validate({
        "algorithm_id": algorithm_id,
        "channel": "F3",
        "time": {"start_s": 0.0, "end_s": 10.0},
        **role_fields,
    })

    runtime_config = config.runtime_config()
    assert runtime_config["channel"] == "Fz"
    assert runtime_config["role_mapping"] == role_fields["tnk_role_mapping"]


def test_tnk_children_are_not_public_run_ids():
    with pytest.raises(ValidationError, match="Input should be"):
        OfficialAlgorithmRunConfig.model_validate({
            "algorithm_id": "tnk_faa",
            "channel": "F3",
            "time": {"start_s": 0.0, "end_s": 10.0},
        })
