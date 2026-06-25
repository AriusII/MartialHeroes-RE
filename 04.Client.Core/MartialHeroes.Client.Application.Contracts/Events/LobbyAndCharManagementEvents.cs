using System.Collections.Immutable;

namespace MartialHeroes.Client.Application.Contracts.Events;

public readonly record struct ServerListEntryView(
    short ServerId,
    short StatusCode,
    short Load,
    short OpenTime,
    ServerLoadBand LoadHint,
    ServerStatusHint StatusHint,
    string DisplayName)
{
    public bool IsSelectable =>
        StatusCode == 0 && Load < 2400;
}

public enum ServerLoadBand
{
    Light,

    Moderate,

    Busy,

    Full
}

public enum ServerStatusHint
{
    Invalid,

    Normal,

    Special,

    Caption
}

public enum ServerListOutcome
{
    Empty,

    Failed,

    Populated
}

public sealed record ServerListReceivedEvent(
    ServerListOutcome Outcome,
    ImmutableArray<ServerListEntryView> Servers) : IClientEvent;

public sealed record ChannelEndpointResolvedEvent(
    ushort ServerId,
    string Host,
    int Port) : IClientEvent;

public enum CharManageSubtype
{
    GenericRefresh,

    RenameApplied,

    DeleteConfirm,

    Other
}

public sealed record CharManageResultEvent(
    bool Success,
    CharManageSubtype Subtype,
    byte RawSubtype,
    uint ReadyTime,
    int AccountCharacterCount) : IClientEvent;

public sealed record CharRenameResultEvent(
    bool Success,
    string NewName,
    byte ErrorCode) : IClientEvent;

public sealed record CharActionResultEvent(
    uint ResultCode,
    bool IsRejection) : IClientEvent;

public sealed record CharStatusBytesByNameEvent(
    bool HasCustomText,
    byte StatusCode,
    byte StatusValue,
    byte Level) : IClientEvent;