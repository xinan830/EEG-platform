"""Closed, typed dependency graphs for registered algorithm groups."""

from __future__ import annotations

import hashlib
import json
from math import isfinite
from collections.abc import Callable, Mapping
from concurrent.futures import ThreadPoolExecutor
from dataclasses import asdict, dataclass, field
from types import MappingProxyType
from typing import Any, Literal


@dataclass(frozen=True)
class AlgorithmGroupDependency:
    node_id: str
    required_for_execution: bool = True


@dataclass(frozen=True)
class AlgorithmGroupNode:
    node_id: str
    dependencies: tuple[AlgorithmGroupDependency, ...]
    input_roles: tuple[str, ...]
    input_representation: Literal["spectrum", "time_series", "spectrum_with_dependencies"]
    source_unit: str
    input_unit: str
    window_s: float | None
    cadence_s: float | None
    supports_dynamic: bool
    stateful: bool
    state_owner: str | None
    preprocessing_identity: str
    output_unit: str
    implementation_identity: str
    parallel_safe: bool
    failure_behavior: Literal["structured_child_outcome"] = "structured_child_outcome"


class AlgorithmGroupGraphError(ValueError):
    def __init__(self, code: str, message: str, detail: dict[str, Any]) -> None:
        super().__init__(message)
        self.code = code
        self.detail = detail


def _positive_finite(value: Any) -> bool:
    return isinstance(value, (int, float)) and not isinstance(value, bool) and isfinite(value) and value > 0


@dataclass(frozen=True)
class AlgorithmGroupGraph:
    version: str
    nodes: tuple[AlgorithmGroupNode, ...]
    max_concurrency: int = 2
    layers: tuple[tuple[str, ...], ...] = field(init=False)
    identity: str = field(init=False)

    def __post_init__(self) -> None:
        layers = _validate_and_order(self)
        object.__setattr__(self, "layers", layers)
        payload = {
            "version": self.version,
            "max_concurrency": self.max_concurrency,
            "nodes": [asdict(node) for node in self.nodes],
        }
        digest = hashlib.sha256(
            json.dumps(payload, sort_keys=True, separators=(",", ":")).encode("utf-8"),
        ).hexdigest()
        object.__setattr__(self, "identity", f"{self.version}:{digest}")

    def node(self, node_id: str) -> AlgorithmGroupNode:
        return next(node for node in self.nodes if node.node_id == node_id)


def _validate_and_order(graph: AlgorithmGroupGraph) -> tuple[tuple[str, ...], ...]:
    issues: list[dict[str, Any]] = []
    ids = [node.node_id for node in graph.nodes if isinstance(node.node_id, str)]
    known = set(ids)
    if not isinstance(graph.version, str) or not graph.version.strip():
        issues.append({"code": "empty_version"})
    if not isinstance(graph.max_concurrency, int) or isinstance(graph.max_concurrency, bool) or graph.max_concurrency < 1:
        issues.append({"code": "invalid_concurrency", "value": graph.max_concurrency})
    if not ids:
        issues.append({"code": "empty_graph"})
    for node in graph.nodes:
        if not isinstance(node.node_id, str) or not node.node_id.strip():
            issues.append({"code": "empty_node_id"})
        valid_roles = (
            isinstance(node.input_roles, tuple) and bool(node.input_roles)
            and all(isinstance(role, str) and role.strip() for role in node.input_roles)
        )
        if not valid_roles:
            issues.append({"code": "invalid_input_roles", "node_id": node.node_id})
        elif len(node.input_roles) != len(set(node.input_roles)):
            issues.append({"code": "duplicate_input_role", "node_id": node.node_id})
        if node.input_representation not in (
            "spectrum", "time_series", "spectrum_with_dependencies",
        ):
            issues.append({"code": "invalid_input_representation", "node_id": node.node_id})
        if not all(
            isinstance(value, str) and value.strip()
            for value in (node.source_unit, node.input_unit, node.preprocessing_identity,
                          node.output_unit, node.implementation_identity)
        ):
            issues.append({"code": "incomplete_contract", "node_id": node.node_id})
        if not all(isinstance(flag, bool) for flag in (node.supports_dynamic, node.stateful, node.parallel_safe)):
            issues.append({"code": "invalid_node_flags", "node_id": node.node_id})
        if node.failure_behavior != "structured_child_outcome":
            issues.append({"code": "invalid_failure_behavior", "node_id": node.node_id})
        if node.supports_dynamic and not (_positive_finite(node.window_s) and _positive_finite(node.cadence_s)):
            issues.append({"code": "invalid_dynamic_schedule", "node_id": node.node_id})
        if not node.supports_dynamic and (node.window_s is not None or node.cadence_s is not None):
            issues.append({"code": "unsupported_node_has_schedule", "node_id": node.node_id})
        if node.state_owner is not None and (
            not isinstance(node.state_owner, str) or not node.state_owner.strip()
        ):
            issues.append({"code": "invalid_state_owner", "node_id": node.node_id})
        if node.stateful != bool(node.state_owner):
            issues.append({"code": "invalid_state_owner", "node_id": node.node_id})
        if not isinstance(node.dependencies, tuple):
            issues.append({"code": "invalid_dependencies", "node_id": node.node_id})
            continue
        valid_dependencies = [
            dependency for dependency in node.dependencies
            if isinstance(dependency, AlgorithmGroupDependency)
        ]
        if len(valid_dependencies) != len(node.dependencies):
            issues.append({"code": "invalid_dependency", "node_id": node.node_id})
        dependency_ids = [
            dependency.node_id for dependency in valid_dependencies
            if isinstance(dependency.node_id, str)
        ]
        if len(dependency_ids) != len(set(dependency_ids)):
            issues.append({"code": "duplicate_dependency", "node_id": node.node_id})
        for dependency in valid_dependencies:
            if not isinstance(dependency.required_for_execution, bool):
                issues.append({"code": "invalid_dependency_policy", "node_id": node.node_id})
            if not isinstance(dependency.node_id, str) or not dependency.node_id.strip():
                issues.append({"code": "invalid_dependency_id", "node_id": node.node_id})
                continue
            if dependency.node_id == node.node_id:
                issues.append({"code": "self_dependency", "node_id": node.node_id})
            elif dependency.node_id not in known:
                issues.append({"code": "missing_dependency", "node_id": node.node_id,
                               "dependency_id": dependency.node_id})
    duplicate_ids = sorted({node_id for node_id in ids if ids.count(node_id) > 1})
    if duplicate_ids:
        issues.append({"code": "duplicate_node", "node_ids": duplicate_ids})
    if issues:
        raise AlgorithmGroupGraphError(
            "ALGORITHM_GROUP_GRAPH_INVALID", "算法组依赖图契约无效", {"issues": issues},
        )

    remaining = set(ids)
    layers: list[tuple[str, ...]] = []
    while remaining:
        ready = tuple(
            node.node_id for node in graph.nodes
            if node.node_id in remaining and
            all(dependency.node_id not in remaining for dependency in node.dependencies)
        )
        if not ready:
            raise AlgorithmGroupGraphError(
                "ALGORITHM_GROUP_GRAPH_CYCLE", "算法组依赖图包含环", {"node_ids": sorted(remaining)},
            )
        owners: dict[str, list[str]] = {}
        for node_id in ready:
            node = next(item for item in graph.nodes if item.node_id == node_id)
            if node.parallel_safe and node.state_owner:
                owners.setdefault(node.state_owner, []).append(node_id)
        conflicts = {owner: members for owner, members in owners.items() if len(members) > 1}
        if conflicts:
            raise AlgorithmGroupGraphError(
                "ALGORITHM_GROUP_GRAPH_UNSAFE_CONCURRENCY",
                "同一并发层不能并行写入共享算法状态",
                {"state_owners": conflicts},
            )
        layers.append(ready)
        remaining.difference_update(ready)
    return tuple(layers)


