from __future__ import annotations

import threading
import time
from dataclasses import replace

import pytest

from app.algorithm_runtime.group_execution import (
    AlgorithmGroupDependency,
    AlgorithmGroupExecutor,
    AlgorithmGroupGraph,
    AlgorithmGroupGraphError,
    AlgorithmGroupNode,
)


def _node(node_id: str, *, dependencies=(), parallel_safe=True, state_owner=None):
    return AlgorithmGroupNode(
        node_id=node_id,
        dependencies=tuple(dependencies),
        input_roles=("Fz",),
        input_representation="spectrum",
        source_unit="V",
        input_unit="V^2/Hz",
        window_s=4.0,
        cadence_s=1.0,
        supports_dynamic=True,
        stateful=state_owner is not None,
        state_owner=state_owner,
        preprocessing_identity="test-preprocessing-v1",
        output_unit="score",
        implementation_identity=f"{node_id}-v1",
        parallel_safe=parallel_safe,
        failure_behavior="structured_child_outcome",
    )


def test_graph_builds_stable_topological_layers_and_identity():
    graph = AlgorithmGroupGraph("test-v1", (
        _node("independent"),
        _node("dependent", dependencies=(AlgorithmGroupDependency("source", False),)),
        _node("source"),
    ))

    assert graph.layers == (("independent", "source"), ("dependent",))
    assert graph.identity.startswith("test-v1:")
    assert replace(graph.nodes[0], cadence_s=2.0) != graph.nodes[0]
    assert AlgorithmGroupGraph("test-v1", (
        replace(graph.nodes[0], cadence_s=2.0), *graph.nodes[1:],
    )).identity != graph.identity


@pytest.mark.parametrize("nodes,code", [
    ((_node("same"), _node("same")), "ALGORITHM_GROUP_GRAPH_INVALID"),
    ((_node("child", dependencies=(AlgorithmGroupDependency("missing"),)),),
     "ALGORITHM_GROUP_GRAPH_INVALID"),
    ((_node("self", dependencies=(AlgorithmGroupDependency("self"),)),),
     "ALGORITHM_GROUP_GRAPH_INVALID"),
    ((_node("a", dependencies=(AlgorithmGroupDependency("b"),)),
      _node("b", dependencies=(AlgorithmGroupDependency("a"),))),
     "ALGORITHM_GROUP_GRAPH_CYCLE"),
    ((_node("a", parallel_safe=True, state_owner="shared"),
      _node("b", parallel_safe=True, state_owner="shared")),
     "ALGORITHM_GROUP_GRAPH_UNSAFE_CONCURRENCY"),
])
def test_graph_rejects_invalid_dependencies_and_shared_state(nodes, code):
    with pytest.raises(AlgorithmGroupGraphError) as error:
        AlgorithmGroupGraph("test-v1", nodes)

    assert error.value.code == code
    assert error.value.detail


@pytest.mark.parametrize("changes,issue_code", [
    ({"input_representation": "waveform"}, "invalid_input_representation"),
    ({"cadence_s": float("nan")}, "invalid_dynamic_schedule"),
    ({"input_unit": " "}, "incomplete_contract"),
    ({"failure_behavior": "silently_drop"}, "invalid_failure_behavior"),
])
def test_graph_rejects_invalid_node_contract(changes, issue_code):
    with pytest.raises(AlgorithmGroupGraphError) as error:
        AlgorithmGroupGraph("test-v1", (replace(_node("invalid"), **changes),))

    assert error.value.code == "ALGORITHM_GROUP_GRAPH_INVALID"
    assert issue_code in {item["code"] for item in error.value.detail["issues"]}


def test_required_failure_blocks_only_required_dependents():
    graph = AlgorithmGroupGraph("test-v1", (
        _node("source"),
        _node("required", dependencies=(AlgorithmGroupDependency("source", True),)),
        _node("optional", dependencies=(AlgorithmGroupDependency("source", False),)),
    ))
    observed = {}

    def run_optional(outcomes):
        observed.update(outcomes)
        return "ran"

    with AlgorithmGroupExecutor(graph) as executor:
        results = executor.execute(
            {"source": lambda _: "failed", "required": lambda _: "must-not-run",
             "optional": run_optional},
            is_failure=lambda value: value == "failed",
            dependency_failure=lambda node, dependencies: ("blocked", node.node_id, dependencies),
        )

    assert results["required"] == ("blocked", "required", ("source",))
    assert results["optional"] == "ran"
    assert observed["source"] == "failed"


def test_parallel_nodes_obey_group_concurrency_limit():
    graph = AlgorithmGroupGraph("test-v1", tuple(_node(f"node-{i}") for i in range(4)), max_concurrency=2)
    lock = threading.Lock()
    barrier = threading.Barrier(2)
    active = 0
    maximum_active = 0

    def action(_):
        nonlocal active, maximum_active
        with lock:
            active += 1
            maximum_active = max(maximum_active, active)
        try:
            barrier.wait(timeout=2)
            time.sleep(0.01)
            return "done"
        finally:
            with lock:
                active -= 1

    with AlgorithmGroupExecutor(graph) as executor:
        outcomes = executor.execute(
            {node.node_id: action for node in graph.nodes},
            is_failure=lambda _: False,
            dependency_failure=lambda node, dependencies: (node.node_id, dependencies),
        )

    assert set(outcomes.values()) == {"done"}
    assert maximum_active == 2
