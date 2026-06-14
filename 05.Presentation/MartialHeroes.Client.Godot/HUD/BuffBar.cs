// HUD/BuffBar.cs
//
// 30-slot HUD buff bar. Clears and rebuilds all slots from a BuffStateEvent each refresh.
// Buff icons are resolved via BuffIconCatalog (stateicon.dds + buff_icon_position.xdb).
//
// Spec facts implemented:
//   - 30 icon slots total.
//     spec: Docs/RE/formats/misc_data.md §1.6 CODE-CONFIRMED — "buff bar has 30 icon slots".
//   - buff_id ≤ 80  → 23×23 px cell, flowing left-to-right counter.
//     spec: Docs/RE/formats/misc_data.md §1.6 CODE-CONFIRMED.
//   - buff_id > 80  → 25×25 px cell, fixed per-slot screen position.
//     spec: Docs/RE/formats/misc_data.md §1.6 CODE-CONFIRMED.
//   - Per-refresh reset: hide all 30 slots, then re-show only active non-zero ones.
//     spec: Docs/RE/formats/misc_data.md §1.6 CODE-CONFIRMED — "per-refresh reset".
//   - buff_id == 0 marks an empty slot (skipped).
//     spec: Docs/RE/formats/misc_data.md §1.6 / Docs/RE/packets/4-102_buff_state.yaml.
//   - Duration rendering NOT implemented: unit (ms vs s) CAPTURE-UNVERIFIED.
//     spec: Docs/RE/formats/misc_data.md §1.6 known unknowns ("duration rendering … not located").
//
// PASSIVE: zero game logic; subscribes to IHudEventHub.BuffStates and drains on _Process.
// All Control mutation on the main thread (drain in _Process, never in a background task).

using System.Threading.Channels;
using Godot;
using MartialHeroes.Client.Application.Hud;
using MartialHeroes.Client.Godot.Adapters;

namespace MartialHeroes.Client.Godot.HUD;

/// <summary>
/// The 30-slot HUD buff bar, displaying active buff/state icons from the server's 4/102 push.
///
/// <para><b>Slot layout</b></para>
/// Two icon classes share the 30 slots:
/// <list type="bullet">
///   <item>buff_id ≤ 80: 23×23 px cells laid left-to-right with a flowing counter.</item>
///   <item>buff_id &gt; 80: 25×25 px cells at fixed per-slot positions.</item>
/// </list>
/// spec: Docs/RE/formats/misc_data.md §1.6 CODE-CONFIRMED.
///
/// <para><b>Offline / demo mode</b></para>
/// When no hub is bound (VFS offline, no server), the bar shows a small demo label.
///
/// <para><b>Binding</b></para>
/// Call <see cref="Bind(IHudEventHub, BuffIconCatalog)"/> from the owning HUD or autoload to wire
/// the bar to the Application event stream; the drain happens in <see cref="_Process"/>.
/// </summary>
public sealed partial class BuffBar : Control
{
    // ─────────────────────────────────────────────────────────────────────────
    //  Constants
    // ─────────────────────────────────────────────────────────────────────────

    // 30 icon slots total.
    // spec: Docs/RE/formats/misc_data.md §1.6 CODE-CONFIRMED — "buff bar has 30 icon slots".
    private const int SlotCount = BuffStateEvent.SlotCount; // spec: misc_data.md §1.6

    // Spacing between flowing buff-class icons.
    private const int BuffSlotSpacing = 2;

    // ─────────────────────────────────────────────────────────────────────────
    //  View state (not domain state)
    // ─────────────────────────────────────────────────────────────────────────

    // Hub channel reader — drained in _Process.
    private ChannelReader<BuffStateEvent>? _buffStates;

    // The catalog used to resolve icon AtlasTextures.
    private BuffIconCatalog? _iconCatalog;

    // 30 TextureRect nodes — one per slot (built in _Ready).
    private readonly TextureRect[] _slots = new TextureRect[SlotCount];

    // Whether Bind() has been called.
    private bool _bound;

    // Demo label — shown when the bar is offline.
    private Label? _demoLabel;

    // ─────────────────────────────────────────────────────────────────────────
    //  Dev demo: placeholder slots shown when no real buff data arrives
    // ─────────────────────────────────────────────────────────────────────────

    // Number of dev-placeholder buff slots to show when offline.
    // DEV ONLY — behind a clear dev seam; never simulates game logic.
    private const int DevDemoSlotCount = 5;

    // Frames to wait before showing demo slots.
    // 30 frames ≈ 0.5s at 60 fps — enough for Bind() to be called, but short enough that
    // dev screenshots (300+ frame warmup) will always see the slots.
    private const int DevDemoDelayFrames = 30;

    private int _devDemoCountdown = DevDemoDelayFrames;

    // True once real buff data has been received from the hub.
    private bool _hasReceivedRealData;

    // ─────────────────────────────────────────────────────────────────────────
    //  Godot lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    public override void _Ready()
    {
        try
        {
            BuildUi();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[BuffBar] _Ready failed: {ex.Message}");
        }

        GD.Print("[BuffBar] Ready. Call Bind(hub, catalog) to wire to Application event stream.");
    }

