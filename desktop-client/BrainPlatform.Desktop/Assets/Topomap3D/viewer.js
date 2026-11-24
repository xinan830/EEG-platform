import * as THREE from "./three.module.min.js";
import { createConnectomeLayer } from "./connectome_layer.js";

// --- Colormaps ---
const COLORMAPS = {
  turbo: {
    label: "Turbo",
    stops: [
      [0.00, [48, 18, 59]], [0.16, [65, 105, 225]], [0.34, [28, 203, 213]],
      [0.52, [118, 238, 88]], [0.70, [245, 206, 28]], [0.86, [242, 96, 22]], [1.00, [138, 14, 18]],
    ],
  },
  jet: {
    label: "Jet",
    stops: [
      [0.00, [0, 0, 143]], [0.15, [0, 48, 255]], [0.36, [0, 225, 255]],
      [0.55, [128, 255, 112]], [0.74, [255, 224, 0]], [0.90, [255, 64, 0]], [1.00, [140, 0, 0]],
    ],
  },
  rdbu: {
    label: "RdBu 对称",
    stops: [
      [0.00, [13, 59, 138]], [0.25, [59, 130, 214]], [0.50, [244, 246, 249]],
      [0.75, [232, 108, 74]], [1.00, [153, 18, 32]],
    ],
  },
  viridis: {
    label: "Viridis",
    stops: [
      [0.00, [68, 1, 84]], [0.25, [59, 82, 139]], [0.50, [33, 145, 140]],
      [0.75, [94, 201, 98]], [1.00, [253, 231, 37]],
    ],
  },
};
const COLORMAP_ORDER = ["turbo", "jet", "rdbu", "viridis"];
const PRESETS = [
  { id: "frontal", label: "额区双侧激活", yaw: 0.32, pitch: -0.35 },
  { id: "occipital", label: "枕叶 Alpha 优势", yaw: -2.55, pitch: -0.32 },
  { id: "asymmetry", label: "右半球偏侧激活", yaw: -0.78, pitch: -0.32 },
];

const state = {
  mode: "cortex",          // "cortex" | "atlas" | "scalp" | "connectome" | "electrodes"
  atlasId: "lobes",
  colormap: "turbo",
  presetIndex: 0,
  isRealField: false,
  isPreviewHeatmap: false,
  enableLabModes: false,
  showDimSwitcher: false,
  scalpData: null,         // normalized vertices, normals, indices, transform info
  cortexData: null,        // normalized vertices, indices, sulc, atlases
  electrodes: [],          // [{ label, canonical, pos: Vector3, normal: Vector3, valueNorm }]
  selectedLabel: null,
  hoveredRegionId: -1,
  pinnedRegionId: -1,
  scaleInfo: { unit: "示意强度", minText: "0.0 低", midText: "0.5 中", maxText: "1.0 高" },
};

// --- Three.js Scene Setup ---
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

// Studio 3-point medical lighting
scene.add(new THREE.HemisphereLight(0xffffff, 0xd8e2ee, 1.18));
const keyLight = new THREE.DirectionalLight(0xffffff, 1.12);
keyLight.position.set(1.6, 2.4, 2.2);
scene.add(keyLight);
const fillLight = new THREE.DirectionalLight(0xe6f0fa, 0.58);
fillLight.position.set(-2.2, 0.8, 1.5);
scene.add(fillLight);
const rimLight = new THREE.DirectionalLight(0xffffff, 0.48);
rimLight.position.set(0.0, 1.5, -2.5);
scene.add(rimLight);

const rootGroup = new THREE.Group();
scene.add(rootGroup);

let scalpOpaqueMesh = null;
let scalpDepthMesh = null;
let scalpFrostedMesh = null;
let cortexMesh = null;
let cortexSolidMaterial = null;
const electrodeGroup = new THREE.Group();
rootGroup.add(electrodeGroup);
let connectomeLayer = null;
let electrodeMeshes = [];
let labelElements = [];
let selectedMarkerMesh = null;
let pulseStartMs = -1;

// Orbit / Pan / Zoom & Smooth Fly-To state (yaw=+0.50 shows subject's LEFT 3/4 profile: Fp1/F3/F7/C3/T3/M1)
const camCtrl = {
  yaw: 0.50, pitch: 0.24, distance: 2.35, target: new THREE.Vector3(0, 0.02, 0),
  defaultYaw: 0.50, defaultPitch: 0.24, defaultDistance: 2.35, defaultTarget: new THREE.Vector3(0, 0.02, 0),
  anim: null,
  dragging: false, panning: false, lastX: 0, lastY: 0, movedPx: 0,
};

const raycaster = new THREE.Raycaster();
const pointerNdc = new THREE.Vector2();

function decodeB64(b64, TypedArrayCtor) {
  const bin = atob(b64);
  const bytes = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
  return new TypedArrayCtor(bytes.buffer);
}

function evalColormap(t, kind) {
  const stops = (COLORMAPS[kind] || COLORMAPS.turbo).stops;
  const c = Math.max(0, Math.min(1, t));
  for (let i = 0; i < stops.length - 1; i++) {
    const [p0, c0] = stops[i];
    const [p1, c1] = stops[i + 1];
    if (c >= p0 && c <= p1) {
      const f = (c - p0) / Math.max(1e-6, p1 - p0);
      return [
        (c0[0] + (c1[0] - c0[0]) * f) / 255,
        (c0[1] + (c1[1] - c0[1]) * f) / 255,
        (c0[2] + (c1[2] - c0[2]) * f) / 255,
      ];
    }
  }
  const last = stops[stops.length - 1][1];
  return [last[0] / 255, last[1] / 255, last[2] / 255];
}

function hexToRgb01(hex) {
  const m = /^#?([0-9a-f]{6})$/i.exec(hex || "#94A3B8");
  if (!m) return [0.58, 0.64, 0.72];
  const n = parseInt(m[1], 16);
  return [((n >> 16) & 255) / 255, ((n >> 8) & 255) / 255, (n & 255) / 255];
}

function smoothstep(e0, e1, x) {
  if (Math.abs(e1 - e0) < 1e-9) return x >= e1 ? 1 : 0;
  const t = Math.max(0, Math.min(1, (x - e0) / (e1 - e0)));
  return t * t * (3 - 2 * t);
}

function srgbToLinear(c) {
  const v = Math.max(0, Math.min(1, c));
  return v <= 0.04045 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
}

// Precomputed 256-entry linear-RGB LUTs so live EEG recoloring uses zero Math.pow calls
const COLORMAP_LUTS = {};
for (const kind of COLORMAP_ORDER) {
  const lut = new Float32Array(256 * 3);
  for (let i = 0; i < 256; i++) {
    const [r, g, b] = evalColormap(i / 255, kind);
    lut[i * 3] = srgbToLinear(r);
    lut[i * 3 + 1] = srgbToLinear(g);
    lut[i * 3 + 2] = srgbToLinear(b);
  }
  COLORMAP_LUTS[kind] = lut;
}

