"""Closed execution graph and frozen dynamic schedule for TNK children."""

from __future__ import annotations

from typing import Any

from app.algorithm_runtime.group_execution import (
    AlgorithmGroupDependency,
    AlgorithmGroupGraph,
    AlgorithmGroupNode,
)

TNK_ROLE_ORDER = ("Fz", "Pz", "Oz", "F3", "F4")

_DECLARATIONS = (
    ("tnk_iapf", 30.0, 5.0),
    ("tnk_relaxation", 4.0, 1.0),
    ("tnk_spatial_distribution", 4.0, 1.0),
    ("tnk_rhythm_stability", 4.0, 1.0),
    ("tnk_composite_meditation", 4.0, 1.0),
    ("tnk_brainbeat", 4.0, 1.0),
    ("tnk_aperiodic", 30.0, 5.0),
    ("tnk_brainbeat_flat", 4.0, 1.0),
    ("tnk_fatigue", 4.0, 1.0),
    ("tnk_hai", 30.0, 5.0),
    ("tnk_faa", None, None),
    ("tnk_rbp", 4.0, 1.0),
    ("tnk_crimson_meditation", 5.0, 1.0),
    ("tnk_zenlite_calmness", 4.6875, 1.0),
    ("tnk_zenlite_awareness", 4.6875, 1.0),
    ("tnk_zenlite_spectral_relaxation", 4.6875, 1.0),
)

_DEPENDENCIES = {
    "tnk_composite_meditation": (
        AlgorithmGroupDependency("tnk_spatial_distribution", required_for_execution=False),
        AlgorithmGroupDependency("tnk_rhythm_stability", required_for_execution=False),
    ),
    "tnk_brainbeat_flat": (
        AlgorithmGroupDependency("tnk_aperiodic", required_for_execution=False),
    ),
}


def build_tnk_group_graph(children: tuple[Any, ...]) -> AlgorithmGroupGraph:
    by_id = {child.manifest.algorithm_id: child for child in children}
    expected_ids = {item[0] for item in _DECLARATIONS}
    if len(by_id) != len(children) or set(by_id) != expected_ids:
        raise ValueError("TNK 子模块注册表与固定执行图不一致")
    nodes = []
    for child_id, window_s, cadence_s in _DECLARATIONS:
        child = by_id[child_id]
        manifest = child.manifest
        source = child.module.manifest
        representation = manifest.signal_requirements.representation
        has_dependencies = child_id in _DEPENDENCIES
        if child_id == "tnk_rbp":
            roles = TNK_ROLE_ORDER
        else:
            roles = tuple(manifest.required_channel_roles)
        stateful = bool(source.stateful)
        shared_stream = getattr(child, "stream_bundle", None) is not None
        nodes.append(AlgorithmGroupNode(
            node_id=child_id,
            dependencies=_DEPENDENCIES.get(child_id, ()),
            input_roles=roles,
            input_representation=(
                "time_series" if representation == "time_series"
                else "spectrum_with_dependencies" if has_dependencies
                else "spectrum"
            ),
            source_unit=source.input_unit,
            input_unit="V" if representation == "time_series" else "V^2/Hz",
            window_s=window_s,
            cadence_s=cadence_s,
            supports_dynamic=window_s is not None,
            stateful=stateful,
            state_owner=("tnk_legacy_stream_bundle" if shared_stream else child_id) if stateful else None,
            preprocessing_identity=(
                "tnk-legacy-time-series-v1" if representation == "time_series"
                else "tnk-window-spectral-v1"
            ),
            output_unit=source.output_unit,
            implementation_identity=manifest.implementation_identity,
            parallel_safe=not shared_stream,
        ))
    return AlgorithmGroupGraph(version="tnk-child-graph-v1", nodes=tuple(nodes), max_concurrency=2)


def dynamic_child_range(
    node: AlgorithmGroupNode, origin_s: float, end_s: float,
) -> tuple[float, float] | None:
    if not node.supports_dynamic or node.window_s is None or node.cadence_s is None:
        return None
    elapsed_s = end_s - origin_s
    if elapsed_s < node.window_s - 1e-9:
        return None
    cadence_index = round(elapsed_s / node.cadence_s)
    if abs(elapsed_s - cadence_index * node.cadence_s) > 1e-7:
        return None
    start_s = end_s - node.window_s
    return (start_s, end_s) if start_s >= origin_s - 1e-9 else None
