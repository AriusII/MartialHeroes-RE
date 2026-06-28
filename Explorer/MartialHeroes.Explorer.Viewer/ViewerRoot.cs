using Godot;
using MartialHeroes.Assets.Mapping;
using MartialHeroes.Assets.Parsers.Mesh.Models;
using Array = Godot.Collections.Array;
using Environment = System.Environment;

namespace MartialHeroes.Explorer.Viewer;

public partial class ViewerRoot : Node3D
{
    private const string LeftBase = "Ui/MainContainer/TopRow/LeftDock/LeftMargin/LeftVBox/";
    private const string RightBase = "Ui/MainContainer/TopRow/RightDock/RightMargin/RightScroll/RightVBox/";

    private readonly List<ViewerSkinnedNode> _avatarParts = [];
    private readonly Dictionary<StandardMaterial3D, ImageTexture?> _worldTexSnapshot = new();

    private OptionButton? _animDropdown;
    private VBoxContainer? _animPanel;
    private Node3D? _axisGizmo;
    private BgTextureCatalog? _bgCatalog;
    private VfsBrowser? _browser;
    private OrbitCamera? _camera;
    private bool _currentIsHeavy;
    private IReadOnlySet<string>? _currentLoadedPaths;
    private string[] _currentMotionPaths = [];
    private Node3D? _currentPreview;
    private string? _currentSelectionKey;
    private ViewerSkinnedNode? _currentSkinned;

    private Label? _fileCountLabel;
    private DirectionalLight3D? _fillLight;
    private Label? _frameReadout;
    private HSlider? _frameScrub;
    private CheckButton? _gizmoToggle;
    private MeshInstance3D? _gridFloor;
    private CheckButton? _gridToggle;
    private RichTextLabel? _inspector;
    private DirectionalLight3D? _keyLight;
    private Aabb _lastFrameAabb;
    private CheckButton? _lightToggle;
    private CheckButton? _loopToggle;
    private Tree? _navigationTree;
    private (string Label, AnimationClip? Clip)[] _parsedClips = [];
    private Button? _pauseBtn;
    private Button? _playBtn;
    private LineEdit? _searchEdit;
    private CheckButton? _skelToggle;
    private SpinBox? _speedSpin;
    private Label? _status;
    private Button? _stopBtn;
    private CheckButton? _texToggle;
    private CheckButton? _wireToggle;
    private WorldEnvironment? _worldEnv;

