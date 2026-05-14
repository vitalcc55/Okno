# ExecPlan: Okno native visual performance substrate

Status: `planned`  
Date: `2026-05-14`

## 1. Goal

Ускорить visual-heavy product loops в `Okno` без переписывания runtime и без
изменения public tool surface.

Целевой эффект:

```text
faster windows.wait visual checks
cheaper observeAfter successor visual proof
better future windows.region_capture verification path
smaller need for full PNG work inside internal loops
```

## 2. Boundary

This plan is not:

- rewrite `Okno` in Rust;
- rewrite UIA traversal in Rust;
- rewrite policy/session/orchestration in Rust;
- add public native-specific tools;
- replace C# control plane.

Working split:

```text
C# = control plane
Rust = raw visual data plane
C++/WinRT = deferred only if capture acquisition proves to be the bottleneck
```

## 3. Why this work belongs in the roadmap

Этот workstream усиливает уже существующие slices:

- `windows.capture`
- `windows.wait`
- shipped `observeAfter=true`
- future `windows.region_capture`
- operations / benchmarks / verification contour

Он не должен появиться как новая user-facing feature family. Это repo-facing
performance substrate.

### Entry conditions for active native work

Этот workstream не должен переходить в active implementation, пока не выполнены
все условия ниже:

1. `windows.region_capture` уже имеет хотя бы минимальный geometry/crop contract;
2. baseline benchmarks для managed visual path уже сняты;
3. managed fallback remains mandatory;
4. native path оценивается как acceleration for the current product loop, а не
   как отдельная feature family.

## 4. Current code facts

### 4.1. Visual grid hot path

Current managed hot path:

```text
src/WinBridge.Runtime.Windows.Capture/WaitVisualComparisonDataBuilder.cs
```

Observed facts from current code:

- allocates `long[] sums` and `int[] counts`;
- loops over every BGRA pixel;
- computes luma;
- maps pixels into a 16x16 grid;
- builds `WaitVisualComparisonData`.

This is the first CPU-heavy candidate because it is:

- CPU-bound;
- buffer-oriented;
- deterministic;
- isolated from policy and contract logic;
- easy to compare against the existing managed implementation.

Но native workstream должен стартовать только после фиксации region/crop
geometry contract, чтобы тот же substrate сразу проектировался и для full-frame,
и для ROI/region proof path.

### 4.2. Visual sample path

Current path:

```text
GraphicsCaptureService.CaptureVisualSampleAsync
  -> CaptureSoftwareBitmapAsync
  -> WaitVisualComparisonDataBuilder.CreateFromSoftwareBitmap
  -> WaitVisualSample
```

This is the main integration seam for native visual analysis.

### 4.3. PNG path

Current code still pays PNG work when artifacts are written or rasterized
captures are returned. PNG must remain for evidence and screenshots, but internal
wait/proof loops should not pay PNG cost unless an artifact or public image block
is actually needed.

### 4.4. Current wait semantics

`PollingWaitService` and `WaitVisualComparisonPolicy` already define the product
semantics. Native work must make this path faster, not change what a visual wait
means.

## 5. Why Rust first

Rust is the best first move for:

- raw BGRA loops;
- grid/hash/diff operations;
- dirty-rect style visual summaries;
- SIMD-friendly pixel math;
- single-call coarse-grained FFI.

Rust is not the first move for:

- UIA/COM traversal;
- MCP contracts;
- policy and approvals;
- foreground/integrity logic;
- input dispatch.

## 6. Why C++/WinRT is deferred

If profiling later proves that the dominant cost is:

- WGC frame acquisition setup;
- D3D11 surface handling;
- WinRT copy path;
- PNG encoder path;

then a native capture helper may be justified. For that specific problem space,
C++/WinRT is likely cheaper and more natural than Rust because the APIs are
Windows-native and WinRT-heavy.

Until that bottleneck is measured, no C++ helper should be introduced.

## 7. Native architecture

Planned layout:

