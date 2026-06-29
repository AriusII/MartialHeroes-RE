namespace MartialHeroes.Client.Application.Social;

public sealed class GuildRosterStore
{
    public short GuildId { get; private set; }

    public string GuildName { get; private set; } = string.Empty;

    public int MemberCount { get; private set; }

    public bool HasGuild { get; private set; }

    public void SetGuild(short guildId, string guildName, int memberCount)
    {
        GuildId = guildId;
        GuildName = guildName;
        MemberCount = memberCount;
        HasGuild = true;
    }

    public void Clear()
    {
        GuildId = 0;
        GuildName = string.Empty;
        MemberCount = 0;
        HasGuild = false;
    }
}
