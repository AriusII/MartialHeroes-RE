using Godot;
using MartialHeroes.Client.Godot.Autoload;
using MartialHeroes.Client.Godot.Ui.Assets;

namespace MartialHeroes.Client.Godot.Ui.Hud;

public sealed partial class HudSkillPanel : Control
{
    private const float SkillPanelW = 964f;
    private const float SkillPanelH = 655f;
    private const float ParkX = 43f;
    private const float ParkY = -655f;
    private const float OpenY = 0f;

    private const int TabActionStart = 802;
    private const int TabCount = 9;

    private const int SkillPipeCount = 4;

    private const int TitleMsgKey = 3027;

    private const int SkillWindowTexId = 3;


    private readonly Button[] _tabs = new Button[TabCount];


    private bool _open;
    private int _activePage;
    private VBoxContainer _skillList = null!;
    private Label _titleLabel = null!;
    private ClientContext? _ctx;


    public void Build(HudAtlasLibrary atlas, HudTextLibrary text)
    {
        Name = "HudSkillPanel";

        _ctx = ClientContext.Instance;

        Position = new Vector2(ParkX, ParkY);
        Size = new Vector2(SkillPanelW, SkillPanelH);
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;

        var chromeTex = atlas.GetById(SkillWindowTexId);
        if (chromeTex is null)
            GD.PrintErr("[HudSkillPanel] skill_window_1.dds (uitex 3) unavailable (VFS offline). " +
                        "spec: Docs/RE/specs/ui_system.md §8.6.1.");

        var bg = new Panel { Name = "Bg" };
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var bgStyle = new StyleBoxFlat();
        bgStyle.BgColor = new Color(0.06f, 0.06f, 0.1f, 0.93f);
        bgStyle.SetBorderWidthAll(2);
        bgStyle.BorderColor = new Color(0.45f, 0.35f, 0.15f, 0.9f);
        bg.AddThemeStyleboxOverride("panel", bgStyle);
        AddChild(bg);

        if (chromeTex is not null)
        {
            var chrome = new TextureRect
            {
                Name = "Chrome",
                Texture = chromeTex,
                StretchMode = TextureRect.StretchModeEnum.KeepAspect,
                MouseFilter = MouseFilterEnum.Ignore
            };
            chrome.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            AddChild(chrome);
        }

        var titleStr = text.GetCaption(TitleMsgKey, "스킬");
        _titleLabel = new Label
        {
            Name = "TitleLabel",
            Text = titleStr,
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _titleLabel.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        _titleLabel.OffsetBottom = 30f;
        AddChild(_titleLabel);

        var tabRow = new HBoxContainer
        {
            Name = "TabRow",
            Position = new Vector2(0f, 30f),
            Size = new Vector2(SkillPanelW, 28f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(tabRow);

        for (var t = 0; t < TabCount; t++)
        {
            var tabIdx = t;
            var tab = new Button
            {
                Name = $"Tab{TabActionStart + t}",
                Text = $"{t + 1}",
                CustomMinimumSize = new Vector2(SkillPanelW / TabCount - 2f, 26f),
                MouseFilter = MouseFilterEnum.Stop
            };
            tab.Pressed += () => OnTabPressed(tabIdx);
            tabRow.AddChild(tab);
            _tabs[t] = tab;
        }

        _skillList = new VBoxContainer
        {
            Name = "SkillList",
            Position = new Vector2(8f, 62f),
            Size = new Vector2(SkillPanelW - 16f, SkillPanelH - 80f),
            MouseFilter = MouseFilterEnum.Ignore
        };
        AddChild(_skillList);

        for (var p = 0; p < SkillPipeCount; p++)
        {
            var pipePlaceholder = new Panel
            {
                Name = $"SkillPipe{p}",
                Position = new Vector2(8f + p * 240f, SkillPanelH - 60f),
                Size = new Vector2(232f, 52f),
                MouseFilter = MouseFilterEnum.Ignore
            };
            var ps = new StyleBoxFlat();
            ps.BgColor = new Color(0.08f, 0.08f, 0.14f, 0.8f);
            ps.SetBorderWidthAll(1);
            ps.BorderColor = new Color(0.4f, 0.3f, 0.1f, 0.7f);
            pipePlaceholder.AddThemeStyleboxOverride("panel", ps);
            AddChild(pipePlaceholder);
        }

        UpdateTabHighlight();

        GD.Print($"[HudSkillPanel] Built — 964×655 parked at ({ParkX},{ParkY}). " +
                 $"9 tabs (actions 802–810), 4 skill-pipe panels. " +
                 "spec: Docs/RE/specs/ui_system.md §8.8 CODE-CONFIRMED. " +
                 "spec: Docs/RE/specs/skill_trees.md §1 — 9 GlobalCategory pages, page select is client-local. " +
                 "Inbound rows BLOCKED: no IHudEventHub skill channel (SkillPointUpdateEvent / SkillHotbarSlotSetEvent / " +
                 "SkillHotbarAssignResultEvent ride the GameLoop-drained IClientEventBus and are not republished) and " +
                 "SkillCatalogue exposes no page-enumeration API — skill list + skill-pipes stay empty, no mock data. " +
                 $"ctx.UseCases present={_ctx?.UseCases is not null} but exposes no learn (2/145) / hotbar-bind (2/41) intent. " +
                 "TODO(spec): skill-pipe exact geometry pending. " +
                 "TODO(spec): hotbar overlay-rect values debugger-pending.");
    }


    public void Toggle()
    {
        _open = !_open;
        var newY = _open ? OpenY : ParkY;
        Position = new Vector2(ParkX, newY);
        Visible = _open;
        GD.Print($"[HudSkillPanel] Toggle → open={_open}, Y={newY}. " +
                 "spec: Docs/RE/specs/ui_system.md §8.8.");
    }


    private void OnTabPressed(int tabIdx)
    {
        if ((uint)tabIdx >= TabCount) return;

        _activePage = tabIdx;
        UpdateTabHighlight();

        GD.Print($"[HudSkillPanel] Tab {TabActionStart + tabIdx} pressed → active page {tabIdx} (GlobalCategory {tabIdx}). " +
                 "Client-local page select per Docs/RE/specs/skill_trees.md §1 (no C2S page opcode). " +
                 "List re-populate BLOCKED: no IHudEventHub owned-skill/page channel and SkillCatalogue has no " +
                 "page-enumeration API — page filter has no data source, list stays empty (no mock data). " +
                 "spec: Docs/RE/specs/ui_system.md §8.8 — action ids 802–810.");
    }

    private void UpdateTabHighlight()
    {
        for (var t = 0; t < TabCount; t++)
        {
            if (_tabs[t] is null) continue;
            var selected = t == _activePage;
            _tabs[t].Disabled = selected;
            _tabs[t].Modulate = selected ? new Color(1f, 1f, 0.7f, 1f) : new Color(0.8f, 0.8f, 0.8f, 1f);
        }
    }
}