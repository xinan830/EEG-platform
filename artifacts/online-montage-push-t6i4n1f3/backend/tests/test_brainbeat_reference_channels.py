"""Brainbeat keeps explicit electrode roles across reference choices."""

import pytest

from app.algorithms.brainbeat.config import BrainbeatConfig


@pytest.mark.parametrize("fz,pz", [("Fz", "Pz"), ("Fz-REF", "Pz-REF"),
                                  ("Fz-AVG", "Pz-AVG"), ("fz-avg", "pz-avg")])
def test_reference_names_preserve_roles(fz, pz):
    config = BrainbeatConfig(channel=fz, secondary_channel=pz, mode="static", start_s=0, end_s=30)
    assert (config.channel, config.pz_channel) == (fz, pz)


@pytest.mark.parametrize("fz,pz", [("Fz-Pz", "Pz-REF"), ("Pz-AVG", "Fz-AVG"),
                                  ("Fz-AVG", "Fz-AVG"), ("F3-AVG", "Pz-AVG")])
def test_wrong_or_bipolar_roles_are_rejected(fz, pz):
    with pytest.raises(ValueError):
        BrainbeatConfig(channel=fz, secondary_channel=pz, mode="static", start_s=0, end_s=30)