    public override void _Ready()
    {
        _navigationTree = GetNode<Tree>(LeftBase + "NavigationTree");
        _searchEdit = GetNode<LineEdit>(LeftBase + "SearchEdit");
        _fileCountLabel = GetNode<Label>(LeftBase + "FileCountLabel");

        _inspector = GetNode<RichTextLabel>(RightBase + "InspectorSection/InspectorBody/Inspector");
        _animPanel = GetNode<VBoxContainer>(RightBase + "AnimSection");
        _animDropdown = GetNode<OptionButton>(RightBase + "AnimSection/AnimBody/AnimDropdown");
        _playBtn = GetNode<Button>(RightBase + "AnimSection/AnimBody/AnimControls/PlayBtn");
        _pauseBtn = GetNode<Button>(RightBase + "AnimSection/AnimBody/AnimControls/PauseBtn");
        _stopBtn = GetNode<Button>(RightBase + "AnimSection/AnimBody/AnimControls/StopBtn");
        _frameScrub = GetNode<HSlider>(RightBase + "AnimSection/AnimBody/FrameScrub");
        _frameReadout = GetNode<Label>(RightBase + "AnimSection/AnimBody/FrameReadout");
        _speedSpin = GetNode<SpinBox>(RightBase + "AnimSection/AnimBody/SpeedRow/SpeedSpin");
        _loopToggle = GetNode<CheckButton>(RightBase + "AnimSection/AnimBody/LoopRow/LoopToggle");

        _texToggle = GetNode<CheckButton>(RightBase + "DisplaySection/DisplayBody/TexToggle");
        _wireToggle = GetNode<CheckButton>(RightBase + "DisplaySection/DisplayBody/WireToggle");
        _lightToggle = GetNode<CheckButton>(RightBase + "DisplaySection/DisplayBody/LightToggle");
        _gridToggle = GetNode<CheckButton>(RightBase + "DisplaySection/DisplayBody/GridToggle");
        _gizmoToggle = GetNode<CheckButton>(RightBase + "DisplaySection/DisplayBody/GizmoToggle");
        _skelToggle = GetNode<CheckButton>(RightBase + "DisplaySection/DisplayBody/SkelToggle");
        var resetViewBtn = GetNode<Button>(RightBase + "DisplaySection/DisplayBody/ResetViewBtn");

        _status = GetNode<Label>("Ui/MainContainer/BottomBar/BottomMargin/Status");
        _camera = GetNode<OrbitCamera>("Camera");
        _gridFloor = GetNode<MeshInstance3D>("GridFloor");
        _keyLight = GetNode<DirectionalLight3D>("KeyLight");
        _fillLight = GetNode<DirectionalLight3D>("FillLight");
        _worldEnv = GetNodeOrNull<WorldEnvironment>("WorldEnv");

        _animPanel.Visible = false;
        _skelToggle.Disabled = true;
        _gridFloor.Mesh = BuildGridMesh(10f, 10);

        _axisGizmo = ViewerGizmos.BuildAxisGizmo();
        AddChild(_axisGizmo);
        _axisGizmo.Visible = _gizmoToggle.ButtonPressed;

        var inspHeader = GetNode<Button>(RightBase + "InspectorSection/InspectorHeader");
        var inspBody = GetNode<VBoxContainer>(RightBase + "InspectorSection/InspectorBody");
        inspHeader.Toggled += on => inspBody.Visible = on;

        var animHeader = GetNode<Button>(RightBase + "AnimSection/AnimHeader");
        var animBody = GetNode<VBoxContainer>(RightBase + "AnimSection/AnimBody");
        animHeader.Toggled += on => animBody.Visible = on;

        var dispHeader = GetNode<Button>(RightBase + "DisplaySection/DisplayHeader");
        var dispBody = GetNode<VBoxContainer>(RightBase + "DisplaySection/DisplayBody");
        dispHeader.Toggled += on => dispBody.Visible = on;

        resetViewBtn.Pressed += () =>
        {
            if (_lastFrameAabb.Size != Vector3.Zero)
                _camera?.FrameAabb(_lastFrameAabb);
        };

        _animDropdown.ItemSelected += OnAnimSelected;
        _playBtn.Pressed += () =>
        {
            foreach (var p in _avatarParts) p.Play();
        };
        _pauseBtn.Pressed += () =>
        {
            foreach (var p in _avatarParts) p.Pause();
        };
        _stopBtn.Pressed += () =>
        {
            foreach (var p in _avatarParts) p.Stop();
        };
        _frameScrub.ValueChanged += v =>
        {
            foreach (var p in _avatarParts) p.SeekFrame((int)v);
        };
        _speedSpin.ValueChanged += v =>
        {
            foreach (var p in _avatarParts) p.SetSpeed((float)v);
        };
        _loopToggle.Toggled += on =>
        {
            foreach (var p in _avatarParts) p.SetLoop(on);
        };

        _texToggle.Toggled += on =>
        {
            if (_avatarParts.Count > 0)
                foreach (var p in _avatarParts)
                    p.SetAlbedoEnabled(on);
            else
                ApplyWorldTextureToggle(on);
        };
        _wireToggle.Toggled += on =>
            GetViewport().DebugDraw = on ? Viewport.DebugDrawEnum.Wireframe : Viewport.DebugDrawEnum.Disabled;
        _lightToggle.Toggled += on =>
        {
            if (_keyLight is not null) _keyLight.Visible = on;
            if (_fillLight is not null) _fillLight.Visible = on;
        };
        _gridToggle.Toggled += on =>
        {
            if (_gridFloor is not null) _gridFloor.Visible = on;
        };
        _gizmoToggle.Toggled += on =>
        {
            if (_axisGizmo is not null) _axisGizmo.Visible = on;
        };
        _skelToggle.Toggled += on =>
        {
            foreach (var p in _avatarParts) p.SetSkeletonVisible(on);
        };

        GD.Print("[Viewer] Toggle signals wired.");

        var clientDir = ClientDirResolver.Resolve();
        if (clientDir is null)
        {
            SetStatus("No client VFS found — place data.inf + data\\data.vfs at D:\\MartialHeroesClient");
            GD.Print("[Viewer] No client VFS found.");
            return;
        }

        _browser = new VfsBrowser();
        var (infPath, vfsPath) = ClientDirResolver.GetVfsPaths(clientDir);

        if (!_browser.TryOpen(infPath, vfsPath))
        {
            SetStatus("Failed to open VFS archive.");
            return;
        }

        GD.Print($"[Viewer] VFS opened: {_browser.TotalEntries} entries");
        _bgCatalog = ViewerTextures.LoadBgCatalog(_browser.Archive);
        GD.Print($"[Viewer] BgTextureCatalog: {(_bgCatalog is not null ? "loaded" : "unavailable")}");

        BuildNavigationTree();

        SetStatus($"VFS ready — {_browser.TotalEntries} total entries, {_browser.Families.Count} 3D families.");

        var autoMap = Environment.GetEnvironmentVariable("MH_VIEWER_MAP");
        if (!string.IsNullOrEmpty(autoMap))
        {
            GD.Print($"[Viewer] auto-map: {autoMap}");
            ClearPreview(EvictionPolicy.Full);
            _currentSelectionKey = "map:" + autoMap;
            BuildMap(autoMap);
            return;
        }

        var autoAssemble = Environment.GetEnvironmentVariable("MH_VIEWER_ASSEMBLE");
        if (!string.IsNullOrEmpty(autoAssemble) && TryParseAssemble(autoAssemble, out var aCls, out var aVar))
        {
            GD.Print($"[Viewer] auto-assemble: class={aCls} variant={aVar}");
            ClearPreview(EvictionPolicy.Full);
            _currentSelectionKey = $"char:{aCls}:{aVar}";
            AssembleAvatar(aCls, aVar);
            return;
        }

        var autoPreview = Environment.GetEnvironmentVariable("MH_VIEWER_PREVIEW");
        if (!string.IsNullOrEmpty(autoPreview) && _browser.Archive.Contains(autoPreview))
        {
            GD.Print($"[Viewer] auto-preview: {autoPreview}");
            ClearPreview(EvictionPolicy.Full);
            _currentSelectionKey = "file:" + autoPreview;
            PreviewPath(autoPreview);
            if (autoPreview.EndsWith(".skn", StringComparison.OrdinalIgnoreCase) && _animDropdown.ItemCount > 0)
            {
                _animDropdown.Selected = 0;
                OnAnimSelected(0);
            }
        }
        else if (string.IsNullOrEmpty(autoPreview))
        {
            ClearPreview(EvictionPolicy.Full);
            _currentSelectionKey = $"char:1:{CharacterAssembler.DefaultVariant(1)}";
            AssembleAvatar(1, CharacterAssembler.DefaultVariant(1));
        }
    }

