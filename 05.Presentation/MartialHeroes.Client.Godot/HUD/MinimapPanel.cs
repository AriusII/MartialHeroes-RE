using Godot;
using MartialHeroes.Assets.Parsers.Models;
using MartialHeroes.Client.Application.Hud;
using MartialHeroes.Client.Domain.Actors;
using MartialHeroes.Client.Godot.Adapters;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.World;

namespace MartialHeroes.Client.Godot.HUD;

/// <summary>
/// HUD radar: a player-centred 133×133 minimap blip overlay, always on.
///
/// PASSIVE: reads actor positions from <see cref="ActorRegistry"/> (the Godot-space
/// positions of live <see cref="VisualActor"/> nodes) and projects them onto the radar
/// body using the BYTE-VERIFIED world→minimap transform:
/// <codela partie DEBUGG
///   rel.X = actorLegacyX − playerLegacyX
///   rel.Z = actorLegacyZ − playerLegacyZ
///   px = rel.X × 0.125 + 66.5
///   py = rel.Z × 0.125 + 66.5
/// </code>
/// Scale 0.125 = 1:8; origin 66.5 = centre of the 133×133 body.
/// Blips whose (px, py) land outside [0, 133] are culled.
/// spec: Docs/RE/specs/minimap.md §2.2 — "BYTE-VERIFIED from the binary's immediate operands."
///
/// World geometry negates Z (spec: CLAUDE.md; spec: Docs/RE/formats/mesh.md §Vertex list).
/// Godot stores world positions with Z already negated relative to the legacy coordinate.
/// The projection uses LEGACY XZ (negate the Godot Z back) so the direction is correct.
/// spec: Docs/RE/specs/minimap.md §2.2 — "rel is measured against the last-packet position".
///
/// Background: the per-cell bitmap tiles (data/effect/map/d*.bmp) are ABSENT from the VFS
/// (52 zones, 0 radar bitmaps found). We render a plain translucent background.
/// spec: Docs/RE/specs/minimap.md §3.2 — "tiles absent from VFS; radar blank-with-blips".
///
/// Blip actor classification mirrors the spec §3.4 class table; without the UI texture
/// manifest we use coloured rectangles instead of sprite icons (the id→DDS mapping is gated
/// on the manifest — spec: Docs/RE/specs/minimap.md §8 Open Question 1).
///
/// Bind contract: call <see cref="Bind"/> with an <see cref="IHudEventHub"/> to subscribe to
/// position updates. When no hub is bound (demo / headless mode) the panel shows a DEMO state
/// with a static player dot at centre and four demo blips.
///
/// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — strictly passive HUD.
/// spec: Docs/RE/specs/minimap.md §2 (projection), §3 (blips + chrome), §6 (VFS ground truth).
/// </summary>
public sealed partial class MinimapPanel : Control
{
    // ── Projection constants — BYTE-VERIFIED from the binary ────────────────────────────────
    // spec: Docs/RE/specs/minimap.md §2.2 — "0.125 and 66.5 are byte-verified from the binary".

    /// <summary>Minimap pixels per world unit. Scale 1:8. spec: minimap.md §2.2 BYTE-VERIFIED.</summary>
    private const float BlipScale = 0.125f; // spec: Docs/RE/specs/minimap.md §2.2 BYTE-VERIFIED

    /// <summary>Pixel origin of the local player (centre of 133×133 body). spec: minimap.md §2.2 BYTE-VERIFIED.</summary>
    private const float BlipOrigin = 66.5f; // spec: Docs/RE/specs/minimap.md §2.2 BYTE-VERIFIED

    // ── Widget geometry — CODE-CONFIRMED ─────────────────────────────────────────────────────
    // spec: Docs/RE/specs/minimap.md §2.1 — "135 px wide; map body 133×133 px". CODE-CONFIRMED.

