import * as THREE from "./vendor/three.module.min.js";

/**
 * 3D EEG Source Localization experimental layer (Demo sLORETA + Equivalent Current Dipoles).
 * Renders translucent sLORETA cortical activation hotspots on the folded fsaverage cortex
 * together with 3D dipole spheres, directional current-moment arrows, confidence rings,
 * hover inspection, and click-to-jump MNI crosshair synchronization with NiiVue MRI slices.
 */

const SOURCE_PRESETS = [
  {
    id: "temporal_epilepsy",
    label: "左颞叶致痫灶 (示意)",
    yaw: 1.18,
    pitch: 0.16,
    dipoles: [
      {
        id: "D1",
        name: "主偶极子 · 左侧颞中回/海马旁回",
        nameEn: "L Middle Temporal / Parahippocampal",
        mni: [-46, -16, -14],
        moment: [-0.45, 0.35, 0.82],
        strength: 92.4,
        gof: 94.6,
        radiusMm: 28,
        primary: true,
      },
      {
        id: "D2",
        name: "次级传播源 · 左侧颞上回后部",
        nameEn: "L Superior Temporal Gyrus",
        mni: [-54, -30, 8],
        moment: [-0.72, -0.25, 0.64],
        strength: 54.1,
        gof: 86.2,
        radiusMm: 22,
        primary: false,
      },
    ],
  },
  {
    id: "motor_hand",
    label: "右手运动皮层 (示意)",
    yaw: 0.78,
    pitch: 0.52,
    dipoles: [
      {
        id: "D1",
        name: "主偶极子 · 左侧中央前回手结区 (M1)",
        nameEn: "L Precentral Hand Knob",
        mni: [-38, -22, 54],
        moment: [-0.36, 0.52, 0.77],
        strength: 78.6,
        gof: 92.8,
        radiusMm: 26,
        primary: true,
      },
      {
        id: "D2",
        name: "辅助运动区 (SMA)",
        nameEn: "Supplementary Motor Area",
        mni: [-6, -4, 58],
        moment: [-0.15, 0.28, 0.95],
        strength: 49.2,
        gof: 84.5,
        radiusMm: 21,
        primary: false,
      },
      {
        id: "D3",
        name: "对侧运动皮层抑制 (R M1)",
        nameEn: "R Precentral Gyrus",
        mni: [36, -24, 52],
        moment: [0.35, 0.45, 0.82],
        strength: 34.8,
        gof: 79.4,
        radiusMm: 18,
        primary: false,
      },
    ],
  },
  {
    id: "visual_vep",
    label: "枕叶视觉诱发 P100 (示意)",
    yaw: 2.72,
    pitch: 0.22,
    dipoles: [
      {
        id: "D1",
        name: "主偶极子 · 左侧距状裂视觉皮层 (V1)",
        nameEn: "L Calcarine Cortex (V1)",
        mni: [-14, -84, 6],
        moment: [-0.28, -0.86, 0.42],
        strength: 84.3,
        gof: 95.1,
        radiusMm: 25,
        primary: true,
      },
      {
        id: "D2",
        name: "对称激活 · 右侧距状裂视觉皮层 (V1)",
        nameEn: "R Calcarine Cortex (V1)",
        mni: [16, -82, 8],
        moment: [0.26, -0.87, 0.41],
        strength: 80.7,
        gof: 94.3,
        radiusMm: 25,
        primary: false,
      },
    ],
  },
];

