import * as THREE from "./vendor/three.module.min.js";
import { srgbToLinear } from "./colormaps.js";

/**
 * Right-handed GNM->Three.js anatomical coordinate transform, height-graded frosted
 * alabaster scalp shader, and scalp/cortex BufferGeometry loaders.
 */

export function decodeB64(b64, TypedArrayCtor) {
  const bin = atob(b64);
  const bytes = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
  return new TypedArrayCtor(bytes.buffer);
}

// Proper right-handed basis (det = +1, zero L/R mirroring):
// +X = Subject LEFT (-R), +Y = Up (+U), +Z = Forward/Face (+F).
export function buildTransformFromScalp(payload) {
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

function ensureOutwardNormals(geom, indices, step = 8) {
  geom.computeVertexNormals();
  const pos = geom.getAttribute("position");
  const norm = geom.getAttribute("normal");
  let dotSum = 0;
  for (let i = 0; i < pos.count; i += step) {
    dotSum += pos.getX(i) * norm.getX(i) + pos.getY(i) * norm.getY(i) + pos.getZ(i) * norm.getZ(i);
  }
  if (dotSum < 0) {
    for (let i = 0; i < indices.length; i += 3) {
      const tmp = indices[i + 1]; indices[i + 1] = indices[i + 2]; indices[i + 2] = tmp;
    }
    geom.setIndex(new THREE.BufferAttribute(indices, 1));
    geom.computeVertexNormals();
  }
}

export function createFrostedScalpMaterial() {
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
        float key = clamp(dot(N, uKeyDir) * 0.5 + 0.5, 0.0, 1.0);
        float fill = clamp(dot(N, uFillDir) * 0.5 + 0.5, 0.0, 1.0);
        float shade = clamp(key * 0.74 + fill * 0.26, 0.0, 1.0);
        vec3 H = normalize(uKeyDir + V);
        float spec = pow(clamp(dot(N, H), 0.0, 1.0), 30.0) * 0.12;
        vec3 baseCol = mix(uShadowCol, uLightCol, smoothstep(0.12, 0.92, shade));
        vec3 col = mix(baseCol, uRimCol, fresnel * 0.55) + vec3(spec);
        float dome = smoothstep(-0.08, 0.14, vLocalY);
        float faceAlpha = clamp(0.56 + fresnel * 0.34, 0.54, 0.90);
        float domeAlpha = clamp(0.07 + fresnel * 0.56, 0.06, 0.72);
        gl_FragColor = vec4(col, mix(faceAlpha, domeAlpha, dome));
      }
    `,
  });
}

export function buildScalpMeshes(payload) {
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
  ensureOutwardNormals(geom, indices, 8);

  const colors = new Float32Array(normVerts.length);
  const nR = srgbToLinear(205 / 255), nG = srgbToLinear(211 / 255), nB = srgbToLinear(218 / 255);
  for (let i = 0; i < normVerts.length; i += 3) {
    colors[i] = nR; colors[i + 1] = nG; colors[i + 2] = nB;
  }
  geom.setAttribute("color", new THREE.BufferAttribute(colors, 3));

  const opaqueMesh = new THREE.Mesh(
    geom,
    new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.72, metalness: 0.02, side: THREE.DoubleSide })
  );
  opaqueMesh.renderOrder = 1;

  // Transparent-queue depth prepass (renderOrder 4) runs AFTER ghost cortex (2) & connectome (3)
  // so chin/ear/neck folds never ghost while interior connectome arcs remain 100% visible.
  const depthMesh = new THREE.Mesh(
    geom.clone(),
    new THREE.MeshBasicMaterial({
      transparent: true, colorWrite: false, depthWrite: true, side: THREE.FrontSide,
      polygonOffset: true, polygonOffsetFactor: 0.5, polygonOffsetUnits: 0.5,
    })
  );
  depthMesh.renderOrder = 4;

  const frostedMesh = new THREE.Mesh(geom.clone(), createFrostedScalpMaterial());
  frostedMesh.renderOrder = 5;

  return {
    opaqueMesh,
    depthMesh,
    frostedMesh,
    scalpData: { normVerts, mapPoint, scale, locations: payload.electrode_locations || [] },
  };
}

export function buildCortexMesh(cortexPayload, mapPoint) {
  const rawVerts = decodeB64(cortexPayload.vertices_f32_b64, Float32Array);
  const rawTris = decodeB64(cortexPayload.triangles_i32_b64, Int32Array);
  const sulc = decodeB64(cortexPayload.sulc_f32_b64, Float32Array);
  const vCount = rawVerts.length / 3;
  const normVerts = new Float32Array(rawVerts.length);
  for (let i = 0; i < vCount; i++) {
    const [x, y, z] = mapPoint(rawVerts[i * 3], rawVerts[i * 3 + 1], rawVerts[i * 3 + 2]);
    normVerts[i * 3] = x; normVerts[i * 3 + 1] = y; normVerts[i * 3 + 2] = z;
  }

  const indices = new Uint32Array(rawTris.length);
  for (let i = 0; i < rawTris.length; i += 3) {
    indices[i] = rawTris[i]; indices[i + 1] = rawTris[i + 1]; indices[i + 2] = rawTris[i + 2];
  }

  const geom = new THREE.BufferGeometry();
  geom.setAttribute("position", new THREE.BufferAttribute(normVerts, 3));
  geom.setIndex(new THREE.BufferAttribute(indices, 1));
  ensureOutwardNormals(geom, indices, 10);
  geom.setAttribute("color", new THREE.BufferAttribute(new Float32Array(vCount * 3), 3));

  const solidMaterial = new THREE.MeshStandardMaterial({ vertexColors: true, roughness: 0.44, metalness: 0.04 });
  const mesh = new THREE.Mesh(geom, solidMaterial);
  mesh.renderOrder = 2;

  const atlases = {};
  for (const [id, atlas] of Object.entries(cortexPayload.atlases || {})) {
    const regionIds = decodeB64(atlas.vertex_region_ids_i16_b64, Int16Array);
    const regionsById = new Map();
    for (const r of atlas.regions) regionsById.set(r.id, r);
    atlases[id] = { ...atlas, regionIds, regionsById };
  }

  return { mesh, solidMaterial, cortexData: { normVerts, sulc, vCount, atlases } };
}
