"""Build bounded 3D scalp fields from existing RBP window results."""

from app.models.topomap_3d import (
    Topomap3DLocationResponse,
    Topomap3DResolvedLocation,
    Topomap3DModelResponse,
)
from app.services.topomap_3d_assets import (
    MAPPING_ID,
    MAPPING_VERSION,
    MODEL_ID,
    MODEL_VERSION,
    Topomap3DAssets,
    load_topomap_3d_assets,
        )
HARDWARE_LANDMARKS = {"ref", "gnd"}
NON_SCALP_FIELD_CHANNELS = {"a1", "a2", "m1", "m2"} | HARDWARE_LANDMARKS


class Topomap3DService:
    """Serve the shared GNM model and resolve standard electrode locations."""

    def __init__(self):
        self._assets: Topomap3DAssets | None = None
        self._model: Topomap3DModelResponse | None = None

    @property
    def assets(self) -> Topomap3DAssets:
        if self._assets is None:
            self._assets = load_topomap_3d_assets()
        return self._assets

    def get_model(self) -> Topomap3DModelResponse:
        if self._model is None:
            assets = self.assets
            self._model = Topomap3DModelResponse(
                model_id=MODEL_ID, model_version=MODEL_VERSION,
                mapping_id=MAPPING_ID, mapping_version=MAPPING_VERSION,
                mapping_sha256=assets.mapping_sha256, mesh_asset_sha256=assets.mesh_asset_sha256,
                mesh_geometry_sha256=assets.mesh_geometry_sha256, source_montage="colin27_1020",
                source_mne_version=assets.mne_version, source_coordinate_frame="head",
                source_coordinate_unit="m", target_coordinate_frame="gnm_native",
                target_coordinate_unit="GNM model units", intended_use="standard_template_visualization",
                accuracy_status=str(assets.full_montage["accuracy_status"]),
                head_origin_gnm=tuple(assets.head_origin), right_axis_gnm=tuple(assets.right_axis),
                forward_axis_gnm=tuple(assets.forward_axis), up_axis_gnm=tuple(assets.up_axis),
                electrode_locations=[
                    {
                        "channel": channel,
                        "canonical_channel": str(location.get("canonical_name") or channel),
                        "coordinate_gnm": tuple(location["coordinate_gnm"]),
                        "mapping_method": str(location.get("method") or "unknown"),
                        "manual_adjusted": bool(location.get("manual_adjusted", False)),
                        "is_scalp_field_site": channel.casefold() not in NON_SCALP_FIELD_CHANNELS,
                    }
                    for channel, location in assets.channel_locations.items()
                ],
                vertices_gnm=assets.vertices.tolist(), triangles=assets.triangles.tolist(),
            )
        return self._model

    def resolve_locations(self, channels: list[str]) -> Topomap3DLocationResponse:
        assets = self.assets
        names = {name.casefold(): name for name in assets.channel_locations}
        locations = []
        for requested in channels:
            label = requested.strip()
            canonical_name = names.get(label.casefold()) if label else None
            site = assets.channel_locations.get(canonical_name) if canonical_name else None
            reason = None
            if not label:
                reason = "blank_channel_label"
            elif label.casefold() in HARDWARE_LANDMARKS:
                reason = "hardware_landmark_has_no_standard_electrode_site"
            elif site is None:
                reason = "electrode_location_unavailable"
            locations.append(Topomap3DResolvedLocation(
                channel=requested,
                canonical_channel=str(site.get("canonical_name") or canonical_name) if site else None,
                source_coordinate_mne=tuple(site["coordinate_mne"]) if site else None,
                target_coordinate_gnm=tuple(site["coordinate_gnm"]) if site and reason is None else None,
                mapping_method=str(site.get("method") or "unknown") if site else None,
                is_scalp_field_site=bool(site and
                    str(site.get("canonical_name") or canonical_name).casefold() not in NON_SCALP_FIELD_CHANNELS),
                status="available" if site and reason is None else "unavailable", reason=reason,
            ))
        return Topomap3DLocationResponse(
            mapping_id=MAPPING_ID, mapping_version=MAPPING_VERSION,
            mapping_sha256=assets.mapping_sha256, model_id=MODEL_ID, model_version=MODEL_VERSION,
            source_montage="colin27_1020", source_coordinate_frame="head", source_coordinate_unit="m",
            target_coordinate_frame="gnm_native", target_coordinate_unit="GNM model units",
            locations=locations,
        )
