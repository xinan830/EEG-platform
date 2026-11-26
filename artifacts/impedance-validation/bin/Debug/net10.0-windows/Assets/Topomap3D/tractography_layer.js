import * as THREE from "./vendor/three.module.min.js";

/**
 * 3D DTI White Matter Tractography experimental layer.
 * - Generates an anatomically structured 5-bundle human white-matter demonstration atlas
 *   fitted strictly inside the loaded fsaverage cortex bounding box.
 * - Uses standard DTI DEC (Directionally Encoded Color) per streamline segment:
 *     Red = Left-Right (|dx|), Green = Anterior-Posterior (|dz|), Blue = Superior-Inferior (|dy|).
 * - Includes built-in offline parsers for real MRtrix3 (.tck) and TrackVis (.trk) streamline files.
 */

export const TRACT_BUNDLES = [
  { id: "all", label: "全脑主要纤维束 (示意)" },
  { id: "cc", label: "胼胝体 · 左右联合 (红)" },
  { id: "cst", label: "皮质脊髓束 / 放射冠 (蓝)" },
  { id: "slf", label: "上纵束 / 弓状束 (绿)" },
  { id: "ifof", label: "下额枕束 (绿)" },
  { id: "cing", label: "扣带束 · 边缘环路 (绿)" },
];

function mulberry32(seed) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function decColorFromTangent(dx, dy, dz, srgbToLinear) {
  const len = Math.hypot(dx, dy, dz) || 1;
  // Boost saturation slightly so red/green/blue tracts stay crisp on light frosted background
  const r = Math.min(1, Math.pow(Math.abs(dx) / len, 0.82) * 0.94 + 0.04);
  const g = Math.min(1, Math.pow(Math.abs(dz) / len, 0.82) * 0.86 + 0.03);
  const b = Math.min(1, Math.pow(Math.abs(dy) / len, 0.82) * 0.98 + 0.04);
  return [srgbToLinear(r), srgbToLinear(g), srgbToLinear(b)];
}

function sampleCurve(controlPts, segments = 22) {
  const curve = new THREE.CatmullRomCurve3(controlPts, false, "centripetal", 0.5);
  return curve.getPoints(segments);
}

function buildCortexHullClamp(bounds, normVerts) {
  const { cx, cy, cz, rx, ry, rz } = bounds;
  const nElev = 16, nAzim = 32;
  const maxR = new Float32Array(nElev * nAzim);
  if (normVerts && normVerts.length >= 9) {
    for (let i = 0; i < normVerts.length; i += 3) {
      const dx = (normVerts[i] - cx) / rx;
      const dy = (normVerts[i + 1] - cy) / ry;
      const dz = (normVerts[i + 2] - cz) / rz;
      const r = Math.hypot(dx, dy, dz);
      if (r < 0.15) continue;
      const elev = Math.asin(Math.max(-1, Math.min(1, dy / r)));
      const azim = Math.atan2(dz, dx);
      const ie = Math.max(0, Math.min(nElev - 1, Math.floor(((elev + Math.PI * 0.5) / Math.PI) * nElev)));
      const ia = Math.max(0, Math.min(nAzim - 1, Math.floor(((azim + Math.PI) / (Math.PI * 2)) * nAzim)));
      const idx = ie * nAzim + ia;
      if (r > maxR[idx]) maxR[idx] = r;
    }
  }
  return (v) => {
    const dx = (v.x - cx) / rx, dy = (v.y - cy) / ry, dz = (v.z - cz) / rz;
    const r = Math.hypot(dx, dy, dz);
    if (r < 1e-4) return v;
    const elev = Math.asin(Math.max(-1, Math.min(1, dy / r)));
    const azim = Math.atan2(dz, dx);
    const ie = Math.max(0, Math.min(nElev - 1, Math.floor(((elev + Math.PI * 0.5) / Math.PI) * nElev)));
    const ia = Math.max(0, Math.min(nAzim - 1, Math.floor(((azim + Math.PI) / (Math.PI * 2)) * nAzim)));
    const hull = maxR[ie * nAzim + ia] > 0.25 ? maxR[ie * nAzim + ia] : 0.88;
    const cap = hull * 0.84;
    if (r > cap) {
      const s = cap / r;
      v.set(cx + dx * s * rx, cy + dy * s * ry, cz + dz * s * rz);
    }
    return v;
  };
}

