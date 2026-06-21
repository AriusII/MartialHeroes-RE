// pktinspect — opcode catalogue + frame decoder over the production Network.Protocol wire layer.
//
//   pktinspect opcodes [--major N] [--c2s | --s2c]   list known (major:minor) -> struct + wire size
//   pktinspect decode  <hex…>                         parse an 8-byte FrameHeader + dump the struct fields
//
// `<hex…>` accepts any spacing/separators: "0800 0400 0100", "08 00 04 00 …", "08:00:…" — all non-hex
// characters are ignored. The catalogue is reflected from the [PacketOpcode]-tagged structs at startup,
// so it always matches the actual wire structs.

using MartialHeroes.Tools.PacketInspect;

if (args.Length == 0)
{
    PrintUsage();
    return 2;
}

switch (args[0])
{
    case "opcodes":
        return CmdOpcodes(args[1..]);
    case "decode":
        return CmdDecode(args[1..]);
    case "-h" or "--help" or "help":
        PrintUsage();
        return 0;
    default:
        Console.Error.WriteLine($"unknown command '{args[0]}'.");
        PrintUsage();
        return 2;
}

static int CmdOpcodes(string[] args)
{
    int? major = null;
    string? dir = null;
    for (var i = 0; i < args.Length; i++)
        switch (args[i])
        {
            case "--major" when i + 1 < args.Length && ushort.TryParse(args[++i], out var m):
                major = m;
                break;
            case "--c2s": dir = "C2S"; break;
            case "--s2c": dir = "S2C"; break;
        }

    IEnumerable<OpcodeEntry> rows = OpcodeCatalog.All;
    if (major is { } mj) rows = rows.Where(e => e.Major == mj);
    if (dir is { } d) rows = rows.Where(e => e.Direction == d);

    var list = rows.ToList();
    Console.WriteLine($"{"opcode",-9}  {"packed",-9}  {"dir",-4}  {"size",-6}  struct");
    Console.WriteLine($"{"------",-9}  {"------",-9}  {"---",-4}  {"----",-6}  ------");
    foreach (var e in list)
    {
        var size = e.WireSize is { } w ? $"{w}{(e.SizeKind == "WireSize" ? "" : "+")}" : "var";
        Console.WriteLine($"{e.Major + "/" + e.Minor,-9}  0x{e.Packed:X7}  {e.Direction,-4}  {size,-6}  {e.Name}");
    }

    Console.WriteLine();
    Console.WriteLine(
        $"{list.Count} opcode(s){(major is null && dir is null ? "" : " (filtered)")} of {OpcodeCatalog.All.Count} catalogued.");
    return 0;
}

static int CmdDecode(string[] args)
{
    if (args.Length == 0)
    {
        Console.Error.WriteLine("decode: needs a hex frame. e.g. decode 0800 0400 0100");
        return 2;
    }

    var frame = ParseHex(string.Concat(args));
    if (frame is null)
    {
        Console.Error.WriteLine("decode: hex must be whole bytes (an even number of hex digits).");
        return 2;
    }

    FrameDecoder.Decode(frame);
    return 0;
}

// Strips every non-hex-digit, then parses pairs. Returns null on an odd digit count.
static byte[]? ParseHex(string s)
{
    var digits = s.Length <= 1024 ? stackalloc char[s.Length] : new char[s.Length];
    var n = 0;
    foreach (var c in s)
        if (Uri.IsHexDigit(c))
            digits[n++] = c;

    if (n % 2 != 0)
        return null;

    var bytes = new byte[n / 2];
    for (var i = 0; i < bytes.Length; i++)
        bytes[i] = (byte)((Uri.FromHex(digits[i * 2]) << 4) | Uri.FromHex(digits[i * 2 + 1]));
    return bytes;
}

static void PrintUsage()
{
    Console.Error.WriteLine(
        "pktinspect — opcode catalogue + frame decoder over the production wire layer.\n" +
        "  opcodes [--major N] [--c2s|--s2c]   list known (major:minor) -> struct + wire size\n" +
        "  decode  <hex…>                      parse an 8-byte FrameHeader + dump the struct's fields\n" +
        "\n" +
        "  hex accepts any spacing/separators; non-hex characters are ignored.\n" +
        "  examples:\n" +
        "    pktinspect opcodes --s2c\n" +
        "    pktinspect decode 0800 0007 ...");
}