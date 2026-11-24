import * as THREE from "./vendor/three.module.min.js";

/**
 * Self-contained 3D Functional Connectivity (Connectome) layer for Three.js.
 *
 * Accepts any standard `{ nodes, edges, metricLabel }` dataset so future real
 * PLV / Coherence / wPLI matrices from the backend plug in with zero rewrite.
 * In preview mode, synthesizes 3 representative neuroscience connectivity motifs
 * from whichever 10-20 electrodes are currently enabled.
 */

export const CONNECTOME_PRESETS = [
  { id: "alpha_posterior", label: "静息 Alpha 枕-顶协同", yaw: -2.35, pitch: 0.28 },
  { id: "frontoparietal", label: "额-顶执行控制网络", yaw: 0.52, pitch: 0.30 },
  { id: "left_lateral", label: "左半球语言/运动偏侧", yaw: 1.48, pitch: 0.22 },
];

const NON_CORTICAL_REFS = new Set(["M1", "M2", "A1", "A2", "TP9", "TP10", "REF", "GND"]);

function computeCortexCenter(cortexNormVerts) {
  if (!cortexNormVerts || cortexNormVerts.length < 3) return new THREE.Vector3(0, 0.20, -0.02);
  let sx = 0, sy = 0, sz = 0;
  const n = cortexNormVerts.length / 3;
  for (let i = 0; i < n; i++) {
    sx += cortexNormVerts[i * 3];
    sy += cortexNormVerts[i * 3 + 1];
    sz += cortexNormVerts[i * 3 + 2];
  }
  return new THREE.Vector3(sx / n, sy / n, sz / n);
}

function projectToCortexSurface(scalpPos, cortexNormVerts, cortexCenter) {
  if (!cortexNormVerts || cortexNormVerts.length < 3) {
    return scalpPos.clone().sub(cortexCenter).multiplyScalar(0.88).add(cortexCenter);
  }
  // Find nearest vertex on the real pial cortex surface directly underneath this scalp electrode
  const sx = scalpPos.x, sy = scalpPos.y, sz = scalpPos.z;
  const n = cortexNormVerts.length / 3;
  let bestIdx = 0, bestD2 = Infinity;
  for (let i = 0; i < n; i++) {
    const dx = cortexNormVerts[i * 3] - sx;
    const dy = cortexNormVerts[i * 3 + 1] - sy;
    const dz = cortexNormVerts[i * 3 + 2] - sz;
    const d2 = dx * dx + dy * dy + dz * dz;
    if (d2 < bestD2) { bestD2 = d2; bestIdx = i; }
  }
  const p = new THREE.Vector3(
    cortexNormVerts[bestIdx * 3],
    cortexNormVerts[bestIdx * 3 + 1],
    cortexNormVerts[bestIdx * 3 + 2]
  );
  // Inset slightly (4%) toward cortex center so hubs & arcs sit cleanly inside the glass cortex shell
  return p.lerp(cortexCenter, 0.04);
}

function canonKey(label) {
  return (label || "").trim().toUpperCase().replace(/^EEG\s+/, "").replace(/-.*$/, "");
}