function buildDemoTracts(bounds, normVerts) {
  const { cx, cy, cz, rx, ry, rz } = bounds;
  const rand = mulberry32(20261124);
  const jitter = (s) => (rand() - 0.5) * 2 * s;
  const clampPt = buildCortexHullClamp(bounds, normVerts);
  const pt = (nx, ny, nz) => clampPt(new THREE.Vector3(cx + nx * rx, cy + ny * ry, cz + nz * rz));
  const clampedCurve = (ctrl, segs) => sampleCurve(ctrl, segs).map((p) => clampPt(p));
  const tracts = [];

  // 1. Corpus Callosum (Genu / Forceps Minor -> Body -> Splenium / Forceps Major): rich RED L-R bridge
  for (let i = 0; i < 220; i++) {
    const t = i / 219;
    const apMid = 0.36 - t * 0.72 + jitter(0.025);
    const crownY = 0.10 + Math.sin(t * Math.PI) * 0.16 + jitter(0.03);
    const fanAP = t < 0.26
      ? apMid + (0.28 - t) * 1.10
      : (t > 0.74 ? apMid - (t - 0.72) * 1.15 : apMid + jitter(0.07));
    const endY = t < 0.26 || t > 0.74
      ? crownY - 0.04 + jitter(0.18)
      : crownY + 0.28 + jitter(0.14);
    const latEnd = 0.48 + rand() * 0.24;
    const pts = clampedCurve([
      pt(-latEnd, endY, fanAP),
      pt(-latEnd * 0.45, crownY + 0.08, (apMid + fanAP) * 0.45),
      pt(0, crownY - 0.03 + jitter(0.02), apMid),
      pt(latEnd * 0.45, crownY + 0.08, (apMid + fanAP) * 0.45),
      pt(latEnd, endY, fanAP),
    ], 24);
    tracts.push({ bundle: "cc", points: pts });
  }

  // 2. Corticospinal Tract (Pure BLUE vertical S-I projection through internal capsule to M1)
  for (const hemi of [-1, 1]) {
    for (let i = 0; i < 65; i++) {
      const fanX = 0.12 + rand() * 0.24;
      const fanZ = -0.14 + rand() * 0.26;
      const pts = clampedCurve([
        pt(hemi * (0.04 + rand() * 0.05), -0.68 + jitter(0.03), -0.06 + jitter(0.04)),
        pt(hemi * (0.10 + rand() * 0.06), -0.28 + jitter(0.04), -0.04 + jitter(0.04)),
        pt(hemi * (0.16 + fanX * 0.35), 0.10 + jitter(0.04), fanZ * 0.4),
        pt(hemi * (0.14 + fanX * 0.78), 0.44 + jitter(0.04), fanZ * 0.8),
        pt(hemi * (0.12 + fanX * 0.95), 0.70 + jitter(0.06), fanZ),
      ], 22);
      tracts.push({ bundle: "cst", points: pts });
    }
  }

  // 3. Superior Longitudinal Fasciculus & Arcuate Fasciculus (GREEN perisylvian C-arch)
  for (const hemi of [-1, 1]) {
    for (let i = 0; i < 70; i++) {
      const lat = hemi * (0.42 + rand() * 0.16);
      const isArcuate = i % 2 === 0;
      const pts = isArcuate
        ? clampedCurve([
            pt(lat * 0.82, 0.04 + jitter(0.08), 0.46 + jitter(0.06)),
            pt(lat * 0.96, 0.28 + jitter(0.06), 0.12 + jitter(0.05)),
            pt(lat * 0.96, 0.24 + jitter(0.06), -0.26 + jitter(0.05)),
            pt(lat * 0.98, -0.06 + jitter(0.06), -0.30 + jitter(0.05)),
            pt(lat * 0.88, -0.26 + jitter(0.06), 0.04 + jitter(0.06)),
          ], 24)
        : clampedCurve([
            pt(lat * 0.76, 0.14 + jitter(0.08), 0.50 + jitter(0.06)),
            pt(lat * 0.94, 0.34 + jitter(0.06), 0.12 + jitter(0.05)),
            pt(lat * 0.92, 0.32 + jitter(0.06), -0.24 + jitter(0.05)),
            pt(lat * 0.74, 0.12 + jitter(0.08), -0.52 + jitter(0.06)),
          ], 22);
      tracts.push({ bundle: "slf", points: pts });
    }
  }

  // 4. Inferior Fronto-Occipital Fasciculus (IFOF: curved ventral GREEN A-P tract)
  for (const hemi of [-1, 1]) {
    for (let i = 0; i < 42; i++) {
      const latMid = hemi * (0.36 + rand() * 0.14);
      const yOff = jitter(0.06);
      const pts = clampedCurve([
        pt(hemi * (0.18 + rand() * 0.18), -0.06 + yOff, 0.58 + jitter(0.05)),
        pt(latMid * 0.88, -0.18 + yOff, 0.18 + jitter(0.05)),
        pt(latMid * 0.96, -0.16 + yOff, -0.20 + jitter(0.05)),
        pt(hemi * (0.18 + rand() * 0.18), -0.06 + yOff, -0.62 + jitter(0.05)),
      ], 22);
      tracts.push({ bundle: "ifof", points: pts });
    }
  }

  // 5. Cingulum Bundle (Medial limbic GREEN arch hugging above corpus callosum)
  for (const hemi of [-1, 1]) {
    for (let i = 0; i < 36; i++) {
      const lat = hemi * (0.10 + rand() * 0.08);
      const yOff = jitter(0.04);
      const pts = clampedCurve([
        pt(lat, 0.00 + yOff, 0.48 + jitter(0.04)),
        pt(lat, 0.30 + yOff, 0.22 + jitter(0.04)),
        pt(lat, 0.36 + yOff, -0.10 + jitter(0.04)),
        pt(lat, 0.20 + yOff, -0.40 + jitter(0.04)),
        pt(hemi * (0.18 + rand() * 0.07), -0.18 + yOff, -0.20 + jitter(0.04)),
      ], 22);
      tracts.push({ bundle: "cing", points: pts });
    }
  }

  return tracts;
}