let electrodeLayoutKey = "";
let cortexRbfCache = null;
let scalpRbfCache = null;
let renderedAtlasKey = "";

// Convert GNM coordinate -> Three.js right-handed space (+X = Right, +Y = Up, +Z = Forward/Face!)
function buildTransformFromScalp(payload) {
  const origin = payload.head_origin_gnm;
  const R = payload.right_axis_gnm;
  const F = payload.forward_axis_gnm;
  const U = payload.up_axis_gnm;
  const rawVerts = payload.vertices_gnm;

  const headPts = new Float32Array(rawVerts.length * 3);
  let minX = Infinity, maxX = -Infinity, minY = Infinity, maxY = -Infinity, minZ = Infinity, maxZ = -Infinity;
  for (let i = 0; i < rawVerts.length; i++) {
    const p = rawVerts[i];
    const dx = p[0] - origin[0], dy = p[1] - origin[1], dz = p[2] - origin[2];
    // Proper right-handed basis (det = +1): +X = Subject LEFT (-R), +Y = Up (+U), +Z = Forward (+F)
    const x = -(dx * R[0] + dy * R[1] + dz * R[2]);
    const y = dx * U[0] + dy * U[1] + dz * U[2];
    const z = dx * F[0] + dy * F[1] + dz * F[2];
    headPts[i * 3] = x; headPts[i * 3 + 1] = y; headPts[i * 3 + 2] = z;
    if (x < minX) minX = x; if (x > maxX) maxX = x;
    if (y < minY) minY = y; if (y > maxY) maxY = y;
    if (z < minZ) minZ = z; if (z > maxZ) maxZ = z;
  }
  const extent = Math.max(maxX - minX, maxY - minY, maxZ - minZ) || 1;
  const cx = (minX + maxX) * 0.5, cy = (minY + maxY) * 0.5, cz = (minZ + maxZ) * 0.5;
  const scale = 1.28 / extent;

  const mapPoint = (gx, gy, gz) => {
    const dx = gx - origin[0], dy = gy - origin[1], dz = gz - origin[2];
    const x = -(dx * R[0] + dy * R[1] + dz * R[2]);
    const y = dx * U[0] + dy * U[1] + dz * U[2];
    const z = dx * F[0] + dy * F[1] + dz * F[2];
    return [(x - cx) * scale, (y - cy) * scale, (z - cz) * scale];
  };

  const normVerts = new Float32Array(rawVerts.length * 3);
  for (let i = 0; i < rawVerts.length; i++) {
    normVerts[i * 3] = (headPts[i * 3] - cx) * scale;
    normVerts[i * 3 + 1] = (headPts[i * 3 + 1] - cy) * scale;
    normVerts[i * 3 + 2] = (headPts[i * 3 + 2] - cz) * scale;
  }
  return { normVerts, mapPoint, scale };
}

function createFrostedScalpMaterial() {
  // Height-graded frosted alabaster shell (paired with depth prepass):
  // - Lower head/face/ears/neck read as a crisp, sculpted frosted-marble mannequin with clear facial relief.
  // - Upper cranial dome stays translucent frosted glass with a defined rim so the colorful inner brain pops.
  return new THREE.ShaderMaterial({
    transparent: true,
    depthWrite: false,
    depthTest: true,
    depthFunc: THREE.LessEqualDepth,
    side: THREE.FrontSide,
    uniforms: {
      uLightCol: { value: new THREE.Vector3(0.92, 0.94, 0.97) },
      uShadowCol: { value: new THREE.Vector3(0.71, 0.75, 0.82) },
      uRimCol: { value: new THREE.Vector3(0.68, 0.74, 0.82) },
      uKeyDir: { value: new THREE.Vector3(0.42, 0.70, 0.58).normalize() },
      uFillDir: { value: new THREE.Vector3(-0.55, 0.20, 0.75).normalize() },
    },
    vertexShader: `
      varying vec3 vNormalView;
      varying vec3 vViewPos;
      varying float vLocalY;
      void main() {
        vLocalY = position.y;
        vec4 mvPosition = modelViewMatrix * vec4(position, 1.0);
        vViewPos = -mvPosition.xyz;
        vNormalView = normalize(normalMatrix * normal);
        gl_Position = projectionMatrix * mvPosition;
      }
    `,
    fragmentShader: `
      uniform vec3 uLightCol;
      uniform vec3 uShadowCol;
      uniform vec3 uRimCol;
      uniform vec3 uKeyDir;
      uniform vec3 uFillDir;
      varying vec3 vNormalView;
      varying vec3 vViewPos;
      varying float vLocalY;
      void main() {
        vec3 N = normalize(vNormalView);
        vec3 V = normalize(vViewPos);
        float ndotv = clamp(dot(N, V), 0.0, 1.0);
        float fresnel = pow(1.0 - ndotv, 1.95);

        // Two-light sculpted shading so nose, brow, eye sockets, jaw, and ears read crisply
        float key = clamp(dot(N, uKeyDir) * 0.5 + 0.5, 0.0, 1.0);
        float fill = clamp(dot(N, uFillDir) * 0.5 + 0.5, 0.0, 1.0);
        float shade = clamp(key * 0.74 + fill * 0.26, 0.0, 1.0);
        vec3 H = normalize(uKeyDir + V);
        float spec = pow(clamp(dot(N, H), 0.0, 1.0), 30.0) * 0.12;

        vec3 baseCol = mix(uShadowCol, uLightCol, smoothstep(0.12, 0.92, shade));
        vec3 col = mix(baseCol, uRimCol, fresnel * 0.55) + vec3(spec);

        // Face/jaw/neck (vLocalY < -0.04) are more opaque alabaster; cranial dome stays clear frosted glass
        float dome = smoothstep(-0.08, 0.14, vLocalY);
        float faceAlpha = clamp(0.56 + fresnel * 0.34, 0.54, 0.90);
        float domeAlpha = clamp(0.18 + fresnel * 0.58, 0.16, 0.78);
        float alpha = mix(faceAlpha, domeAlpha, dome);

        gl_FragColor = vec4(col, alpha);
      }
    `,
  });
}