    public override void _Process(double delta)
    {
        // Drain real buff-state events when bound.
        if (_buffStates is not null)
        {
            // Drain the latest-wins channel. ChannelReader.TryRead is allocation-free.
            // All Control mutation here is on the main thread (Godot requirement).
            while (_buffStates.TryRead(out BuffStateEvent? evt))
            {
                _hasReceivedRealData = true;
                ApplyBuffState(evt);
            }
        }

        // DEV ONLY: show placeholder buff slots after the delay, if no real data arrived.
        // This ensures the buff bar is visually rendered in world-mode dev screenshots.
        // spec: Docs/RE/specs/ui_hud_layout.md §2 — buff icons positioned per buff_icon_position.xdb.
        if (!_hasReceivedRealData && _devDemoCountdown > 0)
        {
            _devDemoCountdown--;
            if (_devDemoCountdown == 0)
                ApplyDevDemoSlots();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Public Bind surface
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Wires the buff bar to the HUD event hub and the buff icon catalog.
    /// Call this once from the owning HUD node after both are available.
    /// Safe to call multiple times; subsequent calls are no-ops.
    ///
    /// <para>When <paramref name="hub"/> is null the bar remains in offline/demo mode.</para>
    /// </summary>
    /// <param name="hub">The application HUD event hub exposing the BuffStates channel.</param>
    /// <param name="catalog">
    /// The buff icon catalog backed by stateicon.dds + buff_icon_position.xdb.
    /// Pass null to keep placeholder rendering.
    /// </param>
    public void Bind(IHudEventHub? hub, BuffIconCatalog? catalog)
    {
        if (_bound) return;
        _bound = true;

        _iconCatalog = catalog;

        if (hub is not null)
        {
            _buffStates = hub.BuffStates;
            GD.Print($"[BuffBar] Bound to IHudEventHub.BuffStates. " +
                     $"BuffIconCatalog entries: {catalog?.TableCount ?? 0}.");
        }
        else
        {
            GD.Print("[BuffBar] Bound with null hub — offline/demo mode.");
        }

        // Keep the demo label visible until real data or dev-demo slots arrive.
        // The label will be updated/hidden by ApplyBuffState (real data) or ApplyDevDemoSlots (dev).
        // Do NOT hide it here even when hub is bound — the bar needs to show SOMETHING until
        // the first real BuffStateEvent arrives (which may never happen in offline-dev mode).
        if (_demoLabel is not null)
        {
            _demoLabel.Text = hub is not null
                ? "[BuffBar — waiting for server buff data]"
                : "[BuffBar — offline / no hub]";
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  UI construction
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildUi()
    {
        // The bar is a thin horizontal strip anchored to the top of whatever container it lives in.
        // The parent HUD is responsible for positioning the bar.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;

        // Semi-transparent background panel so the buff bar strip is visible against the 3D world.
        // The legacy client does not have a separate bar background (icons float on the game view)
        // but we add one for dev legibility. Removed when real atlas chrome is wired.
        var bg = new ColorRect
        {
            Name = "BuffBarBg",
            Color = new Color(0f, 0f, 0f, 0.45f), // 45% black — readable but not obtrusive
            MouseFilter = MouseFilterEnum.Ignore,
        };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(bg);

        // Container for the 30 slot TextureRects.
        var hbox = new HBoxContainer
        {
            Name = "BuffSlots",
            CustomMinimumSize = new Vector2(SlotCount * (BuffIconCatalog.StateCellSize + BuffSlotSpacing),
                BuffIconCatalog.StateCellSize),
        };
        hbox.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        AddChild(hbox);

        for (int i = 0; i < SlotCount; i++)
        {
            var rect = new TextureRect
            {
                Name = $"Buff{i}",
                // Default size: 25×25 (the larger of the two cell sizes — avoids layout shifts).
                // spec: Docs/RE/formats/misc_data.md §1.6 CODE-CONFIRMED (state cell 25×25).
                CustomMinimumSize = new Vector2(BuffIconCatalog.StateCellSize, BuffIconCatalog.StateCellSize),
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            rect.Visible = false; // hidden until a refresh marks the slot active
            hbox.AddChild(rect);
            _slots[i] = rect;
        }

        // Demo label — shown in offline mode.
        _demoLabel = new Label
        {
            Name = "DemoLabel",
            Text = "[BuffBar — offline]",
            Visible = true,
        };
        AddChild(_demoLabel);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Refresh logic (called on main thread from _Process)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Clears and rebuilds the 30 buff slots from the server snapshot.
    ///
    /// Per-refresh reset contract from spec:
    ///   1. Clear and hide all 30 slots.
    ///   2. For each record: if buff_id == 0, leave hidden.
    ///   3. Otherwise look up buff_id in the catalog, choose cell size, show.
    ///
    /// spec: Docs/RE/formats/misc_data.md §1.6 CODE-CONFIRMED — "per-refresh reset".
    /// </summary>
    private void ApplyBuffState(BuffStateEvent evt)
    {
        // Step 1: clear all slots.
        // spec: Docs/RE/formats/misc_data.md §1.6 CODE-CONFIRMED — "clears and hides all 30 slots … then re-shows".
        for (int i = 0; i < SlotCount; i++)
        {
            _slots[i].Texture = null;
            _slots[i].Visible = false;
        }

        // Flowing counter for buff-class (≤80) icons.
        // spec: Docs/RE/formats/misc_data.md §1.6 CODE-CONFIRMED — "flowing left-to-right counter".
        int flowingIndex = 0;

        for (int i = 0; i < evt.Slots.Length && i < SlotCount; i++)
        {
            BuffSlot slot = evt.Slots[i];

            // buff_id == 0 marks an empty slot — skip.
            // spec: Docs/RE/formats/misc_data.md §1.6 / Docs/RE/packets/4-102_buff_state.yaml.
            if (slot.IsEmpty) continue;

            ushort buffId = slot.BuffId;

            // Resolve icon from catalog (null-safe offline).
            AtlasTexture? icon = _iconCatalog?.GetIcon(buffId);

            int cellSize = BuffIconCatalog.CellSizeForId(buffId); // spec: misc_data.md §1.6

            if (buffId <= BuffIconCatalog.BuffStateBoundary)
            {
                // Buff class: place at the next flowing slot position.
                // spec: Docs/RE/formats/misc_data.md §1.6 CODE-CONFIRMED — "placed in the next free
                //       position and the counter advances".
                if (flowingIndex < SlotCount)
                {
                    TextureRect slotRect = _slots[flowingIndex++];
                    slotRect.Texture = icon;
                    slotRect.CustomMinimumSize = new Vector2(cellSize, cellSize);
                    slotRect.Visible = true;
                }
            }
            else
            {
                // State/debuff class: fixed per-slot screen position (use wire slot index i).
                // spec: Docs/RE/formats/misc_data.md §1.6 CODE-CONFIRMED — "fixed per-slot screen position".
                TextureRect slotRect = _slots[i];
                slotRect.Texture = icon;
                slotRect.CustomMinimumSize = new Vector2(cellSize, cellSize);
                slotRect.Visible = true;
            }
        }

        // Hide the demo label once we receive real server data.
        if (_demoLabel is { Visible: true })
            _demoLabel.Visible = false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Dev demo slot population
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// DEV ONLY. Populates the first <see cref="DevDemoSlotCount"/> buff slots with placeholder
    /// colored squares so the buff bar strip is visibly rendered in dev screenshots.
    ///
    /// This fires only when no real buff data has arrived from the hub. It is a pure view
    /// seam — no game logic, no stat computation, no protocol knowledge.
    ///
    /// spec: Docs/RE/specs/ui_hud_layout.md §2 — per-icon screen positions from buff_icon_position.xdb;
    ///       flowing counter for buff_id ≤ 80; fixed per-slot for buff_id > 80. CODE-CONFIRMED.
    /// </summary>
    private void ApplyDevDemoSlots()
    {
        // Solid-colour placeholder palette for the dev demo.
        // These colours represent "buff" slots (flowing, buff_id ≤ 80 class).
        // spec: Docs/RE/formats/misc_data.md §1.6 CODE-CONFIRMED — "buff bar has 30 icon slots".
        Color[] devPalette =
        [
            new Color(0.2f, 0.8f, 0.3f, 0.9f), // green  — slot 0
            new Color(0.3f, 0.5f, 1.0f, 0.9f), // blue   — slot 1
            new Color(1.0f, 0.7f, 0.1f, 0.9f), // orange — slot 2
            new Color(0.9f, 0.2f, 0.2f, 0.9f), // red    — slot 3
            new Color(0.8f, 0.2f, 0.9f, 0.9f), // purple — slot 4
        ];

        for (int i = 0; i < DevDemoSlotCount && i < SlotCount; i++)
        {
            // Build a 1×1 solid-colour Image and upload it as a placeholder texture.
            var img = Image.CreateEmpty(BuffIconCatalog.BuffCellSize, BuffIconCatalog.BuffCellSize,
                false, Image.Format.Rgba8);
            img.Fill(devPalette[i % devPalette.Length]);
            var tex = ImageTexture.CreateFromImage(img);

            _slots[i].Texture = tex;
            _slots[i].CustomMinimumSize = new Vector2(BuffIconCatalog.BuffCellSize, BuffIconCatalog.BuffCellSize);
            _slots[i].Visible = true;
        }

        // Keep the demo label visible — it labels this as a dev placeholder.
        if (_demoLabel is not null)
            _demoLabel.Text = "[BuffBar — DEV placeholder slots]";

        GD.Print($"[BuffBar] DEV demo: applied {DevDemoSlotCount} placeholder buff slots " +
                 "(no real data from hub). spec: Docs/RE/specs/ui_hud_layout.md §2.");
    }
}