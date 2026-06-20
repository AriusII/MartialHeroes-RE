// World/PlayerAvatarResolver.cs
//
// Resolves and builds the IN-WORLD LOCAL PLAYER avatar as a skinned, idle-animated character node,
// reusing the EXACT same recovered chain NpcRenderer uses for mobs/NPCs and the same
// SkinnedCharacterBuilder / SkinnedCharacterNode pipeline. ZERO new skinning path; NO GltfDocument.
//
// The local player is a class, not a mob: the wire ServerClass IS the .skn header SkinClassId
// (id_b) ∈ {1,2,3,4} = Musa/Salsu(Jagaek)/Dosa/Monk → g{SkinClassId}.bnd. From the SkinClassId
// alone the full chain resolves:
//   - body .skn  : data/char/skinlist.txt scan → first entry whose parsed IdB == SkinClassId
//   - skeleton   : data/char/bind/g{SkinClassId}.bnd
//   - idle .mot  : data/char/actormotion.txt row whose col2 (skin_class) == SkinClassId →
//                  motion_ids_a[0] (col15, record +0x40) → data/char/mot/g{id}.mot
//   - albedo     : CharacterTextureResolver (skin.txt: mesh.IdA → tex id → PNG)
//   - build      : SkinnedCharacterBuilder.Build (skinned when the .bnd resolves; static otherwise)
//
// The col15 standing idle for the first human class (g101100001.mot) is STATIC DATA (0 animated
// tracks): a frozen STANDING HUMAN is FAITHFUL, not a defect — only mobs have genuinely animated
// idles. SkinnedCharacterBuilder/Setup auto-engages PlayStandingIdle, so playback is already on.
//
// The player avatar is built SELF-DRIVEN (externalDrive=false): SkinnedCharacterNode._Process
// advances it per frame, identical to RealWorldRenderer's demo avatar. NpcRenderer's ~10 Hz
// staggered scheduler is for the town of mobs only and does not apply here.
//
// spec: Docs/RE/specs/skinning.md §8(e) — skeleton = g{SkinClassId}.bnd for {1,2,3,4}; idle clip =
//       actormotion col2 == skin_class → motion_ids_a[0] (col15); skin .skn = skinlist entry whose
//       id_b == SkinClassId. §10 / §10.2 / §10.5 — col15 stand idle is static data, render faithfully.
// spec: Docs/RE/formats/actormotion.md — col2 = skin_class (int_a @ +0x04); motion_ids_a[0]
//       (+0x40, col15) = file-source idle reference.
// spec: CLAUDE.md "Recovered asset mappings" — skin (.skn IdA → skin.txt), skeleton (g{SkinClassId}.bnd),
//       idle motion via actormotion.txt.

using System.Text;
using Godot;
using MartialHeroes.Assets.Parsers.Character;
using MartialHeroes.Assets.Parsers.Mesh;
using MartialHeroes.Assets.Parsers.Mesh.Models;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.World;