    private const float MapBodySize = 133f; // spec: Docs/RE/specs/minimap.md §2.1 CODE-CONFIRMED
    private const float TitleBarH = 18f; // approximate; chrome height not byte-confirmed (PLAUSIBLE)
    private const float FooterH = 18f; // approximate; PLAUSIBLE
    private const float PanelW = 135f; // spec: Docs/RE/specs/minimap.md §2.1 — "135 px wide" CODE-CONFIRMED
    private const float PanelH = MapBodySize + TitleBarH + FooterH;

    // ── Blip sizes — from spec §3.3 CODE-CONFIRMED pixel sizes ───────────────────────────────
    // spec: Docs/RE/specs/minimap.md §3.3 — element sizes: 4×4 generic; 16×16 player arrow; 10×10 party.
    private const float GenericBlipSize = 4f; // spec: minimap.md §3.3 CODE-CONFIRMED
    private const float PlayerArrowSize = 16f; // spec: minimap.md §3.3 CODE-CONFIRMED
    private const float PartyBlipSize = 10f; // spec: minimap.md §3.3 CODE-CONFIRMED

    // Anchor nudge: the local-player arrow hotspot is at (−6, −6) before rotation.
    // spec: Docs/RE/specs/minimap.md §3.5 — "translates by (−6, −6) to centre the 16×16 sprite".
    private const float ArrowHotspotNudge = -6f; // spec: minimap.md §3.5 CODE-CONFIRMED

    // Per-blip vertical anchor nudges (sprite-anchor offsets, not part of §2.2 transform).
    // spec: Docs/RE/specs/minimap.md §3.5 — "generic blip +14 px in Y; party blip +11; GPS (−8,+8); player arrow (+1,+16)".
    private const float GenericBlipNudgeY = 14f; // spec: minimap.md §3.5 CODE-CONFIRMED
    private const float PartyBlipNudgeY = 11f; // spec: minimap.md §3.5 CODE-CONFIRMED
    private const float PlayerArrowNudgeY = 16f; // spec: minimap.md §3.5 CODE-CONFIRMED

    // ── Blip colours (coloured rectangles standing in for sprite icons) ───────────────────────
    // The actual art comes via texture-group ids 13/30/50–58/75 — gated on VFS UI texture manifest.
    // spec: Docs/RE/specs/minimap.md §3.3 / §3.4 — texture-group ids, roles, and class conditions.
    // Until the manifest is parsed we use distinguishable solid colours as placeholders.

    // Local player arrow — white.
    private static readonly Color PlayerColor = new(1f, 1f, 1f, 1f);

    // Party/same-faction player — cyan. spec: minimap.md §3.4 texture-group 53.
    private static readonly Color PartyPlayerColor = new(0.2f, 1f, 1f, 1f);

    // Enemy player — red. spec: minimap.md §3.4 texture-group 55.
    private static readonly Color EnemyPlayerColor = new(1f, 0.2f, 0.2f, 1f);

    // NPC (default) — green. spec: minimap.md §3.4 texture-group 51.
    private static readonly Color NpcColor = new(0.2f, 1f, 0.3f, 1f);

    // Mob (default) — orange. spec: minimap.md §3.4 texture-group 50.
    private static readonly Color MobColor = new(1f, 0.6f, 0.1f, 1f);

    // ── Demo blip data (used when no ActorRegistry / hub is bound) ────────────────────────────

    private static readonly (float RelX, float RelZ, EntitySort Sort)[] DemoBlips =
    {
        (200f, 0f, EntitySort.NonPlayerCharacter), // NPC due East
        (-150f, 100f, EntitySort.Monster), // mob SW
        (0f, -250f, EntitySort.PlayerCharacter), // party player North
        (350f, -200f, EntitySort.Monster), // mob far NE
    };

    // ── Child nodes — built in BuildUi ───────────────────────────────────────────────────────

    private Panel _background = null!; // translucent dark panel
    private Control _blipRoot = null!; // marker overlay — holds one ColorRect per blip
    private Label _titleLabel = null!; // minimap window title / zone name
    private Label _coordLabel = null!; // footer: X, Z coords
    private Label _areaLabel = null!; // footer: area state caption

