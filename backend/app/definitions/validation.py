"""Validate immutable research Definition graphs and parameter metadata."""

from __future__ import annotations

from collections.abc import Mapping
from dataclasses import dataclass

from app.eeg_core.primitives.registry import UnknownNodeError, resolve_node


class DefinitionValidationError(ValueError):
    """A structured validation error that is safe to expose through an API."""

    def __init__(self, code: str, message: str, details: dict[str, object] | None = None):
        super().__init__(message)
        self.code = code
        self.details = details or {}


@dataclass(frozen=True)
class GraphNode:
    node_id: str
    node_type: str
    inputs: dict[str, str]
    parameters: dict[str, object]


def validate_graph(graph: Mapping[str, object]) -> tuple[GraphNode, ...]:
    """Validate closed node IDs and return a deterministic topological order."""
    raw_nodes = graph.get("nodes")
    outputs = graph.get("outputs")
    if not isinstance(raw_nodes, list) or not raw_nodes or not isinstance(outputs, list) or not outputs:
        raise DefinitionValidationError("DEFINITION_INVALID", "graph requires non-empty nodes and outputs")
    nodes: dict[str, GraphNode] = {}
    for raw in raw_nodes:
        if not isinstance(raw, dict):
            raise DefinitionValidationError("DEFINITION_INVALID", "each graph node must be an object")
        node_id, node_type = raw.get("id"), raw.get("type")
        inputs, parameters = raw.get("inputs", {}), raw.get("parameters", {})
        if not isinstance(node_id, str) or not node_id or not isinstance(node_type, str) or not node_type:
            raise DefinitionValidationError("DEFINITION_INVALID", "node id and type must be non-empty strings")
        if node_id in nodes or not isinstance(inputs, dict) or not isinstance(parameters, dict):
            raise DefinitionValidationError("DEFINITION_INVALID", "node IDs must be unique and bindings/parameters must be objects")
        try:
            resolve_node(node_type)
        except UnknownNodeError as exc:
            raise DefinitionValidationError("UNKNOWN_NODE", str(exc), {"node_id": node_id, "node_type": node_type}) from exc
        if any(not isinstance(value, str) or not value for value in inputs.values()):
            raise DefinitionValidationError("MISSING_INPUT", "node bindings must name an input or node output", {"node_id": node_id})
        nodes[node_id] = GraphNode(node_id, node_type, dict(inputs), dict(parameters))
    for node in nodes.values():
        missing = [source for source in node.inputs.values() if source not in nodes and not source.startswith("$input.")]
        if missing:
            raise DefinitionValidationError("MISSING_INPUT", "node input source does not exist", {"node_id": node.node_id, "sources": missing})
    requested_outputs = tuple(outputs)
    if any(not isinstance(item, str) or item not in nodes for item in requested_outputs):
        raise DefinitionValidationError("MISSING_INPUT", "graph output must name an existing node")
    remaining = {node.node_id: {source for source in node.inputs.values() if source in nodes} for node in nodes.values()}
    ordered: list[GraphNode] = []
    while remaining:
        ready = sorted(node_id for node_id, dependencies in remaining.items() if not dependencies)
        if not ready:
            raise DefinitionValidationError("GRAPH_CYCLE", "algorithm graph contains a cycle", {"node_ids": sorted(remaining)})
        for node_id in ready:
            ordered.append(nodes[node_id])
            remaining.pop(node_id)
        ready_set = set(ready)
        for dependencies in remaining.values():
            dependencies.difference_update(ready_set)
    return tuple(ordered)


def validate_parameters(schema: Mapping[str, object], parameters: Mapping[str, object]) -> None:
    """Validate a deliberately small JSON-schema subset and reject unknown rules."""
    allowed = {"type", "properties", "required", "additionalProperties", "enum", "minimum", "maximum", "items"}
    unknown = set(schema) - allowed
    if unknown or schema.get("type", "object") != "object":
        raise DefinitionValidationError("PARAMETER_INVALID", "unsupported parameter schema", {"keywords": sorted(unknown)})
    properties = schema.get("properties", {})
    required = schema.get("required", [])
    if not isinstance(properties, dict) or not isinstance(required, list):
        raise DefinitionValidationError("PARAMETER_INVALID", "parameter schema properties and required are invalid")
    for name in required:
        if name not in parameters:
            raise DefinitionValidationError("PARAMETER_INVALID", "required parameter is missing", {"parameter": name})
    if schema.get("additionalProperties") is False:
        extras = set(parameters) - set(properties)
        if extras:
            raise DefinitionValidationError("PARAMETER_INVALID", "unknown parameter", {"parameters": sorted(extras)})
    for name, value in parameters.items():
        rule = properties.get(name)
        if rule is None:
            continue
        if not isinstance(rule, dict) or set(rule) - {"type", "enum", "minimum", "maximum", "items"}:
            raise DefinitionValidationError("PARAMETER_INVALID", "unsupported parameter rule", {"parameter": name})
        kind = rule.get("type")
        valid = {"number": isinstance(value, (int, float)) and not isinstance(value, bool), "integer": isinstance(value, int) and not isinstance(value, bool), "string": isinstance(value, str), "boolean": isinstance(value, bool), "array": isinstance(value, list)}.get(kind, False)
        if not valid:
            raise DefinitionValidationError("PARAMETER_INVALID", "parameter has invalid type", {"parameter": name})
        if "enum" in rule and value not in rule["enum"]:
            raise DefinitionValidationError("PARAMETER_INVALID", "parameter is outside enum", {"parameter": name})
        if kind in {"number", "integer"} and (("minimum" in rule and value < rule["minimum"]) or ("maximum" in rule and value > rule["maximum"])):
            raise DefinitionValidationError("PARAMETER_INVALID", "parameter is outside bounds", {"parameter": name})