function loadScalpMesh(payload) {
  const { normVerts, mapPoint, scale } = buildTransformFromScalp(payload);
  const tris = payload.triangles;
  const indices = new Uint32Array(tris.length * 3);
  for (let i = 0; i < tris.length; i++) {
    indices[i * 3] = tris[i][0];
    indices[i * 3 + 1] = tris[i][1];
    indices[i * 3 + 2] = tris[i][2];
  }

  const geom = new THREE.BufferGeometry();
  geom.setAttribute("position", new THREE.BufferAttribute(normVerts, 3));
  geom.setIndex(new THREE.BufferAttribute(indices, 1));
  geom.computeVertexNormals();
  const sPos = geom.getAttribute("position");
  const sNorm = geom.getAttribute("normal");
  let sDot = 0;
  for (let i = 0; i < sPos.count; i += 8) {
    sDot += sPos.getX(i) * sNorm.getX(i) + sPos.getY(i) * sNorm.getY(i) + sPos.getZ(i) * sNorm.getZ(i);
  }
  if (sDot < 0) {
    for (let i = 0; i < indices.length; i += 3) {
      const tmp = indices[i + 1]; indices[i + 1] = indices[i + 2]; indices[i + 2] = tmp;
    }
    geom.setIndex(new THREE.BufferAttribute(indices, 1));
    geom.computeVertexNormals();
  }

  const colors = new Float32Array(normVerts.length);
  const nR = srgbToLinear(205 / 255), nG = srgbToLinear(211 / 255), nB = srgbToLinear(218 / 255);
  for (let i = 0; i < normVerts.length; i += 3) {
    colors[i] = nR; colors[i + 1] = nG; colors[i + 2] = nB;
  }
  geom.setAttribute("color", new THREE.BufferAttribute(colors, 3));

  if (scalpOpaqueMesh) { rootGroup.remove(scalpOpaqueMesh); scalpOpaqueMesh.geometry.dispose(); }
  if (scalpDepthMesh) { rootGroup.remove(scalpDepthMesh); scalpDepthMesh.geometry.dispose(); }
  if (scalpFrostedMesh) { rootGroup.remove(scalpFrostedMesh); scalpFrostedMesh.geometry.dispose(); }

  const opaqueMat = new THREE.MeshStandardMaterial({
    vertexColors: true, roughness: 0.72, metalness: 0.02, side: THREE.DoubleSide,
  });
  scalpOpaqueMesh = new THREE.Mesh(geom, opaqueMat);
  scalpOpaqueMesh.renderOrder = 1;
  rootGroup.add(scalpOpaqueMesh);

  // Depth prepass runs in the transparent queue (renderOrder 4) AFTER inner ghost cortex (2)
  // and connectome arcs (3), writing only the nearest front scalp surface so chin/neck/ear never ghost.
  const depthMat = new THREE.MeshBasicMaterial({
    transparent: true,
    colorWrite: false,
    depthWrite: true,
    side: THREE.FrontSide,
    polygonOffset: true,
    polygonOffsetFactor: 0.5,
    polygonOffsetUnits: 0.5,
  });
  scalpDepthMesh = new THREE.Mesh(geom.clone(), depthMat);
  scalpDepthMesh.renderOrder = 4;
  rootGroup.add(scalpDepthMesh);

  const frostedGeom = geom.clone();
  scalpFrostedMesh = new THREE.Mesh(frostedGeom, createFrostedScalpMaterial());
  scalpFrostedMesh.renderOrder = 5;
  rootGroup.add(scalpFrostedMesh);

  state.scalpData = { normVerts, mapPoint, scale, locations: payload.electrode_locations || [] };
  scalpRbfCache = null;
  cortexRbfCache = null;
  electrodeLayoutKey = "";
  centerStatus.classList.add("hidden");
  applyModeVisibility();
}

function loadCortexMesh(cortexPayload) {
  if (!state.scalpData || !cortexPayload) return;
  const rawVerts = decodeB64(cortexPayload.vertices_f32_b64, Float32Array);
  const rawTris = decodeB64(cortexPayload.triangles_i32_b64, Int32Array);
  const sulc = decodeB64(cortexPayload.sulc_f32_b64, Float32Array);
  const vCount = rawVerts.length / 3;
  const normVerts = new Float32Array(rawVerts.length);
  for (let i = 0; i < vCount; i++) {
    const [x, y, z] = state.scalpData.mapPoint(rawVerts[i * 3], rawVerts[i * 3 + 1], rawVerts[i * 3 + 2]);
    normVerts[i * 3] = x; normVerts[i * 3 + 1] = y; normVerts[i * 3 + 2] = z;
  }

  const indices = new Uint32Array(rawTris.length);
  for (let i = 0; i < rawTris.length; i += 3) {
    indices[i] = rawTris[i];
    indices[i + 1] = rawTris[i + 1];
    indices[i + 2] = rawTris[i + 2];
  }

  const geom = new THREE.BufferGeometry();
  geom.setAttribute("position", new THREE.BufferAttribute(normVerts, 3));
  geom.setIndex(new THREE.BufferAttribute(indices, 1));
  geom.computeVertexNormals();
  // Ensure outward normals on cortex (check average dot(pos - center, normal))
  const posAttr = geom.getAttribute("position");
  const normAttr = geom.getAttribute("normal");
  let dotSum = 0;
  for (let i = 0; i < vCount; i += 10) {
    dotSum += posAttr.getX(i) * normAttr.getX(i) + posAttr.getY(i) * normAttr.getY(i) + posAttr.getZ(i) * normAttr.getZ(i);
  }
  if (dotSum < 0) {
    for (let i = 0; i < indices.length; i += 3) {
      const tmp = indices[i + 1]; indices[i + 1] = indices[i + 2]; indices[i + 2] = tmp;
    }
    geom.setIndex(new THREE.BufferAttribute(indices, 1));
    geom.computeVertexNormals();
  }

  const colors = new Float32Array(vCount * 3);
  geom.setAttribute("color", new THREE.BufferAttribute(colors, 3));

  if (cortexMesh) { rootGroup.remove(cortexMesh); cortexMesh.geometry.dispose(); }
  const mat = new THREE.MeshStandardMaterial({
    vertexColors: true, roughness: 0.44, metalness: 0.04,
  });
  cortexSolidMaterial = mat;
  cortexMesh = new THREE.Mesh(geom, mat);
  cortexMesh.renderOrder = 2;
  rootGroup.add(cortexMesh);

  const atlases = {};
  for (const [id, atlas] of Object.entries(cortexPayload.atlases || {})) {
    const regionIds = decodeB64(atlas.vertex_region_ids_i16_b64, Int16Array);
    const regionsById = new Map();
    for (const r of atlas.regions) regionsById.set(r.id, r);
    atlases[id] = { ...atlas, regionIds, regionsById };
  }

  state.cortexData = { normVerts, sulc, vCount, atlases };
  cortexRbfCache = null;
  renderedAtlasKey = "";
  recolorActiveSurfaces();
  applyModeVisibility();
}