/// <summary>
///     Builds the in-world local player avatar (skinned + idle-animated) from the player's class via the
///     recovered .skn → .bnd → idle .mot chain. Pure build-time helper: it never holds game state and
///     never throws (each step degrades to a simpler-but-visible result). Call on the Godot main thread.
///     spec: Docs/RE/specs/skinning.md §8(e) (idle-clip + skeleton resolution chain).
/// </summary>
internal static class PlayerAvatarResolver
{
    /// <summary>
    ///     Resolves the player's class → SkinClassId and builds the skinned avatar root node, or returns
    ///     <see langword="null" /> when the body .skn cannot be resolved (the caller keeps the placeholder).
    ///     <paramref name="serverClass" /> is the wire character class {1,2,3,4} = the .skn header
    ///     SkinClassId / id_b (Musa/Jagaek/Dosa/Seungnyeo). This identity (ServerClass == SkinClassId)
    ///     is the §8(e) skeleton-selection key used verbatim — no slot transform.
    ///     spec: Docs/RE/specs/skinning.md §8(e); CLAUDE.md "Recovered asset mappings".
    ///     spec: MartialHeroes.Shared.Kernel.Enums.CharacterClass — 1-based {1,2,3,4}.
    /// </summary>
    /// <param name="assets">Open VFS handle (shared with the rest of the world renderer).</param>
    /// <param name="serverClass">Wire character class == SkinClassId ∈ {1,2,3,4}.</param>
    public static Node3D? TryBuild(RealClientAssets assets, ushort serverClass)
    {
        // The wire class is the SkinClassId used verbatim as the §8(e) skeleton-selection key.
        // spec: Docs/RE/specs/skinning.md §8(e) — id_b (SkinClassId) is the pose-pool key, no transform.
        int skinClass = serverClass;
        if (skinClass <= 0)
        {
            GD.Print($"[PlayerAvatar] serverClass={serverClass} not a valid SkinClassId — keeping placeholder. " +
                     "spec: skinning.md §8(e).");
            return null;
        }

        // ── Step 1: body .skn — skinlist.txt scan for the entry whose parsed IdB == SkinClassId ──
        // spec: skinning.md §8(e) — body .skn = the skinlist entry whose id_b == SkinClassId.
        var sknPath = ResolveBodySknPath(assets, skinClass);
        if (sknPath is null)
        {
            GD.Print($"[PlayerAvatar] No .skn with IdB=={skinClass} in skinlist.txt — keeping placeholder. " +
                     "spec: skinning.md §8(e).");
            return null;
        }

        SkinnedMesh mesh;
        try
        {
            var sknData = assets.GetRaw(sknPath);
            if (sknData.IsEmpty)
            {
                GD.PrintErr($"[PlayerAvatar] .skn empty in VFS: {sknPath}");
                return null;
            }

            mesh = SknParser.Parse(sknData);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[PlayerAvatar] SknParser.Parse failed '{sknPath}': {ex.Message}");
            return null;
        }

        // ── Step 2: albedo texture (skin.txt: mesh.IdA → tex id → PNG) ──
        // spec: World/CharacterTextureResolver.cs — Resolve(assets, mesh). CONFIRMED.
        ImageTexture? albedo = null;
        try
        {
            albedo = CharacterTextureResolver.Resolve(assets, mesh);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[PlayerAvatar] Texture resolve failed for '{sknPath}': {ex.Message} — neutral material.");
        }

        // ── Step 3: skeleton — data/char/bind/g{SkinClassId}.bnd ──
        // spec: skinning.md §8(e) — g{SkinClassId}.bnd for {1,2,3,4} is the direct rule.
        var skeleton = TryLoadSkeleton(assets, skinClass);

        // ── Step 4: idle .mot — actormotion row col2 == skinClass → motion_ids_a[0] (col15) ──
        // spec: skinning.md §8(e); formats/actormotion.md — col2 = skin_class, motion_ids_a[0] = col15.
        var idleMotId = skeleton is not null ? ResolveIdleMotionId(assets, skinClass) : 0;
        var clip = skeleton is not null && idleMotId > 0
            ? TryLoadAnimation(assets, idleMotId)
            : null;

        // ── Step 5: build via SkinnedCharacterBuilder (skinned when .bnd present; static otherwise) ──
        // SELF-DRIVEN (externalDrive=false): the node self-ticks per frame via _Process, identical to
        // RealWorldRenderer's demo avatar. The town's ~10 Hz stagger scheduler (NpcRenderer) is mob-only.
        // Setup() auto-calls PlayStandingIdle, so the looping idle is engaged on build.
        // The pivot up-axis remap (UpAxisRemapDeg) is applied inside Build — IDENTICAL to NPCs, so the
        // player stands the same way every spawned actor does (see report: up-axis is §9 debugger-pending).
        // spec: skinning.md §8(b)/§7/§9 (single handedness conversion + importer-layer up-axis remap),
        //       §10.5 (engage the col15 idle, advance with real dt, loop at clip end).
        // spec: CLAUDE.md "Known Godot Pitfalls" — NEVER GltfDocument; build ArrayMesh directly.
        try
        {
            var root = SkinnedCharacterBuilder.Build(
                mesh, skeleton, clip, albedo,
                false,
                0f,
                out var lbs,
                $"local-player class={skinClass}");

            GD.Print($"[PlayerAvatar] Built class={skinClass} from '{sknPath}' " +
                     $"(skeleton={skeleton is not null}, idleMot={idleMotId}, " +
                     $"skinned={lbs is not null}, idlePlaying={lbs?.IsIdlePlaying ?? false}). " +
                     "spec: skinning.md §8(e).");
            return root;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[PlayerAvatar] SkinnedCharacterBuilder.Build failed '{sknPath}': {ex.Message}");
            return null;
        }
    }

    // -------------------------------------------------------------------------
    // Chain steps (mirror NpcRenderer's resolution exactly)
    // -------------------------------------------------------------------------