function buildDemoWeights(nodes, presetId) {
  const byCanon = new Map();
  nodes.forEach((n, idx) => byCanon.set(canonKey(n.canonical || n.label), idx));

  // Curated canonical channel pairs with realistic normalized connectivity strengths
  const motifs = {
    alpha_posterior: [
      ["O1", "O2", 0.96], ["O1", "P3", 0.91], ["O2", "P4", 0.92], ["P3", "Pz", 0.87], ["P4", "Pz", 0.88],
      ["P3", "P4", 0.84], ["O1", "Pz", 0.85], ["O2", "Pz", 0.86], ["O1", "T5", 0.81], ["O2", "T6", 0.82],
      ["P3", "T5", 0.77], ["P4", "T6", 0.78], ["Pz", "Cz", 0.72], ["P3", "C3", 0.66], ["P4", "C4", 0.67],
      ["Pz", "Fz", 0.69], ["O1", "P4", 0.62], ["O2", "P3", 0.63], ["C3", "Cz", 0.54], ["C4", "Cz", 0.55],
      ["T5", "T3", 0.58], ["T6", "T4", 0.59], ["Fz", "F3", 0.46], ["Fz", "F4", 0.47], ["F3", "C3", 0.43],
      ["F4", "C4", 0.44], ["Fp1", "Fz", 0.36], ["Fp2", "Fz", 0.37], ["F7", "T3", 0.34], ["F8", "T4", 0.35],
    ],
    frontoparietal: [
      ["Fp1", "F3", 0.93], ["Fp2", "F4", 0.94], ["F3", "Fz", 0.90], ["F4", "Fz", 0.91], ["Fp1", "Fp2", 0.86],
      ["F3", "F4", 0.88], ["F3", "P3", 0.89], ["F4", "P4", 0.90], ["Fz", "Pz", 0.85], ["Fz", "Cz", 0.80],
      ["F3", "C3", 0.78], ["F4", "C4", 0.79], ["C3", "P3", 0.75], ["C4", "P4", 0.76], ["Fp1", "F7", 0.73],
      ["Fp2", "F8", 0.74], ["F3", "F7", 0.70], ["F4", "F8", 0.71], ["P3", "Pz", 0.68], ["P4", "Pz", 0.69],
      ["F3", "P4", 0.60], ["F4", "P3", 0.61], ["C3", "C4", 0.56], ["T3", "F7", 0.48], ["T4", "F8", 0.49],
      ["O1", "P3", 0.42], ["O2", "P4", 0.43], ["T5", "P3", 0.39], ["T6", "P4", 0.40],
    ],
    left_lateral: [
      ["F7", "F3", 0.95], ["F3", "C3", 0.93], ["F7", "T3", 0.91], ["T3", "C3", 0.89], ["C3", "P3", 0.88],
      ["T3", "T5", 0.86], ["T5", "P3", 0.84], ["Fp1", "F7", 0.82], ["Fp1", "F3", 0.80], ["F3", "P3", 0.85],
      ["F7", "P3", 0.78], ["C3", "Cz", 0.75], ["F3", "Fz", 0.74], ["C3", "C4", 0.71], ["T5", "O1", 0.68],
      ["P3", "O1", 0.67], ["P3", "Pz", 0.64], ["F3", "F4", 0.58], ["T3", "P3", 0.76], ["F7", "C3", 0.81],
      ["Fz", "Cz", 0.52], ["C4", "P4", 0.38], ["F4", "C4", 0.36], ["Fp2", "F4", 0.34], ["O1", "O2", 0.41],
    ],
  };

  // Alias map (legacy 10-20 <-> modern 10-10)
  const aliases = { T3: "T7", T7: "T3", T4: "T8", T8: "T4", T5: "P7", P7: "T5", T6: "P8", P8: "T6" };
  const resolveIdx = (name) => {
    const k = canonKey(name);
    if (byCanon.has(k)) return byCanon.get(k);
    const alt = aliases[k];
    return alt && byCanon.has(alt) ? byCanon.get(alt) : -1;
  };

  const pairs = motifs[presetId] || motifs.alpha_posterior;
  const edges = [];
  const seen = new Set();
  for (const [aName, bName, w] of pairs) {
    const i = resolveIdx(aName);
    const j = resolveIdx(bName);
    if (i < 0 || j < 0 || i === j) continue;
    const key = i < j ? `${i}:${j}` : `${j}:${i}`;
    if (seen.has(key)) continue;
    seen.add(key);
    edges.push({ i, j, weight: w });
  }

  // Fallback if custom non-10-20 channel names are used: connect nearest pairs smoothly
  if (edges.length === 0 && nodes.length >= 2) {
    for (let i = 0; i < nodes.length; i++) {
      for (let j = i + 1; j < nodes.length; j++) {
        const d = nodes[i].pos.distanceTo(nodes[j].pos);
        const w = Math.max(0.2, Math.min(0.95, 1.05 - d * 1.15));
        if (w >= 0.35) edges.push({ i, j, weight: Number(w.toFixed(2)) });
      }
    }
  }
  return edges;
}