function evalPresetValue(pos, presetId) {
  // +X = Right, +Y = Up, +Z = Forward (Anterior), -Z = Posterior (Occipital)
  const g = (cx, cy, cz, s) => {
    const dx = pos.x - cx, dy = pos.y - cy, dz = pos.z - cz;
    return Math.exp(-(dx * dx + dy * dy + dz * dz) / (2 * s * s));
  };
  if (presetId === "occipital") {
    return 0.90 * g(-0.11, 0.14, -0.34, 0.22) + 0.95 * g(0.12, 0.14, -0.34, 0.22) + 0.50 * g(0.0, 0.36, -0.16, 0.24);
  }
  if (presetId === "asymmetry") {
    // Subject Right hemisphere is -X in proper right-handed coordinates
    return 0.95 * g(-0.26, 0.28, 0.06, 0.23) + 0.72 * g(-0.22, 0.24, 0.24, 0.21) - 0.45 * g(0.26, 0.26, 0.04, 0.22) + 0.35;
  }
  // frontal
  return 0.95 * g(0.0, 0.24, 0.34, 0.24) + 0.80 * g(-0.18, 0.20, 0.26, 0.22) + 0.82 * g(0.18, 0.20, 0.26, 0.22);
}

function updateElectrodes(items) {
  if (!state.scalpData || !items || items.length === 0) {
    while (electrodeGroup.children.length > 0) {
      const child = electrodeGroup.children.pop();
      child.geometry?.dispose();
      child.material?.dispose();
    }
    labelsLayer.innerHTML = "";
    electrodeMeshes = [];
    labelElements = [];
    selectedMarkerMesh = null;
    state.electrodes = [];
    electrodeLayoutKey = "";
    cortexRbfCache = null;
    scalpRbfCache = null;
    return;
  }

  const presetId = PRESETS[state.presetIndex].id;
  const nextKey = items.map((it) => `${it.label}|${it.canonical || ""}`).join(";");

  // Fast path during live EEG streaming: same montage -> update values in-place with zero mesh/DOM churn
  if (nextKey === electrodeLayoutKey && state.electrodes.length === items.length) {
    for (let i = 0; i < items.length; i++) {
      const it = items[i];
      const e = state.electrodes[i];
      e.valueNorm = typeof it.valueNorm === "number" ? it.valueNorm : evalPresetValue(e.pos, presetId);
    }
    if (!state.isRealField && state.electrodes.length > 1) {
      let mn = Infinity, mx = -Infinity;
      for (const e of state.electrodes) { if (e.valueNorm < mn) mn = e.valueNorm; if (e.valueNorm > mx) mx = e.valueNorm; }
      const span = mx - mn;
      if (span > 1e-6) {
        for (const e of state.electrodes) e.valueNorm = 0.05 + 0.90 * ((e.valueNorm - mn) / span);
      }
    }
    return;
  }

  while (electrodeGroup.children.length > 0) {
    const child = electrodeGroup.children.pop();
    child.geometry?.dispose();
    child.material?.dispose();
  }
  labelsLayer.innerHTML = "";
  electrodeMeshes = [];
  labelElements = [];
  selectedMarkerMesh = null;
  state.electrodes = [];
  electrodeLayoutKey = nextKey;
  cortexRbfCache = null;
  scalpRbfCache = null;

  const baseRadius = 0.0102;
  const sphereGeom = new THREE.SphereGeometry(baseRadius, 16, 12);

  const rawValues = [];
  for (const item of items) {
    const [x, y, z] = state.scalpData.mapPoint(item.x, item.y, item.z);
    const rawPos = new THREE.Vector3(x, y, z);
    const normal = rawPos.clone().normalize();
    if (normal.lengthSq() < 1e-6) normal.set(0, 1, 0);
    const pos = rawPos.clone().addScaledVector(normal, baseRadius * 0.35);
    const v = typeof item.valueNorm === "number" ? item.valueNorm : evalPresetValue(pos, presetId);
    rawValues.push(v);
    state.electrodes.push({ label: item.label, canonical: item.canonical || item.label, pos, normal, valueNorm: v });
  }

  // Auto-stretch preset values to [0.05, 0.95] so demo looks vivid
  if (!state.isRealField && rawValues.length > 1) {
    const mn = Math.min(...rawValues), mx = Math.max(...rawValues), span = mx - mn;
    if (span > 1e-6) {
      for (const e of state.electrodes) e.valueNorm = 0.05 + 0.90 * ((e.valueNorm - mn) / span);
    }
  }

  for (const e of state.electrodes) {
    const isSel = isSelectedElectrode(e);
    const mat = new THREE.MeshStandardMaterial({
      color: isSel ? 0xf97316 : (state.mode === "electrodes" ? 0x1d4ed8 : 0x1e293b),
      emissive: isSel ? 0xea580c : 0x000000,
      emissiveIntensity: isSel ? 0.55 : 0.0,
      roughness: 0.32, metalness: 0.08,
    });
    const mesh = new THREE.Mesh(sphereGeom, mat);
    mesh.position.copy(e.pos);
    mesh.renderOrder = 8;
    if (isSel) {
      mesh.scale.setScalar(1.45);
      selectedMarkerMesh = mesh;
    }
    electrodeGroup.add(mesh);
    electrodeMeshes.push({ mesh, e });

    const el = document.createElement("div");
    el.className = isSel ? "elec-label selected" : "elec-label";
    el.textContent = e.label;
    labelsLayer.appendChild(el);
    labelElements.push({ el, e });
  }
}

function isSelectedElectrode(e) {
  if (!state.selectedLabel) return false;
  const s = state.selectedLabel.trim().toLowerCase();
  return e.label.toLowerCase() === s || e.canonical.toLowerCase() === s;
}

