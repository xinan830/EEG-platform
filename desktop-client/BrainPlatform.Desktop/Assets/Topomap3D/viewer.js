import * as THREE from "./vendor/three.module.min.js";
import { COLORMAPS, COLORMAP_ORDER, PRESETS, evalColormap, srgbToLinear } from "./colormaps.js";
import { buildCortexMesh, buildScalpMeshes } from "./scalp_cortex_meshes.js";
import { createSurfaceRecolorer } from "./surface_recolor.js";
import { createCameraAndElectrodeRig } from "./camera_labels.js";
import { createConnectomeLayer } from "./connectome_layer.js";
import { createSourceLayer } from "./source_layer.js";
import { createTractographyLayer } from "./tractography_layer.js";
import { createMriPanel } from "./mri_panel.js";

const state = {
  mode: "cortex",          // "cortex" | "atlas" | "scalp" | "connectome" | "source" | "tractography" | "mri" | "electrodes"
  atlasId: "lobes",
  colormap: "turbo",
  presetIndex: 0,
  isRealField: false,
  isPreviewHeatmap: false,
  enableLabModes: false,
  hideInternalModeTabs: false,
  showDimSwitcher: false,
  mriJumpFromSource: false,
  scalpData: null,
  cortexData: null,
  hoveredRegionId: -1,
  pinnedRegionId: -1,
  hoveredDipole: null,
  scaleInfo: { unit: "示意强度", minText: "0.0 低", midText: "0.5 中", maxText: "1.0 高" },
};

// --- Three.js Scene & Lighting ---
const host = document.getElementById("canvas-host");
const labelsLayer = document.getElementById("labels-layer");
const centerStatus = document.getElementById("center-status");
const tooltip = document.getElementById("region-tooltip");

const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true, powerPreference: "high-performance" });
renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
renderer.setSize(host.clientWidth || 600, host.clientHeight || 500);
renderer.outputColorSpace = THREE.SRGBColorSpace;
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = 1.15;
host.appendChild(renderer.domElement);

const scene = new THREE.Scene();
const camera = new THREE.PerspectiveCamera(36, (host.clientWidth || 600) / Math.max(1, host.clientHeight || 500), 0.05, 30);

scene.add(new THREE.HemisphereLight(0xffffff, 0xd8e2ee, 1.18));
const keyLight = new THREE.DirectionalLight(0xffffff, 1.12); keyLight.position.set(1.6, 2.4, 2.2); scene.add(keyLight);
const fillLight = new THREE.DirectionalLight(0xe6f0fa, 0.58); fillLight.position.set(-2.2, 0.8, 1.5); scene.add(fillLight);
const rimLight = new THREE.DirectionalLight(0xffffff, 0.48); rimLight.position.set(0.0, 1.5, -2.5); scene.add(rimLight);

const rootGroup = new THREE.Group();
scene.add(rootGroup);

const recolorer = createSurfaceRecolorer();
const rig = createCameraAndElectrodeRig({ camera, host, labelsLayer, rootGroup });
const raycaster = new THREE.Raycaster();
const pointerNdc = new THREE.Vector2();

let scalpOpaqueMesh = null;
let scalpDepthMesh = null;
let scalpFrostedMesh = null;
let cortexMesh = null;
let cortexSolidMaterial = null;
let connectomeLayer = null;
let sourceLayer = null;
let tractographyLayer = null;
let renderedAtlasKey = "";

const mriPanel = createMriPanel({
  container: document.getElementById("mri-host"),
  canvas: document.getElementById("mri-canvas"),
  statusEl: document.getElementById("mri-status"),
  coordEl: document.getElementById("mri-coord-pill"),
});

function ensureConnectomeLayer() {
  if (!connectomeLayer) {
    connectomeLayer = createConnectomeLayer({ rootGroup, evalColormap, srgbToLinear });
  }
  return connectomeLayer;
}

