/**
 * Scientific EEG colormaps, precomputed 256-entry linear-RGB LUTs,
 * sRGB/linear helpers, and demo preset spatial fields.
 */

export const COLORMAPS = {
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

export const COLORMAP_ORDER = ["turbo", "jet", "rdbu", "viridis"];

export const PRESETS = [
  { id: "frontal", label: "额区双侧激活", yaw: 0.32, pitch: -0.35 },
  { id: "occipital", label: "枕叶 Alpha 优势", yaw: -2.55, pitch: -0.32 },
  { id: "asymmetry", label: "右半球偏侧激活", yaw: -0.78, pitch: -0.32 },
];

export function evalColormap(t, kind) {
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

export function hexToRgb01(hex) {
  const m = /^#?([0-9a-f]{6})$/i.exec(hex || "#94A3B8");
  if (!m) return [0.58, 0.64, 0.72];
  const n = parseInt(m[1], 16);
  return [((n >> 16) & 255) / 255, ((n >> 8) & 255) / 255, (n & 255) / 255];
}

export function smoothstep(e0, e1, x) {
  if (Math.abs(e1 - e0) < 1e-9) return x >= e1 ? 1 : 0;
  const t = Math.max(0, Math.min(1, (x - e0) / (e1 - e0)));
  return t * t * (3 - 2 * t);
}

export function srgbToLinear(c) {
  const v = Math.max(0, Math.min(1, c));
  return v <= 0.04045 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
}

export const COLORMAP_LUTS = {};
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

export function evalPresetValue(pos, presetId) {
  // Proper right-handed coords: +X = Subject Left, -X = Subject Right, +Y = Up, +Z = Forward
  const g = (cx, cy, cz, s) => {
    const dx = pos.x - cx, dy = pos.y - cy, dz = pos.z - cz;
    return Math.exp(-(dx * dx + dy * dy + dz * dz) / (2 * s * s));
  };
  if (presetId === "occipital") {
    return 0.90 * g(0.11, 0.14, -0.34, 0.22) + 0.95 * g(-0.12, 0.14, -0.34, 0.22) + 0.50 * g(0.0, 0.36, -0.16, 0.24);
  }
  if (presetId === "asymmetry") {
    return 0.95 * g(-0.26, 0.28, 0.06, 0.23) + 0.72 * g(-0.22, 0.24, 0.24, 0.21) - 0.45 * g(0.26, 0.26, 0.04, 0.22) + 0.35;
  }
  return 0.95 * g(0.0, 0.24, 0.34, 0.24) + 0.80 * g(0.18, 0.20, 0.26, 0.22) + 0.82 * g(-0.18, 0.20, 0.26, 0.22);
}