function getCortexRbfCache() {
  const elecs = state.electrodes;
  const { normVerts, sulc, vCount } = state.cortexData;
  const mCount = elecs.length;
  if (cortexRbfCache && cortexRbfCache.key === electrodeLayoutKey && cortexRbfCache.vCount === vCount) {
    return cortexRbfCache;
  }
  const weights = new Float32Array(vCount * mCount);
  const aoLinear = new Float32Array(vCount);
  const sigma = 0.22, twoSigSq = 2 * sigma * sigma, sigSq = sigma * sigma;
  const ex = new Float32Array(mCount), ey = new Float32Array(mCount), ez = new Float32Array(mCount);
  for (let m = 0; m < mCount; m++) {
    ex[m] = elecs[m].pos.x; ey[m] = elecs[m].pos.y; ez[m] = elecs[m].pos.z;
  }
  for (let i = 0; i < vCount; i++) {
    const px = normVerts[i * 3], py = normVerts[i * 3 + 1], pz = normVerts[i * 3 + 2];
    const row = i * mCount;
    let wSum = 0;
    for (let m = 0; m < mCount; m++) {
      const dx = px - ex[m], dy = py - ey[m], dz = pz - ez[m];
      const d2 = dx * dx + dy * dy + dz * dz;
      const w = Math.exp(-d2 / twoSigSq) + 0.12 / Math.pow(d2 + sigSq, 1.5);
      weights[row + m] = w;
      wSum += w;
    }
    const invW = wSum > 1e-9 ? 1 / wSum : 0;
    for (let m = 0; m < mCount; m++) weights[row + m] *= invW;
    const ao = 1.0 - 0.34 * Math.max(0, Math.min(1, (sulc[i] + 0.18) * 0.60));
    aoLinear[i] = Math.pow(ao, 2.2);
  }
  cortexRbfCache = { key: electrodeLayoutKey, vCount, mCount, weights, aoLinear };
  return cortexRbfCache;
}

function recolorCortexHeatmap() {
  if (!cortexMesh || !state.cortexData) return;
  const { sulc, vCount } = state.cortexData;
  const colorAttr = cortexMesh.geometry.getAttribute("color");
  const arr = colorAttr.array;
  const elecs = state.electrodes;

  if (elecs.length === 0) {
    for (let i = 0; i < vCount; i++) {
      const shade = srgbToLinear(0.78 - 0.18 * Math.max(-1, Math.min(1, sulc[i] * 0.48)));
      arr[i * 3] = shade; arr[i * 3 + 1] = shade; arr[i * 3 + 2] = shade;
    }
    colorAttr.needsUpdate = true;
    return;
  }

  const cache = getCortexRbfCache();
  const { weights, aoLinear, mCount } = cache;
  const vals = new Float32Array(mCount);
  for (let m = 0; m < mCount; m++) vals[m] = elecs[m].valueNorm;
  const lut = COLORMAP_LUTS[state.colormap] || COLORMAP_LUTS.turbo;

  for (let i = 0; i < vCount; i++) {
    const row = i * mCount;
    let u = 0;
    for (let m = 0; m < mCount; m++) u += weights[row + m] * vals[m];
    const idx = (u <= 0 ? 0 : (u >= 1 ? 255 : (u * 255) | 0)) * 3;
    const ao = aoLinear[i];
    arr[i * 3] = lut[idx] * ao;
    arr[i * 3 + 1] = lut[idx + 1] * ao;
    arr[i * 3 + 2] = lut[idx + 2] * ao;
  }
  colorAttr.needsUpdate = true;
}

function recolorCortexAtlas() {
  if (!cortexMesh || !state.cortexData) return;
  const atlas = state.cortexData.atlases[state.atlasId];
  if (!atlas) return;
  const { sulc, vCount } = state.cortexData;
  const colorAttr = cortexMesh.geometry.getAttribute("color");
  const arr = colorAttr.array;
  const ids = atlas.regionIds;
  const activeId = state.hoveredRegionId >= 0 ? state.hoveredRegionId : state.pinnedRegionId;

  // Precompute RGB table
  const rgbTable = new Map();
  for (const r of atlas.regions) rgbTable.set(r.id, hexToRgb01(r.color));

  for (let i = 0; i < vCount; i++) {
    const rid = ids[i];
    const base = rgbTable.get(rid) || [0.78, 0.82, 0.88];
    const ao = 1.0 - 0.34 * Math.max(0, Math.min(1, (sulc[i] + 0.18) * 0.60));
    let r = base[0] * ao, g = base[1] * ao, b = base[2] * ao;
    if (activeId > 0) {
      if (rid === activeId) {
        r = Math.min(1, r * 1.15 + 0.06);
        g = Math.min(1, g * 1.15 + 0.06);
        b = Math.min(1, b * 1.15 + 0.06);
      } else {
        // Gently desaturate other regions so hovered/selected ROI pops clearly
        const luma = 0.299 * r + 0.587 * g + 0.114 * b;
        r = r * 0.42 + luma * 0.58;
        g = g * 0.42 + luma * 0.58;
        b = b * 0.42 + luma * 0.58;
      }
    }
    arr[i * 3] = srgbToLinear(r);
    arr[i * 3 + 1] = srgbToLinear(g);
    arr[i * 3 + 2] = srgbToLinear(b);
  }
  colorAttr.needsUpdate = true;
}

function getScalpRbfCache() {
  const elecs = state.electrodes;
  const { normVerts } = state.scalpData;
  const vCount = normVerts.length / 3;
  const mCount = elecs.length;
  if (scalpRbfCache && scalpRbfCache.key === electrodeLayoutKey && scalpRbfCache.vCount === vCount) {
    return scalpRbfCache;
  }
  let minY = Infinity, minFrontY = Infinity;
  const ex = new Float32Array(mCount), ey = new Float32Array(mCount), ez = new Float32Array(mCount);
  for (let m = 0; m < mCount; m++) {
    const p = elecs[m].pos;
    ex[m] = p.x; ey[m] = p.y; ez[m] = p.z;
    if (p.y < minY) minY = p.y;
    if (p.z > 0.12 && p.y < minFrontY) minFrontY = p.y;
  }
  if (!Number.isFinite(minFrontY)) minFrontY = minY + 0.04;

  const weights = new Float32Array(vCount * mCount);
  const mask = new Float32Array(vCount);
  const sigma = 0.145, twoSigSq = 2 * sigma * sigma, sigSq = sigma * sigma;
  for (let i = 0; i < vCount; i++) {
    const px = normVerts[i * 3], py = normVerts[i * 3 + 1], pz = normVerts[i * 3 + 2];
    const row = i * mCount;
    let wSum = 0, minD2 = Infinity;
    for (let m = 0; m < mCount; m++) {
      const dx = px - ex[m], dy = py - ey[m], dz = pz - ez[m];
      const d2 = dx * dx + dy * dy + dz * dz;
      if (d2 < minD2) minD2 = d2;
      const w = Math.exp(-d2 / twoSigSq) + 0.14 / Math.pow(d2 + sigSq, 1.5);
      weights[row + m] = w;
      wSum += w;
    }
    const invW = wSum > 1e-9 ? 1 / wSum : 0;
    for (let m = 0; m < mCount; m++) weights[row + m] *= invW;
    const prox = 1.0 - smoothstep(0.16, 0.29, Math.sqrt(minD2));
    const frontBlend = smoothstep(0.04, 0.24, pz);
    const cutoffY = (minY - 0.045) + frontBlend * ((minFrontY - 0.068) - (minY - 0.045));
    const hMask = smoothstep(cutoffY - 0.036, cutoffY + 0.036, py);
    const earLat = smoothstep(0.33, 0.41, Math.abs(px)) * (1.0 - smoothstep(minY - 0.02, minY + 0.09, py));
    mask[i] = Math.max(0, Math.min(1, prox * hMask * Math.max(0, 1 - earLat)));
  }
  scalpRbfCache = { key: electrodeLayoutKey, vCount, mCount, weights, mask };
  return scalpRbfCache;
}

