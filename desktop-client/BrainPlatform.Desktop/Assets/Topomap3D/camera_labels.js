import * as THREE from "./vendor/three.module.min.js";
import { evalPresetValue } from "./colormaps.js";

/**
 * Manages 3D scalp electrode pins (with in-place live reuse), dynamic 3D->2D label
 * projection, selected-electrode pulse highlight, and orbit/pan/zoom/fly-to camera.
 */
export function createCameraAndElectrodeRig({ camera, host, labelsLayer, rootGroup }) {
  const electrodeGroup = new THREE.Group();
  rootGroup.add(electrodeGroup);

  let electrodeMeshes = [];
  let labelElements = [];
  let electrodes = [];
  let layoutKey = "";
  let selectedLabel = null;
  let selectedMarkerMesh = null;
  let pulseStartMs = -1;
  let showLabels = true;

  // yaw=+0.50 faces subject's LEFT 3/4 view (Fp1/F3/F7/C3/T3/M1)
  const camCtrl = {
    yaw: 0.50, pitch: 0.24, distance: 2.35, target: new THREE.Vector3(0, 0.02, 0),
    defaultYaw: 0.50, defaultPitch: 0.24, defaultDistance: 2.35, defaultTarget: new THREE.Vector3(0, 0.02, 0),
    anim: null,
    dragging: false, panning: false, lastX: 0, lastY: 0, movedPx: 0,
  };

  function isSelectedElectrode(e) {
    if (!selectedLabel) return false;
    const s = selectedLabel.trim().toLowerCase();
    return e.label.toLowerCase() === s || e.canonical.toLowerCase() === s;
  }

  function clearElectrodes() {
    while (electrodeGroup.children.length > 0) {
      const child = electrodeGroup.children.pop();
      child.geometry?.dispose();
      child.material?.dispose();
    }
    labelsLayer.innerHTML = "";
    electrodeMeshes = [];
    labelElements = [];
    selectedMarkerMesh = null;
    electrodes = [];
    layoutKey = "";
  }

  function updateElectrodes(items, { scalpData, isRealField, mode, presetId, showLabels: nextShowLabels = true }) {
    showLabels = nextShowLabels !== false;
    if (!scalpData || !items || items.length === 0) {
      clearElectrodes();
      return { layoutChanged: true };
    }

    const nextKey = items.map((it) => `${it.label}|${it.canonical || ""}|${Number(it.x).toFixed(4)},${Number(it.y).toFixed(4)},${Number(it.z).toFixed(4)}`).join(";");
    if (nextKey === layoutKey && electrodes.length === items.length) {
      for (let i = 0; i < items.length; i++) {
        const it = items[i];
        const e = electrodes[i];
        e.valueNorm = typeof it.valueNorm === "number" ? it.valueNorm : evalPresetValue(e.pos, presetId);
      }
      if (!isRealField && electrodes.length > 1) {
        let mn = Infinity, mx = -Infinity;
        for (const e of electrodes) { if (e.valueNorm < mn) mn = e.valueNorm; if (e.valueNorm > mx) mx = e.valueNorm; }
        const span = mx - mn;
        if (span > 1e-6) for (const e of electrodes) e.valueNorm = 0.05 + 0.90 * ((e.valueNorm - mn) / span);
      }
      return { layoutChanged: false };
    }

    clearElectrodes();
    layoutKey = nextKey;

    const baseRadius = items.length > 200 ? 0.0058 : items.length > 90 ? 0.0076 : 0.0102;
    const sphereGeom = new THREE.SphereGeometry(baseRadius, 14, 10);
    const rawValues = [];
    for (const item of items) {
      const [x, y, z] = scalpData.mapPoint(item.x, item.y, item.z);
      const rawPos = new THREE.Vector3(x, y, z);
      const normal = rawPos.clone().normalize();
      if (normal.lengthSq() < 1e-6) normal.set(0, 1, 0);
      const pos = rawPos.clone().addScaledVector(normal, baseRadius * 0.35);
      const v = typeof item.valueNorm === "number" ? item.valueNorm : evalPresetValue(pos, presetId);
      rawValues.push(v);
      electrodes.push({ label: item.label, canonical: item.canonical || item.label, pos, normal, valueNorm: v });
    }

    if (!isRealField && rawValues.length > 1) {
      const mn = Math.min(...rawValues), mx = Math.max(...rawValues), span = mx - mn;
      if (span > 1e-6) for (const e of electrodes) e.valueNorm = 0.05 + 0.90 * ((e.valueNorm - mn) / span);
    }

    for (const e of electrodes) {
      const isSel = isSelectedElectrode(e);
      const mat = new THREE.MeshStandardMaterial({
        color: isSel ? 0xf97316 : (mode === "electrodes" ? 0x1d4ed8 : 0x1e293b),
        emissive: isSel ? 0xea580c : 0x000000,
        emissiveIntensity: isSel ? 0.55 : 0.0,
        roughness: 0.32, metalness: 0.08,
      });
      const mesh = new THREE.Mesh(sphereGeom, mat);
      mesh.position.copy(e.pos);
      mesh.renderOrder = 8;
      if (isSel) { mesh.scale.setScalar(1.45); selectedMarkerMesh = mesh; }
      electrodeGroup.add(mesh);
      electrodeMeshes.push({ mesh, e });

      const el = document.createElement("div");
      el.className = isSel ? "elec-label selected" : "elec-label";
      el.textContent = e.label;
      labelsLayer.appendChild(el);
      labelElements.push({ el, e });
    }
    return { layoutChanged: true };
  }

  function applyPresetValues(presetId) {
    for (const e of electrodes) e.valueNorm = evalPresetValue(e.pos, presetId);
    if (electrodes.length > 1) {
      const vals = electrodes.map((e) => e.valueNorm);
      const mn = Math.min(...vals), mx = Math.max(...vals), span = mx - mn;
      if (span > 1e-6) for (const e of electrodes) e.valueNorm = 0.05 + 0.90 * ((e.valueNorm - mn) / span);
    }
  }

  function normalizeAngleDelta(d) {
    let w = (d + Math.PI) % (2 * Math.PI);
    if (w < 0) w += 2 * Math.PI;
    return w - Math.PI;
  }

  function startCameraAnim(targetYaw, targetPitch, targetDist = camCtrl.distance, targetVec = camCtrl.defaultTarget, durationMs = 540) {
    camCtrl.anim = {
      startMs: performance.now(),
      durationMs,
      y0: camCtrl.yaw, y1: camCtrl.yaw + normalizeAngleDelta(targetYaw - camCtrl.yaw),
      p0: camCtrl.pitch, p1: Math.max(-1.32, Math.min(1.32, targetPitch)),
      d0: camCtrl.distance, d1: targetDist,
      t0: camCtrl.target.clone(), t1: targetVec.clone(),
    };
  }

  function resetCamera() {
    startCameraAnim(camCtrl.defaultYaw, camCtrl.defaultPitch, camCtrl.defaultDistance, camCtrl.defaultTarget, 460);
  }

  function selectElectrode(label, mode) {
    selectedLabel = label || null;
    selectedMarkerMesh = null;
    for (const { mesh, e } of electrodeMeshes) {
      const sel = isSelectedElectrode(e);
      mesh.material.color.setHex(sel ? 0xf97316 : (mode === "electrodes" ? 0x1d4ed8 : 0x1e293b));
      mesh.material.emissive.setHex(sel ? 0xea580c : 0x000000);
      mesh.material.emissiveIntensity = sel ? 0.55 : 0.0;
      mesh.scale.setScalar(sel ? 1.45 : 1.0);
      if (sel) selectedMarkerMesh = mesh;
    }
    for (const { el, e } of labelElements) el.classList.toggle("selected", isSelectedElectrode(e));
    if (selectedLabel) {
      const found = electrodes.find(isSelectedElectrode);
      if (found) {
        const p = found.pos;
        const targetYaw = Math.atan2(p.x, p.z);
        const targetPitch = Math.max(-1.25, Math.min(1.25, Math.atan2(p.y, Math.hypot(p.x, p.z))));
        startCameraAnim(targetYaw, targetPitch, camCtrl.distance, camCtrl.defaultTarget, 560);
        pulseStartMs = performance.now();
      }
    }
  }

  function stepFrame(now) {
    if (camCtrl.anim) {
      const a = camCtrl.anim;
      const t = Math.min(1, (now - a.startMs) / a.durationMs);
      const eased = t < 0.5 ? 4 * t * t * t : 1 - Math.pow(-2 * t + 2, 3) / 2;
      camCtrl.yaw = a.y0 + (a.y1 - a.y0) * eased;
      camCtrl.pitch = a.p0 + (a.p1 - a.p0) * eased;
      camCtrl.distance = a.d0 + (a.d1 - a.d0) * eased;
      camCtrl.target.lerpVectors(a.t0, a.t1, eased);
      if (t >= 1) camCtrl.anim = null;
    }

    if (pulseStartMs >= 0 && selectedMarkerMesh) {
      const elapsed = (now - pulseStartMs) / 1000;
      if (elapsed >= 1.2) {
        selectedMarkerMesh.scale.setScalar(1.45);
        selectedMarkerMesh.material.emissiveIntensity = 0.55;
        pulseStartMs = -1;
      } else {
        const wave = 0.5 + 0.5 * Math.sin((elapsed / 1.2) * Math.PI * 4 - Math.PI * 0.5);
        selectedMarkerMesh.scale.setScalar(1.45 + 0.42 * wave);
        selectedMarkerMesh.material.emissiveIntensity = 0.45 + 0.65 * wave;
      }
    }

    const cosP = Math.cos(camCtrl.pitch), sinP = Math.sin(camCtrl.pitch);
    camera.position.set(
      camCtrl.target.x + camCtrl.distance * cosP * Math.sin(camCtrl.yaw),
      camCtrl.target.y + camCtrl.distance * sinP,
      camCtrl.target.z + camCtrl.distance * cosP * Math.cos(camCtrl.yaw)
    );
    camera.lookAt(camCtrl.target);
  }

  function projectLabels(connectomeNodeResolver = null) {
    if (labelElements.length === 0) return;
    const w = host.clientWidth, h = host.clientHeight;
    if (w <= 1 || h <= 1) return;
    const camPos = camera.position;
    const v = new THREE.Vector3();
    const toCam = new THREE.Vector3();
    const inConnectome = typeof connectomeNodeResolver === "function";

    for (const { el, e } of labelElements) {
      const isSel = isSelectedElectrode(e);
      if (!showLabels && !isSel) { el.style.display = "none"; continue; }
      let anchorPos = e.pos;
      let anchorNormal = e.normal;
      if (inConnectome) {
        const cNode = connectomeNodeResolver(e);
        if (!cNode) { el.style.display = "none"; continue; }
        anchorPos = cNode.pos;
        anchorNormal = cNode.normal;
      }
      toCam.copy(camPos).sub(anchorPos).normalize();
      const dot = anchorNormal.dot(toCam);
      if (!isSel && dot <= 0.05) { el.style.display = "none"; continue; }
      v.copy(anchorPos).project(camera);
      if (v.z > 1 || v.x < -1.1 || v.x > 1.1 || v.y < -1.1 || v.y > 1.1) { el.style.display = "none"; continue; }

      const sx = (v.x * 0.5 + 0.5) * w;
      const sy = (-v.y * 0.5 + 0.5) * h;
      const dist = camPos.distanceTo(anchorPos);
      const halfHWorld = dist * Math.tan((camera.fov * Math.PI) / 360);
      const spherePx = ((isSel ? 0.017 : (inConnectome ? 0.015 : 0.011)) / Math.max(0.01, halfHWorld)) * (h * 0.5);
      const opacity = isSel ? (dot > -0.15 ? 1 : 0.45) : Math.max(0, Math.min(1, (dot - 0.05) / 0.22));
      el.style.display = "block";
      el.style.opacity = opacity.toFixed(2);
      el.style.left = `${sx.toFixed(1)}px`;
      el.style.top = `${(sy - spherePx - 4).toFixed(1)}px`;
    }
  }

  return {
    camCtrl,
    electrodeGroup,
    updateElectrodes,
    applyPresetValues,
    selectElectrode,
    startCameraAnim,
    resetCamera,
    stepFrame,
    projectLabels,
    resetLayoutKey() { layoutKey = ""; },
    getElectrodes() { return electrodes; },
    getLayoutKey() { return layoutKey; },
  };
}
