"""Persisted current official Definition metadata is immutable and idempotent."""

from app.algorithms.catalog import ensure_official_definitions, official_definition, official_definition_draft
from app.services.definitions import DefinitionService


def test_official_definition_metadata_retains_distinct_faa_brainbeat_and_iapf_semantics():
    assert official_definition("faa")["paired_quality"] is True
    assert official_definition("brainbeat")["independent_windows"] is True
    assert official_definition("brainbeat")["cross_window_ema"] is False
    assert official_definition("iapf")["sources"] == ["peak", "cog"]


def test_official_definitions_are_immutable_published_records(tmp_path):
    service = DefinitionService(tmp_path / "official-definitions.sqlite3")
    first = ensure_official_definitions(service)
    second = ensure_official_definitions(service)
    assert first == second
    assert len(service.list()) == 9
    assert official_definition_draft("rbp").graph["outputs"] == [
        "delta_rbp", "theta_rbp", "alpha_rbp", "beta_rbp", "gamma_rbp",
    ]
    assert official_definition_draft("iapf").quality_rules["execution_kind"] == "official_composite_run_adapter"
