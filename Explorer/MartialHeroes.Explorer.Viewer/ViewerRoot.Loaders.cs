using Godot;
using MartialHeroes.Assets.Parsers.Mesh;
using MartialHeroes.Assets.Parsers.Mesh.Models;
using MartialHeroes.Assets.Parsers.Terrain;

namespace MartialHeroes.Explorer.Viewer;

public partial class ViewerRoot
{
    private void PreviewPath(string vfsPath)
    {
        if (_browser is null) return;
        SetStatus($"Loading {vfsPath}...");

        try
        {
            ViewerTextures.BeginSession();
            var bytes = _browser.GetContent(vfsPath);
            Node3D? preview = null;
            var ext = Path.GetExtension(vfsPath).ToLowerInvariant();

            if (ext == ".xobj")
            {
                var mesh = XobjParser.Parse(bytes);
                preview = XobjMeshBuilder.Build(mesh);
                SnapshotWorldTextures(preview);
                SetInspector(BuildXobjInspector(mesh));
            }
            else if (ext == ".ted")
            {
                var cell = TedTerrainParser.Parse(bytes);
                preview = TedTerrainBuilder.Build(cell, _browser.Archive, _bgCatalog, vfsPath);
                SnapshotWorldTextures(preview);
                SetInspector(BuildTedInspector(cell));
            }
            else if (ext == ".bud")
            {
                var scene = TerrainSceneParser.Parse(bytes);
                preview = BudSceneBuilder.Build(scene, _browser.Archive, _bgCatalog);
                SnapshotWorldTextures(preview);
                SetInspector(BuildBudInspector(scene));
            }
            else if (ext == ".skn")
            {
                var skn = SknParser.Parse(bytes);
                var t = ViewerTextures.ResolveSkn(_browser.Archive, skn);
                GD.Print($"[Viewer] SKN texture resolve: path={t.Path ?? "none"} note={t.Note}");
                preview = SknSkinnedBuilder.Build(
                    _browser.Archive, skn, t.Texture,
                    out _currentSkinned, out _currentMotionPaths, out var info);
                _avatarParts.Clear();
                if (_currentSkinned is not null) _avatarParts.Add(_currentSkinned);
                _worldTexSnapshot.Clear();
                SetInspector(BuildSknInspector(skn, t, info));
                PopulateAnimPanel();
            }

            _currentLoadedPaths = ViewerTextures.EndSession();

            if (preview is not null)
            {
                _currentIsHeavy = false;
                PreviewLifecycle.Register(preview, ref _currentPreview, this);

                if (ext != ".skn")
                {
                    if (_texToggle is not null && !_texToggle.ButtonPressed)
                        ApplyWorldTextureToggle(false);
                }
                else if (_currentSkinned is not null)
                {
                    _currentSkinned.SetAlbedoEnabled(_texToggle?.ButtonPressed ?? true);
                    _currentSkinned.SetSkeletonVisible(_skelToggle?.ButtonPressed ?? false);
                }

                if (_skelToggle is not null)
                    _skelToggle.Disabled = _currentSkinned is null;

                var aabb = ComputePreviewAabb(preview);
                FrameCamera(aabb);
                FitGridToAabb(aabb);
                GD.Print($"[Viewer] Preview ready: {vfsPath}");
                SetStatus($"Showing: {vfsPath}");
            }
        }
        catch (Exception ex)
        {
            _currentLoadedPaths = ViewerTextures.EndSession();
            GD.PrintErr($"[Viewer] Failed to preview '{vfsPath}': {ex.Message}");
            SetStatus($"Error loading {vfsPath}: {ex.Message}");
        }
    }

    private void BuildMap(string mapId)
    {
        if (_browser is null) return;
        SetStatus($"Assembling map {mapId}...");

        try
        {
            ViewerTextures.BeginSession();
            var preview = MapAssembler.Build(_browser.Archive, _bgCatalog, mapId, out var info);
            _worldTexSnapshot.Clear();
            SnapshotWorldTextures(preview);

            if (info.TerrainCells.Count > 0)
            {
                var cellOrigin = info.TerrainCells[0].CellOriginGodot;
                var cellCentreRawX = cellOrigin.X + 512f;
                var cellCentreRawZ = cellOrigin.Z - 512f;
                var vpX = cellCentreRawX + info.RootOffset.X;
                var vpZ = cellCentreRawZ + info.RootOffset.Z;
                var rawH = MapAssembler.SampleRawGroundHeight(info.TerrainCells, info.RootOffset, vpX, vpZ);
                if (rawH.HasValue)
                    try
                    {
                        var avatarRoot = CharacterAssembler.Build(
                            _browser.Archive, 1, CharacterAssembler.DefaultVariant(1),
                            out _, out _, out _, out _);
                        var ep = avatarRoot.Position;
                        avatarRoot.Position = new Vector3(
                            ep.X + cellCentreRawX,
                            ep.Y + rawH.Value,
                            ep.Z + cellCentreRawZ);
                        preview.AddChild(avatarRoot);
                        GD.Print(
                            $"[Map] Ground-snap: cell0 rawH={rawH.Value:F1} viewport=({vpX:F1},{vpZ:F1}) feetY={rawH.Value + info.RootOffset.Y:F1}.");
                    }
                    catch (Exception ex)
                    {
                        GD.PrintErr($"[Map] Demo avatar skipped: {ex.Message}");
                    }
            }

            _currentLoadedPaths = ViewerTextures.EndSession();
            _currentIsHeavy = true;
            PreviewLifecycle.Register(preview, ref _currentPreview, this);

            if (_texToggle is not null && !_texToggle.ButtonPressed)
                ApplyWorldTextureToggle(false);

            SetInspector(BuildMapInspector(info));

            var aabb = info.FrameBox.Size != Vector3.Zero ? info.FrameBox : ComputePreviewAabb(preview);
            FrameCamera(aabb);
            FitGridToAabb(aabb);
            TryApplyLightBin(mapId);
            SetStatus($"Map {mapId}: {info.CellsBuilt} cells, {info.BudScenesBuilt} building scenes " +
                      $"({info.BudObjects} objects).");
        }
        catch (Exception ex)
        {
            _currentLoadedPaths = ViewerTextures.EndSession();
            GD.PrintErr($"[Viewer] BuildMap failed: {ex.Message}");
            SetStatus($"Map assemble error: {ex.Message}");
        }
    }