export function createConnectomeLayer({ rootGroup, evalColormap, srgbToLinear }) {
  const group = new THREE.Group();
  group.visible = false;
  rootGroup.add(group);

  const edgeGroup = new THREE.Group();
  const nodeGroup = new THREE.Group();
  group.add(edgeGroup);
  group.add(nodeGroup);

  // Subtle glass-brain ghost material used on cortexMesh while connectome is active
  const ghostCortexMaterial = new THREE.ShaderMaterial({
    transparent: true,
    depthWrite: false,
    side: THREE.FrontSide,
    uniforms: {
      uColor: { value: new THREE.Vector3(0.76, 0.82, 0.90) },
      uRimColor: { value: new THREE.Vector3(0.56, 0.66, 0.80) },
    },
    vertexShader: `
      varying vec3 vNormalView;
      varying vec3 vViewPos;
      void main() {
        vec4 mv = modelViewMatrix * vec4(position, 1.0);
        vViewPos = -mv.xyz;
        vNormalView = normalize(normalMatrix * normal);
        gl_Position = projectionMatrix * mv;
      }
    `,
    fragmentShader: `
      uniform vec3 uColor;
      uniform vec3 uRimColor;
      varying vec3 vNormalView;
      varying vec3 vViewPos;
      void main() {
        vec3 N = normalize(vNormalView);
        vec3 V = normalize(vViewPos);
        float ndotv = clamp(dot(N, V), 0.0, 1.0);
        float fresnel = pow(1.0 - ndotv, 1.8);
        vec3 col = mix(uColor, uRimColor, fresnel * 0.7);
        float alpha = clamp(0.11 + fresnel * 0.24, 0.10, 0.36);
        gl_FragColor = vec4(col, alpha);
      }
    `,
  });

  let nodes = [];        // [{ label, canonical, pos: Vector3 (cortical anchor), normal: Vector3 }]
  let nodesByKey = new Map();
  let edges = [];        // [{ i, j, weight }]
  let edgeMeshes = [];   // [{ mesh, edge, baseColor, baseOpacity }]
  let nodeMeshes = [];   // [{ mesh, nodeIdx, degree }]
  let cortexCenter = new THREE.Vector3(0, 0.20, -0.02);
  let threshold = 0.50;
  let colormap = "turbo";
  let presetIndex = 0;
  let hoveredMesh = null;
  let metricLabel = "示意 PLV";

  const nodeGeom = new THREE.SphereGeometry(0.0125, 18, 14);

  function clearMeshes() {
    while (edgeGroup.children.length > 0) {
      const m = edgeGroup.children.pop();
      m.geometry?.dispose();
      m.material?.dispose();
    }
    while (nodeGroup.children.length > 0) {
      const m = nodeGroup.children.pop();
      m.material?.dispose();
    }
    edgeMeshes = [];
    nodeMeshes = [];
    hoveredMesh = null;
  }

  function colorForWeight(w) {
    // Map weight [0.25 .. 1.0] smoothly into colormap range [0.18 .. 0.96] for vibrant glow
    const u = Math.max(0.15, Math.min(0.97, (w - 0.22) / 0.78));
    const [r, g, b] = evalColormap(u, colormap);
    return new THREE.Color().setRGB(srgbToLinear(r), srgbToLinear(g), srgbToLinear(b));
  }

  function rebuildGeometry() {
    clearMeshes();
    if (nodes.length === 0) return { shown: 0, total: 0 };

    const activeEdges = edges.filter((e) => e.weight >= threshold);
    const degree = new Float32Array(nodes.length);
    for (const e of activeEdges) {
      degree[e.i] += e.weight;
      degree[e.j] += e.weight;
    }
    let maxDeg = 0.001;
    for (let i = 0; i < degree.length; i++) if (degree[i] > maxDeg) maxDeg = degree[i];

    for (const e of activeEdges) {
      const p0 = nodes[e.i].pos;
      const p1 = nodes[e.j].pos;
      const dist = p0.distanceTo(p1);
      const mid = new THREE.Vector3().addVectors(p0, p1).multiplyScalar(0.5);
      // Uniform subtle inward arc toward true cortex centroid (10%-18% geometric rhythm)
      const pull = Math.min(0.18, 0.09 + dist * 0.14);
      const ctrl = mid.lerp(cortexCenter, pull);

      const curve = new THREE.QuadraticBezierCurve3(p0, ctrl, p1);
      const tNorm = Math.max(0, Math.min(1, (e.weight - threshold) / Math.max(0.12, 1.0 - threshold)));
      const radius = 0.0024 + tNorm * 0.0042;
      const geom = new THREE.TubeGeometry(curve, 24, radius, 8, false);
      const col = colorForWeight(e.weight);
      const opacity = 0.48 + tNorm * 0.48;

      const mat = new THREE.MeshStandardMaterial({
        color: col,
        emissive: col,
        emissiveIntensity: 0.32 + tNorm * 0.36,
        roughness: 0.26,
        metalness: 0.08,
        transparent: true,
        opacity,
      });
      const mesh = new THREE.Mesh(geom, mat);
      mesh.renderOrder = 3;
      mesh.userData = { kind: "edge", edge: e };
      edgeGroup.add(mesh);
      edgeMeshes.push({ mesh, edge: e, baseOpacity: opacity, baseEmissive: 0.32 + tNorm * 0.36 });
    }

    // Hub nodes scaled by normalized weighted degree centrality
    for (let i = 0; i < nodes.length; i++) {
      const degNorm = degree[i] / maxDeg;
      const isActive = degree[i] > 1e-4;
      const col = isActive ? colorForWeight(0.35 + degNorm * 0.65) : new THREE.Color(0x94a3b8);
      const baseEmissive = isActive ? 0.26 + degNorm * 0.44 : 0.04;
      const baseScale = isActive ? 0.78 + degNorm * 0.66 : 0.46;
      const mat = new THREE.MeshStandardMaterial({
        color: col,
        emissive: col,
        emissiveIntensity: baseEmissive,
        roughness: 0.25,
        metalness: 0.12,
        transparent: true,
        opacity: 1.0,
      });
      const mesh = new THREE.Mesh(nodeGeom, mat);
      mesh.position.copy(nodes[i].pos);
      mesh.scale.setScalar(baseScale);
      mesh.renderOrder = 3;
      mesh.userData = { kind: "node", nodeIndex: i, degree: degree[i] };
      nodeGroup.add(mesh);
      nodeMeshes.push({ mesh, nodeIndex: i, degree: degree[i], baseEmissive, baseScale });
    }

    return { shown: activeEdges.length, total: edges.length };
  }

  function setElectrodes(electrodes, cortexNormVerts = null, customEdges = null, customMetric = null) {
    cortexCenter = computeCortexCenter(cortexNormVerts);
    nodes = [];
    nodesByKey = new Map();
    for (const e of (electrodes || [])) {
      const ck = canonKey(e.canonical || e.label);
      // Exclude ear/mastoid reference sites from cortical connectome hubs
      if (NON_CORTICAL_REFS.has(ck)) continue;
      const pos = projectToCortexSurface(e.pos, cortexNormVerts, cortexCenter);
      const normal = pos.clone().sub(cortexCenter).normalize();
      if (normal.lengthSq() < 1e-6) normal.set(0, 1, 0);
      const node = { label: e.label, canonical: e.canonical || e.label, pos, normal };
      nodes.push(node);
      nodesByKey.set(ck, node);
    }
    metricLabel = customMetric || "示意 PLV";
    edges = customEdges || buildDemoWeights(nodes, CONNECTOME_PRESETS[presetIndex].id);
    return rebuildGeometry();
  }

  function cyclePreset() {
    presetIndex = (presetIndex + 1) % CONNECTOME_PRESETS.length;
    const preset = CONNECTOME_PRESETS[presetIndex];
    edges = buildDemoWeights(nodes, preset.id);
    const stats = rebuildGeometry();
    return { preset, stats };
  }

  function setThreshold(val) {
    threshold = Math.max(0.15, Math.min(0.95, val));
    return rebuildGeometry();
  }

  function setColormap(cm) {
    colormap = cm;
    return rebuildGeometry();
  }

  function applyHoverStyles(hitMesh) {
    if (!hitMesh) {
      for (const item of edgeMeshes) {
        item.mesh.material.opacity = item.baseOpacity;
        item.mesh.material.emissiveIntensity = item.baseEmissive;
      }
      for (const item of nodeMeshes) {
        item.mesh.material.opacity = 1.0;
        item.mesh.material.emissiveIntensity = item.baseEmissive;
        item.mesh.scale.setScalar(item.baseScale);
      }
      return;
    }

    const activeNodes = new Set();
    const isNode = hitMesh.userData.kind === "node";
    const targetNodeIdx = isNode ? hitMesh.userData.nodeIndex : -1;
    if (isNode) activeNodes.add(targetNodeIdx);

    for (const item of edgeMeshes) {
      const related = isNode
        ? (item.edge.i === targetNodeIdx || item.edge.j === targetNodeIdx)
        : (item.mesh === hitMesh);
      if (related) {
        activeNodes.add(item.edge.i);
        activeNodes.add(item.edge.j);
        item.mesh.material.opacity = 1.0;
        item.mesh.material.emissiveIntensity = 0.90;
      } else {
        item.mesh.material.opacity = 0.14;
        item.mesh.material.emissiveIntensity = 0.08;
      }
    }

    for (const item of nodeMeshes) {
      const isFocus = isNode && item.nodeIndex === targetNodeIdx;
      const related = activeNodes.has(item.nodeIndex);
      item.mesh.material.opacity = related ? 1.0 : 0.24;
      item.mesh.material.emissiveIntensity = isFocus ? 0.95 : (related ? Math.max(0.55, item.baseEmissive) : 0.05);
      item.mesh.scale.setScalar(isFocus ? item.baseScale * 1.18 : item.baseScale);
    }
  }

  function pickHover(raycaster) {
    if (!group.visible || (edgeMeshes.length === 0 && nodeMeshes.length === 0)) return null;
    // Prioritize hub node spheres so dense multi-edge hubs (e.g. P3, P4) are easy to inspect
    const nodeHits = raycaster.intersectObjects(nodeMeshes.map((i) => i.mesh), false);
    const hitMesh = nodeHits.length > 0
      ? nodeHits[0].object
      : (raycaster.intersectObjects(edgeMeshes.map((i) => i.mesh), false)[0]?.object || null);

    if (hoveredMesh !== hitMesh) {
      hoveredMesh = hitMesh;
      applyHoverStyles(hitMesh);
    }
    if (!hitMesh) return null;

    if (hitMesh.userData.kind === "node") {
      const ni = hitMesh.userData.nodeIndex;
      const neighbors = edgeMeshes
        .filter((item) => item.edge.i === ni || item.edge.j === ni)
        .map((item) => ({
          label: nodes[item.edge.i === ni ? item.edge.j : item.edge.i]?.label || "?",
          weight: item.edge.weight,
        }))
        .sort((a, b) => b.weight - a.weight);
      const topText = neighbors.length > 0
        ? neighbors.slice(0, 3).map((n) => `${n.label} ${n.weight.toFixed(2)}`).join(" · ")
        : "当前阈值下无连边";
      return {
        title: `${nodes[ni]?.label || "?"} 枢纽节点（${neighbors.length} 条连边）`,
        sub: topText,
      };
    }

    const e = hitMesh.userData.edge;
    return {
      title: `${nodes[e.i]?.label || "?"} ↔ ${nodes[e.j]?.label || "?"}`,
      sub: `${metricLabel}：${e.weight.toFixed(2)}`,
    };
  }

  function clearHover() {
    if (!hoveredMesh) return;
    hoveredMesh = null;
    applyHoverStyles(null);
  }

  return {
    group,
    ghostCortexMaterial,
    setVisible(v) { group.visible = !!v; if (!v) clearHover(); },
    setElectrodes,
    cyclePreset,
    setThreshold,
    setColormap,
    pickHover,
    clearHover,
    getPreset() { return CONNECTOME_PRESETS[presetIndex]; },
    getThreshold() { return threshold; },
    getNodeForElectrode(e) { return nodesByKey.get(canonKey(e.canonical || e.label)) || null; },
    getStats() {
      const shown = edges.filter((e) => e.weight >= threshold).length;
      return { shown, total: edges.length };
    },
  };
}
