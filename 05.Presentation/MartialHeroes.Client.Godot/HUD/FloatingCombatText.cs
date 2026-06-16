using Godot;
using MartialHeroes.Client.Application.Hud;
using MartialHeroes.Client.Domain.Actors;
using MartialHeroes.Client.Godot.World;

namespace MartialHeroes.Client.Godot.HUD;

/// <summary>
/// Pooled overlay that spawns short-lived rising+fading combat numbers anchored to a target actor.
///
/// PASSIVE: subscribes to <see cref="IHudEventHub.CombatTexts"/>; drains each frame in _Process;
/// resolves the target's head position from <see cref="ActorRegistry"/> (world → screen) via the
/// viewport's active camera; renders per-kind colours. Zero game logic.
///
/// -- Combat spec references --
/// Kind → colour table: Docs/RE/specs/combat.md §12.3 (CODE-CONFIRMED)
///   kind 0 = red   (physical, normal)
///   kind 1 = red   (physical variant — blue in some sub-paths, but we map raw palette)
///   kind 2 = white (self / element-class 2, non-skill)
///   kind 3 = blue  (physical skill)
///   kind 4 = gold  (element-class 2, skill)
///   kind 5 = white-crit (element-class 2, non-skill, crit size)
///   kind 6 = green (element-class 5, skill — rising, delayed SFX path)
///   kind 7 = green (element-class 5, non-skill — crit-style size)
/// Crit emphasis: Docs/RE/specs/combat.md §12.3 — crit flag → larger font size.
/// Multi-hit: Docs/RE/specs/combat.md §12.4 — up to 7 chunks, each staggered by a time offset.
/// Lifetime: Docs/RE/specs/combat.md §12.3 — "alpha-fades over a ~1-second lifetime".
///
/// Pool design: <see cref="CombatLabel"/> instances are recycled into <see cref="_pool"/> when
/// their animation ends (via <see cref="CombatLabel.Reset"/>) so no per-hit heap allocation occurs
/// on the hot path. The pool has a hard cap (<see cref="PoolMaxSize"/>) to avoid unbounded growth
/// under extreme burst; oldest entries are expired first.
///
/// Camera-facing / world→screen: uses <see cref="Camera3D.UnprojectPosition"/> to map the actor's
/// head position in 3D world space to a 2D viewport coordinate. Actors are <see cref="VisualActor"/>
/// nodes (capsule height ≈ 1.8 m); head position is GlobalPosition + Y offset.
/// spec: Docs/RE/specs/game_loop.md §6 — VisualActor.GlobalPosition is the authoritative world pos.
///
/// Control hierarchy (procedural, overlays full viewport):
///   Control "FloatingCombatText"   (full-rect, MouseFilter=Ignore)
///     [dynamic] Label _active[]   — pulled from pool per hit
/// </summary>
public sealed partial class FloatingCombatText : Control
{
    // -------------------------------------------------------------------------
    // Kind → ARGB colour table
    // spec: Docs/RE/specs/combat.md §12.3 — kind → colour family (CODE-CONFIRMED).
    // -------------------------------------------------------------------------

    /// <summary>
    /// Per-kind base colour (RGBA). Index = animation kind in 0..7.
    /// spec: Docs/RE/specs/combat.md §12.3 (kind → colour family).
    /// </summary>
    private static readonly Color[] KindColours =
    [
        new Color(0.95f, 0.20f, 0.10f), // kind 0 — red (physical, normal)         spec: combat.md §12.3
        new Color(0.95f, 0.20f, 0.10f), // kind 1 — red variant                    spec: combat.md §12.3
        new Color(0.98f, 0.98f, 0.98f), // kind 2 — white (self / element-class 2) spec: combat.md §12.3
        new Color(0.30f, 0.55f, 0.95f), // kind 3 — blue (physical skill)           spec: combat.md §12.3
        new Color(0.98f, 0.78f, 0.10f), // kind 4 — gold (element-class 2, skill)   spec: combat.md §12.3
        new Color(0.98f, 0.98f, 0.98f), // kind 5 — white-crit (element-class 2)    spec: combat.md §12.3
        new Color(0.20f, 0.90f, 0.30f), // kind 6 — green (element-class 5, skill)  spec: combat.md §12.3
        new Color(0.20f, 0.90f, 0.30f), // kind 7 — green (element-class 5, crit)   spec: combat.md §12.3
    ];

