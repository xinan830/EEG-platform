import { COLORMAP_LUTS, hexToRgb01, smoothstep, srgbToLinear } from "./colormaps.js";

/**
 * High-speed surface vertex-colorer with cached RBF interpolation weight matrices
 * so live EEG streaming updates cortex (20,484 verts) & scalp in < 1.5 ms/frame.
 */
export function createSurfaceRecolorer() {
  let cortexRbfCache = null;
  let scalpRbfCache = null;

  function invalidateAll() {
    cortexRbfCache = null;
    scalpRbfCache = null;
  }

  function invalidateCortex() {
    cortexRbfCache = null;
  }

  function getCortexRbfCache(cortexData, electrodes, layoutKey) {
    const { normVerts, sulc, vCount } = cortexData;
    const mCount = electrodes.length;
    if (cortexRbfCache && cortexRbfCache.key === layoutKey && cortexRbfCache.vCount === vCount) {
      return cortexRbfCache;
    }
    const weights = new Float32Array(vCount * mCount);
    const aoLinear = new Float32Array(vCount);
    const sigma = 0.22, twoSigSq = 2 * sigma * sigma, sigSq = sigma * sigma;
    const ex = new Float32Array(mCount), ey = new Float32Array(mCount), ez = new Float32Array(mCount);
    for (let m = 0; m < mCount; m++) {
      ex[m] = electrodes[m].pos.x; ey[m] = electrodes[m].pos.y; ez[m] = electrodes[m].pos.z;
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
    cortexRbfCache = { key: layoutKey, vCount, mCount, weights, aoLinear };
    return cortexRbfCache;
  }

  function recolorCortexHeatmap(cortexMesh, cortexData, electrodes, layoutKey, colormap) {
    if (!cortexMesh || !cortexData) return;
    const { sulc, vCount } = cortexData;
    const colorAttr = cortexMesh.geometry.getAttribute("color");
    const arr = colorAttr.array;

    if (electrodes.length === 0) {
      for (let i = 0; i < vCount; i++) {
        const shade = srgbToLinear(0.78 - 0.18 * Math.max(-1, Math.min(1, sulc[i] * 0.48)));
        arr[i * 3] = shade; arr[i * 3 + 1] = shade; arr[i * 3 + 2] = shade;
      }
      colorAttr.needsUpdate = true;
      return;
    }

    const { weights, aoLinear, mCount } = getCortexRbfCache(cortexData, electrodes, layoutKey);
    const vals = new Float32Array(mCount);
    for (let m = 0; m < mCount; m++) vals[m] = electrodes[m].valueNorm;
    const lut = COLORMAP_LUTS[colormap] || COLORMAP_LUTS.turbo;

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

  function recolorCortexAtlas(cortexMesh, cortexData, atlasId, hoveredRegionId, pinnedRegionId) {
    if (!cortexMesh || !cortexData) return;
    const atlas = cortexData.atlases[atlasId];
    if (!atlas) return;
    const { sulc, vCount } = cortexData;
    const colorAttr = cortexMesh.geometry.getAttribute("color");
    const arr = colorAttr.array;
    const ids = atlas.regionIds;
    const activeId = hoveredRegionId >= 0 ? hoveredRegionId : pinnedRegionId;

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

  function getScalpRbfCache(scalpData, electrodes, layoutKey) {
    const { normVerts } = scalpData;
    const vCount = normVerts.length / 3;
    const mCount = electrodes.length;
    if (scalpRbfCache && scalpRbfCache.key === layoutKey && scalpRbfCache.vCount === vCount) {
      return scalpRbfCache;
    }
    let minY = Infinity, minFrontY = Infinity;
    const ex = new Float32Array(mCount), ey = new Float32Array(mCount), ez = new Float32Array(mCount);
    for (let m = 0; m < mCount; m++) {
      const p = electrodes[m].pos;
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
    scalpRbfCache = { key: layoutKey, vCount, mCount, weights, mask };
    return scalpRbfCache;
  }

  function recolorScalpSurface(scalpOpaqueMesh, scalpData, electrodes, layoutKey, mode, colormap) {
    if (!scalpOpaqueMesh || !scalpData) return;
    const { normVerts } = scalpData;
    const colorAttr = scalpOpaqueMesh.geometry.getAttribute("color");
    const arr = colorAttr.array;
    const vCount = normVerts.length / 3;
    const nr = srgbToLinear(205 / 255), ng = srgbToLinear(211 / 255), nb = srgbToLinear(218 / 255);

    if (mode === "electrodes" || electrodes.length === 0) {
      for (let i = 0; i < vCount; i++) {
        arr[i * 3] = nr; arr[i * 3 + 1] = ng; arr[i * 3 + 2] = nb;
      }
      colorAttr.needsUpdate = true;
      return;
    }

    const { weights, mask, mCount } = getScalpRbfCache(scalpData, electrodes, layoutKey);
    const vals = new Float32Array(mCount);
    for (let m = 0; m < mCount; m++) vals[m] = electrodes[m].valueNorm;
    const lut = COLORMAP_LUTS[colormap] || COLORMAP_LUTS.turbo;

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

  return { invalidateAll, invalidateCortex, recolorCortexHeatmap, recolorCortexAtlas, recolorScalpSurface };
}