    public override void _Process(double delta)
    {
        if (_currentSkinned is null || _frameScrub is null || _frameReadout is null) return;
        var frame = _currentSkinned.CurrentFrame;
        _frameScrub.SetValueNoSignal(frame);
        var ids = _currentSkinned.AnimatedBoneIds;
        var boneStr = ids.Length > 0 ? string.Join(",", ids) : "none";
        _frameReadout.Text =
            $"frame {frame} / {_currentSkinned.FrameCount} · tracks={_currentSkinned.TrackCount} · keyframes={_currentSkinned.TotalKeyframeCount} · bones=[{boneStr}]";
    }

    public override void _ExitTree()
    {
        _browser?.Dispose();
    }

    private void ClearPreview(EvictionPolicy policy = EvictionPolicy.Targeted)
    {
        PreviewLifecycle.Unload(ref _currentPreview, this, _currentLoadedPaths, policy);
        _currentLoadedPaths = null;
        _currentIsHeavy = false;
        _currentSelectionKey = null;
        _currentSkinned = null;
        _avatarParts.Clear();
        _currentMotionPaths = [];
        _parsedClips = [];
        _worldTexSnapshot.Clear();
        if (_animPanel is not null) _animPanel.Visible = false;
        if (_skelToggle is not null) _skelToggle.Disabled = true;
        if (_inspector is not null) _inspector.Text = string.Empty;
        if (_frameReadout is not null) _frameReadout.Text = "frame 0 / 0";
        if (_frameScrub is not null)
        {
            _frameScrub.MaxValue = 0;
            _frameScrub.SetValueNoSignal(0);
        }
    }