```text
native/
  okno_vision/
    Cargo.toml
    src/lib.rs

src/WinBridge.Runtime.Windows.Capture/Native/
  OknoVisionNative.cs
  NativeVisionAvailability.cs
  NativeVisionOptions.cs
  NativeVisualComparisonDataBuilder.cs
```

Runtime shape:

```text
GraphicsCaptureService
  -> SoftwareBitmap lock / BGRA pointer
  -> NativeVisualComparisonDataBuilder.TryCreate(...)
      -> Rust cdylib
  -> fallback to current managed builder if unavailable or invalid
```

## 8. Phase 0 — measurement first

Before any native code lands, add a benchmark project:

```text
tests/WinBridge.Runtime.Performance/
```

Minimum benchmark set:

```text
CreateFromBgra32Pixels_1280x720
CreateFromBgra32Pixels_1920x1080
CreateFromBgra32Pixels_2560x1440
CreateFromBgra32Pixels_3840x2160
VisualSample_SoftwareBitmap_ToGrid
EncodePng_SoftwareBitmap
```

Metrics:

```text
visual_grid_ms
visual_grid_allocated_bytes
visual_sample_total_ms
png_encode_ms
capture_software_bitmap_ms
wait_visual_probe_ms
```

Exit criteria:

- baseline numbers exist;
- no native change merges without baseline comparison;
- benchmark evidence is referenced from docs/observability or exec-plan notes.

### Phase 0.5 — region/crop geometry contract

Before native code starts, define the minimal geometry contract that future
`windows.region_capture` and internal ROI proof will share.

The contract must fix:

- original geometry basis;
- crop request shape;
- stale capture-reference behavior;
- how ROI maps back to current screenshot/capture coordinates;
- what is considered equal between full-frame and region-level visual proof.

This is needed so the first Rust API does not optimize only the current 16x16
full-frame wait path and then become awkward to reuse for ROI proof.

## 9. Phase 1 — Rust grid builder

### Goal

Reproduce current managed 16x16 visual grid semantics exactly, while keeping the
API and internal implementation ready for later region/ROI reuse.

### Native ABI

The first native contract should be coarse-grained:

```text
one frame in
one compact grid result out
```

Not:

```text
one call per pixel
one call per row
one call per cell
```

### Managed rules

- native path is optional;
- managed path remains available;
- normal runtime must never fail only because native DLL is missing;
- native path must be observable in diagnostics.

### Config shape

```text
OKNO_NATIVE_VISION=auto
OKNO_NATIVE_VISION=0
OKNO_NATIVE_VISION=1
```

During initial rollout, default should remain safe-fallback oriented.

## 10. Phase 1 correctness tests

Native path must initially be a semantics-preserving replacement, not a new
algorithm.

Minimum equivalence cases:

```text
solid_black_1x1
solid_white_1x1
solid_black_16x16
solid_white_16x16
checkerboard_16x16
odd_width_17x13
stride_equals_width_times_4
stride_greater_than_width_times_4
random_deterministic_640x480
random_deterministic_1920x1080
```

Assertions:

- grid bytes exactly match managed path;
- populated cell count exactly matches managed path.

If exact equality is not preserved, native path does not ship.

## 11. Phase 2 — ROI-ready builder and region reuse path

Before native integration into the runtime loop, the same substrate should be
able to serve both:

```text
full frame
region / ROI frame
```

This does not require public `windows.region_capture` to be shipped first, but
it does require the native design to avoid a full-frame-only dead end.

Expected internal direction:

```text
okno_bgra32_visual_grid
okno_bgra32_region_grid
shared grid/digest code path
```

## 12. Phase 3 — integrate into current builder

Integration target:

```text
WaitVisualComparisonDataBuilder.CreateFromSoftwareBitmap
```

Rules:

- keep all current validation;
- try native path after pointer/capacity checks;
- on missing DLL, missing symbol or invalid native result, fall back to managed;
- do not remove current managed implementation.

Managed builder remains:

- fallback;
- correctness oracle;
- debug path.

## 13. Phase 4 — expand to frame hash and diff

Only after Phase 1 is proven and measured.

Second-layer native work:

