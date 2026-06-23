using MartialHeroes.Assets.Mapping;
using MartialHeroes.Client.Application.World;

namespace MartialHeroes.Client.Presentation.Adapters;

public sealed class AssembledAreaViewAdapter : IAssembledAreaView
{
    private readonly AssembledArea _area;

    private IReadOnlyList<AreaSpawnDescriptor>? _spawns;

    public AssembledAreaViewAdapter(AssembledArea area)
    {
        _area = area ?? throw new ArgumentNullException(nameof(area));
    }

    public int AreaId => _area.AreaId;

    public int CellKeyCount => _area.CellKeys.Count;

    public IReadOnlyList<AreaSpawnDescriptor> Spawns
    {
        get
        {
            if (_spawns is not null) return _spawns;

            var src = _area.Spawns;
            var projected = new AreaSpawnDescriptor[src.Count];
            for (var i = 0; i < src.Count; i++)
            {
                var s = src[i];
                projected[i] = new AreaSpawnDescriptor(
                    s.WorldX,
                    s.WorldZ,
                    s.Yaw,
                    s.VisualId,
                    s.IsNpc);
            }

            _spawns = projected;
            return _spawns;
        }
    }
}