function parseTckBuffer(buffer) {
  const bytes = new Uint8Array(buffer);
  const decoder = new TextDecoder("ascii");
  // Locate header end ("END\n") within the first 16 KB
  const headerProbe = decoder.decode(bytes.subarray(0, Math.min(bytes.length, 16384)));
  const endIdx = headerProbe.indexOf("END");
  if (endIdx < 0) throw new Error("无效的 .tck 文件头（未找到 END 标记）");
  const fileMatch = headerProbe.match(/file:\s*\.\s*(\d+)/i);
  const dtMatch = headerProbe.match(/datatype:\s*([A-Za-z0-9]+)/i);
  const offset = fileMatch ? parseInt(fileMatch[1], 10) : (headerProbe.indexOf("\n", endIdx) + 1);
  const littleEndian = !dtMatch || !dtMatch[1].toUpperCase().endsWith("BE");
  const isFloat64 = dtMatch && dtMatch[1].toLowerCase().includes("64");
  const stride = isFloat64 ? 8 : 4;
  const view = new DataView(buffer, offset);
  const nTriplets = Math.floor((buffer.byteLength - offset) / (stride * 3));

  const tracts = [];
  let cur = [];
  const maxStreamlines = 6000;
  for (let i = 0; i < nTriplets && tracts.length < maxStreamlines; i++) {
    const off = i * stride * 3;
    const x = isFloat64 ? view.getFloat64(off, littleEndian) : view.getFloat32(off, littleEndian);
    const y = isFloat64 ? view.getFloat64(off + stride, littleEndian) : view.getFloat32(off + stride, littleEndian);
    const z = isFloat64 ? view.getFloat64(off + stride * 2, littleEndian) : view.getFloat32(off + stride * 2, littleEndian);
    if (!Number.isFinite(x) || !Number.isFinite(y) || !Number.isFinite(z)) {
      if (cur.length >= 2) tracts.push(cur);
      cur = [];
      if (x === Infinity || x === -Infinity) break;
    } else {
      // MRtrix RAS (R, A, S) -> right-handed viewer (+X=-R, +Y=+S, +Z=+A)
      cur.push([-x, z, y]);
    }
  }
  if (cur.length >= 2) tracts.push(cur);
  return tracts;
}