function recolorScalpSurface() {
  if (!scalpOpaqueMesh || !state.scalpData) return;
  const { normVerts } = state.scalpData;
  const colorAttr = scalpOpaqueMesh.geometry.getAttribute("color");
  const arr = colorAttr.array;
  const vCount = normVerts.length / 3;
  const nr = srgbToLinear(205 / 255), ng = srgbToLinear(211 / 255), nb = srgbToLinear(218 / 255);

  if (state.mode === "electrodes" || state.electrodes.length === 0) {
    for (let i = 0; i < vCount; i++) {
      arr[i * 3] = nr; arr[i * 3 + 1] = ng; arr[i * 3 + 2] = nb;
    }
    colorAttr.needsUpdate = true;
    return;
  }

  const elecs = state.electrodes;
  const cache = getScalpRbfCache();
  const { weights, mask, mCount } = cache;
  const vals = new Float32Array(mCount);
  for (let m = 0; m < mCount; m++) vals[m] = elecs[m].valueNorm;
  const lut = COLORMAP_LUTS[state.colormap] || COLORMAP_LUTS.turbo;

  for (let i = 0; i < vCount; i++) {
    const wMask = mask[i];
    if (wMask <= 0.001) {
      arr[i * 3] = nr; arr[i * 3 + 1] = ng; arr[i * 3 + 2] = nb;
      continue;
    }
    const row = i * mCount;
    let u = 0;
    for (let m = 0; m < mCount; m++) u += weights[row + m] * vals[m];
    const idx = (u <= 0 ? 0 : (u >= 1 ? 255 : (u * 255) | 0)) * 3;
    const inv = 1 - wMask;
    arr[i * 3] = lut[idx] * wMask + nr * inv;
    arr[i * 3 + 1] = lut[idx + 1] * wMask + ng * inv;
    arr[i * 3 + 2] = lut[idx + 2] * wMask + nb * inv;
  }
  colorAttr.needsUpdate = true;
}

function ensureConnectomeLayer() {
  if (!connectomeLayer) {
    connectomeLayer = createConnectomeLayer({ rootGroup, evalColormap, srgbToLinear });
  }
  return connectomeLayer;
}

function updateConnectomeStatusUI(stats) {
  const s = stats || connectomeLayer?.getStats() || { shown: 0, total: 0 };
  document.getElementById("connectome-edge-count").textContent = `显示 ${s.shown} / ${s.total} 条连边`;
}

function recolorActiveSurfaces() {
  if (state.mode === "cortex" && cortexMesh) recolorCortexHeatmap();
  else if (state.mode === "atlas" && cortexMesh) recolorCortexAtlas();
  else if (state.mode === "connectome") {
    const layer = ensureConnectomeLayer();
    layer.setColormap(state.colormap);
    const stats = layer.setElectrodes(state.electrodes, state.cortexData?.normVerts);
    updateConnectomeStatusUI(stats);
  }
  else recolorScalpSurface();
}

function applyModeVisibility() {
  const hasCortex = !!cortexMesh;
  const isConnectome = state.mode === "connectome";
  const showFrostedShell = (state.mode === "cortex" || state.mode === "atlas" || isConnectome) && hasCortex;
  if (scalpOpaqueMesh) scalpOpaqueMesh.visible = !showFrostedShell;
  if (scalpDepthMesh) scalpDepthMesh.visible = showFrostedShell;
  if (scalpFrostedMesh) scalpFrostedMesh.visible = showFrostedShell;
  if (cortexMesh) {
    cortexMesh.visible = showFrostedShell;
    if (isConnectome) {
      cortexMesh.material = ensureConnectomeLayer().ghostCortexMaterial;
    } else if (cortexSolidMaterial) {
      cortexMesh.material = cortexSolidMaterial;
    }
  }
  electrodeGroup.visible = !isConnectome;
  if (isConnectome) ensureConnectomeLayer().setVisible(true);
  else if (connectomeLayer) connectomeLayer.setVisible(false);

  document.getElementById("mode-connectome-btn").classList.toggle("hidden", !state.enableLabModes);
  document.querySelectorAll("#mode-group .seg-btn").forEach((btn) => {
    btn.classList.toggle("active", btn.dataset.mode === state.mode);
  });
  document.getElementById("mode-group").classList.toggle("hidden", state.mode === "electrodes");
  document.getElementById("atlas-select").classList.toggle("hidden", state.mode !== "atlas");
  document.getElementById("atlas-card").classList.toggle("hidden", state.mode !== "atlas");
  document.getElementById("connectome-bar").classList.toggle("hidden", !isConnectome);
  const showHeatUi = state.mode === "cortex" || state.mode === "scalp" || isConnectome;
  document.getElementById("colorbar-card").classList.toggle("hidden", !showHeatUi);
  document.getElementById("colormap-btn").classList.toggle("hidden", !showHeatUi);
  document.getElementById("preset-btn").classList.toggle("hidden", !((showHeatUi && !state.isRealField) || isConnectome));
  document.getElementById("dim-switcher").style.display = state.showDimSwitcher ? "inline-flex" : "none";

  updateColorbarUI();
  updateAtlasLegendUI();
}

