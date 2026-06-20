// World/RealWorldRenderer.ModelLoader.cs
//
// .bud/.skn model loading + mesh instancing, environment wiring, camera placement.
// Part of the RealWorldRenderer partial class split.

using System.Text;
using Godot;
using MartialHeroes.Assets.Parsers.Mesh;
using MartialHeroes.Assets.Parsers.Mesh.Models;
using MartialHeroes.Assets.Parsers.Terrain.Models;
using MartialHeroes.Client.Godot.Adapters;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class RealWorldRenderer
{
    // -------------------------------------------------------------------------
    // Skinned character loading — static pose ArrayMesh (no GltfDocument)
    // -------------------------------------------------------------------------

    /// <summary>Display scale applied to the spawned character node (legacy unit → Godot). Tuned visually.</summary>
    private const float CharacterScale = 5.0f;

    /// <summary>Corrective rotation (degrees) that stands the legacy character mesh upright. Tuned visually.</summary>
    private static readonly Vector3 CharacterUprightRotationDeg = Vector3.Zero;
    // -------------------------------------------------------------------------
    // Environment + water wiring
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Assembles the area's environment (sky/fog/light) into a <see cref="EnvironmentNode" />.
    ///     The environment is read+parsed from the per-area bins via
    ///     <see cref="VfsEnvironmentSource" /> (the same VFS-read+parse adapter pattern as terrain).
    ///     Water rendering visuals are a free engineering choice — the legacy client has no water
    ///     renderer (RESOLVED-NEGATIVE). RECONCILED Campaign 5: <c>map_option%d.bin</c> carries NO water
    ///     field, so the map_option water path (<see cref="WaterPlacement.FromMapOption" />) is always
    ///     disabled; per-cell water presence is detected separately from <c>.map</c> FX texture names.
    ///     spec: Docs/RE/specs/environment.md §3 (assembly) + §4 (water RESOLVED-NEGATIVE).
    ///     spec: Docs/RE/formats/environment_bins.md §1.1 (NO water fields in map_option).
    /// </summary>
    private void WireEnvironmentAndWater()
    {
        if (Assets is null) return;

        // ---- Environment (D3) ----
        // Resolve the World scene's OWN WorldEnvironment + DirectionalLight3D (defined in World.tscn)
        // and pass them explicitly into EnvironmentNode so it drives them in place instead of
        // creating duplicates. This renderer is a direct child of the World scene root (GameLoop),
        // so the scene's env/light are our SIBLINGS — found via our parent. Under boot_flow=login the
        // scene tree is /root → Boot → World → RealWorldRenderer, and a generic top-of-tree walk lands
        // on Boot (whose direct children exclude these) → that was the duplicate-sun bug.
        var worldSceneRoot = GetParent();
        var sceneWorldEnv = FindDirectChildOfType<WorldEnvironment>(worldSceneRoot);
        var sceneDirLight = FindDirectChildOfType<DirectionalLight3D>(worldSceneRoot);
        GD.Print($"[RealWorldRenderer] Scene env nodes under '{worldSceneRoot.Name}': " +
                 $"WorldEnvironment={sceneWorldEnv is not null} DirectionalLight3D={sceneDirLight is not null}.");

        var envNode = new EnvironmentNode { Name = "EnvironmentNode" };
        AddChild(envNode);
        envNode.Configure(Assets, TargetAreaId, sceneWorldEnv, sceneDirLight);

        // ---- Water (D4) ----
        // RECONCILED Campaign 5: map_option has NO water field, so this path is always disabled.
        // Per-cell water presence is detected from the .map FX texture names (CellHasWater), not here.
        // spec: Docs/RE/formats/environment_bins.md §1.1 (no water fields) + §1.4 (RESOLVED-NEGATIVE).
        var env = VfsEnvironmentSource.Load(Assets, TargetAreaId);
        var water = WaterPlacement.FromMapOption(env.MapOption);

        if (!water.Enabled)
            // spec: environment_bins.md §1.1 — map_option carries no water plane (always the case).
            GD.Print($"[Water] area={TargetAreaId} no map_option-driven water plane — checking per-cell FX textures.");

        // ---- Per-cell water detection (D4 / Campaign 5) ----
        // map_option has no water field, so water presence is detected from the .map FX texture names.
        // CellHasWater enumerates FX1–FX7 sections; resolves each TexId via bgtexture.txt; checks for
        // "_water", "_sea", "_wateredge" substrings in the relative path.
        // spec: Docs/RE/formats/terrain.md §3.5 — .map FX sections carry water overlays. CONFIRMED.
        // spec: WaterRenderer.CellHasWater — detection rule; VFS-confirmed texture rel-paths. 2026-06-12.
        if (_cellMap is null || _bgTextures is null)
        {
            GD.Print($"[Water] area={TargetAreaId} cell .map or bgtexture catalog not loaded — no water plane.");
            return;
        }

        var hasWater = WaterRenderer.CellHasWater(_cellMap, _bgTextures);
        GD.Print($"[Water] area={TargetAreaId} per-cell FX detection: hasWater={hasWater}.");

        if (!hasWater)
        {
            GD.Print($"[Water] area={TargetAreaId} cell has no water FX textures — no water plane.");
            return;
        }

        // Centre the plane on the resolved cell, sized to the loaded streaming ring so it covers
        // the visible terrain. Ring radius cells × 1024 wu, +1 cell of slop for the borders.
        // spec: Docs/RE/formats/terrain.md §1.4 — cell size 1024 wu. CONFIRMED.
        var ringRadius = ReadRingRadiusFromConfig();
        var ringCells = 2 * ringRadius + 1 + 1f; // (2r+1) ring + 1 cell slop
        var size = ringCells * 1024f;

        var centreX = (TargetMapX - 10000) * 1024f + 512f;
        var centreZ = (TargetMapZ - 10000) * 1024f + 512f;
        var waterY = WaterRenderer.WaterSurfaceY(_cellMap);
        // spec: WorldCoordinates.ToGodot — negate Z; Y is the data-driven water_y (unchanged).
        var centre = new Vector3(centreX, waterY, -centreZ);

        var waterNode = new WaterRenderer { Name = "WaterRenderer" };
        AddChild(waterNode);
        waterNode.Configure(centre, size, waterY);

        GD.Print($"[Water] cell has water → plane configured at Y={waterY:F1} " +
                 $"area={TargetAreaId} size={size:F0}u centre=({centre.X:F0},{centre.Y:F1},{centre.Z:F0}).");
    }

    /// <summary>First direct child of <paramref name="parent" /> assignable to T, or null.</summary>
    private static T? FindDirectChildOfType<T>(Node parent) where T : Node
    {
        foreach (var child in parent.GetChildren())
            if (child is T match)
                return match;
        return null;
    }

    // -------------------------------------------------------------------------
    // BUD scene loading — ArrayMesh path (no GltfDocument)
    // -------------------------------------------------------------------------

    private void LoadAndSpawnBudScene()
    {
        if (Assets is null) return;

        // Get the BUILDING DATAFILE path from the .map descriptor.
        (string? _, string? budPath) = (null, null);
        try
        {
            (_, budPath) = Assets.LoadMapDatafilePaths(TargetAreaId, TargetMapX, TargetMapZ);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] LoadMapDatafilePaths failed: {ex.Message}");
            return;
        }

        if (budPath is null)
        {
            GD.Print("[RealWorldRenderer] No BUILDING section in .map — skipping BUD scene.");
            return;
        }

        GD.Print($"[RealWorldRenderer] Loading BUD scene: {budPath}");

        BudScene? scene = null;
        try
        {
            scene = Assets.LoadBud(budPath);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] LoadBud failed: {ex.Message}");
            return;
        }

        if (scene is null || scene.Objects.Length == 0)
        {
            GD.Print($"[RealWorldRenderer] BUD scene empty for: {budPath}");
            return;
        }

        GD.Print($"[RealWorldRenderer] BUD scene parsed: {scene.Objects.Length} objects — building ArrayMesh.");

        // Build ArrayMesh directly via BudMeshBuilder (NO GltfDocument).
        // BUD coordinates are absolute world-space (no cell-relative offset needed).
        // spec: Docs/RE/formats/terrain_scene.md §Coordinate system — "positions are pre-baked
        //       into absolute world-space": CONFIRMED.
        Node3D budRoot;
        try
        {
            // Wire a texture resolver for BUD objects: maps a 1-based tex_id to a real ImageTexture
            // via the .map BUILDING section's TEXTURES list and the bgtexture pool (same two-hop
            // chain as the terrain). spec: Docs/RE/formats/terrain.md §3.5 + §4.2. CONFIRMED.
            var budTexCache = new Dictionary<uint, ImageTexture?>();

            Func<uint, ImageTexture?> budTexResolver = texId =>
            {
                if (budTexCache.TryGetValue(texId, out var cached)) return cached;
                var tex = ResolveSectionTexture("BUILDING", (int)texId);
                budTexCache[texId] = tex;
                return tex;
            };

            budRoot = BudMeshBuilder.Build(scene, budTexResolver);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] BudMeshBuilder.Build failed: {ex.Message}");
            return;
        }

        budRoot.Name = "BudSceneNode";
        AddChild(budRoot);

        GD.Print(
            $"[RealWorldRenderer] BUD scene spawned: {scene.Objects.Length} objects (ArrayMesh, no GltfDocument).");
    }

    private void LoadAndSpawnCharacter()
    {
        if (Assets is null) return;

        // Resolve .skn path.
        string? sknPath = null;
        try
        {
            sknPath = SknVirtualPath ?? DiscoverFirstSknPath();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] DiscoverFirstSknPath failed: {ex.Message}");
            return;
        }

        if (sknPath is null)
        {
            GD.Print("[RealWorldRenderer] No .skn found — skipping character render.");
            return;
        }

        GD.Print($"[RealWorldRenderer] Loading .skn: {sknPath}");

        // Parse .skn directly (no GLB conversion needed — SkinnedCharacterBuilder works on SkinnedMesh).
        // spec: Docs/RE/formats/mesh.md §.skn.
        SkinnedMesh? skinnedMesh = null;
        try
        {
            var sknData = Assets.GetRaw(sknPath);
            if (sknData.IsEmpty)
            {
                GD.PrintErr($"[RealWorldRenderer] .skn file not found in VFS: {sknPath}");
                return;
            }

            skinnedMesh = SknParser.Parse(sknData);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] SknParser.Parse failed for '{sknPath}': {ex.Message}");
            return;
        }

        GD.Print($"[RealWorldRenderer] .skn parsed: '{skinnedMesh.Name}', " +
                 $"{skinnedMesh.FaceCount} faces, {skinnedMesh.Positions.Length} verts — building character node.");

        // Build a live character node (Skeleton3D + skinned mesh + optional AnimationPlayer).
        // The skeleton (.bnd) and animation (.mot) are optional — a missing one yields a static
        // pose. spec: Docs/RE/formats/mesh.md (.skn/.bnd); animation.md (.mot). NO GltfDocument.
        var skeleton = TryLoadSkeleton(skinnedMesh);
        var clip = TryLoadAnimation(skinnedMesh);

        Node3D charRoot;
        try
        {
            // Resolve the character's diffuse texture from skin.txt (mesh.IdA -> tex id -> PNG).
            // spec: Docs/RE/formats/mesh.md §.skn texture binding via data/char/skin.txt. CONFIRMED.
            var albedo = CharacterTextureResolver.Resolve(Assets, skinnedMesh);
            // Render the SKINNED + animated character via faithful CPU linear-blend skinning
            // (SkinnedCharacterNode). The legacy bind/inverse-bind/LBS pipeline is now recovered
            // (Docs/RE/specs/skinning.md), so the mesh deforms correctly and the idle .mot plays.
            // A single unified handedness conversion (world Z-negate) is applied to bones+verts+
            // keyframes, preserving the rest-pose cancellation that previously exploded the mesh.
            // spec: Docs/RE/specs/skinning.md §0 (cancellation), §8(b) (single conversion).
            SkinnedCharacterBuilder.ForceSkinned = true;
            charRoot = SkinnedCharacterBuilder.Build(skinnedMesh, skeleton, clip, albedo);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] SkinnedCharacterBuilder.Build failed: {ex.Message}");
            return;
        }

        // Place the character on the terrain in an OPEN spot, offset ~350 units toward -Z (in
        // front of the building cluster, which sits near the cell centre) so it is not spawned
        // inside a building. Y is lifted onto the (flat) terrain surface (~26).
        // spec: Docs/RE/formats/terrain.md §1.4 (world-space bounds); WorldCoordinates.ToGodot (negate Z).
        var legacyX = (TargetMapX - 10000) * 1024.0f + 512.0f;
        var legacyZ = (TargetMapZ - 10000) * 1024.0f + 512.0f;

        // The mesh is rendered SKINNED + animated (CPU LBS) in the single unified handedness
        // conversion; no corrective rotation is applied (the conversion handles handedness).
        // spec: Docs/RE/specs/skinning.md §8(b).
        charRoot.RotationDegrees = CharacterUprightRotationDeg;
        charRoot.Position = new Vector3(legacyX, 26f, -legacyZ - 350f);
        charRoot.Scale = Vector3.One * CharacterScale;
        charRoot.Name = "CharacterNode";
        AddChild(charRoot);

        GD.Print($"[RealWorldRenderer] Character spawned from '{sknPath}' " +
                 $"(skeleton={skeleton is not null}, anim={clip is not null}, scale={CharacterScale:F1}).");

        // Attach a player controller so the avatar can be moved (left-click to move / WASD).
        // Strictly visual/local for now (no game-rule authority).
        try
        {
            var playerController = new PlayerController { Name = "PlayerController" };
            AddChild(playerController);
            playerController.SetAvatar(charRoot);
            playerController.SetGroundY(26f);
            // Follow the terrain each frame: convert the avatar's Godot position to legacy world XZ
            // (worldZ = -godotZ) and sample the heightmap. Falls back to 26 until sectors stream in.
            playerController.GroundHeightFunc = gp => _terrainNode?.GetGroundHeight(gp.X, -gp.Z, 26f) ?? 26f;
            // Store reference so _Process can push TargetForCamera → CameraController each frame.
            _playerController = playerController;
            GD.Print("[RealWorldRenderer] PlayerController attached (left-click to move / WASD).");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] PlayerController attach failed: {ex.Message}");
        }
    }

    /// <summary>
    ///     Attempts to load the character's <c>.bnd</c> skeleton. The <c>.skn</c> <c>id_b</c> matches
    ///     the <c>.bnd</c> <c>actor_id</c>; the skeleton path is <c>data/char/bind/g{id_b}.bnd</c>.
    ///     Returns null (static pose) when it cannot be resolved — never throws.
    ///     spec: Docs/RE/formats/mesh.md §.skn header (id_b → .bnd actor_id) + §.bnd actor_id. CONFIRMED.
    /// </summary>
    private Skeleton? TryLoadSkeleton(SkinnedMesh mesh)
    {
        if (Assets is null) return null;
        try
        {
            var bndPath = BndVirtualPath ?? $"data/char/bind/g{mesh.IdB}.bnd";
            if (mesh.IdB == 0 || !Assets.Contains(bndPath))
            {
                GD.Print($"[RealWorldRenderer] No skeleton for IdB={mesh.IdB} ({bndPath}) — static pose.");
                return null;
            }

            var data = Assets.GetRaw(bndPath);
            if (data.IsEmpty) return null;

            var skel = BndParser.Parse(data);
            GD.Print($"[RealWorldRenderer] Skeleton loaded: {bndPath} ({skel.Bones.Length} bones).");
            return skel;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] .bnd load failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    ///     Attempts to load the character's looped idle animation <c>.mot</c> via
    ///     <c>actormotion.txt</c>. Returns null (static rest pose) when none is resolved — never throws.
    ///     spec: Docs/RE/formats/animation.md §.mot; Docs/RE/formats/mesh.md §actormotion.txt. CONFIRMED.
    /// </summary>
    private AnimationClip? TryLoadAnimation(SkinnedMesh mesh)
    {
        if (Assets is null || mesh.IdB == 0) return null;
        try
        {
            var motPath = ResolveIdleMotPath(mesh.IdB);
            if (motPath is null || !Assets.Contains(motPath))
            {
                GD.Print($"[RealWorldRenderer] No idle .mot for IdB={mesh.IdB} — static rest pose.");
                return null;
            }

            var data = Assets.GetRaw(motPath);
            if (data.IsEmpty) return null;

            var clip = AnimationParser.Parse(data); // CS8600: Parse returns nullable
            GD.Print($"[RealWorldRenderer] Animation loaded: {motPath}.");
            return clip;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[RealWorldRenderer] .mot load failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    ///     Resolves the looped idle motion id for an actor class via <c>data/char/actormotion.txt</c>:
    ///     the row whose column 2 equals <paramref name="actorClassId" /> (= the skin/skeleton id_b);
    ///     the idle-peace motion id is column 15 (0-based), record offset +0x40.
    ///     IDB-confirmed operand-for-operand. Any read of cols[16] for idle is an off-by-one.
    ///     spec: Docs/RE/formats/actormotion.md — col15 (0-based) = idle motion id @ +0x40: IDB-CONFIRMED.
    ///     spec: Docs/RE/formats/animation.md — .mot idle resolution.
    ///     Returns <c>data/char/mot/g{id}.mot</c> or null.
    /// </summary>
    private string? ResolveIdleMotPath(uint actorClassId)
    {
        if (Assets is null) return null;
        const string tablePath = "data/char/actormotion.txt";
        if (!Assets.Contains(tablePath)) return null;

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var text = Encoding.GetEncoding(949).GetString(Assets.GetRaw(tablePath).Span);

        foreach (var rawLine in text.Split('\n'))
        {
            var cols = rawLine.Replace("\r", string.Empty).Split('\t');
            // Need at least 16 columns (indices 0..15) for the idle field at col15.
            // spec: Docs/RE/formats/actormotion.md — col15 = idle motion id (0-based); IDB-CONFIRMED.
            if (cols.Length <= 15) continue;
            if (!uint.TryParse(cols[2].Trim(), out var classId) || classId != actorClassId) continue;

            // col15 (0-based) = motion_ids_a[0] = idle-peace motion id @ record offset +0x40.
            // spec: Docs/RE/formats/actormotion.md — col15=+0x40=idle: IDB-CONFIRMED.
            var idle = cols[15].Trim();
            if (idle.Length == 0 || idle == "0") return null;
            return $"data/char/mot/g{idle}.mot";
        }

        return null;
    }

    private string? DiscoverFirstSknPath()
    {
        if (Assets is null) return null;

        // Prefer a real HUMANOID player-class skin (Musa class, IdB=1) over the spider/item props,
        // so the default avatar is a recognisable character. spec: Docs/RE/formats/mesh.md §.skn.
        var humanoid = CharacterTextureResolver.PickHumanoidPlayerSkin(Assets);
        if (humanoid is not null) return humanoid;

        // Fallback: other character body skins (data/char/skin/g{id}.skn) then item-prop skins.
        // spec: Docs/RE/formats/mesh.md §.skn — "data/char/skin/": CONFIRMED.
        string[] candidates =
        [
            "data/char/skin/g200002620.skn",
            "data/char/skin/g200003000.skn",
            "data/char/skin/g200002630.skn",
            // item-prop fallbacks (single-bone) if no character skin is present:
            "data/item/skin/gi213050001.skn"
        ];

        foreach (var candidate in candidates)
            if (Assets.Contains(candidate))
                return candidate;

        GD.Print("[RealWorldRenderer] None of the known .skn candidates found in VFS.");
        return null;
    }

    // -------------------------------------------------------------------------
    // Camera placement
    // -------------------------------------------------------------------------

    private void SpawnCamera()
    {
        // Compute the Godot-space centre of the resolved terrain cell.
        // spec: Docs/RE/formats/terrain.md §1.4 — worldX_min = (mapX-10000)×1024, cell size 1024. CONFIRMED.
        var centreX = (TargetMapX - 10000) * 1024.0f + 512.0f;
        var centreZ = (TargetMapZ - 10000) * 1024.0f + 512.0f;
        var godotZ = -centreZ; // negate Z: spec WorldCoordinates.ToGodot.
        // spec: WorldCoordinates.ToGodot — legacy Z negated to Godot Z. CONFIRMED.

        var cellCentre = new Vector3(centreX, 0f, godotZ);

        // Replace any existing static Camera3D with the spec-faithful CameraController.
        var existing = GetViewport()?.GetCamera3D();
        if (existing is not null && existing is not CameraController)
        {
            existing.GetParent()?.RemoveChild(existing);
            existing.QueueFree();
        }

        var cam = new CameraController { Name = "CameraController" };
        AddChild(cam);

        // Wire terrain ground-height delegate so the camera can do vertical collision.
        // The delegate accepts LEGACY world coordinates (legacyX, legacyZ).
        // spec: Docs/RE/specs/camera_movement.md §A.6 — terrain height clamp (Third only). CODE-CONFIRMED.
        if (_terrainNode is not null)
        {
            var terrainCapture = _terrainNode;
            cam.GroundHeightFunc = (lx, lz) => terrainCapture.GetGroundHeight(lx, lz);
        }

        cam.Configure(cellCentre, 1024f); // spec: terrain.md §1.4 — cell size 1024. CONFIRMED.
        cam.MakeCurrent();

        _cameraController = cam;

        GD.Print($"[RealWorldRenderer] CameraController spawned (spec-faithful Third-person orbit). " +
                 $"Cell centre ({centreX:F0}, 0, {godotZ:F0}). " +
                 "RMB=orbit, wheel=elevation, ESC=reset-to-Third, Tab=devFreeFly.");
    }
}