    private void SnapshotWorldTextures(Node3D root)
    {
        _worldTexSnapshot.Clear();
        var instances = new List<MeshInstance3D>();
        CollectMeshInstancesInto(root, instances);
        foreach (var mi in instances)
        {
            if (mi.Mesh is null) continue;
            for (var s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                if (mi.Mesh.SurfaceGetMaterial(s) is StandardMaterial3D std && !_worldTexSnapshot.ContainsKey(std))
                    _worldTexSnapshot[std] = std.AlbedoTexture as ImageTexture;
        }
    }

    private static void CollectMeshInstancesInto(Node root, List<MeshInstance3D> result)
    {
        if (root is MeshInstance3D mi) result.Add(mi);
        foreach (var child in root.GetChildren())
            CollectMeshInstancesInto(child, result);
    }

    private void ApplyWorldTextureToggle(bool on)
    {
        foreach (var (std, snapshot) in _worldTexSnapshot)
            std.AlbedoTexture = on ? snapshot : null;
    }

    private void FrameCamera(Aabb aabb)
    {
        _lastFrameAabb = aabb;
        _camera?.FrameAabb(aabb);
    }

    private void TryApplyLightBin(string mapId)
    {
        if (_browser is null || _keyLight is null) return;
        if (!int.TryParse(mapId, out var mapIdInt)) return;
        var binPath = $"data/sky/dat/light{mapIdInt}.bin";
        if (!_browser.Archive.Contains(binPath)) return;
        try
        {
            var bytes = _browser.Archive.GetFileContent(binPath);
            var frame = LightBinReader.TryRead(bytes.Span, 24);
            if (frame is null) return;

            var dir = frame.Value.DirectionalColor;
            var dirMax = Mathf.Max(dir.R, Mathf.Max(dir.G, dir.B));
            if (!frame.Value.DirectionalEnabled || dirMax < 0.2f)
            {
                GD.Print(
                    $"[Viewer] LightBin {binPath}: directional unusable (dir={dir}, enabled={frame.Value.DirectionalEnabled}) — keeping default lighting.");
                return;
            }

            _keyLight.LightColor = dir;
            _keyLight.LightEnergy = 1.35f;
            _keyLight.GlobalPosition = frame.Value.SunDirectionGodot * 1000f;
            var up = Mathf.Abs(frame.Value.SunDirectionGodot.Dot(Vector3.Up)) > 0.9f
                ? Vector3.Forward
                : Vector3.Up;
            _keyLight.LookAt(Vector3.Zero, up);

            var amb = frame.Value.AmbientColor;
            var ambMax = Mathf.Max(amb.R, Mathf.Max(amb.G, amb.B));
            var godotEnv = _worldEnv?.Environment;
            if (godotEnv is not null && ambMax is >= 0.05f and <= 0.95f)
            {
                godotEnv.AmbientLightSource = Godot.Environment.AmbientSource.Color;
                godotEnv.AmbientLightColor = amb;
                godotEnv.AmbientLightEnergy = 1.15f;
            }

            GD.Print($"[Viewer] LightBin {binPath}: applied dir={dir} amb={amb}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[Viewer] LightBin skipped ({binPath}): {ex.Message}");
        }
    }

    private void SetStatus(string text)
    {
        if (_status is not null)
            _status.Text = text;
    }

    private static Aabb ComputePreviewAabb(Node3D root)
    {
        var combined = new Aabb();
        var first = true;

        foreach (var child in root.GetChildren())
        {
            if (child.Name == "SkeletonOverlay") continue;
            Aabb childAabb;
            if (child is MeshInstance3D mi && mi.Mesh is not null)
                childAabb = mi.GlobalTransform * mi.GetAabb();
            else if (child is Node3D n3d)
                childAabb = ComputePreviewAabb(n3d);
            else
                continue;

            if (first)
            {
                combined = childAabb;
                first = false;
            }
            else
            {
                combined = combined.Merge(childAabb);
            }
        }

        if (first)
            combined = new Aabb(new Vector3(-1, 0, -1), new Vector3(2, 2, 2));

        return combined;
    }

    private void FitGridToAabb(Aabb aabb)
    {
        if (_gridFloor is null) return;
        var horiz = Mathf.Max(aabb.Size.X, aabb.Size.Z);
        var half = Mathf.Max(horiz * 1.5f, 1f);
        _gridFloor.Mesh = BuildGridMesh(half, 20);
        _gridFloor.Position = new Vector3(0f, aabb.Position.Y, 0f);
    }

    private static ArrayMesh BuildGridMesh(float halfExtent, int halfCells)
    {
        var cellSize = halfExtent / halfCells;
        var lineCount = (halfCells * 2 + 1) * 2;
        var positions = new Vector3[lineCount * 2];
        var idx = 0;

        for (var i = -halfCells; i <= halfCells; i++)
        {
            positions[idx++] = new Vector3(i * cellSize, 0f, -halfExtent);
            positions[idx++] = new Vector3(i * cellSize, 0f, halfExtent);
            positions[idx++] = new Vector3(-halfExtent, 0f, i * cellSize);
            positions[idx++] = new Vector3(halfExtent, 0f, i * cellSize);
        }

        var arrays = new Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = positions;

        var gridMesh = new ArrayMesh();
        gridMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);

        var mat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = new Color(0.35f, 0.35f, 0.35f, 0.7f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha
        };
        gridMesh.SurfaceSetMaterial(0, mat);

        return gridMesh;
    }
}