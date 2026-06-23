using Godot;
using MartialHeroes.Assets.Parsers.Effects.Models;
using MartialHeroes.Client.Application.Contracts.Hud;
using MartialHeroes.Client.Domain.Actors.Actors;
using MartialHeroes.Client.Godot.Composition;
using Array = Godot.Collections.Array;

namespace MartialHeroes.Client.Godot.World;


public sealed partial class EffectRenderer : Node3D
{

    private const uint XeffResourceParticleThreshold = 10000;

    private const uint EmitterBillboard = 0;
    private const uint EmitterMesh = 1;
    private const uint EmitterDirectional = 2;

    private const float UvScrollPeriodMs = 5000f;

    private const float EmitterHeightOffset = 0.9f;

    private const string XeffectLstPath = "data/effect/xeffect.lst";

    private const int XeffLstNameLen = 30;

    private const string ParticleEmitterEffPath = "data/effect/particle/particleemitter.eff";

    private readonly Dictionary<ActorKey, LiveEffect> _live = new();


    private RealClientAssets? _assets;

    private CancellationTokenSource? _cts;


    private Dictionary<uint, string>? _effectRegistry;


    private IHudEventHub? _hub;


    private ParticleEmitterTable? _particleEmitterTable;

    private bool _particleEmitterTableAttempted;

    private bool _registryBuildAttempted;


    public override void _Ready()
    {
        GD.Print("[EffectRenderer] _Ready.");

        _assets = RealClientAssets.TryOpen();
        if (_assets is not null)
        {
            GD.Print("[EffectRenderer] VFS available — real .xeff loading enabled.");
            BuildEffectRegistry(_assets);
        }
        else
        {
            GD.Print("[EffectRenderer] VFS unavailable — effects disabled; renders nothing.");
        }
    }

    public override void _Process(double delta)
    {
        if (_hub is not null)
        {
            var reader = _hub.CombatTexts;
            while (reader.TryRead(out var ev))
                _ = ev;
        }

        var deltaMs = delta * 1000.0;

        List<ActorKey>? toRemove = null;
        foreach (var kv in _live)
        {
            var live = kv.Value;
            if (!live.Active)
            {
                toRemove ??= new List<ActorKey>(2);
                toRemove.Add(kv.Key);
                continue;
            }

            live.ElapsedMs += deltaMs;

            if (live.SubEffects is { } subEffects) TickXeffEffect(live, subEffects, deltaMs);
        }

        if (toRemove is not null)
            foreach (var key in toRemove)
                _live.Remove(key);
    }