    // -------------------------------------------------------------------------
    // Timing / animation constants
    // spec: Docs/RE/specs/combat.md §12.3 — "alpha-fades over a ~1-second lifetime".
    // spec: Docs/RE/specs/combat.md §12.4 — staggered multi-hit chunks.
    // -------------------------------------------------------------------------

    /// <summary>Total animation duration in seconds. spec: combat.md §12.3 (~1 s lifetime).</summary>
    private const double LifetimeSec = 1.0; // spec: Docs/RE/specs/combat.md §12.3

    /// <summary>Distance the label rises over its lifetime (pixels). PLAUSIBLE — pixel travel not specified.</summary>
    private const float RisePixels = 48f; // PLAUSIBLE

    /// <summary>
    /// Per-chunk stagger offset (seconds). spec: combat.md §12.4 — chunks are "delayed by a
    /// fraction of the motion start" so numbers stagger.  We use a fixed step. PLAUSIBLE (exact
    /// fraction not specified).
    /// </summary>
    private const double ChunkStaggerSec = 0.08; // PLAUSIBLE — combat.md §12.4

    /// <summary>
    /// Maximum multi-hit chunks per event. spec: Docs/RE/specs/combat.md §12.4 — "up to 7 chunks".
    /// </summary>
    private const int MaxChunks = 7; // spec: Docs/RE/specs/combat.md §12.4

    /// <summary>Font size for a normal hit. PLAUSIBLE.</summary>
    private const int FontSizeNormal = 18; // PLAUSIBLE

    /// <summary>Font size for a critical hit (larger emphasis). spec: combat.md §12.3 — "crit-style size".</summary>
    private const int FontSizeCrit = 26; // spec: Docs/RE/specs/combat.md §12.3 (crit-style size)

    /// <summary>
    /// Approximate height (in world units) from actor root to head centre (capsule height 1.8 m).
    /// spec: CLAUDE.md — "character skinning: capsule height 1.8 m" (VisualActor placeholder mesh).
    /// </summary>
    private const float ActorHeadOffsetY = 2.0f; // PLAUSIBLE — visual capsule height + margin

    // -------------------------------------------------------------------------
    // Pool
    // -------------------------------------------------------------------------

    /// <summary>Hard pool cap — prevents unbounded growth under burst. PLAUSIBLE.</summary>
    private const int PoolMaxSize = 64;

    private readonly Stack<CombatLabel> _pool = new(PoolMaxSize);
    private readonly List<CombatLabel> _active = new(PoolMaxSize);

    // -------------------------------------------------------------------------
    // Wiring
    // -------------------------------------------------------------------------

    private IHudEventHub? _hub;
    private ActorRegistry? _actorRegistry;

    // Cached viewport camera (re-resolved each frame if null — camera can be replaced).
    private Camera3D? _camera;

    // -------------------------------------------------------------------------
    // Godot lifecycle
    // -------------------------------------------------------------------------

    public override void _Ready()
    {
        GD.Print("[FloatingCombatText] _Ready start");
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // Overlay starts empty — synthetic demo numbers removed per no-invented-data discipline.
        // spec: layer-05 — no synthetic data without an explicit DEV_OFFLINE_FLOW guard.

        GD.Print("[FloatingCombatText] Ready. Pool overlay active (full-rect, mouse-ignore).");
    }