function ensureSourceLayer() {
  if (!sourceLayer) {
    sourceLayer = createSourceLayer({ rootGroup, evalColormap, srgbToLinear });
  }
  return sourceLayer;
}

function ensureTractographyLayer() {
  if (!tractographyLayer) {
    tractographyLayer = createTractographyLayer({ rootGroup, srgbToLinear });
  }
  return tractographyLayer;
}

function updateSourceBarUI() {
  const d = sourceLayer?.getSelectedDipole();
  const el = document.getElementById("source-info-text");
  if (!d) { el.textContent = "等待皮层加载..."; return; }
  el.textContent = `${d.id} ${d.name.replace(/^[^·]+·\s*/, "")} · MNI (${d.mni[0]}, ${d.mni[1]}, ${d.mni[2]}) mm · ${d.strength.toFixed(0)} nA·m · GOF ${d.gof.toFixed(1)}%`;
}

function updateTractBarUI(stats) {
  const s = stats || tractographyLayer?.getStats() || { shown: 0, total: 0 };
  document.getElementById("tract-count").textContent = `显示 ${s.shown} / ${s.total} 条纤维`;
  const custom = !!tractographyLayer?.isCustomLoaded();
  document.getElementById("tract-reset-file-btn").classList.toggle("hidden", state.mode !== "tractography" || !custom);
  document.getElementById("tract-bundle-select").classList.toggle("hidden", state.mode !== "tractography" || custom);
}

function switchMode(nextMode, { notifyHost = true } = {}) {
  if (!nextMode) return;
  if (nextMode !== "mri") state.mriJumpFromSource = false;
  state.mode = nextMode;
  state.hoveredRegionId = -1;
  state.pinnedRegionId = -1;
  state.hoveredDipole = null;
  if (nextMode === "source" && cortexMesh) {
    const p = ensureSourceLayer().getPreset();
    rig.startCameraAnim(p.yaw, p.pitch);
  } else if (nextMode === "tractography") {
    rig.startCameraAnim(0.68, 0.24);
  }
  applyModeVisibility();
  recolorActiveSurfaces();
  if (notifyHost) window.chrome?.webview?.postMessage({ type: "modeChanged", mode: state.mode });
}

async function jumpSourceToMri(dipole) {
  if (!dipole) return;
  state.mriJumpFromSource = true;
  state.mode = "mri";
  tooltip.style.display = "none";
  applyModeVisibility();
  window.chrome?.webview?.postMessage({ type: "modeChanged", mode: "mri" });
  await mriPanel.jumpToMni(dipole.mni, `${dipole.id} ${dipole.name.replace(/^[^·]+·\s*/, "")}`);
}

function updateConnectomeStatusUI(stats) {
  const s = stats || connectomeLayer?.getStats() || { shown: 0, total: 0 };
  document.getElementById("connectome-edge-count").textContent = `显示 ${s.shown} / ${s.total} 条连边`;
}

function loadScalp(payload) {
  if (scalpOpaqueMesh) { rootGroup.remove(scalpOpaqueMesh); scalpOpaqueMesh.geometry.dispose(); }
  if (scalpDepthMesh) { rootGroup.remove(scalpDepthMesh); scalpDepthMesh.geometry.dispose(); }
  if (scalpFrostedMesh) { rootGroup.remove(scalpFrostedMesh); scalpFrostedMesh.geometry.dispose(); }

  const built = buildScalpMeshes(payload);
  scalpOpaqueMesh = built.opaqueMesh;
  scalpDepthMesh = built.depthMesh;
  scalpFrostedMesh = built.frostedMesh;
  rootGroup.add(scalpOpaqueMesh, scalpDepthMesh, scalpFrostedMesh);

  state.scalpData = built.scalpData;
  recolorer.invalidateAll();
  rig.resetLayoutKey();
  centerStatus.classList.add("hidden");
  applyModeVisibility();
}