    public override void _ExitTree()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _assets?.Dispose();
        _assets = null;
    }


    public void Bind(IHudEventHub hub)
    {
        ArgumentNullException.ThrowIfNull(hub);
        _hub = hub;

        GD.Print("[EffectRenderer] Hub bound. Subscribed to CombatTexts channel.");
    }


    private void ClearAllEffects()
    {
        foreach (var live in _live.Values)
            TeardownLiveEffect(live);
        _live.Clear();
    }


    private static ActorKey ResolveActorKey(Node3D actor)
    {
        if (actor is VisualActor va)
            return va.ActorKey;

        var instanceId = actor.GetInstanceId();
        return new ActorKey((uint)(instanceId & 0xFFFF_FFFF), default);
    }


    internal sealed class SubEffectDesc
    {
        public float[] AlphaKeys = [];
        public uint AnimBaseTime;
        public uint AnimFlag;

        public byte AnimLoop;
        public uint AnimStride;
        public float[] DiffuseB = [];
        public float[] DiffuseG = [];

        public float[] DiffuseR = [];

        public uint EmitterType;

        public XeffKeyframe[] Keyframes = [];
        public uint ResourceId;

        public bool ScrollU;
        public bool ScrollV;
        public uint TexCount;

        public string[] TextureNames = [];

        public uint TotalTime;
    }


    private sealed class LiveEffect
    {
        public bool Active = true;

        public bool AmbientAnchorOwned;

        public Node3D Anchor = null!;

        public uint EffectId;
        public double ElapsedMs;

#pragma warning disable CS0649 // field always null; intentional (compat guard, never assigned)
        public GpuParticles3D?[]? GpuParticles;
#pragma warning restore CS0649

        public MeshInstance3D?[]? MeshInstances;

        public GpuParticleSimNode?[]? SimNodes;

        public SubEffectDesc[]? SubEffects;

        public ImageTexture?[][]? Textures;
    }


    internal sealed partial class GpuParticleSimNode : Node3D
    {
        private const double SimStepSec = 0.067;

        private const float BrightnessAlphaFloor = 0.05f;
        private readonly float[] _colA;
        private readonly float[] _colB;
        private readonly float[] _colG;
        private readonly float[] _colR;
        private readonly int[] _delayTick;

        private readonly ParticleEmitterEntry _entry;
        private readonly int[] _lifeTick;
        private readonly MeshInstance3D[] _meshes;

        private readonly float[] _posX;
        private readonly float[] _posY;
        private readonly float[] _posZ;
        private readonly float[] _size;
        private readonly ImageTexture? _texture;
        private readonly float[] _velX;
        private readonly float[] _velY;
        private readonly float[] _velZ;

        private double _accumSec;

        internal GpuParticleSimNode(ParticleEmitterEntry entry, ImageTexture? texture)
        {
            _entry = entry;
            _texture = texture;

            var n = (int)entry.NumFrames;
            _posX = new float[n];
            _posY = new float[n];
            _posZ = new float[n];
            _velX = new float[n];
            _velY = new float[n];
            _velZ = new float[n];
            _size = new float[n];
            _colR = new float[n];
            _colG = new float[n];
            _colB = new float[n];
            _colA = new float[n];
            _lifeTick = new int[n];
            _delayTick = new int[n];
            _meshes = new MeshInstance3D[n];

            for (var i = 0; i < n; i++)
                SpawnParticle(i);
        }

        public override void _Ready()
        {
            for (var i = 0; i < _meshes.Length; i++)
            {
                var mi = BuildParticleMesh(i);
                _meshes[i] = mi;
                AddChild(mi);
            }
        }

        public void Tick(double deltaSec)
        {
            _accumSec += deltaSec;
            while (_accumSec >= SimStepSec)
            {
                _accumSec -= SimStepSec;
                StepAll((float)SimStepSec);
            }

            UpdateMeshes();
        }


        private void SpawnParticle(int i)
        {
            var sr = _entry.SubRecords[i];
            _posX[i] = sr.SpawnPosX;
            _posY[i] = sr.SpawnPosY;
            _posZ[i] = sr.SpawnPosZ;
            _velX[i] = sr.VelocityX;
            _velY[i] = sr.VelocityY;
            _velZ[i] = sr.VelocityZ;
            _size[i] = sr.SizeInit;
            _colR[i] = sr.ColorR;
            _colG[i] = sr.ColorG;
            _colB[i] = sr.ColorB;
            _colA[i] = sr.ColorA;
            _lifeTick[i] = sr.Lifetime + sr.LifeBonus;
            _delayTick[i] = sr.SpawnDelay;
        }


        private void StepAll(float dt)
        {
            for (var i = 0; i < _entry.NumFrames; i++)
            {
                if (_delayTick[i] > 0)
                {
                    _delayTick[i]--;
                    continue;
                }

                if (_lifeTick[i] <= 0)
                {
                    SpawnParticle(i);
                    continue;
                }

                _lifeTick[i]--;

                var sr = _entry.SubRecords[i];

                if (sr.VelocityDamp != 0f)
                {
                    _velX[i] *= sr.VelocityDamp;
                    _velY[i] *= sr.VelocityDamp;
                    _velZ[i] *= sr.VelocityDamp;
                }

                _posX[i] += _velX[i] * dt;
                _posY[i] += _velY[i] * dt;
                _posZ[i] += _velZ[i] * dt;

                _size[i] += sr.SizeRate * dt;
                if (_size[i] < 0f) _size[i] = 0f;

                _colR[i] = Math.Clamp(_colR[i] + sr.ColorRRate * dt, 0f, 255f);
                _colG[i] = Math.Clamp(_colG[i] + sr.ColorGRate * dt, 0f, 255f);
                _colB[i] = Math.Clamp(_colB[i] + sr.ColorBRate * dt, 0f, 255f);

                var alpha = _colA[i] + sr.ColorARate * dt;
                var brightnessFactor =
                    BrightnessAlphaFloor + (1f - BrightnessAlphaFloor) * 1.0f;
                alpha *= brightnessFactor;
                _colA[i] = Math.Clamp(alpha, 0f, 255f);
            }
        }


        private void UpdateMeshes()
        {
            for (var i = 0; i < _meshes.Length; i++)
            {
                var mi = _meshes[i];
                if (!IsInstanceValid(mi)) continue;

                var dormant = _delayTick[i] > 0 || _lifeTick[i] <= 0;
                mi.Visible = !dormant;
                if (dormant) continue;

                mi.Position = new Vector3(_posX[i], _posY[i], _posZ[i]);

                var qw = MathF.Max(_size[i], 0.01f);
                var qh = MathF.Max(_size[i], 0.01f);
                mi.Scale = new Vector3(qw, qh, 1f);

                if (mi.GetSurfaceOverrideMaterial(0) is StandardMaterial3D mat)
                    mat.AlbedoColor = new Color(
                        _colR[i] / 255f,
                        _colG[i] / 255f,
                        _colB[i] / 255f,
                        _colA[i] / 255f);
            }
        }


        private MeshInstance3D BuildParticleMesh(int i)
        {
            var sr = _entry.SubRecords[i];

            const float hw = 0.5f;
            const float hh = 0.5f;

            var arrays = new Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = new Vector3[]
            {
                new(-hw, hh, 0f),
                new(hw, hh, 0f),
                new(hw, -hh, 0f),
                new(-hw, -hh, 0f)
            };
            arrays[(int)Mesh.ArrayType.TexUV] = new Vector2[]
            {
                new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f)
            };
            arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 1, 2, 0, 2, 3 };

            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

            var initColor = new Color(
                sr.ColorR / 255f,
                sr.ColorG / 255f,
                sr.ColorB / 255f,
                sr.ColorA / 255f);

            var mat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = initColor,
                AlbedoTexture = _texture,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Mix,
                BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmaps
            };

            var mi = new MeshInstance3D();
            mi.Mesh = mesh;
            mi.SetSurfaceOverrideMaterial(0, mat);
            mi.Position = new Vector3(sr.SpawnPosX, sr.SpawnPosY, sr.SpawnPosZ);
            mi.Visible = sr.SpawnDelay == 0;
            return mi;
        }
    }
}