class AlgorithmGroupExecutor:
    """Run prepared node actions by dependency layer with a request-scoped pool."""

    def __init__(self, graph: AlgorithmGroupGraph) -> None:
        self.graph = graph
        self._pool = (
            ThreadPoolExecutor(max_workers=graph.max_concurrency, thread_name_prefix="algorithm-group")
            if graph.max_concurrency > 1 else None
        )

    def __enter__(self) -> "AlgorithmGroupExecutor":
        return self

    def __exit__(self, exc_type, exc, traceback) -> bool:
        if self._pool is not None:
            self._pool.shutdown(wait=True, cancel_futures=True)
            self._pool = None
        return False

    def execute(
        self,
        actions: Mapping[str, Callable[[Mapping[str, Any]], Any]],
        *,
        is_failure: Callable[[Any], bool],
        dependency_failure: Callable[[AlgorithmGroupNode, tuple[str, ...]], Any],
    ) -> dict[str, Any]:
        node_ids = {node.node_id for node in self.graph.nodes}
        if set(actions) != node_ids:
            raise AlgorithmGroupGraphError(
                "ALGORITHM_GROUP_ACTIONS_MISMATCH", "算法组节点任务与依赖图不一致",
                {"missing": sorted(node_ids - set(actions)), "unexpected": sorted(set(actions) - node_ids)},
            )
        nodes = {node.node_id: node for node in self.graph.nodes}
        outcomes: dict[str, Any] = {}
        for layer in self.graph.layers:
            runnable: list[str] = []
            for node_id in layer:
                node = nodes[node_id]
                failed = tuple(
                    dependency.node_id for dependency in node.dependencies
                    if dependency.required_for_execution and is_failure(outcomes[dependency.node_id])
                )
                if failed:
                    outcomes[node_id] = dependency_failure(node, failed)
                else:
                    runnable.append(node_id)
            serial = [node_id for node_id in runnable if not nodes[node_id].parallel_safe or self._pool is None]
            parallel = [node_id for node_id in runnable if node_id not in serial]
            for node_id in serial:
                outcomes[node_id] = actions[node_id](MappingProxyType(dict(outcomes)))
            if parallel:
                futures = {
                    node_id: self._pool.submit(actions[node_id], MappingProxyType(dict(outcomes)))
                    for node_id in parallel
                }
                for node_id in parallel:
                    outcomes[node_id] = futures[node_id].result()
        return {node.node_id: outcomes[node.node_id] for node in self.graph.nodes}