function loadCortex(cortexPayload) {
  if (!state.scalpData || !cortexPayload) return;
  if (cortexMesh) { rootGroup.remove(cortexMesh); cortexMesh.geometry.dispose(); }

  const built = buildCortexMesh(cortexPayload, state.scalpData.mapPoint);
  cortexMesh = built.mesh;
  cortexSolidMaterial = built.solidMaterial;
  rootGroup.add(cortexMesh);

  state.cortexData = built.cortexData;
  recolorer.invalidateCortex();
  renderedAtlasKey = "";
  recolorActiveSurfaces();
  applyModeVisibility();
}

function recolorActiveSurfaces() {
  if (state.mode === "mri") return;
  const elecs = rig.getElectrodes();
  const key = rig.getLayoutKey();
  if (state.mode === "cortex" && cortexMesh) {
    recolorer.recolorCortexHeatmap(cortexMesh, state.cortexData, elecs, key, state.colormap);
  } else if (state.mode === "atlas" && cortexMesh) {
    recolorer.recolorCortexAtlas(cortexMesh, state.cortexData, state.atlasId, state.hoveredRegionId, state.pinnedRegionId);
  } else if (state.mode === "connectome") {
    const layer = ensureConnectomeLayer();
    layer.setColormap(state.colormap);
    updateConnectomeStatusUI(layer.setElectrodes(elecs, state.cortexData?.normVerts));
  } else if (state.mode === "source" && cortexMesh) {
    const layer = ensureSourceLayer();
    layer.setColormap(state.colormap);
    layer.update(cortexMesh, state.cortexData);
    updateSourceBarUI();
  } else if (state.mode === "tractography") {
    updateTractBarUI(ensureTractographyLayer().ensureDemoBuilt(state.cortexData?.normVerts));
  } else {
    recolorer.recolorScalpSurface(scalpOpaqueMesh, state.scalpData, elecs, key, state.mode, state.colormap);
  }
}

