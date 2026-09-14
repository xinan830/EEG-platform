"""Safety checks for the future immutable algorithm-definition executor."""

import pytest

from app.eeg_core.definition_engine import DefinitionEngineError, evaluate_formula, execute_graph, validate_graph
from app.eeg_core.primitives.types import Scalar
from app.eeg_core.primitives.units import Unit


def test_graph_executes_registered_scalar_nodes_in_topological_order():
    graph = {
        "nodes": [
            {"id": "sum", "type": "add", "inputs": {"left": "$input.a", "right": "$input.b"}},
            {"id": "result", "type": "divide", "inputs": {"left": "sum", "right": "$input.b"}},
        ],
        "outputs": ["result"],
    }
    result = execute_graph(graph, {"a": Scalar(2.0, Unit.V2), "b": Scalar(2.0, Unit.V2)})
    assert result["result"].value == 2.0
    assert result["result"].unit is Unit.DIMENSIONLESS


@pytest.mark.parametrize("graph,code", [
    ({"nodes": [{"id": "x", "type": "not_allowed", "inputs": {}}], "outputs": ["x"]}, "UNKNOWN_NODE"),
    ({"nodes": [{"id": "x", "type": "add", "inputs": {"left": "y", "right": "$input.a"}}, {"id": "y", "type": "add", "inputs": {"left": "x", "right": "$input.a"}}], "outputs": ["x"]}, "GRAPH_CYCLE"),
])
def test_graph_rejects_unknown_nodes_and_cycles_before_execution(graph, code):
    with pytest.raises(DefinitionEngineError) as exc:
        validate_graph(graph)
    assert exc.value.code == code


def test_formula_allows_only_named_scalars_arithmetic_and_ln():
    result = evaluate_formula("ln(right) - ln(left)", {"left": Scalar(2.0, Unit.V2), "right": Scalar(8.0, Unit.V2)})
    assert result.value == pytest.approx(1.38629436112)
    for bad in ("__import__('os')", "a.__class__", "items[0]", "sum(a)"):
        with pytest.raises(DefinitionEngineError, match="formula"):
            evaluate_formula(bad, {"a": Scalar(1.0, Unit.RATIO)})


def test_formula_preserves_division_by_zero_as_unavailable():
    result = evaluate_formula("a / b", {"a": Scalar(2.0, Unit.V2), "b": Scalar(0.0, Unit.V2)})
    assert result.value is None
    assert result.quality.reasons == ("division_by_zero",)


def test_graph_reports_incompatible_units_with_a_stable_code():
    graph = {"nodes": [{"id": "out", "type": "add", "inputs": {"left": "$input.power", "right": "$input.frequency"}}], "outputs": ["out"]}
    with pytest.raises(DefinitionEngineError) as exc:
        execute_graph(graph, {"power": Scalar(1.0, Unit.V2), "frequency": Scalar(1.0, Unit.HZ)})
    assert exc.value.code == "UNIT_MISMATCH"