    private void AssembleAvatar(int internalClass, int variant)
    {
        if (_browser is null) return;
        SetStatus($"Assembling class {internalClass} variant {variant}...");

        try
        {
            ViewerTextures.BeginSession();
            var preview = CharacterAssembler.Build(
                _browser.Archive, internalClass, variant,
                out var parts, out var bodyNode, out _currentMotionPaths, out var info);

            _avatarParts.Clear();
            _avatarParts.AddRange(parts);
            _currentSkinned = bodyNode;
            _worldTexSnapshot.Clear();
            SetInspector(BuildAssemblyInspector(info));
            PopulateAnimPanel();

            _currentLoadedPaths = ViewerTextures.EndSession();
            _currentIsHeavy = false;
            PreviewLifecycle.Register(preview, ref _currentPreview, this);

            foreach (var part in _avatarParts)
            {
                part.SetAlbedoEnabled(_texToggle?.ButtonPressed ?? true);
                part.SetSkeletonVisible(_skelToggle?.ButtonPressed ?? false);
            }

            if (_skelToggle is not null)
                _skelToggle.Disabled = _currentSkinned is null;

            if (_animDropdown is not null && _animDropdown.ItemCount > 0)
            {
                _animDropdown.Selected = 0;
                OnAnimSelected(0);
            }

            var aabb = ComputePreviewAabb(preview);
            FrameCamera(aabb);
            FitGridToAabb(aabb);
            SetStatus($"Avatar: class {internalClass} variant {variant} — " +
                      $"{info.PartsResolved} parts ({info.SkinnedParts} skinned).");
            GD.Print($"[Viewer] Avatar assembled: class={internalClass} variant={variant} " +
                     $"parts={info.PartsResolved} skinned={info.SkinnedParts}.");
        }
        catch (Exception ex)
        {
            _currentLoadedPaths = ViewerTextures.EndSession();
            GD.PrintErr($"[Viewer] Assemble failed: {ex.Message}");
            SetStatus($"Assemble error: {ex.Message}");
        }
    }

    private void PopulateAnimPanel()
    {
        if (_animDropdown is null || _animPanel is null) return;
        _animDropdown.Clear();

        var boneCount = _currentSkinned?.BoneCount ?? 0;
        var clips = new (string Label, AnimationClip? Clip)[_currentMotionPaths.Length];
        for (var i = 0; i < _currentMotionPaths.Length; i++)
        {
            var path = _currentMotionPaths[i];
            var filename = Path.GetFileName(path);
            var prefix = i == 0 ? "[idle] " : string.Empty;
            try
            {
                if (!_browser!.Archive.Contains(path))
                {
                    clips[i] = ($"{prefix}{filename}  (unavailable)", null);
                    continue;
                }

                var bytes = _browser.Archive.GetFileContent(path);
                if (AnimationParser.IsBaniVariant(bytes.Span))
                {
                    clips[i] = ($"{prefix}{filename}  (bani variant)", null);
                    continue;
                }

                var clip = AnimationParser.Parse(bytes);
                if (clip is null)
                {
                    clips[i] = ($"{prefix}{filename}  (parse failed)", null);
                    continue;
                }

                var fit = boneCount > 0 && clip.Tracks.Length <= boneCount ? "fits" : "≠rig";
                clips[i] = ($"{prefix}{filename}  ({clip.FrameCount}f, {clip.Tracks.Length}t, {fit})", clip);
            }
            catch
            {
                clips[i] = ($"{prefix}{filename}  (error)", null);
            }
        }

        _parsedClips = clips;

        foreach (var (label, _) in _parsedClips)
            _animDropdown.AddItem(label);

        _animPanel.Visible = _parsedClips.Length > 0;

        if (_frameScrub is not null)
        {
            _frameScrub.MaxValue = 0;
            _frameScrub.SetValueNoSignal(0);
        }

        if (_frameReadout is not null)
            _frameReadout.Text = "frame 0 / 0";
    }

    private void OnAnimSelected(long index)
    {
        if (_avatarParts.Count == 0) return;
        if (index < 0 || index >= _parsedClips.Length) return;
        var clip = _parsedClips[(int)index].Clip;
        foreach (var p in _avatarParts) p.PlayClip(clip);
        if (_frameScrub is not null)
            _frameScrub.MaxValue = clip is not null ? Math.Max(0, (int)clip.FrameCount - 1) : 0;
        GD.Print($"[Viewer] Animation clip selected: index={index} frames={clip?.FrameCount ?? 0}");
    }

    private static bool TryParseAssemble(string spec, out int internalClass, out int variant)
    {
        internalClass = 0;
        variant = 0;
        var bits = spec.Split(':');
        if (bits.Length == 0 || !int.TryParse(bits[0], out internalClass)) return false;
        if (internalClass is < 1 or > 4) return false;
        variant = bits.Length > 1 && int.TryParse(bits[1], out var v)
            ? v
            : CharacterAssembler.DefaultVariant(internalClass);
        return true;
    }
}