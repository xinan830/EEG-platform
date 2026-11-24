"""Current Definition validation remains independent of retired execution."""

import pytest

from app.definitions.validation import DefinitionValidationError, validate_graph, validate_parameters


def test_graph_validation_returns_deterministic_dependency_order():
    nodes = validate_graph({"nodes": [
        {"id": "b", "type": "output", "inputs": {"value": "a"}},
        {"id": "a", "type": "output", "inputs": {"value": "$input.signal"}},
    ], "outputs": ["b"]})
    assert [node.node_id for node in nodes] == ["a", "b"]


@pytest.mark.parametrize("nodes,outputs,code", [
    ([{"id": "x", "type": "not_allowed"}], ["x"], "UNKNOWN_NODE"),
    ([{"id": "x", "type": "output", "inputs": {"value": "y"}},
      {"id": "y", "type": "output", "inputs": {"value": "x"}}], ["x"], "GRAPH_CYCLE"),
    ([{"id": "x", "type": "output", "inputs": {"value": "missing"}}], ["x"], "MISSING_INPUT"),
    ([{"id": "x", "type": "output"}], ["missing"], "MISSING_INPUT"),
    ([{"id": "x", "type": "output"}, {"id": "x", "type": "output"}], ["x"], "DEFINITION_INVALID"),
])
def test_graph_validation_preserves_structured_errors(nodes, outputs, code):
    with pytest.raises(DefinitionValidationError) as exc:
        validate_graph({"nodes": nodes, "outputs": outputs})
    assert exc.value.code == code
    assert isinstance(exc.value.details, dict)


@pytest.mark.parametrize("parameters", [{}, {"window": True}, {"window": 0}, {"window": 3, "extra": 1}])
def test_parameters_preserve_required_type_bounds_and_unknown_field_errors(parameters):
    schema = {"type": "object", "properties": {"window": {"type": "integer", "minimum": 1}},
              "required": ["window"], "additionalProperties": False}
    with pytest.raises(DefinitionValidationError) as exc:
        validate_parameters(schema, parameters)
    assert exc.value.code == "PARAMETER_INVALID"


def test_current_validation_never_calls_a_registered_calculator(monkeypatch):
    from app.eeg_core.primitives.registry import NODE_REGISTRY

    def forbidden(*args, **kwargs):
        raise AssertionError("validation must not execute a graph")

    monkeypatch.setitem(NODE_REGISTRY, "output", forbidden)
    validate_graph({"nodes": [{"id": "x", "type": "output"}], "outputs": ["x"]})
    validate_parameters({"type": "object"}, {})