    /// <summary>
    /// Per-frame update:
    ///   1. Drain the combat-text channel (main thread only).
    ///   2. Tick all active labels and recycle completed ones.
    ///   3. Re-project world anchors to screen space (camera may have moved).
    ///
    /// spec: PRESERVATION_AND_ARCHITECTURE.md §05.Presentation — "drain Application channels on
    /// _Process; never touch a Control from a background channel-reader task".
    /// </summary>
    public override void _Process(double delta)
    {
        // 1. Drain the hub channel (non-blocking TryRead).
        if (_hub is not null)
        {
            while (_hub.CombatTexts.TryRead(out CombatTextEvent? evt))
                SpawnFromEvent(evt);
        }

        // 2. Tick active labels and return completed ones to the pool.
        // Walk backwards so we can remove without index shift.
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            CombatLabel lbl = _active[i];
            lbl.Tick(delta);

            if (lbl.IsComplete)
            {
                lbl.Reset(); // hides & detaches from tree
                _pool.Push(lbl);
                _active.RemoveAt(i);
            }
        }

        // 3. Update screen positions for still-active labels.
        ResolveCamera();
        if (_camera is not null && _actorRegistry is not null)
        {
            foreach (CombatLabel lbl in _active)
                RefreshScreenAnchor(lbl);
        }
    }

    // -------------------------------------------------------------------------
    // Public Bind surface
    // -------------------------------------------------------------------------

    /// <summary>
    /// Wires this overlay to the application-layer HUD event hub and the actor registry.
    /// Called once by the integration stage after both are created.
    /// </summary>
    public void Bind(IHudEventHub hub, ActorRegistry registry)
    {
        _hub = hub;
        _actorRegistry = registry;
        GD.Print("[FloatingCombatText] Bound to IHudEventHub + ActorRegistry.");
    }

    // -------------------------------------------------------------------------
    // Spawn helpers
    // -------------------------------------------------------------------------

    private void SpawnFromEvent(CombatTextEvent evt)
    {
        // spec: Docs/RE/specs/combat.md §12.3 — kind clamped to 0..7.
        byte kind = (byte)Math.Clamp(evt.Kind, CombatTextEvent.MinKind, CombatTextEvent.MaxKind);
        Color colour = KindColours[kind];
        bool isCrit = evt.IsCrit;

        // spec: Docs/RE/specs/combat.md §12.4 — split into up to 7 chunks.
        // We show the full value in chunk 0 and fractional chunks 1..N-1.
        // Chunk count of 1 for "MISS" / zero values.
        int chunkCount = evt.Value == 0 ? 1 : Math.Min(MaxChunks, Math.Abs(evt.Value));
        // Ensure at least 1 chunk — divide the absolute value across the chunks.
        if (chunkCount < 1) chunkCount = 1;

        // Resolve initial screen position from actor head.
        Vector2 screenOrigin = ResolveScreenOrigin(evt.TargetKey);

        for (int chunk = 0; chunk < chunkCount; chunk++)
        {
            // Per-chunk value share: each chunk shows (value / chunkCount) rounded,
            // with remainder added to chunk 0. PLAUSIBLE — spec does not pin per-chunk magnitude.
            // spec: Docs/RE/specs/combat.md §12.4 — "split into up to 7 multi-hit chunks".
            int chunkValue;
            if (chunkCount == 1)
            {
                chunkValue = evt.Value;
            }
            else
            {
                int share = evt.Value / chunkCount;
                int remainder = evt.Value % chunkCount;
                chunkValue = share + (chunk == 0 ? remainder : 0);
            }

            double delay = chunk * ChunkStaggerSec; // spec: combat.md §12.4 — staggered delay
            SpawnLabel(evt.TargetKey, chunkValue, colour, isCrit, screenOrigin, delay);
        }
    }

    private void SpawnLabel(
        ActorKey targetKey,
        int value,
        Color colour,
        bool isCrit,
        Vector2 screenOrigin,
        double delay)
    {
        // Evict oldest active entry when pool is at the cap.
        if (_pool.Count == 0 && _active.Count >= PoolMaxSize)
        {
            CombatLabel evicted = _active[0];
            evicted.Reset();
            _pool.Push(evicted);
            _active.RemoveAt(0);
        }

        CombatLabel lbl;
        if (_pool.Count > 0)
        {
            lbl = _pool.Pop();
        }
        else
        {
            lbl = new CombatLabel();
            AddChild(lbl);
        }

        // Slight horizontal scatter per label so multi-hit numbers don't stack exactly.
        // PLAUSIBLE — exact spread not in spec.
        float xScatter = (float)(GD.Randf() - 0.5f) * 16f; // PLAUSIBLE

        lbl.Initialise(
            targetKey,
            value,
            colour,
            isCrit,
            screenOrigin + new Vector2(xScatter, 0f),
            delay);

        _active.Add(lbl);
    }

    // -------------------------------------------------------------------------
    // World→screen anchor resolution
    // -------------------------------------------------------------------------

    private void ResolveCamera()
    {
        if (_camera is not null && global::Godot.GodotObject.IsInstanceValid(_camera)) return;
        _camera = GetViewport()?.GetCamera3D();
    }

    private Vector2 ResolveScreenOrigin(ActorKey key)
    {
        if (_actorRegistry is not null && _camera is not null)
        {
            VisualActor? actor = _actorRegistry.TryGetActor(key);
            if (actor is not null && global::Godot.GodotObject.IsInstanceValid(actor))
            {
                Vector3 headPos = actor.GlobalPosition + new Vector3(0f, ActorHeadOffsetY, 0f);
                // Camera3D.UnprojectPosition: world pos → viewport 2D pixel coords.
                // spec: Godot Engine docs — Camera3D.UnprojectPosition.
                return _camera.UnprojectPosition(headPos);
            }
        }

        // Fallback: centre of viewport — shown when actor is not yet in the registry.
        Rect2 vp = GetViewport()?.GetVisibleRect() ?? new Rect2(0f, 0f, 1280f, 720f);
        return new Vector2(vp.Size.X * 0.5f, vp.Size.Y * 0.5f);
    }

    private void RefreshScreenAnchor(CombatLabel lbl)
    {
        if (!lbl.HasAnchorActor) return;
        Vector2 newOrigin = ResolveScreenOrigin(lbl.TargetKey);
        lbl.UpdateAnchor(newOrigin);
    }

    // -------------------------------------------------------------------------
    // DEMO state
    // -------------------------------------------------------------------------

    private void SpawnDemoEntry()
    {
        // Show a synthetic damage number so the overlay is testable in isolation.
        var demoKey = new ActorKey(ActorKey.UnassignedRawId, default);
        var origin = new Vector2(640f, 300f);
        SpawnLabel(demoKey, 1234, KindColours[0], false, origin, 0.0);
        SpawnLabel(demoKey, 567, KindColours[3], false, origin, ChunkStaggerSec);
        SpawnLabel(demoKey, 999, KindColours[4], true, origin, ChunkStaggerSec * 2);
        GD.Print("[FloatingCombatText] DEMO mode: no hub bound — showing placeholder combat numbers.");
    }

    // =========================================================================
    // Inner class: one pooled label that rises and fades
    // =========================================================================

    /// <summary>
    /// A single pooled combat number label. Manages its own rise+fade tween state
    /// so the outer overlay just ticks it each frame.
    ///
    /// PASSIVE: pure view state (position, alpha, elapsed time). No domain mutation.
    /// </summary>
    private sealed partial class CombatLabel : Label
    {
        // ---- Pool state ----

        /// <summary>True when the animation has finished — the outer class recycles this node.</summary>
        public bool IsComplete { get; private set; }

        /// <summary>
        /// True when this label tracks a real actor so <see cref="UpdateAnchor"/> is meaningful.
        /// False for DEMO / fallback labels with an unassigned key.
        /// </summary>
        public bool HasAnchorActor { get; private set; }

        /// <summary>The actor this number floats above (for anchor refresh each frame).</summary>
        public ActorKey TargetKey { get; private set; }

        // ---- Animation state ----

        private Vector2 _basePosition; // screen origin at spawn time
        private double _elapsed; // seconds since Initialise (including pre-delay)
        private double _delay; // pre-spawn delay (stagger, seconds)
        private bool _visible; // whether delay has expired yet

        // Cache original colour so we can modulate alpha without losing the hue.
        private Color _baseColour;

        // ---- Godot lifecycle ----

        public override void _Ready()
        {
            // Invisible until Initialise is called (pool state).
            Visible = false;
            AutowrapMode = TextServer.AutowrapMode.Off;
            MouseFilter = MouseFilterEnum.Ignore;
        }

        // ---- Pool API ----

        /// <summary>
        /// Re-activates this label from the pool. Resets all state without allocating.
        /// </summary>
        public void Initialise(
            ActorKey targetKey,
            int value,
            Color colour,
            bool isCrit,
            Vector2 screenOrigin,
            double delay)
        {
            TargetKey = targetKey;
            HasAnchorActor = targetKey.RawId != ActorKey.UnassignedRawId;

            // spec: Docs/RE/specs/combat.md §12.3 — crit-style size.
            int fontSize = isCrit ? FontSizeCrit : FontSizeNormal;
            AddThemeFontSizeOverride("font_size", fontSize);

            _baseColour = colour;
            AddThemeColorOverride("font_color", colour);

            // Bold for crits (emphasis). PLAUSIBLE.
            // spec: combat.md §12.3 — "crit-style size" (font size; weight is PLAUSIBLE).
            // We use a Label outline for readability regardless of crit.
            AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
            AddThemeConstantOverride("outline_size", 2);

            // Zero value → display "MISS" (spec: combat.md §12.3 — MISS / over-low outcomes shown).
            // spec: Docs/RE/specs/combat.md §12.4 — "MISS … from own graphic/glyph"; we use text as
            // a fallback (real glyph atlas not yet wired).
            Text = value == 0 ? "MISS" : value.ToString();

            _basePosition = screenOrigin;
            _elapsed = 0.0;
            _delay = delay;
            _visible = delay <= 0.0;
            IsComplete = false;

            // Position the label centred around the screen origin.
            Position = _basePosition - Size * 0.5f;
            Visible = _visible;
        }

        /// <summary>Hides and resets the label back to pool state. No allocation.</summary>
        public void Reset()
        {
            Visible = false;
            IsComplete = false;
            Text = "";
        }

        /// <summary>
        /// Advances the animation.  Call once per frame from
        /// <see cref="FloatingCombatText._Process(double)"/>.
        ///
        /// spec: Docs/RE/specs/combat.md §12.3 — "rises and alpha-fades over ~1 s".
        /// </summary>
        public void Tick(double delta)
        {
            _elapsed += delta;

            // Honour stagger delay.
            if (!_visible && _elapsed >= _delay)
            {
                _visible = true;
                Visible = true;
            }

            if (!_visible) return;

            double activeElapsed = _elapsed - _delay;
            if (activeElapsed >= LifetimeSec)
            {
                IsComplete = true;
                return;
            }

            // Progress in [0, 1].
            float t = (float)(activeElapsed / LifetimeSec);

            // Rise: lerp Y upward from base.
            // spec: combat.md §12.3 — number rises.
            float riseY = -RisePixels * t; // negative Y = up in Godot 2D
            Vector2 pos = _basePosition + new Vector2(0f, riseY);
            Position = pos - Size * 0.5f;

            // Alpha fade: full for first 30 % of lifetime, then fades out.
            // PLAUSIBLE — exact fade curve not specified; linear from 1→0 in last 70 %.
            // spec: combat.md §12.3 — "alpha-fades".
            float alpha;
            const float holdFraction = 0.3f; // PLAUSIBLE
            if (t < holdFraction)
                alpha = 1.0f;
            else
                alpha = 1.0f - (t - holdFraction) / (1.0f - holdFraction);

            Color c = _baseColour;
            c.A = Math.Clamp(alpha, 0f, 1f);
            AddThemeColorOverride("font_color", c);
        }

        /// <summary>
        /// Updates the screen-space anchor point (called each frame while the actor moves).
        /// </summary>
        public void UpdateAnchor(Vector2 newOrigin)
        {
            // We do not snap to the new origin instantly — keep the rise offset relative
            // to the original base so numbers don't jump with the actor.  Only update base
            // when anchor drift is large (actor teleported).
            float drift = (_basePosition - newOrigin).Length();
            if (drift > 80f) // PLAUSIBLE threshold
                _basePosition = newOrigin;
        }
    }
}