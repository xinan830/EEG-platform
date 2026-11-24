from __future__ import annotations

from app.algorithms.tnk.group_graph import build_tnk_group_graph, dynamic_child_range
from app.algorithms.tnk.runtime import build_tnk_child_adapters


def test_tnk_graph_is_closed_and_declares_roles_units_state_and_dependencies():
    children = build_tnk_child_adapters()
    graph = build_tnk_group_graph(children)
    by_id = {node.node_id: node for node in graph.nodes}

    assert set(by_id) == {child.manifest.algorithm_id for child in children}
    assert by_id["tnk_rbp"].input_roles == ("Fz", "Pz", "Oz", "F3", "F4")
    assert by_id["tnk_faa"].input_roles == ("F3", "F4")
    assert by_id["tnk_faa"].supports_dynamic is False
    assert by_id["tnk_relaxation"].source_unit == "V"
    assert by_id["tnk_relaxation"].input_unit == "V^2/Hz"
    assert by_id["tnk_relaxation"].output_unit == "score"
    assert all(node.failure_behavior == "structured_child_outcome" for node in graph.nodes)
    assert {item.node_id for item in by_id["tnk_composite_meditation"].dependencies} == {
        "tnk_spatial_distribution", "tnk_rhythm_stability",
    }
    assert not any(item.required_for_execution for item in by_id["tnk_composite_meditation"].dependencies)
    assert by_id["tnk_brainbeat_flat"].dependencies[0].node_id == "tnk_aperiodic"
    assert by_id["tnk_brainbeat_flat"].dependencies[0].required_for_execution is False
    legacy_owners = {
        node.state_owner for node in graph.nodes if node.input_representation == "time_series"
    }
    assert legacy_owners == {"tnk_legacy_stream_bundle"}
    assert all(not node.parallel_safe for node in graph.nodes if node.state_owner == "tnk_legacy_stream_bundle")


def test_tnk_graph_layers_wait_for_optional_context_dependencies():
    graph = build_tnk_group_graph(build_tnk_child_adapters())
    layer_by_id = {
        node_id: index for index, layer in enumerate(graph.layers) for node_id in layer
    }

    assert layer_by_id["tnk_spatial_distribution"] < layer_by_id["tnk_composite_meditation"]
    assert layer_by_id["tnk_rhythm_stability"] < layer_by_id["tnk_composite_meditation"]
    assert layer_by_id["tnk_aperiodic"] < layer_by_id["tnk_brainbeat_flat"]
    assert layer_by_id["tnk_composite_meditation"] == layer_by_id["tnk_brainbeat_flat"]


def test_tnk_graph_preserves_each_child_window_and_update_cadence():
    graph = build_tnk_group_graph(build_tnk_child_adapters())
    node = graph.node

    assert dynamic_child_range(node("tnk_rbp"), 0.0, 3.0) is None
    assert dynamic_child_range(node("tnk_rbp"), 0.0, 4.0) == (0.0, 4.0)
    assert dynamic_child_range(node("tnk_iapf"), 0.0, 29.0) is None
    assert dynamic_child_range(node("tnk_iapf"), 0.0, 30.0) == (0.0, 30.0)
    assert dynamic_child_range(node("tnk_iapf"), 0.0, 32.0) is None
    assert dynamic_child_range(node("tnk_iapf"), 0.0, 35.0) == (5.0, 35.0)
    assert dynamic_child_range(node("tnk_crimson_meditation"), 0.0, 4.0) is None
    assert dynamic_child_range(node("tnk_crimson_meditation"), 0.0, 5.0) == (0.0, 5.0)
    assert dynamic_child_range(node("tnk_zenlite_calmness"), 0.0, 5.0) == (0.3125, 5.0)
    assert dynamic_child_range(node("tnk_faa"), 0.0, 30.0) is None