    // ── Blip pool (fixed-size, created once in _Ready) ───────────────────────────────────────────
    // Avoids per-frame heap allocation from GetChildren() + new ColorRect construction.

    /// <summary>Maximum number of blips that can be rendered in a single frame.</summary>
    private const int MaxBlips = 64;

    /// <summary>Fixed pool of <see cref="ColorRect"/> nodes; created once in <see cref="_Ready"/>, never reallocated.</summary>
    private readonly ColorRect[] _blipPool = new ColorRect[MaxBlips];

    // ── Drag state (view-only) ────────────────────────────────────────────────────────────────

    private bool _dragging;
    private Vector2 _dragOffset;
    private bool _collapsed;

    // ── Live state ────────────────────────────────────────────────────────────────────────────

    // The IHudEventHub — assigned via Bind(); null in demo mode.
    private IHudEventHub? _hub;

    // References to sibling nodes (assigned via Bind()).
    private ActorRegistry? _actorRegistry;

    // The local-player VisualActor key — tracked by our own actor-spawn watcher.
    private ActorKey _playerKey;
    private bool _hasPlayer;

    // Last-known player legacy world position (for projection + footer coord label).
    // We store LEGACY XZ because that is what the minimap projection uses.
    // Converting from Godot: legacyX = godotX, legacyZ = -godotZ.
    // spec: Docs/RE/specs/minimap.md §2.2 — "rel = actorWorldXZ − localPlayerWorldXZ".
    // spec: CLAUDE.md — "World geometry negates Z": Godot.z = -legacy.z.
    private float _playerLegacyX;
    private float _playerLegacyZ;
    private float _playerFacingRad; // heading in radians — used to rotate the player arrow

    // Zone catalog — borrowed from ClientContext.Instance (shared handle; this panel owns nothing).
    private ZoneCatalog? _zoneCatalog;
    private string _zoneName = string.Empty;
    private MapZoneRecord? _currentZone;

    // Coord label cache: only rebuild the string when the integer display values change.
    private int _lastDisplayX = int.MinValue;
    private int _lastDisplayZ = int.MinValue;