function applyModeVisibility() {
  const hasCortex = !!cortexMesh;
  const isMri = state.mode === "mri";
  const isConnectome = state.mode === "connectome";
  const isSource = state.mode === "source";
  const isTract = state.mode === "tractography";
  const showFrostedShell = !isMri && (state.mode === "cortex" || state.mode === "atlas" || isConnectome || isSource || isTract) && hasCortex;

  if (isMri) mriPanel.show();
  else mriPanel.hide();

  labelsLayer.classList.toggle("hidden", isMri || isSource || isTract);
  if (scalpOpaqueMesh) scalpOpaqueMesh.visible = !isMri && !showFrostedShell;
  if (scalpDepthMesh) scalpDepthMesh.visible = showFrostedShell;
  if (scalpFrostedMesh) scalpFrostedMesh.visible = showFrostedShell;
  if (cortexMesh) {
    cortexMesh.visible = showFrostedShell;
    if (isConnectome) cortexMesh.material = ensureConnectomeLayer().ghostCortexMaterial;
    else if (isSource) cortexMesh.material = ensureSourceLayer().sourceCortexMaterial;
    else if (isTract) cortexMesh.material = ensureTractographyLayer().ghostCortexMaterial;
    else cortexMesh.material = cortexSolidMaterial || cortexMesh.material;
  }
  rig.electrodeGroup.visible = !isMri && !isConnectome && !isSource && !isTract;
  if (isConnectome) ensureConnectomeLayer().setVisible(true);
  else if (connectomeLayer) connectomeLayer.setVisible(false);
  if (isSource) ensureSourceLayer().setVisible(true);
  else if (sourceLayer) sourceLayer.setVisible(false);
  if (isTract) ensureTractographyLayer().setVisible(true);
  else if (tractographyLayer) tractographyLayer.setVisible(false);

  document.getElementById("mode-connectome-btn").classList.toggle("hidden", !state.enableLabModes);
  document.getElementById("mode-source-btn").classList.toggle("hidden", !state.enableLabModes);
  document.getElementById("mode-tract-btn").classList.toggle("hidden", !state.enableLabModes);
  document.getElementById("mode-mri-btn").classList.toggle("hidden", !state.enableLabModes);
  document.querySelectorAll("#mode-group .seg-btn").forEach((btn) => btn.classList.toggle("active", btn.dataset.mode === state.mode));
  document.getElementById("mode-group").classList.toggle("hidden", state.mode === "electrodes" || state.hideInternalModeTabs);
  document.getElementById("atlas-select").classList.toggle("hidden", state.mode !== "atlas");
  document.getElementById("atlas-card").classList.toggle("hidden", state.mode !== "atlas");
  document.getElementById("connectome-bar").classList.toggle("hidden", !isConnectome);
  document.getElementById("source-bar").classList.toggle("hidden", !isSource);
  document.getElementById("source-mri-btn").classList.toggle("hidden", !isSource);
  document.getElementById("tract-bar").classList.toggle("hidden", !isTract);
  document.getElementById("tract-open-btn").classList.toggle("hidden", !isTract);
  document.getElementById("tract-bundle-select").classList.toggle("hidden", !isTract || !!tractographyLayer?.isCustomLoaded());
  document.getElementById("tract-reset-file-btn").classList.toggle("hidden", !isTract || !tractographyLayer?.isCustomLoaded());
  document.getElementById("mri-layout-select").classList.toggle("hidden", !isMri);
  document.getElementById("mri-back-source-btn").classList.toggle("hidden", !isMri || !state.mriJumpFromSource);
  document.getElementById("mri-clip-btn").classList.toggle("hidden", !isMri);
  document.getElementById("mri-atlas-btn").classList.toggle("hidden", !isMri);
  document.getElementById("mri-open-btn").classList.toggle("hidden", !isMri);
  document.getElementById("hint-pill").classList.toggle("hidden", isMri || isConnectome || isSource || isTract);
  document.getElementById("mri-coord-pill").classList.toggle("hidden", !isMri);

  const showHeatUi = !isMri && (state.mode === "cortex" || state.mode === "scalp" || isConnectome || isSource);
  document.getElementById("colorbar-card").classList.toggle("hidden", !showHeatUi);
  document.getElementById("colormap-btn").classList.toggle("hidden", !showHeatUi);
  document.getElementById("preset-btn").classList.toggle("hidden", !((showHeatUi && !state.isRealField) || isConnectome || isSource));
  document.getElementById("dim-switcher").style.display = state.showDimSwitcher ? "inline-flex" : "none";

  updateColorbarUI();
  updateAtlasLegendUI();
}

function updateColorbarUI() {
  const cm = COLORMAPS[state.colormap] || COLORMAPS.turbo;
  const isConnectome = state.mode === "connectome";
  const isSource = state.mode === "source";
  document.getElementById("colormap-btn").textContent = `色带：${cm.label}`;
  document.getElementById("preset-btn").textContent = isConnectome
    ? `网络：${ensureConnectomeLayer().getPreset().label}`
    : (isSource ? `场景：${ensureSourceLayer().getPreset().label}` : `分布：${PRESETS[state.presetIndex].label}`);
  const gradStops = [...cm.stops].reverse().map(([p, rgb]) => `rgb(${rgb[0]},${rgb[1]},${rgb[2]}) ${((1 - p) * 100).toFixed(1)}%`).join(", ");
  document.getElementById("colorbar-gradient").style.background = `linear-gradient(to bottom, ${gradStops})`;
  document.getElementById("colorbar-unit").textContent = isConnectome ? "连接强度" : (isSource ? "sLORETA (nA·m)" : state.scaleInfo.unit);
  document.getElementById("tick-max").textContent = isConnectome ? "1.00" : (isSource ? "100" : state.scaleInfo.maxText);
  document.getElementById("tick-mid").textContent = isConnectome ? "0.60" : (isSource ? "50" : state.scaleInfo.midText);
  document.getElementById("tick-min").textContent = isConnectome ? "0.20" : (isSource ? "0" : state.scaleInfo.minText);
}