function updateColorbarUI() {
  const cm = COLORMAPS[state.colormap] || COLORMAPS.turbo;
  const isConnectome = state.mode === "connectome";
  document.getElementById("colormap-btn").textContent = `色带：${cm.label}`;
  document.getElementById("preset-btn").textContent = isConnectome
    ? `网络：${ensureConnectomeLayer().getPreset().label}`
    : `分布：${PRESETS[state.presetIndex].label}`;
  const gradStops = [...cm.stops].reverse().map(([p, rgb]) => `rgb(${rgb[0]},${rgb[1]},${rgb[2]}) ${((1 - p) * 100).toFixed(1)}%`).join(", ");
  document.getElementById("colorbar-gradient").style.background = `linear-gradient(to bottom, ${gradStops})`;
  if (isConnectome) {
    document.getElementById("colorbar-unit").textContent = "连接强度";
    document.getElementById("tick-max").textContent = "1.00";
    document.getElementById("tick-mid").textContent = "0.60";
    document.getElementById("tick-min").textContent = "0.20";
  } else {
    document.getElementById("colorbar-unit").textContent = state.scaleInfo.unit;
    document.getElementById("tick-max").textContent = state.scaleInfo.maxText;
    document.getElementById("tick-mid").textContent = state.scaleInfo.midText;
    document.getElementById("tick-min").textContent = state.scaleInfo.minText;
  }
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
  document.getElementById("atlas-card-title").textContent = atlas.id === "lobes" ? "脑叶 (Atlas)" : (atlas.id === "yeo7" ? "Yeo-7 网络" : "DK 脑区 (68)");
  for (const r of atlas.regions) {
    if (r.id === 0) continue;
    const item = document.createElement("div");
    item.className = "atlas-item" + (r.id === state.pinnedRegionId ? " active" : "");
    item.innerHTML = `<span class="atlas-swatch" style="background:${r.color}"></span><span class="atlas-name" title="${r.name} (${r.name_en})">${r.name}</span>`;
    item.addEventListener("mouseenter", () => { state.hoveredRegionId = r.id; recolorCortexAtlas(); });
    item.addEventListener("mouseleave", () => { state.hoveredRegionId = -1; recolorCortexAtlas(); });
    item.addEventListener("click", () => {
      state.pinnedRegionId = state.pinnedRegionId === r.id ? -1 : r.id;
      updateAtlasLegendUI();
      recolorCortexAtlas();
    });
    listEl.appendChild(item);
  }
}

// --- Camera & Smooth Fly-To ---
function updateCameraTransform() {
  const cosP = Math.cos(camCtrl.pitch);
  const sinP = Math.sin(camCtrl.pitch);
  const sinY = Math.sin(camCtrl.yaw);
  const cosY = Math.cos(camCtrl.yaw);
  camera.position.set(
    camCtrl.target.x + camCtrl.distance * cosP * sinY,
    camCtrl.target.y + camCtrl.distance * sinP,
    camCtrl.target.z + camCtrl.distance * cosP * cosY
  );
  camera.lookAt(camCtrl.target);
}

function normalizeAngleDelta(d) {
  let w = (d + Math.PI) % (2 * Math.PI);
  if (w < 0) w += 2 * Math.PI;
  return w - Math.PI;
}

function startCameraAnim(targetYaw, targetPitch, targetDist, targetVec, durationMs = 540) {
  camCtrl.anim = {
    startMs: performance.now(),
    durationMs,
    y0: camCtrl.yaw, y1: camCtrl.yaw + normalizeAngleDelta(targetYaw - camCtrl.yaw),
    p0: camCtrl.pitch, p1: Math.max(-1.32, Math.min(1.32, targetPitch)),
    d0: camCtrl.distance, d1: targetDist,
    t0: camCtrl.target.clone(), t1: targetVec.clone(),
  };
}

function focusSelectedElectrode() {
  if (!state.selectedLabel) return;
  const found = state.electrodes.find(isSelectedElectrode);
  if (!found) return;
  const p = found.pos;
  const targetYaw = Math.atan2(p.x, p.z);
  const hRad = Math.hypot(p.x, p.z);
  const targetPitch = Math.max(-1.25, Math.min(1.25, Math.atan2(p.y, hRad)));
  startCameraAnim(targetYaw, targetPitch, camCtrl.distance, camCtrl.defaultTarget, 560);
  pulseStartMs = performance.now();
}