    /// <summary>
    ///     Scans <c>data/char/skinlist.txt</c> (CP949 bare filenames under <c>data/char/skin/</c>) for the
    ///     first <c>.skn</c> whose parsed header <c>IdB</c> == <paramref name="skinClass" />.
    ///     spec: skinning.md §8(e) — body .skn = the skinlist entry whose id_b == SkinClassId.
    ///     spec: Docs/RE/formats/mesh.md §.skn header — id_b at +4. CONFIRMED.
    /// </summary>
    private static string? ResolveBodySknPath(RealClientAssets assets, int skinClass)
    {
        const string listPath = "data/char/skinlist.txt";
        if (!assets.Contains(listPath)) return null;

        try
        {
            // spec: project brief — "ALL game text is CP949".
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var text = Encoding.GetEncoding(949).GetString(assets.GetRaw(listPath).Span);

            // skinlist.txt lines are bare filenames; the full path is "data/char/skin/" + filename.
            // spec: verified on real VFS (NpcRenderer.EnsureSkinClassMapLoaded) — bare filenames.
            const string skinDir = "data/char/skin/";
            foreach (var rawLine in text.Split('\n'))
            {
                var fname = rawLine.Trim('\r', '\n', ' ').ToLowerInvariant();
                if (fname.Length == 0 || !fname.EndsWith(".skn", StringComparison.Ordinal)) continue;

                var sknPath = skinDir + fname;
                if (!assets.Contains(sknPath)) continue;

                try
                {
                    var sknData = assets.GetRaw(sknPath);
                    if (sknData.IsEmpty) continue;
                    var mesh = SknParser.Parse(sknData);
                    if ((int)mesh.IdB == skinClass) return sknPath; // first match wins (§8(e))
                }
                catch
                {
                    // Skip unparseable entries; keep scanning.
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[PlayerAvatar] skinlist.txt scan failed: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    ///     Loads <c>data/char/bind/g{SkinClassId}.bnd</c>, or null (→ static rest pose) when absent.
    ///     spec: skinning.md §8(e) — g{SkinClassId}.bnd for {1,2,3,4}. CONFIRMED.
    /// </summary>
    private static Skeleton? TryLoadSkeleton(RealClientAssets assets, int skinClass)
    {
        var bndPath = $"data/char/bind/g{skinClass}.bnd";
        if (!assets.Contains(bndPath))
        {
            GD.Print($"[PlayerAvatar] No skeleton g{skinClass}.bnd — static rest pose. spec: skinning.md §8(e).");
            return null;
        }

        try
        {
            var data = assets.GetRaw(bndPath);
            if (data.IsEmpty) return null;
            return BndParser.Parse(data);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[PlayerAvatar] .bnd load failed '{bndPath}': {ex.Message}");
            return null;
        }
    }

    /// <summary>
    ///     Finds the <c>actormotion.txt</c> row whose <c>col2</c> (skin_class) == <paramref name="skinClass" />
    ///     and returns its <c>motion_ids_a[0]</c> (col15, record +0x40) — the file-source idle reference.
    ///     Returns 0 when no row matches or the slot is empty.
    ///     spec: skinning.md §8(e); formats/actormotion.md — col2 = skin_class, motion_ids_a[0] = col15.
    /// </summary>
    private static int ResolveIdleMotionId(RealClientAssets assets, int skinClass)
    {
        const string tablePath = "data/char/actormotion.txt";
        if (!assets.Contains(tablePath)) return 0;

        try
        {
            var catalogue = ActormotionParser.Parse(assets.GetRaw(tablePath));
            foreach (var entry in catalogue.AllEntries)
            {
                // col2 == skin_class is the player idle key (the player IS a class, not a mob_id).
                // spec: skinning.md §8(e) — actormotion col2 == id_b → motion_ids_a[0].
                if (entry.SkinClassId != skinClass) continue;
                // col15 = motion_ids_a[0]. Zero = empty slot. spec: formats/actormotion.md §col15.
                return entry.MotionIds.Length > 0 ? entry.MotionIds[0] : 0;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[PlayerAvatar] actormotion.txt parse failed: {ex.Message}");
        }

        return 0;
    }

    /// <summary>
    ///     Loads the idle <c>.mot</c> at <c>data/char/mot/g{idleMotId}.mot</c>, or null (→ rest pose) when
    ///     absent / unparseable. Never throws.
    ///     spec: skinning.md §8(e); formats/actormotion.md — motion_ids_a[0] → data/char/mot/g{id}.mot.
    /// </summary>
    private static AnimationClip? TryLoadAnimation(RealClientAssets assets, int idleMotId)
    {
        var motPath = $"data/char/mot/g{idleMotId}.mot";
        if (!assets.Contains(motPath)) return null;

        try
        {
            var data = assets.GetRaw(motPath);
            if (data.IsEmpty) return null;
            return AnimationParser.Parse(data);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[PlayerAvatar] .mot load failed '{motPath}': {ex.Message}");
            return null;
        }
    }
}