function updateAtlasLegendUI() {
  if (state.mode !== "atlas") return;
  const nextKey = `${state.atlasId}|${state.pinnedRegionId}`;
  if (nextKey === renderedAtlasKey) return;
  renderedAtlasKey = nextKey;

  const listEl = document.getElementById("atlas-list");
  listEl.innerHTML = "";
  const atlas = state.cortexData?.atlases?.[state.atlasId];
  if (!atlas) return;
  document.getElementById("atlas-card-title").textContent =
    atlas.id === "lobes" ? "脑叶 (Atlas)" : (atlas.id === "yeo7" ? "Yeo-7 网络" : "DK 脑区 (68)");
  for (const r of atlas.regions) {
    if (r.id === 0) continue;
    const item = document.createElement("div");
    item.className = "atlas-item" + (r.id === state.pinnedRegionId ? " active" : "");
    item.innerHTML = `<span class="atlas-swatch" style="background:${r.color}"></span><span class="atlas-name" title="${r.name} (${r.name_en})">${r.name}</span>`;
    item.addEventListener("mouseenter", () => { state.hoveredRegionId = r.id; recolorActiveSurfaces(); });
    item.addEventListener("mouseleave", () => { state.hoveredRegionId = -1; recolorActiveSurfaces(); });
    item.addEventListener("click", () => {
      state.pinnedRegionId = state.pinnedRegionId === r.id ? -1 : r.id;
      updateAtlasLegendUI();
      recolorActiveSurfaces();
    });
    listEl.appendChild(item);
  }
}

// --- Pointer Interaction ---
const { camCtrl } = rig;

host.addEventListener("pointerdown", (ev) => {
  if (ev.button !== 0) return;
  camCtrl.anim = null; camCtrl.dragging = true; camCtrl.panning = ev.shiftKey;
  camCtrl.lastX = ev.clientX; camCtrl.lastY = ev.clientY; camCtrl.movedPx = 0;
  host.classList.add("dragging");
  host.setPointerCapture(ev.pointerId);
});