- perceptual hash;
- changed-cell summary;
- difference ratio;
- optional dirty-region summary.

Product uses:

- cheaper visual change checks;
- less unnecessary PNG work;
- stronger future region-level verification;
- potential future heuristics for “screen unchanged, skip broader proof refresh”.

## 14. Phase 5 — region capture synergy

When `windows.region_capture` lands, reuse the same native substrate.

Required behavior:

- crop stays in original geometry basis;
- region proof remains narrow and action-adjacent;
- native implementation details never leak into public tool shape.

This path may later support:

- region grid;
- region hash;
- region diff;

but only as internal substrate.

## 15. Phase 6 — decide whether capture acquisition is the real bottleneck

After native visual analysis is integrated, compare:

```text
capture_software_bitmap_ms
visual_grid_ms
png_encode_ms
```

Decision rule:

- if `visual_grid_ms` dominates, Rust substrate is enough for this wave;
- if `capture_software_bitmap_ms` dominates, first try persistent WGC/session
  improvements in C#;
- if WinRT/D3D/WGC setup still dominates after that, open a separate planned
  C++/WinRT capture-helper workstream.

## 16. Packaging

Native packaging rules:

- native DLL can ship in local output and plugin-local runtime artifacts when
  enabled;
- absence of the DLL must not break the default runtime;
- version/availability should be visible in health/diagnostics;
- the public tool surface remains unchanged.

Useful diagnostics:

```text
native_vision_available
native_vision_mode
native_vision_version
native_vision_last_error
visual_analysis_backend
visual_analysis_ms
```

## 17. Security and safety

Native layer must never become a policy shortcut.

Native code may do:

- grid;
- hash;
- diff;
- region crop summary;
- dirty-rect style visual analysis.

Native code must not do:

- filesystem orchestration;
- network;
- process launch;
- input dispatch;
- clipboard access;
- policy decisions.

All policy stays in C#.

## 18. Failure modes

Expected native failure handling:

| Failure | Normal runtime behavior |
| --- | --- |
| DLL missing | fallback to managed |
| symbol missing | fallback to managed |
| invalid native return | fallback and log reason |
| native panic/abort risk | contain inside FFI boundary as much as possible |
| output mismatch in tests | do not ship native path |

For perf/smoke environments where native path is explicitly required, absence or
drift may fail the verification contour.

## 19. Verification contour

### L0

Performance measurement before and after native changes.

### L1

Unit/contract equivalence tests between managed and native outputs.

### L2

Integration tests:

- capture service can use native path when enabled;
- managed fallback still works;
- wait semantics remain unchanged.

### L3

Real smoke:

- `STDIO` runtime;
- visual waits;
- action + `observeAfter` path where visual proof is relevant;
- artifact writing still works when requested.

### Docs sync

- roadmap references;
- observability docs;
- `CHANGELOG`;
- exec-plan status notes.

## 20. Recommended first PRs

1. benchmark baseline project
2. region/crop geometry contract
3. Rust crate skeleton for full-frame grid path
4. ROI-ready native API shape
5. managed/native equivalence tests
6. integrate into wait / observeAfter / region_capture-adjacent paths
7. visual wait smoke and diagnostics evidence

## 21. Deferred work

Not in the first wave:

- persistent native WGC engine;
- GPU processing;
- OCR;
- broad dirty-rect-driven UIA refresh;
- native input dispatch;
- public native tool family.

Deferred after proof:

- richer hash/diff;
- SIMD tuning;
- region-grid specialization;
- persistent capture session helper;
- optional C++/WinRT capture helper.

## 22. Definition of done

This workstream is successful when:

1. default runtime still works without native binaries;
2. native visual path is observable and testable;
3. managed/native outputs are semantics-equivalent for the first shipped path;
4. benchmark evidence shows no regression and preferably clear improvement;
5. public tool surface does not fragment;
6. future `windows.region_capture` can reuse this substrate instead of inventing
   a second internal visual stack.

## 22. One-line summary

```text
Use native code to make Okno's visual proof path faster, not to change what Okno is.
```