    // ── Initialisation ────────────────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        GD.Print("[MinimapPanel] _Ready start");
        try
        {
            BuildUi();
            BuildBlipPool();

            // Borrow the shared ZoneCatalog from ClientContext — do NOT open a new VFS handle here.
            // ClientContext owns the handle and its lifetime; we hold only a reference.
            // If ClientContext or its ZoneCatalog is unavailable (offline/headless), fall back gracefully.
            try
            {
                var ctx = GetNode<ClientContext>("/root/ClientContext");
                _zoneCatalog = ctx?.ZoneCatalog; // may be null if ctx is offline
                if (_zoneCatalog is not null)
                    GD.Print("[MinimapPanel] ZoneCatalog borrowed from ClientContext (shared handle).");
                else
                    GD.Print("[MinimapPanel] ZoneCatalog unavailable (ClientContext offline) — zone names suppressed.");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[MinimapPanel] Could not reach ClientContext for ZoneCatalog: {ex.Message}");
                _zoneCatalog = null; // offline fallback — no zone captions
            }

            // In demo mode (no Bind() call yet), show a static demo state.
            ShowDemo();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MinimapPanel] _Ready failed: {ex.Message}");
        }
    }

    public override void _ExitTree()
    {
        // This panel borrows the shared ZoneCatalog from ClientContext and owns no VFS handle.
        // Nothing to dispose here — lifetime is managed by ClientContext._ExitTree.
    }

    // ── Public API ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Wires the minimap to the live game state. Call this after the hub + registry are ready
    /// (from GameLoop._Ready, analogous to how <see cref="GameHud.Initialise"/> is called).
    ///
    /// Once bound, the minimap redraws each frame from the live <see cref="ActorRegistry"/>.
    /// Before Bind() the panel shows a static demo state.
    /// </summary>
    /// <param name="hub">The Application event hub (used to subscribe to position changes).</param>
    /// <param name="actorRegistry">The live actor registry providing VisualActor node positions.</param>
    public void Bind(IHudEventHub hub, ActorRegistry actorRegistry)
    {
        ArgumentNullException.ThrowIfNull(hub);
        ArgumentNullException.ThrowIfNull(actorRegistry);
        _hub = hub;
        _actorRegistry = actorRegistry;

        GD.Print("[MinimapPanel] Bound to IHudEventHub + ActorRegistry. Live radar active.");
    }

    /// <summary>
    /// Notifies the minimap that an actor was spawned. Call from GameLoop after
    /// ActorRegistry.OnActorSpawned — mirrors the <see cref="GameHud.OnActorSpawned"/> pattern.
    ///
    /// Used to identify the local player's ActorKey for the projection reference position.
    /// </summary>
    public void OnActorSpawned(Client.Application.Events.ActorSpawnedEvent evt)
    {
        if (!_hasPlayer && evt.Key.Sort == EntitySort.PlayerCharacter)
        {
            _hasPlayer = true;
            _playerKey = evt.Key;
            // Convert Q16.16 → float at the presentation boundary.
            // spec: Vector3Fixed.ToVector3Float() — presentation boundary only.
            var (fx, _, fz) = evt.Position.ToVector3Float();
            // Store LEGACY XZ — Godot negates Z, so legacyZ = -godotZ.
            // spec: CLAUDE.md — "World geometry negates Z": legacyZ = -godotZ.
            _playerLegacyX = fx;
            _playerLegacyZ = fz; // this is the LEGACY Z (pre-negate), matching the projection
            UpdateZoneName();
        }
    }

    /// <summary>
    /// Notifies the minimap that an actor moved. Used to update the player's reference position.
    /// Call from GameLoop after ActorRegistry.OnActorMoved.
    /// </summary>
    public void OnActorMoved(Client.Application.Events.ActorMovedEvent evt)
    {
        if (!_hasPlayer || evt.Key != _playerKey) return;
        var (fx, _, fz) = evt.MoveTarget.ToVector3Float();
        _playerLegacyX = fx;
        _playerLegacyZ = fz;
        UpdateZoneName();
    }

    // ── Godot lifecycle ───────────────────────────────────────────────────────────────────────

    public override void _Process(double delta)
    {
        if (_collapsed) return;

        // Update coordinate footer only when the integer display value changes (FIX 3: zero alloc when stationary).
        // spec: Docs/RE/specs/minimap.md §3.6 CODE-CONFIRMED — integer legacy coords displayed.
        if (_coordLabel is not null)
        {
            int displayX = (int)_playerLegacyX;
            int displayZ = (int)_playerLegacyZ;
            if (displayX != _lastDisplayX || displayZ != _lastDisplayZ)
            {
                _lastDisplayX = displayX;
                _lastDisplayZ = displayZ;
                _coordLabel.Text = $"X:{displayX}  Z:{displayZ}";
            }
        }

        // Redraw blips.
        RedrawBlips();
    }

    public override void _Input(InputEvent ev)
    {
        // Drag — left-mouse-button on the panel.
        if (ev is InputEventMouseButton mb)
        {
            if (mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                if (GetRect().HasPoint(mb.Position))
                {
                    _dragging = true;
                    _dragOffset = mb.Position - GlobalPosition;
                    GetViewport().SetInputAsHandled();
                }
            }
            else if (!mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                _dragging = false;
            }
        }

        if (_dragging && ev is InputEventMouseMotion motion)
        {
            GlobalPosition = motion.Position - _dragOffset;
        }
    }

    // ── UI construction ───────────────────────────────────────────────────────────────────────

    private void BuildUi()
    {
        // Anchor: top-right of the viewport (matching the common minimap corner placement).
        AnchorLeft = 1f;
        AnchorTop = 0f;
        AnchorRight = 1f;
        AnchorBottom = 0f;
        OffsetLeft = -PanelW - 8f;
        OffsetTop = 8f;
        OffsetRight = -8f;
        OffsetBottom = PanelH + 8f;
        MouseFilter = MouseFilterEnum.Pass;

        // ── Title bar ────────────────────────────────────────────────────────
        var titleBar = new Panel();
        titleBar.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        titleBar.Position = new Vector2(0, 0);
        titleBar.Size = new Vector2(PanelW, TitleBarH);
        AddChild(titleBar);

        _titleLabel = new Label
        {
            Text = "Map",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _titleLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        titleBar.AddChild(_titleLabel);

        // Collapse/expand button — spec: minimap.md §4 collapse/expand.
        var collapseBtn = new Button
        {
            Text = "─",
            CustomMinimumSize = new Vector2(16, TitleBarH),
        };
        collapseBtn.Position = new Vector2(PanelW - 18f, 0f);
        collapseBtn.Pressed += ToggleCollapse;
        AddChild(collapseBtn);

        // ── Radar body ───────────────────────────────────────────────────────
        _background = new Panel();
        _background.Position = new Vector2(0f, TitleBarH);
        _background.Size = new Vector2(MapBodySize, MapBodySize);

        // Translucent dark background — no per-cell bitmap tiles (spec §3.2 SAMPLE-VERIFIED absent).
        // spec: Docs/RE/specs/minimap.md §3.2 — "tiles ABSENT from VFS; render plain background".
        var bgStyle = new StyleBoxFlat();
        bgStyle.BgColor = new Color(0.05f, 0.05f, 0.1f, 0.7f);
        bgStyle.SetBorderWidthAll(1);
        bgStyle.BorderColor = new Color(0.4f, 0.4f, 0.5f, 0.9f);
        _background.AddThemeStyleboxOverride("panel", bgStyle);
        AddChild(_background);

        // Marker overlay covers the radar body.
        _blipRoot = new Control();
        _blipRoot.Position = new Vector2(0f, TitleBarH);
        _blipRoot.Size = new Vector2(MapBodySize, MapBodySize);
        _blipRoot.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_blipRoot);

        // ── Footer ───────────────────────────────────────────────────────────
        var footer = new Panel();
        footer.Position = new Vector2(0f, TitleBarH + MapBodySize);
        footer.Size = new Vector2(PanelW, FooterH);
        AddChild(footer);

        var footerBox = new VBoxContainer();
        footerBox.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        footer.AddChild(footerBox);

        _areaLabel = new Label
        {
            Text = string.Empty,
            HorizontalAlignment = HorizontalAlignment.Center,
            ClipText = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _areaLabel.AddThemeColorOverride("font_color", new Color(1f, 1f, 0.7f));
        footerBox.AddChild(_areaLabel);

        _coordLabel = new Label
        {
            Text = "X:0  Z:0",
            HorizontalAlignment = HorizontalAlignment.Center,
            ClipText = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _coordLabel.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.8f));
        footerBox.AddChild(_coordLabel);

        GD.Print($"[MinimapPanel] UI built. Body: {MapBodySize}×{MapBodySize} px. " +
                 "Projection scale=0.125, origin=66.5. " +
                 "spec: Docs/RE/specs/minimap.md §2.2 BYTE-VERIFIED.");
    }

    // ── Collapse / expand ─────────────────────────────────────────────────────────────────────

    private void ToggleCollapse()
    {
        // spec: Docs/RE/specs/minimap.md §4 — "collapse/expand button toggles between expanded and
        //       title-bar-only height, hides/shows the marker overlay". CODE-CONFIRMED.
        _collapsed = !_collapsed;
        _background.Visible = !_collapsed;
        _blipRoot.Visible = !_collapsed;

        if (_collapsed)
        {
            // Hide all pooled blips on collapse (spec: minimap.md §4 "on collapse clears the tile cache").
            // Iterate the array by index — no GetChildren() allocation.
            for (int i = 0; i < _blipPool.Length; i++)
                _blipPool[i].Visible = false;
            GD.Print("[MinimapPanel] Collapsed — blip pool hidden.");
        }
        else
        {
            GD.Print("[MinimapPanel] Expanded.");
        }
    }

    // ── Demo state ────────────────────────────────────────────────────────────────────────────

    private void ShowDemo()
    {
        _titleLabel.Text = "Minimap (DEMO)";
        _areaLabel.Text = "—";
        _coordLabel.Text = "X:0  Z:0";
    }

    // ── Blip pool creation ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates all <see cref="MaxBlips"/> pooled <see cref="ColorRect"/> nodes once in _Ready
    /// and adds them as children of <see cref="_blipRoot"/> — zero per-frame heap allocation.
    /// Each frame <see cref="RedrawBlips"/> sets Visible=false on all, then enables only the live ones.
    /// </summary>
    private void BuildBlipPool()
    {
        for (int i = 0; i < MaxBlips; i++)
        {
            var rect = new ColorRect
            {
                Visible = false,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            _blipRoot.AddChild(rect);
            _blipPool[i] = rect;
        }
    }

    // ── Blip rendering ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Redraws all blips each frame using the pre-allocated pool.
    ///
    /// Zero per-frame heap allocation: all <see cref="ColorRect"/> nodes are created once in
    /// <see cref="BuildBlipPool"/>; this method only mutates their properties by index.
    ///
    /// spec: Docs/RE/specs/minimap.md §3.4 — "each frame the radar walks the active actor list".
    /// spec: Docs/RE/specs/minimap.md §2.2 — projection is applied per actor per frame.
    /// </summary>
    private void RedrawBlips()
    {
        // First pass: hide all pooled rects by index (no GetChildren() allocation).
        for (int i = 0; i < _blipPool.Length; i++)
            _blipPool[i].Visible = false;

        // Pool cursor: next available pooled rect for this frame.
        int poolIndex = 0;

        if (_actorRegistry is null)
        {
            // Demo mode — draw static demo blips.
            DrawDemoBlips(ref poolIndex);
            return;
        }

        // Live mode — walk VisualActors in the registry.
        DrawLiveBlips(ref poolIndex);
    }

    private void DrawDemoBlips(ref int poolIndex)
    {
        // Player arrow at centre.
        DrawPlayerArrow(BlipOrigin, BlipOrigin, 0f, ref poolIndex);

        // Static demo blips.
        foreach (var (relX, relZ, sort) in DemoBlips)
        {
            float px = relX * BlipScale + BlipOrigin; // spec: minimap.md §2.2 BYTE-VERIFIED
            float py = relZ * BlipScale + BlipOrigin; // spec: minimap.md §2.2 BYTE-VERIFIED
            if (px < 0f || px > MapBodySize || py < 0f || py > MapBodySize) continue;
            DrawGenericBlip(px, py, sort, false, ref poolIndex);
        }
    }

    private void DrawLiveBlips(ref int poolIndex)
    {
        if (!_hasPlayer) return;

        // Walk all VisualActor children of the ActorRegistry node.
        // They are Node3D (CharacterBody3D) children; we read GlobalPosition.
        // GlobalPosition is in GODOT space: legacyZ = -godotZ.
        // spec: CLAUDE.md — "World geometry negates Z": Helpers/WorldCoordinates.ToGodot: z → -z.
        for (int i = 0; i < _actorRegistry!.GetChildCount(); i++)
        {
            if (_actorRegistry.GetChild(i) is not VisualActor actor) continue;

            // Convert Godot position → legacy XZ for the projection.
            // spec: Docs/RE/specs/minimap.md §2.2 — projection is in legacy XZ.
            // spec: Helpers/WorldCoordinates.cs — ToGodot negates Z → ToLegacy negates Godot.z.
            float actorLegacyX = actor.GlobalPosition.X;
            float actorLegacyZ = -actor.GlobalPosition.Z; // spec: WorldCoordinates — legacy Z = -godotZ

            // Compute relative vector from player.
            // spec: Docs/RE/specs/minimap.md §2.2 — "rel.X = actorWorldX − localPlayerWorldX".
            float relX = actorLegacyX - _playerLegacyX;
            float relZ = actorLegacyZ - _playerLegacyZ;

            // Apply the BYTE-VERIFIED projection.
            // spec: Docs/RE/specs/minimap.md §2.2 — "px = rel.X × 0.125 + 66.5; py = rel.Z × 0.125 + 66.5".
            float px = relX * BlipScale + BlipOrigin; // spec: minimap.md §2.2 BYTE-VERIFIED
            float py = relZ * BlipScale + BlipOrigin; // spec: minimap.md §2.2 BYTE-VERIFIED

            // Cull rule: discard actors outside [0, 133].
            // spec: Docs/RE/specs/minimap.md §2.2 — "draw a blip only if 0 ≤ px ≤ 133 AND 0 ≤ py ≤ 133".
            if (px < 0f || px > MapBodySize || py < 0f || py > MapBodySize) continue; // spec: minimap.md §2.2

            bool isLocalPlayer = actor.ActorKey == _playerKey;
            if (isLocalPlayer)
            {
                // Local player is always drawn last, at centre (66.5, 66.5) by spec §3.4.
                // We'll draw the local player separately after the loop.
                continue;
            }

            DrawGenericBlip(px, py, actor.ActorKey.Sort, false, ref poolIndex);
        }

        // Draw local player arrow at body centre — always last, always centred.
        // spec: Docs/RE/specs/minimap.md §3.4 — "Local player: drawn last, centred; texture-group id 13".
        DrawPlayerArrow(BlipOrigin, BlipOrigin, _playerFacingRad, ref poolIndex);
    }

    /// <summary>
    /// Configures the next pooled <see cref="ColorRect"/> as a generic blip (NPC/mob/other player)
    /// at radar pixel (px, py). Overflow beyond <see cref="MaxBlips"/> is silently capped.
    ///
    /// spec: Docs/RE/specs/minimap.md §3.4 — class byte → texture-group id table.
    /// spec: Docs/RE/specs/minimap.md §3.3 — "generic actor blip 4×4; party/lead 10×10".
    /// </summary>
    private void DrawGenericBlip(float px, float py, EntitySort sort, bool isParty, ref int poolIndex)
    {
        if (poolIndex >= MaxBlips) return; // cap overflow silently

        // Choose blip colour by actor class.
        // spec: Docs/RE/specs/minimap.md §3.4 — class-based texture-group selection.
        Color colour;
        float size;

        switch (sort)
        {
            case EntitySort.NonPlayerCharacter:
                // NPC (class 2): default texture-group 51 (green stand-in).
                // spec: Docs/RE/specs/minimap.md §3.4 — "NPC default → id 51".
                colour = NpcColor;
                size = GenericBlipSize; // spec: minimap.md §3.3 — "generic actor blip 4×4"
                break;

            case EntitySort.Monster:
                // Mob (class 3): default texture-group 50 (orange stand-in).
                // spec: Docs/RE/specs/minimap.md §3.4 — "Mob default → id 50".
                colour = MobColor;
                size = GenericBlipSize; // spec: minimap.md §3.3
                break;

            case EntitySort.PlayerCharacter:
                // Other player: party = 53 (cyan), enemy = 55 (red).
                // spec: Docs/RE/specs/minimap.md §3.4 — "Player same faction/party → id 53; enemy → id 55".
                // Without a party/faction oracle we default to party (same-faction).
                colour = isParty ? PartyPlayerColor : EnemyPlayerColor;
                size = isParty ? PartyBlipSize : GenericBlipSize; // spec: minimap.md §3.3
                break;

            default:
                colour = NpcColor;
                size = GenericBlipSize;
                break;
        }

        // Per-blip vertical anchor nudge (sprite-anchor offset, NOT part of §2.2 transform).
        // spec: Docs/RE/specs/minimap.md §3.5 — "generic blip +14 px in Y".
        float nudgeY = sort == EntitySort.PlayerCharacter && isParty
            ? PartyBlipNudgeY // spec: minimap.md §3.5 — "party blip +11"
            : GenericBlipNudgeY; // spec: minimap.md §3.5 — "generic blip +14"

        ColorRect rect = _blipPool[poolIndex++];
        rect.Color = colour;
        rect.Size = new Vector2(size, size);
        // Centre the blip on (px, py), then apply the vertical anchor nudge.
        rect.Position = new Vector2(px - size / 2f, py - size / 2f + nudgeY);
        rect.Visible = true;
    }

    /// <summary>
    /// Configures the next pooled <see cref="ColorRect"/> as the local-player arrow at the given
    /// radar pixel, rotated to the player's heading. Overflow beyond <see cref="MaxBlips"/> is silently capped.
    ///
    /// The arrow is a 16×16 blip centred at (px, py). The rotation is a Z-rotation applied to a
    /// canvas item, with a (−6, −6) hotspot centre nudge before rotation.
    /// spec: Docs/RE/specs/minimap.md §3.5 — "Z-rotation, translates by (−6, −6) to centre the 16×16 sprite".
    /// spec: Docs/RE/specs/minimap.md §3.5 — "handedness vs world Z-negation: UNVERIFIED; validate live".
    /// spec: Docs/RE/specs/minimap.md §3.4 — "Local player: always drawn last, centred; texture-group id 13".
    /// </summary>
    private void DrawPlayerArrow(float px, float py, float facingRad, ref int poolIndex)
    {
        if (poolIndex >= MaxBlips) return; // cap overflow silently

        // Per-blip anchor nudge for the player arrow.
        // spec: Docs/RE/specs/minimap.md §3.5 — "player arrow (+1, +16) before rotation".
        float nudgeY = PlayerArrowNudgeY; // spec: minimap.md §3.5

        ColorRect arrowRect = _blipPool[poolIndex++];
        arrowRect.Color = PlayerColor;
        arrowRect.Size = new Vector2(PlayerArrowSize, PlayerArrowSize);

        // Position: centre at (px, py) with the hotspot nudge (ArrowHotspotNudge = −6).
        // spec: Docs/RE/specs/minimap.md §3.5 — "translates by (−6, −6) to centre the 16×16 sprite on its hotspot".
        arrowRect.Position = new Vector2(px + ArrowHotspotNudge, py + ArrowHotspotNudge + nudgeY);
        arrowRect.PivotOffset = new Vector2(PlayerArrowSize / 2f, PlayerArrowSize / 2f);

        // Apply the Z-rotation for facing direction.
        // spec: Docs/RE/specs/minimap.md §3.5 — "builds a Z-rotation from the player's facing orientation".
        // Handedness vs world Z-negation: UNVERIFIED — validate against a running client.
        // spec: Docs/RE/specs/minimap.md §3.5 — "handedness caveat (UNVERIFIED)".
        arrowRect.Rotation = facingRad; // PLAUSIBLE — sign may need flipping after live validation

        arrowRect.Visible = true;
    }

    // ── Zone name update ──────────────────────────────────────────────────────────────────────

    private void UpdateZoneName()
    {
        if (_zoneCatalog is null) return;

        MapZoneRecord? zone = _zoneCatalog.GetZoneRecord(_playerLegacyX, _playerLegacyZ);
        _currentZone = zone;

        // Update title label with zone name (area name from mapsetting.scr).
        // spec: Docs/RE/specs/minimap.md §3.6 — "area name from the area-info table".
        // spec: Docs/RE/specs/minimap.md §6.3 — "zone display names authoritative in mapsetting.scr".
        if (zone is not null && !string.IsNullOrEmpty(zone.ZoneName))
        {
            _zoneName = zone.ZoneName;
            if (_areaLabel is not null) _areaLabel.Text = _zoneName;
            if (_titleLabel is not null) _titleLabel.Text = _zoneName;
        }
        else if (_areaLabel is not null)
        {
            _areaLabel.Text = string.Empty;
        }
    }
}