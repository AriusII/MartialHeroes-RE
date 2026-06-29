namespace MartialHeroes.Client.Application.Assets;

public interface ICatalogueAssembler
{
    bool TryAssemble(string logicalPath);
}