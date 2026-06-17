namespace MartialHeroes.Client.Application.Assets;

/// <summary>
/// Reads the authentic typo-keyed <c>[OPENNING] SKIP</c> private-profile integer. Non-zero skips the
/// opening; false/absent proceeds to Opening. spec: Docs/RE/specs/resource_pipeline.md §2.5.
/// </summary>
public interface IOpeningSkipReader
{
    bool ReadSkipOpening();
}