function projectLabels() {
  if (labelElements.length === 0) return;
  const w = host.clientWidth, h = host.clientHeight;
  if (w <= 1 || h <= 1) return;
  const camPos = camera.position;
  const v = new THREE.Vector3();
  const toCam = new THREE.Vector3();
  const inConnectome = state.mode === "connectome" && !!connectomeLayer;
  for (const { el, e } of labelElements) {
    const isSel = isSelectedElectrode(e);
    let anchorPos = e.pos;
    let anchorNormal = e.normal;
    if (inConnectome) {
      const cNode = connectomeLayer.getNodeForElectrode(e);
      if (!cNode) { el.style.display = "none"; continue; }
      anchorPos = cNode.pos;
      anchorNormal = cNode.normal;
    }
    toCam.copy(camPos).sub(anchorPos).normalize();
    const dot = anchorNormal.dot(toCam);
    if (!isSel && dot <= 0.05) {
      el.style.display = "none";
      continue;
    }
    v.copy(anchorPos).project(camera);
    if (v.z > 1 || v.x < -1.1 || v.x > 1.1 || v.y < -1.1 || v.y > 1.1) {
      el.style.display = "none";
      continue;
    }
    const sx = (v.x * 0.5 + 0.5) * w;
    const sy = (-v.y * 0.5 + 0.5) * h;
    // Dynamic pixel offset proportional to apparent sphere radius so label never overlaps sphere
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

// --- Pointer Interaction (Rotate / Shift+Pan / Wheel Zoom / Atlas Hover) ---
host.addEventListener("pointerdown", (ev) => {
  if (ev.button !== 0) return;
  camCtrl.anim = null;
  camCtrl.dragging = true;
  camCtrl.panning = ev.shiftKey;
  camCtrl.lastX = ev.clientX;
  camCtrl.lastY = ev.clientY;
  camCtrl.movedPx = 0;
  host.classList.add("dragging");
  host.setPointerCapture(ev.pointerId);
});

host.addEventListener("pointermove", (ev) => {
  if (camCtrl.dragging) {
    const dx = ev.clientX - camCtrl.lastX;
    const dy = ev.clientY - camCtrl.lastY;
    camCtrl.movedPx += Math.hypot(dx, dy);
    camCtrl.lastX = ev.clientX;
    camCtrl.lastY = ev.clientY;
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

  // Hover picking for cortical atlas regions
  if (state.mode === "atlas" && cortexMesh && state.cortexData) {
    const rect = renderer.domElement.getBoundingClientRect();
    pointerNdc.x = ((ev.clientX - rect.left) / rect.width) * 2 - 1;
    pointerNdc.y = -((ev.clientY - rect.top) / rect.height) * 2 + 1;
    raycaster.setFromCamera(pointerNdc, camera);
    const hits = raycaster.intersectObject(cortexMesh, false);
    if (hits.length > 0 && hits[0].face) {
      const atlas = state.cortexData.atlases[state.atlasId];
      const rid = atlas?.regionIds?.[hits[0].face.a] ?? 0;
      const info = atlas?.regionsById?.get(rid);
      if (info && rid > 0) {
        if (state.hoveredRegionId !== rid) { state.hoveredRegionId = rid; recolorCortexAtlas(); }
        tooltip.innerHTML = `<div>${info.name}</div><div class="sub">${info.name_en} · ${info.lobe}</div>`;
        tooltip.style.display = "block";
        tooltip.style.left = `${Math.min(rect.width - 170, ev.clientX - rect.left + 14)}px`;
        tooltip.style.top = `${Math.max(10, ev.clientY - rect.top - 12)}px`;
        return;
      }
    }
    if (state.hoveredRegionId !== -1) { state.hoveredRegionId = -1; recolorCortexAtlas(); }
    tooltip.style.display = "none";
  } else if (state.mode === "connectome" && connectomeLayer) {
    const rect = renderer.domElement.getBoundingClientRect();
    pointerNdc.x = ((ev.clientX - rect.left) / rect.width) * 2 - 1;
    pointerNdc.y = -((ev.clientY - rect.top) / rect.height) * 2 + 1;
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
  } else {
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
    recolorCortexAtlas();
  }
});

host.addEventListener("wheel", (ev) => {
  ev.preventDefault();
  camCtrl.anim = null;
  const factor = Math.pow(1.0014, ev.deltaY);
  camCtrl.distance = Math.max(0.75, Math.min(5.2, camCtrl.distance * factor));
}, { passive: false });

// --- UI Button Wiring ---
document.querySelectorAll("#mode-group .seg-btn").forEach((btn) => {
  btn.addEventListener("click", () => {
    state.mode = btn.dataset.mode;
    state.hoveredRegionId = -1;
    state.pinnedRegionId = -1;
    applyModeVisibility();
    recolorActiveSurfaces();
  });
});

document.getElementById("atlas-select").addEventListener("change", (ev) => {
  state.atlasId = ev.target.value;
  state.hoveredRegionId = -1;
  state.pinnedRegionId = -1;
  updateAtlasLegendUI();
  recolorCortexAtlas();
});

document.getElementById("colormap-btn").addEventListener("click", () => {
  const idx = (COLORMAP_ORDER.indexOf(state.colormap) + 1) % COLORMAP_ORDER.length;
  state.colormap = COLORMAP_ORDER[idx];
  updateColorbarUI();
  recolorActiveSurfaces();
});

document.getElementById("preset-btn").addEventListener("click", () => {
  if (state.mode === "connectome") {
    const { preset, stats } = ensureConnectomeLayer().cyclePreset();
    updateColorbarUI();
    updateConnectomeStatusUI(stats);
    startCameraAnim(preset.yaw, preset.pitch, camCtrl.distance, camCtrl.defaultTarget, 560);
    return;
  }
  state.presetIndex = (state.presetIndex + 1) % PRESETS.length;
  const p = PRESETS[state.presetIndex];
  updateColorbarUI();
  // Re-evaluate preset values on existing electrodes
  for (const e of state.electrodes) e.valueNorm = evalPresetValue(e.pos, p.id);
  const vals = state.electrodes.map((e) => e.valueNorm);
  const mn = Math.min(...vals), mx = Math.max(...vals), span = mx - mn;
  if (span > 1e-6) for (const e of state.electrodes) e.valueNorm = 0.05 + 0.90 * ((e.valueNorm - mn) / span);
  recolorActiveSurfaces();
  startCameraAnim(p.yaw, -p.pitch, camCtrl.distance, camCtrl.defaultTarget, 560);
});

document.getElementById("connectome-threshold").addEventListener("input", (ev) => {
  const val = parseFloat(ev.target.value);
  document.getElementById("connectome-threshold-val").textContent = val.toFixed(2);
  if (state.mode === "connectome") {
    const stats = ensureConnectomeLayer().setThreshold(val);
    updateConnectomeStatusUI(stats);
  }
});

document.getElementById("reset-btn").addEventListener("click", () => {
  startCameraAnim(camCtrl.defaultYaw, camCtrl.defaultPitch, camCtrl.defaultDistance, camCtrl.defaultTarget, 460);
});

document.getElementById("btn-2d").addEventListener("click", () => {
  window.chrome?.webview?.postMessage({ type: "switchTo2D" });
});

window.addEventListener("resize", () => {
  const w = host.clientWidth || 1, h = host.clientHeight || 1;
  camera.aspect = w / h;
  camera.updateProjectionMatrix();
  renderer.setSize(w, h);
});

// --- WPF <-> WebView2 Message Bridge ---
window.chrome?.webview?.addEventListener("message", (ev) => {
  const msg = typeof ev.data === "string" ? JSON.parse(ev.data) : ev.data;
  if (!msg || !msg.type) return;

  if (msg.type === "setScalpModel") {
    loadScalpMesh(msg.model);
  } else if (msg.type === "setCortexModel") {
    loadCortexMesh(msg.model);
  } else if (msg.type === "setSceneState") {
    state.isRealField = !!msg.isRealField;
    state.isPreviewHeatmap = !!msg.isPreviewHeatmap;
    state.enableLabModes = !!msg.enableLabModes;
    state.showDimSwitcher = !!msg.showDimSwitcher;
    if (msg.forcedMode) {
      state.mode = msg.forcedMode;
    } else if (!state.isRealField && !state.isPreviewHeatmap) {
      state.mode = "electrodes";
    } else if (state.mode === "electrodes") {
      state.mode = "cortex";
    } else if (state.mode === "connectome" && !state.enableLabModes) {
      state.mode = "cortex";
    }
    if (msg.scaleInfo) state.scaleInfo = msg.scaleInfo;
    const winEl = document.getElementById("window-pill");
    if (msg.windowText) { winEl.textContent = msg.windowText; winEl.style.display = "block"; }
    else { winEl.style.display = "none"; }

    updateElectrodes(msg.electrodes || []);
    applyModeVisibility();
    recolorActiveSurfaces();
  } else if (msg.type === "selectElectrode") {
    state.selectedLabel = msg.label || null;
    for (const { mesh, e } of electrodeMeshes) {
      const sel = isSelectedElectrode(e);
      mesh.material.color.setHex(sel ? 0xf97316 : (state.mode === "electrodes" ? 0x1d4ed8 : 0x1e293b));
      mesh.material.emissive.setHex(sel ? 0xea580c : 0x000000);
      mesh.material.emissiveIntensity = sel ? 0.55 : 0.0;
      mesh.scale.setScalar(sel ? 1.45 : 1.0);
      if (sel) selectedMarkerMesh = mesh;
    }
    for (const { el, e } of labelElements) el.classList.toggle("selected", isSelectedElectrode(e));
    if (state.selectedLabel) focusSelectedElectrode();
  }
});

// --- Render Loop ---
function animate(now) {
  requestAnimationFrame(animate);
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

  // 2 crisp pulses over 1.2s on selected electrode
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

  updateCameraTransform();
  renderer.render(scene, camera);
  projectLabels();
}

updateCameraTransform();
requestAnimationFrame(animate);
window.chrome?.webview?.postMessage({ type: "ready" });