host.addEventListener("pointermove", (ev) => {
  if (camCtrl.dragging) {
    const dx = ev.clientX - camCtrl.lastX, dy = ev.clientY - camCtrl.lastY;
    camCtrl.movedPx += Math.hypot(dx, dy);
    camCtrl.lastX = ev.clientX; camCtrl.lastY = ev.clientY;
    if (camCtrl.panning) {
      const right = new THREE.Vector3(1, 0, 0).applyQuaternion(camera.quaternion);
      const up = new THREE.Vector3(0, 1, 0).applyQuaternion(camera.quaternion);
      const panScale = camCtrl.distance * 0.0014;
      camCtrl.target.addScaledVector(right, -dx * panScale).addScaledVector(up, dy * panScale);
    } else {
      camCtrl.yaw -= dx * 0.0085;
      camCtrl.pitch = Math.max(-1.35, Math.min(1.35, camCtrl.pitch + dy * 0.0085));
    }
    tooltip.style.display = "none";
    return;
  }

  const rect = renderer.domElement.getBoundingClientRect();
  pointerNdc.x = ((ev.clientX - rect.left) / rect.width) * 2 - 1;
  pointerNdc.y = -((ev.clientY - rect.top) / rect.height) * 2 + 1;

  if (state.mode === "atlas" && cortexMesh && state.cortexData) {
    raycaster.setFromCamera(pointerNdc, camera);
    const hits = raycaster.intersectObject(cortexMesh, false);
    if (hits.length > 0 && hits[0].face) {
      const atlas = state.cortexData.atlases[state.atlasId];
      const rid = atlas?.regionIds?.[hits[0].face.a] ?? 0;
      const info = atlas?.regionsById?.get(rid);
      if (info && rid > 0) {
        if (state.hoveredRegionId !== rid) { state.hoveredRegionId = rid; recolorActiveSurfaces(); }
        tooltip.innerHTML = `<div>${info.name}</div><div class="sub">${info.name_en} · ${info.lobe}</div>`;
        tooltip.style.display = "block";
        tooltip.style.left = `${Math.min(rect.width - 170, ev.clientX - rect.left + 14)}px`;
        tooltip.style.top = `${Math.max(10, ev.clientY - rect.top - 12)}px`;
        return;
      }
    }
    if (state.hoveredRegionId !== -1) { state.hoveredRegionId = -1; recolorActiveSurfaces(); }
    tooltip.style.display = "none";
  } else if (state.mode === "connectome" && connectomeLayer) {
    raycaster.setFromCamera(pointerNdc, camera);
    const info = connectomeLayer.pickHover(raycaster);
    if (info) {
      tooltip.innerHTML = `<div>${info.title}</div><div class="sub">${info.sub}</div>`;
      tooltip.style.display = "block";
      tooltip.style.left = `${Math.min(rect.width - 170, ev.clientX - rect.left + 14)}px`;
      tooltip.style.top = `${Math.max(10, ev.clientY - rect.top - 12)}px`;
    } else {
      tooltip.style.display = "none";
    }
  } else if (state.mode === "source" && sourceLayer) {
    raycaster.setFromCamera(pointerNdc, camera);
    const info = sourceLayer.pickHover(raycaster);
    state.hoveredDipole = info ? info.dipole : null;
    if (info) {
      tooltip.innerHTML = `<div>${info.title}</div><div class="sub">${info.sub}</div>`;
      tooltip.style.display = "block";
      tooltip.style.left = `${Math.min(rect.width - 240, ev.clientX - rect.left + 14)}px`;
      tooltip.style.top = `${Math.max(10, ev.clientY - rect.top - 12)}px`;
      host.style.cursor = "pointer";
    } else {
      tooltip.style.display = "none";
      host.style.cursor = "grab";
    }
  } else {
    state.hoveredDipole = null;
    host.style.cursor = "grab";
    tooltip.style.display = "none";
  }
});

host.addEventListener("pointerup", (ev) => {
  if (!camCtrl.dragging) return;
  camCtrl.dragging = false;
  host.classList.remove("dragging");
  try { host.releasePointerCapture(ev.pointerId); } catch {}
  if (camCtrl.movedPx < 4 && state.mode === "atlas" && state.hoveredRegionId > 0) {
    state.pinnedRegionId = state.pinnedRegionId === state.hoveredRegionId ? -1 : state.hoveredRegionId;
    updateAtlasLegendUI();
    recolorActiveSurfaces();
  } else if (camCtrl.movedPx < 4 && state.mode === "source" && state.hoveredDipole) {
    const picked = ensureSourceLayer().selectDipole(state.hoveredDipole.id);
    updateSourceBarUI();
    jumpSourceToMri(picked);
  }
});

host.addEventListener("wheel", (ev) => {
  ev.preventDefault();
  camCtrl.anim = null;
  camCtrl.distance = Math.max(0.75, Math.min(5.2, camCtrl.distance * Math.pow(1.0014, ev.deltaY)));
}, { passive: false });

// --- UI Button Wiring ---
document.querySelectorAll("#mode-group .seg-btn").forEach((btn) => {
  btn.addEventListener("click", () => switchMode(btn.dataset.mode));
});

document.getElementById("atlas-select").addEventListener("change", (ev) => {
  state.atlasId = ev.target.value;
  state.hoveredRegionId = -1;
  state.pinnedRegionId = -1;
  updateAtlasLegendUI();
  recolorActiveSurfaces();
});

