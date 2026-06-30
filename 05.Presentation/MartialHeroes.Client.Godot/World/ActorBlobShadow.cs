using Godot;
using MartialHeroes.Client.Godot.Composition;

namespace MartialHeroes.Client.Godot.World;

public sealed partial class ActorBlobShadow : MeshInstance3D
{
    private const string BlobTexturePath = "data/effect/tex/shadow.dds";

    private const float GroundLift = 0.4f;

    private const float NativeShadowMaxDistance = 2000f;

    private const float MinHalfExtent = 0.25f;

    private const float DefaultHalfExtent = 1.5f;

    private bool _available;

    private float _halfExtent = DefaultHalfExtent;

    private StandardMaterial3D? _material;

    private PlaneMesh? _plane;

    public bool Available => _available;

    public void Configure(RealClientAssets? assets)
    {
        Name = "BlobShadow";
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        Visible = false;
        Position = new Vector3(0f, GroundLift, 0f);

        if (assets is null || !assets.Contains(BlobTexturePath))
        {
            _available = false;
            GD.Print($"[ActorBlobShadow] '{BlobTexturePath}' absent from VFS — blob fallback shadow disabled " +
                     "(no fabricated texture). spec: Docs/RE/structs/shadow_projector.md (blob-quad path, +84 blob_texture).");
            return;
        }

        var tex = assets.LoadTexture(BlobTexturePath);
        if (tex is null)
        {
            _available = false;
            GD.Print($"[ActorBlobShadow] '{BlobTexturePath}' failed to decode — blob fallback shadow disabled. " +
                     "spec: Docs/RE/structs/shadow_projector.md (blob-quad path).");
            return;
        }

        _material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Mix,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            DisableFog = true,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps,
            AlbedoColor = new Color(0f, 0f, 0f, 0.6f),
            AlbedoTexture = tex
        };

        _plane = new PlaneMesh
        {
            Orientation = PlaneMesh.OrientationEnum.Y,
            Size = new Vector2(_halfExtent * 2f, _halfExtent * 2f),
            SubdivideWidth = 0,
            SubdivideDepth = 0,
            Material = _material
        };

        Mesh = _plane;
        _available = true;

        GD.Print("[ActorBlobShadow] Blob ground-quad ready (horizontal +Y PlaneMesh, lifted +0.4, unshaded alpha mix, " +
                 "cull off, fog off, cast-shadow off) bound to shadow.dds — serves as the FAR / OPTION_SHADOW=3 fallback " +
                 "so it never stacks with the native DirectionalLight3D shadow on near actors. " +
                 "spec: Docs/RE/structs/shadow_projector.md (ActorShadow_DrawBlobQuads; near=projected / far=blob handoff).");
    }

    public void SetFootprintHalfExtent(float halfExtent)
    {
        _halfExtent = halfExtent > MinHalfExtent ? halfExtent : DefaultHalfExtent;
        if (_plane is not null)
            _plane.Size = new Vector2(_halfExtent * 2f, _halfExtent * 2f);
    }

    public void UpdateState(float planarDistanceToCamera, int optionShadowMode)
    {
        if (!_available)
        {
            if (Visible) Visible = false;
            return;
        }

        var show = optionShadowMode == 3 || planarDistanceToCamera > NativeShadowMaxDistance;
        if (Visible != show) Visible = show;
    }
}
