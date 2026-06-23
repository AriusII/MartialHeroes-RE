using MartialHeroes.Assets.Mapping;
using MartialHeroes.Client.Application.World;

namespace MartialHeroes.Client.Presentation.Adapters;

public sealed class AssembledAreaViewAdapter(AssembledArea area) : IAssembledAreaView
{
    private readonly AssembledArea _area = area ?? throw new ArgumentNullException(nameof(area));
    public int AreaId => _area.AreaId;
    public int CellKeyCount => _area.CellKeys.Count;

    public IReadOnlyList<AreaSpawnDescriptor> Spawns
    {
        get
        {
            if (field is not null) return field;

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

            field = projected;
            return field;
        }
    }
}