document.getElementById("colormap-btn").addEventListener("click", () => {
  state.colormap = COLORMAP_ORDER[(COLORMAP_ORDER.indexOf(state.colormap) + 1) % COLORMAP_ORDER.length];
  updateColorbarUI();
  recolorActiveSurfaces();
});

document.getElementById("preset-btn").addEventListener("click", () => {
  if (state.mode === "connectome") {
    const { preset, stats } = ensureConnectomeLayer().cyclePreset();
    updateColorbarUI();
    updateConnectomeStatusUI(stats);
    rig.startCameraAnim(preset.yaw, preset.pitch);
    return;
  }
  if (state.mode === "source") {
    const preset = ensureSourceLayer().cyclePreset(cortexMesh, state.cortexData);
    updateColorbarUI();
    updateSourceBarUI();
    rig.startCameraAnim(preset.yaw, preset.pitch);
    return;
  }
  state.presetIndex = (state.presetIndex + 1) % PRESETS.length;
  const p = PRESETS[state.presetIndex];
  updateColorbarUI();
  rig.applyPresetValues(p.id);
  recolorActiveSurfaces();
  rig.startCameraAnim(p.yaw, -p.pitch);
});

document.getElementById("source-mri-btn").addEventListener("click", () => {
  jumpSourceToMri(ensureSourceLayer().getSelectedDipole());
});
document.getElementById("source-toggle-dipole").addEventListener("click", (ev) => {
  const layer = ensureSourceLayer();
  const next = !layer.isShowDipoles();
  layer.setShowDipoles(next);
  ev.currentTarget.classList.toggle("active", next);
});
document.getElementById("source-toggle-hotspot").addEventListener("click", (ev) => {
  const layer = ensureSourceLayer();
  const next = !layer.isShowHotspots();
  layer.setShowHotspots(next);
  ev.currentTarget.classList.toggle("active", next);
  layer.update(cortexMesh, state.cortexData);
});

document.getElementById("tract-bundle-select").addEventListener("change", (ev) => {
  updateTractBarUI(ensureTractographyLayer().setBundle(ev.target.value));
});
document.getElementById("tract-opacity").addEventListener("input", (ev) => {
  ensureTractographyLayer().setOpacity(parseFloat(ev.target.value));
});
document.getElementById("tract-open-btn").addEventListener("click", () => document.getElementById("tract-file-input").click());
document.getElementById("tract-file-input").addEventListener("change", async (ev) => {
  const file = ev.target.files?.[0];
  ev.target.value = "";
  if (!file) return;
  try {
    const stats = await ensureTractographyLayer().loadLocalFile(file);
    updateTractBarUI(stats);
  } catch (err) {
    document.getElementById("tract-count").textContent = `加载失败：${err?.message || err}`;
  }
});
document.getElementById("tract-reset-file-btn").addEventListener("click", () => {
  updateTractBarUI(ensureTractographyLayer().clearCustomFile());
});

document.getElementById("mri-back-source-btn").addEventListener("click", () => {
  switchMode("source");
});

document.getElementById("connectome-threshold").addEventListener("input", (ev) => {
  const val = parseFloat(ev.target.value);
  document.getElementById("connectome-threshold-val").textContent = val.toFixed(2);
  if (state.mode === "connectome") updateConnectomeStatusUI(ensureConnectomeLayer().setThreshold(val));
});

