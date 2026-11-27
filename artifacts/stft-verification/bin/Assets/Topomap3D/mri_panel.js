/**
 * Lazy-loaded NiiVue MRI viewer module (Axial / Coronal / Sagittal + 3D cutaway).
 *
 * Only imports `./vendor/niivue.min.js` and allocates a WebGL2 context on the first
 * time the user enters `MRI 切片` mode — zero RAM/GPU cost during normal 3D EEG views.
 */

export function createMriPanel({ container, canvas, statusEl, coordEl, onReadyChange }) {
  let nv = null;
  let SLICE_TYPE = null;
  let SHOW_RENDER = null;
  let MULTIPLANAR_TYPE = null;
  let initPromise = null;
  let initialized = false;
  let clipEnabled = true;
  let atlasEnabled = false;
  let atlasLoaded = false;
  let currentSourceLabel = "模板参考：MNI152 0.74mm T1 MRI（非个体扫描）";

  function setStatus(text) {
    if (!statusEl) return;
    statusEl.textContent = text || "";
    statusEl.classList.toggle("hidden", !text);
  }

  function updateCoordText(loc) {
    if (!coordEl) return;
    if (!loc || !loc.mm || loc.mm.length < 3) {
      coordEl.textContent = currentSourceLabel;
      return;
    }
    const [x, y, z] = loc.mm;
    const valStr = typeof loc.string === "string" && loc.string.trim() ? ` · ${loc.string.trim()}` : "";
    coordEl.textContent = `${currentSourceLabel} · RAS (${x.toFixed(1)}, ${y.toFixed(1)}, ${z.toFixed(1)}) mm${valStr}`;
  }

  async function ensureInitialized() {
    if (initialized) return nv;
    if (initPromise) return initPromise;

    initPromise = (async () => {
      setStatus("正在加载 NiiVue 医学影像引擎与高清 MNI152 T1 MRI...");
      const mod = await import("./vendor/niivue.min.js");
      const Niivue = mod.Niivue;
      SLICE_TYPE = mod.SLICE_TYPE;
      SHOW_RENDER = mod.SHOW_RENDER;
      MULTIPLANAR_TYPE = mod.MULTIPLANAR_TYPE;

      nv = new Niivue({
        backColor: [0.05, 0.07, 0.10, 1.0],
        crosshairColor: [0.23, 0.57, 0.98, 0.85],
        crosshairWidth: 0.75,
        clipPlaneColor: [0, 0, 0, 0],
        show3Dcrosshair: true,
        isColorbar: false,
        isOrientCube: true,
        isHighDPI: true,
        multiplanarForceRender: true,
        multiplanarShowRender: SHOW_RENDER.ALWAYS,
        multiplanarLayout: MULTIPLANAR_TYPE.GRID,
        multiplanarPadPixels: 4,
        dragAndDropEnabled: true,
        onLocationChange: (loc) => updateCoordText(loc),
      });

      await nv.attachToCanvas(canvas);
      nv.setSliceType(SLICE_TYPE.MULTIPLANAR);
      nv.setMultiplanarLayout(MULTIPLANAR_TYPE.GRID);
      nv.setSliceMM(true);
      await nv.loadVolumes([
        {
          url: "./mri/mni152_t1_1mm.nii.gz",
          colormap: "gray",
          opacity: 1.0,
        },
      ]);

      nv.setVolumeRenderIllumination(0.55);
      applyClipState();
      nv.setRenderAzimuthElevation(125, 18);
      nv.updateGLVolume();

      initialized = true;
      setStatus("");
      updateCoordText(null);
      if (onReadyChange) onReadyChange(true);
      return nv;
    })().catch((err) => {
      initPromise = null;
      setStatus(`MRI 引擎加载失败：${err?.message || err}`);
      throw err;
    });

    return initPromise;
  }

  function applyClipState() {
    if (!nv) return;
    if (clipEnabled) {
      // Clean anterior-superior-left cutaway revealing internal gyri, ventricles & deep nuclei
      nv.setClipPlane([-0.10, 225, 22]);
    } else {
      nv.setClipPlane([2, 0, 0]);
    }
  }

  async function show() {
    container.classList.remove("hidden");
    try {
      await ensureInitialized();
      resize();
    } catch {
      // Status banner already displays the error cleanly
    }
  }

  function hide() {
    container.classList.add("hidden");
  }

  function resize() {
    if (!nv || container.classList.contains("hidden")) return;
    const rect = canvas.getBoundingClientRect();
    if (rect.width > 2 && rect.height > 2) {
      const dpr = Math.min(window.devicePixelRatio || 1, 2);
      canvas.width = Math.round(rect.width * dpr);
      canvas.height = Math.round(rect.height * dpr);
      nv.resizeListener();
      nv.drawScene();
    }
  }

  function setLayout(kind) {
    if (!nv || !SLICE_TYPE) return;
    const map = {
      multiplanar: SLICE_TYPE.MULTIPLANAR,
      render: SLICE_TYPE.RENDER,
      axial: SLICE_TYPE.AXIAL,
      coronal: SLICE_TYPE.CORONAL,
      sagittal: SLICE_TYPE.SAGITTAL,
    };
    nv.setSliceType(map[kind] ?? SLICE_TYPE.MULTIPLANAR);
  }

  function toggleClip() {
    clipEnabled = !clipEnabled;
    applyClipState();
    return clipEnabled;
  }

  async function toggleAtlas() {
    if (!nv) return false;
    atlasEnabled = !atlasEnabled;
    if (atlasEnabled) {
      if (!atlasLoaded) {
        setStatus("正在叠加 FreeSurfer 皮层与深部核团图谱...");
        try {
          await nv.addVolumeFromUrl({
            url: "./mri/fsaverage_aparc_aseg.mgz",
            colormap: "freesurfer",
            cal_min: 1,
            cal_max: 2035,
            opacity: 0.45,
          });
          atlasLoaded = true;
        } catch (err) {
          atlasEnabled = false;
          setStatus(`核团图谱加载失败：${err?.message || err}`);
          return false;
        }
        setStatus("");
      } else if (nv.volumes.length > 1) {
        nv.setOpacity(1, 0.44);
      }
    } else if (atlasLoaded && nv.volumes.length > 1) {
      nv.setOpacity(1, 0.0);
    }
    nv.updateGLVolume();
    return atlasEnabled;
  }

  async function openLocalFile(file) {
    if (!file) return;
    await ensureInitialized();
    setStatus(`正在读取本地 MRI：${file.name}...`);
    try {
      atlasEnabled = false;
      atlasLoaded = false;
      await nv.loadFromFile(file);
      nv.setSliceType(SLICE_TYPE.MULTIPLANAR);
      applyClipState();
      currentSourceLabel = `本地 MRI：${file.name}`;
      updateCoordText(null);
      setStatus("");
    } catch (err) {
      setStatus(`无法解析 MRI 文件：${err?.message || err}`);
    }
  }

  async function jumpToMni(mni, label) {
    if (!mni || mni.length < 3) return;
    await show();
    if (!nv || !nv.volumes || nv.volumes.length === 0) return;
    const frac = nv.mm2frac([mni[0], mni[1], mni[2]]);
    if (frac && frac.length >= 3 && Number.isFinite(frac[0])) {
      nv.scene.crosshairPos = [
        Math.max(0.02, Math.min(0.98, frac[0])),
        Math.max(0.02, Math.min(0.98, frac[1])),
        Math.max(0.02, Math.min(0.98, frac[2])),
      ];
      nv.setCrosshairColor([1.0, 0.36, 0.22, 0.95]);
      nv.drawScene();
    }
    if (coordEl) {
      coordEl.textContent = `已定位源点：${label || "EEG 偶极子"} · MNI (${mni[0]}, ${mni[1]}, ${mni[2]}) mm`;
    }
  }

  function resetView() {
    if (!nv) return;
    nv.scene.crosshairPos = [0.5, 0.5, 0.5];
    nv.setCrosshairColor([0.23, 0.57, 0.98, 0.85]);
    nv.setRenderAzimuthElevation(125, 18);
    applyClipState();
    nv.drawScene();
    updateCoordText(null);
  }

  return {
    show,
    hide,
    resize,
    setLayout,
    toggleClip,
    toggleAtlas,
    openLocalFile,
    jumpToMni,
    resetView,
    isClipEnabled: () => clipEnabled,
    isAtlasEnabled: () => atlasEnabled,
  };
}
