"""Build bounded 3D scalp fields from existing RBP window results."""

from functools import lru_cache

import numpy as np

from app.models.topomap_3d import (
    Topomap3DLocationResponse,
    Topomap3DMontageCatalogResponse,
    Topomap3DMontagePreviewResponse,
    Topomap3DMontageSite,
    Topomap3DMontageSummary,
    Topomap3DResolvedLocation,
    Topomap3DModelResponse,
)
from app.scientific.topomap_3d_projection import (
    CORE_MONTAGE_BY_NAME,
    CORE_MONTAGE_CATALOG,
    is_ten_twenty_montage,
    montage_head_positions,
    normalized_cap_directions,
    project_directions_to_2d_aep,
    project_head_points_to_gnm_mesh,
)
from app.services.electrode_locations import resolve_montage_2d_coordinates
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

    def list_montages(self) -> Topomap3DMontageCatalogResponse:
        self.assets  # Ensure packaged 3D assets are valid.
        default_name = next(item.name for item in CORE_MONTAGE_CATALOG if item.is_default)
        summaries = [
            Topomap3DMontageSummary(
                name=item.name,
                display_name=item.display_name,
                category=item.category,
                channel_count=len(self.assets.channel_locations) if item.is_default else len(montage_head_positions(item.name)),
                is_default=item.is_default,
            )
            for item in CORE_MONTAGE_CATALOG
        ]
        return Topomap3DMontageCatalogResponse(default_montage=default_name, montages=summaries)

    def preview_montage(self, montage_name: str) -> Topomap3DMontagePreviewResponse:
        spec = CORE_MONTAGE_BY_NAME.get(montage_name)
        if spec is None:
            raise KeyError(montage_name)
        return self._cached_montage_preview(montage_name)

    @lru_cache(maxsize=24)
    def _cached_montage_preview(self, montage_name: str) -> Topomap3DMontagePreviewResponse:
        spec = CORE_MONTAGE_BY_NAME[montage_name]
        assets = self.assets
        coords_2d = resolve_montage_2d_coordinates(montage_name)

        if spec.is_default:
            sites = [
                Topomap3DMontageSite(
                    channel=channel,
                    canonical_channel=str(location.get("canonical_name") or channel),
                    map_x=coords_2d[channel.casefold()][0],
                    map_y=coords_2d[channel.casefold()][1],
                    coordinate_gnm=tuple(location["coordinate_gnm"]),
                    is_scalp_field_site=channel.casefold() not in NON_SCALP_FIELD_CHANNELS,
                )
                for channel, location in assets.channel_locations.items()
                if channel.casefold() in coords_2d
            ]
            return Topomap3DMontagePreviewResponse(
                montage_name=spec.name,
                display_name=spec.display_name,
                is_default=True,
                channel_count=len(sites),
                projection_source="gnm_mapped_colin27",
                sites=sites,
            )

        head_positions = montage_head_positions(montage_name)
        names = list(head_positions)
        pts_m = np.asarray([head_positions[name] for name in names], dtype=np.float64)
        anchors = {
            key: tuple(val["coordinate_gnm"])
            for key, val in assets.channel_locations.items()
        }
        dirs_local = normalized_cap_directions(
            pts_m,
            channel_names=names,
            head_origin_gnm=assets.head_origin,
            right_axis_gnm=assets.right_axis,
            forward_axis_gnm=assets.forward_axis,
            up_axis_gnm=assets.up_axis,
            anchor_gnm_by_channel=anchors,
        )
        coords_2d_arr = project_directions_to_2d_aep(dirs_local)
        projected_gnm = project_head_points_to_gnm_mesh(
            pts_m,
            head_origin_gnm=assets.head_origin,
            right_axis_gnm=assets.right_axis,
            forward_axis_gnm=assets.forward_axis,
            up_axis_gnm=assets.up_axis,
            vertices_gnm=assets.vertices,
            triangles=assets.triangles,
            dirs_local=dirs_local,
        )
        ten_twenty = is_ten_twenty_montage(names)
        sites = [
            Topomap3DMontageSite(
                channel=name,
                canonical_channel=name,
                map_x=float(xy[0]),
                map_y=float(xy[1]),
                coordinate_gnm=(float(gnm[0]), float(gnm[1]), float(gnm[2])),
                is_scalp_field_site=not (ten_twenty and name.casefold() in NON_SCALP_FIELD_CHANNELS),
            )
            for name, xy, gnm in zip(names, coords_2d_arr, projected_gnm, strict=True)
        ]
        return Topomap3DMontagePreviewResponse(
            montage_name=spec.name,
            display_name=spec.display_name,
            is_default=False,
            channel_count=len(sites),
            projection_source="dynamic_head_mesh_projection",
            sites=sites,
        )