document.getElementById("mri-layout-select").addEventListener("change", (ev) => mriPanel.setLayout(ev.target.value));
document.getElementById("mri-clip-btn").addEventListener("click", (ev) => {
  const on = mriPanel.toggleClip();
  ev.currentTarget.classList.toggle("active", on);
  ev.currentTarget.textContent = on ? "3D 剖切：开" : "3D 剖切：关";
});
document.getElementById("mri-atlas-btn").addEventListener("click", async (ev) => {
  const btn = ev.currentTarget;
  const on = await mriPanel.toggleAtlas();
  btn.classList.toggle("active", on);
  btn.textContent = on ? "核团叠加：开" : "核团叠加：关";
});
document.getElementById("mri-open-btn").addEventListener("click", () => document.getElementById("mri-file-input").click());
document.getElementById("mri-file-input").addEventListener("change", async (ev) => {
  const file = ev.target.files?.[0];
  ev.target.value = "";
  if (!file) return;
  await mriPanel.openLocalFile(file);
  const atlasBtn = document.getElementById("mri-atlas-btn");
  atlasBtn.classList.remove("active");
  atlasBtn.textContent = "核团叠加：关";
});

document.getElementById("reset-btn").addEventListener("click", () => {
  if (state.mode === "mri") mriPanel.resetView();
  else rig.resetCamera();
});
document.getElementById("btn-2d").addEventListener("click", () => window.chrome?.webview?.postMessage({ type: "switchTo2D" }));

window.addEventListener("resize", () => {
  const w = host.clientWidth || 1, h = host.clientHeight || 1;
  camera.aspect = w / h;
  camera.updateProjectionMatrix();
  renderer.setSize(w, h);
  mriPanel.resize();
});

// --- WPF <-> WebView2 Message Bridge ---
window.chrome?.webview?.addEventListener("message", (ev) => {
  const msg = typeof ev.data === "string" ? JSON.parse(ev.data) : ev.data;
  if (!msg || !msg.type) return;

  if (msg.type === "setScalpModel") {
    loadScalp(msg.model);
  } else if (msg.type === "setCortexModel") {
    loadCortex(msg.model);
  } else if (msg.type === "setSceneState") {
    state.isRealField = !!msg.isRealField;
    state.isPreviewHeatmap = !!msg.isPreviewHeatmap;
    state.enableLabModes = !!msg.enableLabModes;
    state.hideInternalModeTabs = !!msg.hideInternalModeTabs;
    state.showDimSwitcher = !!msg.showDimSwitcher;
    if (msg.forcedMode) state.mode = msg.forcedMode;
    else if (!state.isRealField && !state.isPreviewHeatmap) state.mode = "electrodes";
    else if (state.mode === "electrodes") state.mode = "cortex";
    else if ((state.mode === "connectome" || state.mode === "mri" || state.mode === "source" || state.mode === "tractography") && !state.enableLabModes) state.mode = "cortex";

    if (msg.scaleInfo) state.scaleInfo = msg.scaleInfo;
    const winEl = document.getElementById("window-pill");
    if (msg.windowText) { winEl.textContent = msg.windowText; winEl.style.display = "block"; }
    else { winEl.style.display = "none"; }

    const { layoutChanged } = rig.updateElectrodes(msg.electrodes || [], {
      scalpData: state.scalpData,
      isRealField: state.isRealField,
      mode: state.mode,
      presetId: PRESETS[state.presetIndex].id,
      showLabels: msg.showLabels,
    });
    if (layoutChanged) recolorer.invalidateAll();
    applyModeVisibility();
    recolorActiveSurfaces();
  } else if (msg.type === "setMode") {
    switchMode(msg.mode, { notifyHost: false });
  } else if (msg.type === "selectElectrode") {
    rig.selectElectrode(msg.label, state.mode);
  }
});

// --- Render Loop ---
function animate(now) {
  requestAnimationFrame(animate);
  if (state.mode === "mri") return;
  rig.stepFrame(now);
  if (state.mode === "source" && sourceLayer) sourceLayer.stepFrame(now);
  renderer.render(scene, camera);
  rig.projectLabels(
    state.mode === "connectome" && connectomeLayer
      ? (e) => connectomeLayer.getNodeForElectrode(e)
      : null
  );
}

rig.stepFrame(performance.now());
requestAnimationFrame(animate);
window.chrome?.webview?.postMessage({ type: "ready" });
