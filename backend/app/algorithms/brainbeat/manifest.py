from app.algorithm_runtime.contracts import AlgorithmManifest, DynamicAnalysisPolicy, SignalRequirements
from app.algorithms.capabilities import declared_output_contract, official_dynamic_capabilities, official_result_graph


_WINDOW_POLICY = DynamicAnalysisPolicy(
    minimum_window_s=10.0, window_options_s=[10.0, 20.0, 30.0],
    default_window_s=20.0, refresh_step_s=1.0, allow_warmup=False,
)


def _manifest(*, algorithm_id: str, display_name: str, abbreviation: str,
              scientific_version: str, implementation_identity: str,
              meaning: str, formula: str, iapf_required: bool) -> AlgorithmManifest:
    manifest = AlgorithmManifest(
        algorithm_id=algorithm_id,
        display_name_zh=display_name,
        abbreviation=abbreviation,
        purpose_zh=meaning,
        scientific_version=scientific_version,
        is_current_version=True,
        implementation_identity=implementation_identity,
        supported_modes=["static", "dynamic"],
        dynamic_policy=_WINDOW_POLICY.model_copy(deep=True),
        signal_requirements=SignalRequirements(
            input_roles=["Fz", "Pz"], representation="spectrum", source_shape="paired",
            preprocessing_scope="configured_continuous_before_slice",
        ),
        dynamic_capabilities=official_dynamic_capabilities(
            full_to_preview=False,
            combined_evidence=[
                "backend/tests/test_official_combined_strategies.py",
                "desktop-client/BrainPlatform.Desktop.Tests/Algorithms/OfficialCombinedStrategyTests.cs",
            ],
        ),
        output_schema={
            "window_output_kind": "scalar_series",
            "window_axes": [{"name": "time_s", "unit": "s"}],
            "window_shapes": {"time_s": [1], "brainbeat": [1]},
            "fields": [{"name": "brainbeat", "unit": "ratio", "meaning": meaning}],
        },
        definition_persistence={
            "name": display_name, "version": "3.0.0" if not iapf_required else "1.0.0",
            "graph": official_result_graph(["brainbeat"]),
            "definition_metadata": {
                "inputs": ["Fz", "Pz", "IAPF"] if iapf_required else ["Fz", "Pz"],
                "formula": formula,
                "bands_hz": {"theta_fz": ["IAPF-relative"] if iapf_required else [4.0, 8.0],
                             "alpha_pz": ["IAPF-relative"] if iapf_required else [8.0, 12.0]},
                "power_type": "relative" if iapf_required else "absolute",
                "welch_segment_s": 4.0, "overlap": 0.5,
                "band_integration": "boundary_interpolated_trapezoid",
                "independent_windows": True, "cross_window_ema": False,
            },
            "inputs": {"Fz": {"type": "TimeSeries", "unit": "V"},
                       "Pz": {"type": "TimeSeries", "unit": "V"}},
            "outputs": {"brainbeat": {"type": "Scalar", "unit": "ratio"}},
            "quality_rules": {"cross_window_ema": False, "iapf_required": iapf_required},
            "references": ["docs/algorithms/04-spectral-metrics.md"],
        },
        execution_kind="official_composite_run_adapter",
        availability="available", is_runnable=True,
        required_channel_roles=["Fz", "Pz"],
    )
    manifest.output_contract = declared_output_contract(
        manifest, static_result_type="scalar", dynamic_result_type="series",
        evidence_fields=["brainbeat_evidence", "paired_role_order", "window_source"],
    )
    return manifest


MANIFEST = _manifest(
    algorithm_id="brainbeat", display_name="经典 BrainBeat", abbreviation="BrainBeat",
    scientific_version="official-brainbeat-v6-classic-absolute",
    implementation_identity="brainbeat-runtime-v6-classic-fixed-band-absolute",
    meaning="Fz 4-8 Hz absolute power / Pz 8-12 Hz absolute power",
    formula="absolute_power(Fz,4-8Hz)/absolute_power(Pz,8-12Hz)", iapf_required=False,
)

INDIVIDUALIZED_MANIFEST = _manifest(
    algorithm_id="brainbeat_individualized", display_name="个体化 BrainBeat（IAPF 相对功率）",
    abbreviation="BrainBeat IAPF",
    scientific_version="official-brainbeat-individualized-v1-relative",
    implementation_identity="brainbeat-individualized-runtime-v1-iapf-relative",
    meaning="Fz IAPF-relative theta share / Pz IAPF-relative alpha share",
    formula="relative_theta_Fz/relative_alpha_Pz", iapf_required=True,
)