export function createSourceLayer({ rootGroup, evalColormap, srgbToLinear }) {
  const group = new THREE.Group();
  group.visible = false;
  rootGroup.add(group);

  // Translucent cortex material so deep dipoles remain clearly visible inside active gyri.
  const sourceCortexMaterial = new THREE.MeshStandardMaterial({
    vertexColors: true,
    roughness: 0.46,
    metalness: 0.04,
    transparent: true,
    opacity: 0.44,
    depthWrite: false,
    side: THREE.FrontSide,
  });

  const dipoleGroup = new THREE.Group();
  group.add(dipoleGroup);

  let presetIndex = 0;
  let colormapId = "turbo";
  let showDipoles = true;
  let showHotspots = true;
  let cortexBounds = null;
  let dipoleItems = [];
  let pickTargets = [];
  let hoveredId = null;
  let selectedId = null;
  let ringMeshes = [];

  function computeCortexBounds(normVerts) {
    if (!normVerts || normVerts.length < 9) return null;
    let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity, minZ = Infinity, maxZ = -Infinity;
    for (let i = 0; i < normVerts.length; i += 3) {
      const x = normVerts[i], y = normVerts[i + 1], z = normVerts[i + 2];
      if (x < minX) minX = x; if (x > maxX) maxX = x;
      if (y < minY) minY = y; if (y > maxY) maxY = y;
      if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
    }
    return {
      cx: (minX + maxX) * 0.5,
      cy: (minY + maxY) * 0.5,
      cz: (minZ + maxZ) * 0.5,
      rx: Math.max(0.05, (maxX - minX) * 0.5),
      ry: Math.max(0.05, (maxY - minY) * 0.5),
      rz: Math.max(0.05, (maxZ - minZ) * 0.5),
    };
  }

  // Maps MNI RAS (mm) into our right-handed Three.js cortex space (+X = -R, +Y = +S, +Z = +A).
  function mniToScene(mni) {
    const b = cortexBounds || { cx: 0, cy: 0.08, cz: 0, rx: 0.24, ry: 0.22, rz: 0.29 };
    const [mniR, mniA, mniS] = mni;
    const x = b.cx - (mniR / 70.0) * b.rx * 0.94;
    const y = b.cy + ((mniS - 14.0) / 62.0) * b.ry * 0.94;
    const z = b.cz + ((mniA + 16.0) / 88.0) * b.rz * 0.94;
    return new THREE.Vector3(x, y, z);
  }

  function clearDipoles() {
    while (dipoleGroup.children.length > 0) {
      const child = dipoleGroup.children[0];
      dipoleGroup.remove(child);
      child.traverse?.((obj) => {
        obj.geometry?.dispose?.();
        obj.material?.dispose?.();
      });
    }
    dipoleItems = [];
    pickTargets = [];
    ringMeshes = [];
  }

  function rebuildDipoles() {
    clearDipoles();
    const preset = SOURCE_PRESETS[presetIndex];
    const b = cortexBounds || { rx: 0.24 };
    const unitScale = b.rx / 0.24;

    if (!selectedId || !preset.dipoles.some((d) => d.id === selectedId)) {
      selectedId = preset.dipoles[0]?.id || null;
    }

    for (const d of preset.dipoles) {
      const origin = mniToScene(d.mni);
      const dir = new THREE.Vector3(-d.moment[0], d.moment[2], d.moment[1]).normalize();
      const holder = new THREE.Group();
      holder.position.copy(origin);

      const isPrimary = d.primary;
      const baseColor = isPrimary ? 0xff4d2e : 0xf59e0b;
      const emissiveColor = isPrimary ? 0xc2270f : 0xb45309;
      const sphereRadius = (isPrimary ? 0.023 : 0.017) * unitScale;

      const sphereMat = new THREE.MeshStandardMaterial({
        color: baseColor,
        emissive: emissiveColor,
        emissiveIntensity: 0.65,
        roughness: 0.22,
        metalness: 0.15,
      });
      const sphere = new THREE.Mesh(new THREE.SphereGeometry(sphereRadius, 24, 24), sphereMat);
      sphere.renderOrder = 3;
      sphere.userData = { dipole: d };
      holder.add(sphere);

      // Dipole current-moment vector arrow (shaft + sharp cone head)
      const arrowLen = (0.075 + (d.strength / 100) * 0.065) * unitScale;
      const shaftLen = arrowLen * 0.72;
      const headLen = arrowLen * 0.28;
      const shaftRadius = (isPrimary ? 0.0068 : 0.0052) * unitScale;
      const headRadius = (isPrimary ? 0.017 : 0.013) * unitScale;

      const arrowPivot = new THREE.Group();
      const quat = new THREE.Quaternion().setFromUnitVectors(new THREE.Vector3(0, 1, 0), dir);
      arrowPivot.quaternion.copy(quat);

      const arrowMat = new THREE.MeshStandardMaterial({
        color: isPrimary ? 0xff7b1c : 0xfbbf24,
        emissive: isPrimary ? 0xb43b06 : 0x92400e,
        emissiveIntensity: 0.45,
        roughness: 0.26,
        metalness: 0.22,
      });

      const shaft = new THREE.Mesh(new THREE.CylinderGeometry(shaftRadius * 0.85, shaftRadius, shaftLen, 14), arrowMat);
      shaft.position.y = sphereRadius * 0.6 + shaftLen * 0.5;
      shaft.renderOrder = 3;
      shaft.userData = { dipole: d };
      arrowPivot.add(shaft);

      const cone = new THREE.Mesh(new THREE.ConeGeometry(headRadius, headLen, 18), arrowMat);
      cone.position.y = sphereRadius * 0.6 + shaftLen + headLen * 0.5;
      cone.renderOrder = 3;
      cone.userData = { dipole: d };
      arrowPivot.add(cone);
      holder.add(arrowPivot);

      // Translucent confidence halo sphere (pulsed subtly in stepFrame)
      const haloMat = new THREE.MeshBasicMaterial({
        color: isPrimary ? 0xff5a36 : 0xf59e0b,
        transparent: true,
        opacity: isPrimary ? 0.24 : 0.16,
        depthWrite: false,
      });
      const halo = new THREE.Mesh(new THREE.SphereGeometry(sphereRadius * 1.9, 20, 20), haloMat);
      halo.renderOrder = 2;
      holder.add(halo);
      ringMeshes.push({ mesh: halo, baseOpacity: haloMat.opacity, dipoleId: d.id });

      // Invisible slightly larger pick sphere for effortless hovering/clicking
      const hitMesh = new THREE.Mesh(
        new THREE.SphereGeometry(sphereRadius * 2.2, 12, 12),
        new THREE.MeshBasicMaterial({ visible: false })
      );
      hitMesh.userData = { dipole: d };
      holder.add(hitMesh);

      dipoleGroup.add(holder);
      dipoleItems.push({ dipole: d, holder, sphereMat, arrowMat, origin });
      pickTargets.push(hitMesh, shaft, cone);
    }

    dipoleGroup.visible = showDipoles;
    applyHighlightState();
  }

  function paintCortexHotspots(cortexMesh, cortexData) {
    if (!cortexMesh || !cortexData?.normVerts) return;
    const verts = cortexData.normVerts;
    const sulc = cortexData.sulc;
    const n = verts.length / 3;
    const colAttr = cortexMesh.geometry.getAttribute("color");
    if (!colAttr) return;
    const colors = colAttr.array;

    const preset = SOURCE_PRESETS[presetIndex];
    const b = cortexBounds || { rx: 0.24 };
    const mmToScene = b.rx / 70.0;
    const sources = preset.dipoles.map((d) => ({
      pos: mniToScene(d.mni),
      sigma2: Math.pow(Math.max(14, d.radiusMm) * mmToScene * 1.25, 2),
      weight: Math.min(1.0, d.strength / 92.0),
    }));

    for (let i = 0; i < n; i++) {
      const vx = verts[i * 3], vy = verts[i * 3 + 1], vz = verts[i * 3 + 2];
      const s = sulc ? sulc[i] : 0;
      const ao = 0.84 + 0.16 * Math.max(-1, Math.min(1, s));

      let act = 0;
      if (showHotspots) {
        for (let k = 0; k < sources.length; k++) {
          const src = sources[k];
          const dx = vx - src.pos.x, dy = vy - src.pos.y, dz = vz - src.pos.z;
          const d2 = dx * dx + dy * dy + dz * dz;
          const g = src.weight * Math.exp(-d2 / (2 * src.sigma2));
          if (g > act) act = g;
        }
      }

      // Base translucent cool slate-ivory cortex + focal glowing sLORETA hot spots
      let r = 188 * ao, g = 198 * ao, bl = 212 * ao;
      if (act > 0.16) {
        const t = Math.min(1, (act - 0.16) / 0.74);
        const blend = Math.pow(t, 0.62);
        const hot = evalColormap(0.42 + 0.58 * t, colormapId);
        r = r * (1 - blend) + hot[0] * 255 * (0.92 + 0.08 * ao) * blend;
        g = g * (1 - blend) + hot[1] * 255 * (0.92 + 0.08 * ao) * blend;
        bl = bl * (1 - blend) + hot[2] * 255 * (0.92 + 0.08 * ao) * blend;
      }

      colors[i * 3] = srgbToLinear(r / 255);
      colors[i * 3 + 1] = srgbToLinear(g / 255);
      colors[i * 3 + 2] = srgbToLinear(bl / 255);
    }
    colAttr.needsUpdate = true;
  }

  function applyHighlightState() {
    for (const item of dipoleItems) {
      const active = item.dipole.id === hoveredId || item.dipole.id === selectedId;
      item.sphereMat.emissiveIntensity = active ? 1.05 : 0.48;
      item.arrowMat.emissiveIntensity = active ? 0.78 : 0.34;
      const s = item.dipole.id === hoveredId ? 1.18 : (item.dipole.id === selectedId ? 1.08 : 0.96);
      item.holder.scale.setScalar(s);
    }
  }

  function update(cortexMesh, cortexData) {
    if (cortexData?.normVerts) cortexBounds = computeCortexBounds(cortexData.normVerts);
    rebuildDipoles();
    paintCortexHotspots(cortexMesh, cortexData);
  }

  function stepFrame(now) {
    if (!group.visible || !ringMeshes.length) return;
    const pulse = 0.5 + 0.5 * Math.sin(now * 0.0042);
    for (const r of ringMeshes) {
      const isFocus = r.dipoleId === selectedId || r.dipoleId === hoveredId;
      const scale = isFocus ? (0.95 + 0.36 * pulse) : 0.92;
      r.mesh.scale.setScalar(scale);
      r.mesh.material.opacity = isFocus ? (r.baseOpacity * (1.25 - 0.55 * pulse)) : (r.baseOpacity * 0.5);
    }
  }

  function pickHover(raycaster) {
    if (!group.visible || !pickTargets.length) return null;
    const hits = raycaster.intersectObjects(pickTargets, false);
    const d = hits.length > 0 ? hits[0].object.userData?.dipole : null;
    const nextId = d ? d.id : null;
    if (nextId !== hoveredId) {
      hoveredId = nextId;
      applyHighlightState();
    }
    if (!d) return null;
    return {
      dipole: d,
      title: `${d.id} · ${d.name}`,
      sub: `MNI (${d.mni[0]}, ${d.mni[1]}, ${d.mni[2]}) mm · ${d.strength.toFixed(1)} nA·m · GOF ${d.gof.toFixed(1)}% · 点击跳转 MRI 切片`,
    };
  }

  function selectDipole(id) {
    selectedId = id;
    applyHighlightState();
    return getSelectedDipole();
  }

  function getSelectedDipole() {
    const preset = SOURCE_PRESETS[presetIndex];
    return preset.dipoles.find((d) => d.id === selectedId) || preset.dipoles[0] || null;
  }

  return {
    sourceCortexMaterial,
    setVisible(v) { group.visible = !!v; if (!v) hoveredId = null; },
    setColormap(id) { colormapId = id || "inferno"; },
    setShowDipoles(v) { showDipoles = !!v; dipoleGroup.visible = showDipoles; },
    setShowHotspots(v) { showHotspots = !!v; },
    isShowDipoles: () => showDipoles,
    isShowHotspots: () => showHotspots,
    getPreset: () => SOURCE_PRESETS[presetIndex],
    getSelectedDipole,
    selectDipole,
    cyclePreset(cortexMesh, cortexData) {
      presetIndex = (presetIndex + 1) % SOURCE_PRESETS.length;
      selectedId = SOURCE_PRESETS[presetIndex].dipoles[0]?.id || null;
      update(cortexMesh, cortexData);
      return SOURCE_PRESETS[presetIndex];
    },
    update,
    stepFrame,
    pickHover,
  };
}
