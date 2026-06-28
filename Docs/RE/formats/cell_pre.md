# Format: `.pre` (Cell-Level Outline & Mesh Deltas)

> Clean-room format specification. Derived-truth from static analysis and on-disk file comparisons.
>
> **Verification:** sample-verified (Outline structures and compiler math verified against VFS files).
> ida_reverified: 2026-06-27 · ida_anchor: f61f66a9ae0ec1e946105b2ecff76e8930cb1d1367df64e5688a5266f5ad9963
> subsystems: [terrain_assets, collision_compilation, offline_editor_pipeline]
> C# implementation: `MartialHeroes.Assets.Parsers.Terrain` (Optionally used by compiler shims; runtime client ignores `.pre` files).

---

## 1. Overview and Core Verdict

The `.pre` file extension family (specifically `*.bud.pre` and `*.sod.pre`) represents **pre-compiled outline and mesh deltas** produced by the offline map authoring tools.

- **No Runtime Consumption:** The shipped client binary completely ignores `.pre` files. There are no references to the `.pre` extension in the client code segment. Cell loading routines strictly load runtime formats (`.bud` and `.sod`).
- **Development Tool Role:** These files serve as the workspace source assets containing raw outline vectors and unoptimized mesh grids. The compiler processes them into optimized runtime binaries.

---

## 2. On-Disk Layout

### 2.1 Building/Scene Mesh Delta: `*.bud.pre`
- **Layout Structure:** Identical to the runtime `.bud` format (see `terrain_scene.md`).
- **Logical Content:** Contains raw unoptimized mesh records (`MassObject`). It holds complete vertex lists and triangle indices before geometry optimization.
- **Compiler Pass:** The offline compiler parses the `.bud.pre` records, performs vertex welding, re-orders vertices for cache coherence, updates vertex tag flags (e.g. culling tags at offset `+12` in the vertex stride), and writes the optimized runtime `.bud` binary.

### 2.2 Collision Outline Delta: `*.sod.pre`
- **Layout Structure:** Stores the pre-compiled horizontal 2D-XZ boundaries of physical walls.
- **File Geometry:** Headerless. Stride is variable and determined by the vertex count of each polygon.

```
+0x00            polyCount (u32, little-endian)
+0x04            polygons  (sequence of polygon records)
```

**Each Polygon Record Stride:**
`4 + (vertexCount * 8)` bytes

| Offset | Size (Bytes) | Type | Field Name | Description |
|:---:|:---:|:---|:---|:---|
| `+0x00` | 4 | `u32` LE | `vertexCount` | Number of outline points (typically 3 to 7). |
| `+0x04` | `vertexCount * 8` | `f32[2]` LE | `outlineVerts` | Coordinates array of `{ f32 X, f32 Z }` representing the outline vertices in absolute world space. No Y coordinate is stored. |

---

## 3. Compiler Compilation Algorithm (`.sod.pre` ──► `.sod`)

The offline compiler function `Sod_CompileOutline` processes `.sod.pre` files into runtime `.sod` collision records using the following algorithm:

1. **Polygon Read:** Reads `polyCount` and parses the list of coordinate rings.
2. **AABB Computation:** For each polygon, computes the axis-aligned bounding box:
   - `aabbMinX = min(verts.X)`
   - `aabbMinZ = min(verts.Z)`
   - `aabbMaxX = max(verts.X)`
   - `aabbMaxZ = max(verts.Z)`
   - Saves these boundaries into the `SolidRecord` structure (offset `+0x00` to `+0x0F` inside each 108-byte record).
3. **Double-Sided Wall Generation:** To compile the wall segments into `QuadRecord` (48-byte) objects:
   - Iterates through the polygon vertices sequentially.
   - For each edge from point $A$ to point $B$, it instantiates a quad record representing a bidirectional flat wall:
     - Corner $A = \text{point}[A]$
     - Corner $B = \text{point}[B]$
     - Corner $C = \text{point}[B]$
     - Corner $D = \text{point}[A]$
     - Computes the squared edge length: $L^2 = (B_x - A_x)^2 + (B_z - A_z)^2$, storing it at offset `+0x28` (`edgeScalar1`).
     - Computes a slope coefficient (stored at `+0x20` as `edgeScalar0`).
4. **Output Serializer:** Writes the packed `SolidRecord` array followed by the `QuadRecord` arrays to create the runtime `.sod` file.