function parseTrkBuffer(buffer) {
  if (buffer.byteLength < 1004) throw new Error("无效的 .trk 文件（长度不足 1000 字节）");
  const view = new DataView(buffer);
  const littleEndian = view.getInt32(996, true) === 1000;
  const nScalars = Math.max(0, view.getInt16(36, littleEndian));
  const nProps = Math.max(0, view.getInt16(98, littleEndian));
  let offset = 1000;
  const tracts = [];
  const maxStreamlines = 6000;

  while (offset + 4 <= buffer.byteLength && tracts.length < maxStreamlines) {
    const nPts = view.getInt32(offset, littleEndian);
    offset += 4;
    if (nPts <= 0 || nPts > 50000) break;
    const pts = [];
    for (let i = 0; i < nPts && offset + 12 <= buffer.byteLength; i++) {
      const x = view.getFloat32(offset, littleEndian);
      const y = view.getFloat32(offset + 4, littleEndian);
      const z = view.getFloat32(offset + 8, littleEndian);
      offset += 12 + nScalars * 4;
      pts.push([-x, z, y]);
    }
    offset += nProps * 4;
    if (pts.length >= 2) tracts.push(pts);
  }
  return tracts;
}

export function createTractographyLayer({ rootGroup, srgbToLinear }) {
  const group = new THREE.Group();
  group.visible = false;
  rootGroup.add(group);

  const ghostCortexMaterial = new THREE.MeshStandardMaterial({
    color: 0xdbe6f2,
    roughness: 0.42,
    metalness: 0.04,
    transparent: true,
    opacity: 0.14,
    depthWrite: false,
    side: THREE.FrontSide,
  });

  let cortexBounds = null;
  let activeBundle = "all";
  let opacity = 0.88;
  let demoTracts = null;
  let customTracts = null;
  let customName = "";
  let linesMesh = null;

  function computeBounds(normVerts) {
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

  function fitRawTractsToCortex(rawTracts) {
    const b = cortexBounds || { cx: 0, cy: 0.08, cz: 0, rx: 0.24, ry: 0.22, rz: 0.29 };
    let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity, minZ = Infinity, maxZ = -Infinity;
    for (const line of rawTracts) {
      for (const p of line) {
        if (p[0] < minX) minX = p[0]; if (p[0] > maxX) maxX = p[0];
        if (p[1] < minY) minY = p[1]; if (p[1] > maxY) maxY = p[1];
        if (p[2] < minZ) minZ = p[2]; if (p[2] > maxZ) maxZ = p[2];
      }
    }
    const rcx = (minX + maxX) * 0.5, rcy = (minY + maxY) * 0.5, rcz = (minZ + maxZ) * 0.5;
    const scale = Math.min(
      (b.rx * 1.8) / Math.max(1e-4, maxX - minX),
      (b.ry * 1.8) / Math.max(1e-4, maxY - minY),
      (b.rz * 1.8) / Math.max(1e-4, maxZ - minZ)
    );
    return rawTracts.map((line) => ({
      bundle: "custom",
      points: line.map((p) => new THREE.Vector3(
        b.cx + (p[0] - rcx) * scale,
        b.cy + (p[1] - rcy) * scale,
        b.cz + (p[2] - rcz) * scale
      )),
    }));
  }

  function rebuildGeometry() {
    if (linesMesh) {
      group.remove(linesMesh);
      linesMesh.geometry.dispose();
      linesMesh.material.dispose();
      linesMesh = null;
    }

    const source = customTracts || demoTracts;
    if (!source || !source.length) return { shown: 0, total: 0, sourceLabel: "" };

    const active = customTracts
      ? source
      : (activeBundle === "all" ? source : source.filter((t) => t.bundle === activeBundle));

    let segCount = 0;
    for (const t of active) segCount += Math.max(0, t.points.length - 1);

    const pos = new Float32Array(segCount * 6);
    const col = new Float32Array(segCount * 6);
    let ptr = 0;
    for (const t of active) {
      const pts = t.points;
      for (let i = 0; i < pts.length - 1; i++) {
        const a = pts[i], b = pts[i + 1];
        const rgb = decColorFromTangent(b.x - a.x, b.y - a.y, b.z - a.z, srgbToLinear);
        pos[ptr] = a.x; pos[ptr + 1] = a.y; pos[ptr + 2] = a.z;
        pos[ptr + 3] = b.x; pos[ptr + 4] = b.y; pos[ptr + 5] = b.z;
        col[ptr] = rgb[0]; col[ptr + 1] = rgb[1]; col[ptr + 2] = rgb[2];
        col[ptr + 3] = rgb[0]; col[ptr + 4] = rgb[1]; col[ptr + 5] = rgb[2];
        ptr += 6;
      }
    }

    const geom = new THREE.BufferGeometry();
    geom.setAttribute("position", new THREE.BufferAttribute(pos, 3));
    geom.setAttribute("color", new THREE.BufferAttribute(col, 3));

    const mat = new THREE.LineBasicMaterial({
      vertexColors: true,
      transparent: true,
      opacity,
      depthWrite: false,
    });
    linesMesh = new THREE.LineSegments(geom, mat);
    linesMesh.renderOrder = 3;
    group.add(linesMesh);

    return {
      shown: active.length,
      total: source.length,
      sourceLabel: customTracts ? `本地纤维：${customName}` : "DTI 模板示意（红=左右 · 绿=前后 · 蓝=上下）",
    };
  }

  function ensureDemoBuilt(normVerts) {
    const hadBounds = !!cortexBounds;
    if (normVerts) cortexBounds = computeBounds(normVerts);
    if (!demoTracts || (!hadBounds && normVerts)) {
      demoTracts = buildDemoTracts(cortexBounds || { cx: 0, cy: 0.08, cz: 0, rx: 0.24, ry: 0.22, rz: 0.29 }, normVerts);
    }
    return rebuildGeometry();
  }

  async function loadLocalFile(file) {
    if (!file) return null;
    const ext = file.name.toLowerCase().split(".").pop();
    const buf = await file.arrayBuffer();
    let raw = null;
    if (ext === "tck") raw = parseTckBuffer(buf);
    else if (ext === "trk") raw = parseTrkBuffer(buf);
    else throw new Error(`暂不支持 .${ext}，请选择 .tck 或 .trk 纤维文件`);
    if (!raw || raw.length === 0) throw new Error("文件中未解析出有效纤维轨迹");
    customTracts = fitRawTractsToCortex(raw);
    customName = file.name;
    return rebuildGeometry();
  }

  function clearCustomFile() {
    customTracts = null;
    customName = "";
    return rebuildGeometry();
  }

  return {
    ghostCortexMaterial,
    setVisible(v) { group.visible = !!v; },
    setBundle(id) { activeBundle = id || "all"; return rebuildGeometry(); },
    setOpacity(v) {
      opacity = Math.max(0.15, Math.min(1.0, v));
      if (linesMesh) linesMesh.material.opacity = opacity;
    },
    ensureDemoBuilt,
    loadLocalFile,
    clearCustomFile,
    isCustomLoaded: () => !!customTracts,
    getStats() {
      const src = customTracts || demoTracts || [];
      const active = customTracts ? src : (activeBundle === "all" ? src : src.filter((t) => t.bundle === activeBundle));
      return {
        shown: active.length,
        total: src.length,
        sourceLabel: customTracts ? `本地纤维：${customName}` : "DTI 模板示意（红=左右 · 绿=前后 · 蓝=上下）",
      };
    },
  };
}
