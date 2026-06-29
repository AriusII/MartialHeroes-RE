using System;
using System.Collections.Generic;
using MartialHeroes.Assets.Parsers.DataTables;
using MartialHeroes.Explorer.Files.Models;

namespace MartialHeroes.Explorer.Files.Services.Decoders;

public sealed class ScrDecoder : IFormatDecoder
{
    private const int MaxRows = 100_000;

    private static readonly HexDumpDecoder Fallback = new();

    public DecodedDocument Decode(VfsFileNode node, ReadOnlyMemory<byte> bytes)
    {
        try
        {
            if (Is(node, "items.scr"))
                return Items(node, bytes);
            if (Is(node, "events.scr"))
                return Events(node, bytes);
            if (Is(node, "quests.scr"))
                return Quests(node, bytes);
            if (Is(node, "npc.scr"))
                return Npc(node, bytes);
            if (Is(node, "autoquestion_cl.scr"))
                return AutoQuestion(node, bytes);
            if (Is(node, "exp.scr"))
                return Exp(node, bytes);
            if (Is(node, "userlevel.scr"))
                return UserLevel(node, bytes);
            if (Is(node, "userpoint.scr"))
                return UserPoint(node, bytes);
            if (Is(node, "users.scr"))
                return Users(node, bytes);
            if (Is(node, "skills.scr"))
                return Skills(node, bytes);
            if (Is(node, "mobs.scr"))
                return Mobs(node, bytes);

            return Fallback.Decode(node, bytes);
        }
        catch (Exception ex)
        {
            return DecoderFallback.AsText(node, bytes, ex, Fallback);
        }
    }

    private static bool Is(VfsFileNode node, string name)
    {
        return string.Equals(node.Name, name, StringComparison.OrdinalIgnoreCase);
    }

    private static DecodedDocument Items(VfsFileNode node, ReadOnlyMemory<byte> bytes)
    {
        var rows = new List<TableRow>();
        var total = 0;
        foreach (var r in ItemsScrParser.Parse(bytes))
        {
            total++;
            if (rows.Count >= MaxRows)
                continue;
            rows.Add(new TableRow
            {
                Cells =
                [
                    total.ToString(),
                    r.ItemUid.ToString(),
                    r.ItemName,
                    r.ModelRefKey.ToString(),
                    r.AnimRefKey.ToString(),
                    r.EffectCount.ToString()
                ]
            });
        }

        var summary = $"{total:N0} item records · 548-byte block + N×8 effects · CP949";
        if (total > rows.Count)
            summary += $" · capped (showing {rows.Count:N0})";

        return new TableDocument
        {
            Title = node.Name,
            Summary = summary,
            Columns = ["#", "item uid", "name", "model ref", "anim ref", "effects"],
            Rows = rows
        };
    }

    private static DecodedDocument Events(VfsFileNode node, ReadOnlyMemory<byte> bytes)
    {
        var records = EventsScrParser.Parse(bytes);
        var rows = new List<TableRow>(records.Length);
        for (var i = 0; i < records.Length && i < MaxRows; i++)
        {
            var r = records[i];
            rows.Add(new TableRow
            {
                Cells =
                [
                    (i + 1).ToString(),
                    r.EventId.ToString(),
                    r.EventType.ToString(),
                    r.DayCount.ToString(),
                    r.ModeFlag.ToString(),
                    r.RateArray.Count.ToString(),
                    r.ActorArray.Count.ToString()
                ]
            });
        }

        return new TableDocument
        {
            Title = node.Name,
            Summary = $"{records.Length:N0} event records · 520-byte stride",
            Columns = ["#", "event id", "type", "day count", "mode", "rates", "actors"],
            Rows = rows
        };
    }

    private static DecodedDocument Quests(VfsFileNode node, ReadOnlyMemory<byte> bytes)
    {
        var records = QuestsScrParser.Parse(bytes);
        var rows = new List<TableRow>(records.Length);
        for (var i = 0; i < records.Length && i < MaxRows; i++)
        {
            var r = records[i];
            rows.Add(new TableRow
            {
                Cells =
                [
                    (i + 1).ToString(),
                    r.QuestId.ToString(),
                    r.Category.ToString(),
                    r.QuestName
                ]
            });
        }

        return new TableDocument
        {
            Title = node.Name,
            Summary = $"{records.Length:N0} populated quest records · 4960-byte stride · CP949",
            Columns = ["#", "quest id", "category", "name"],
            Rows = rows
        };
    }

    private static DecodedDocument Npc(VfsFileNode node, ReadOnlyMemory<byte> bytes)
    {
        var records = NpcScrParser.Parse(bytes);
        var rows = new List<TableRow>(records.Length);
        for (var i = 0; i < records.Length && i < MaxRows; i++)
        {
            var r = records[i];
            rows.Add(new TableRow
            {
                Cells =
                [
                    (i + 1).ToString(),
                    r.Id.ToString(),
                    r.Kind.ToString(),
                    r.Job.ToString(),
                    string.Join(" | ", r.NameSlots)
                ]
            });
        }

        return new TableDocument
        {
            Title = node.Name,
            Summary = $"{records.Length:N0} npc string records · 404-byte stride · CP949",
            Columns = ["#", "id", "kind", "job", "name slots"],
            Rows = rows
        };
    }

    private static DecodedDocument AutoQuestion(VfsFileNode node, ReadOnlyMemory<byte> bytes)
    {
        var records = AutoQuestionParser.Parse(bytes);
        var rows = new List<TableRow>(records.Length);
        for (var i = 0; i < records.Length && i < MaxRows; i++)
        {
            var r = records[i];
            rows.Add(new TableRow
            {
                Cells =
                [
                    (i + 1).ToString(),
                    r.QuestionId.ToString(),
                    r.QuestionText,
                    r.AnswerPrompt
                ]
            });
        }

        return new TableDocument
        {
            Title = node.Name,
            Summary = $"{records.Length:N0} quiz records · 92-byte stride · CP949",
            Columns = ["#", "question id", "question", "answer prompt"],
            Rows = rows
        };
    }

    private static DecodedDocument Exp(VfsFileNode node, ReadOnlyMemory<byte> bytes)
    {
        var records = ConfigTableParser.ParseExpScr(bytes);
        var rows = new List<TableRow>(records.Length);
        for (var i = 0; i < records.Length && i < MaxRows; i++)
        {
            var r = records[i];
            rows.Add(new TableRow
            {
                Cells =
                [
                    (i + 1).ToString(),
                    r.Level.ToString(),
                    r.Const64.ToString(),
                    r.PrimaryExp.ToString(),
                    r.SecondaryExp.ToString(),
                    r.TertiaryExp.ToString()
                ]
            });
        }

        return new TableDocument
        {
            Title = node.Name,
            Summary = $"{records.Length:N0} level-xp records · 20-byte stride",
            Columns = ["#", "level", "const64", "primary xp", "secondary xp", "tertiary xp"],
            Rows = rows
        };
    }

    private static DecodedDocument UserLevel(VfsFileNode node, ReadOnlyMemory<byte> bytes)
    {
        var records = ConfigTableParser.ParseUserLevelScr(bytes);
        var rows = new List<TableRow>(records.Length);
        for (var i = 0; i < records.Length && i < MaxRows; i++)
        {
            var r = records[i];
            rows.Add(new TableRow
            {
                Cells =
                [
                    (i + 1).ToString(),
                    r.Level.ToString(),
                    r.TierStepA.ToString(),
                    r.TierStepB.ToString(),
                    r.DivisorC.ToString(),
                    string.Join(",", Array.ConvertAll(r.StatScalePositive, v => v.ToString("0.##"))),
                    string.Join(",", Array.ConvertAll(r.StatScaleNegative, v => v.ToString("0.##")))
                ]
            });
        }

        return new TableDocument
        {
            Title = node.Name,
            Summary = $"{records.Length:N0} level-scale records · 60-byte stride",
            Columns = ["#", "level", "step a", "step b", "divisor", "scale +", "scale -"],
            Rows = rows
        };
    }

    private static DecodedDocument UserPoint(VfsFileNode node, ReadOnlyMemory<byte> bytes)
    {
        var records = ConfigTableParser.ParseUserPointScr(bytes);
        var rows = new List<TableRow>(records.Length);
        for (var i = 0; i < records.Length && i < MaxRows; i++)
        {
            var r = records[i];
            rows.Add(new TableRow
            {
                Cells =
                [
                    (i + 1).ToString(),
                    r.Key.ToString(),
                    r.StatGroup1Gain.ToString(),
                    r.StatGroup1Cumulative.ToString(),
                    r.StatGroup2Gain.ToString(),
                    r.StatGroup2Cumulative.ToString()
                ]
            });
        }

        return new TableDocument
        {
            Title = node.Name,
            Summary = $"{records.Length:N0} stat-point records · 32-byte stride",
            Columns = ["#", "key", "g1 gain", "g1 cumulative", "g2 gain", "g2 cumulative"],
            Rows = rows
        };
    }

    private static DecodedDocument Users(VfsFileNode node, ReadOnlyMemory<byte> bytes)
    {
        var block = ConfigTableParser.ParseUsersScr(bytes);
        var rows = new List<TableRow>(block.ClassBlocks.Length);
        for (var i = 0; i < block.ClassBlocks.Length; i++)
        {
            var b = block.ClassBlocks[i];
            rows.Add(new TableRow
            {
                Cells =
                [
                    (i + 1).ToString(),
                    b.ClassId.ToString(),
                    string.Join(",", Array.ConvertAll(b.StatGroupA, v => v.ToString("0.##"))),
                    string.Join(",", Array.ConvertAll(b.ClassSpecificRatios, v => v.ToString("0.##")))
                ]
            });
        }

        return new TableDocument
        {
            Title = node.Name,
            Summary = "496-byte single structure · 4 class windows",
            Columns = ["#", "class id", "stat group a", "class ratios"],
            Rows = rows
        };
    }

    private static DecodedDocument Skills(VfsFileNode node, ReadOnlyMemory<byte> bytes)
    {
        var records = ConfigTableParser.ParseSkillsScr(bytes);
        var rows = new List<TableRow>(records.Length);
        for (var i = 0; i < records.Length && i < MaxRows; i++)
        {
            var r = records[i];
            rows.Add(new TableRow
            {
                Cells =
                [
                    (i + 1).ToString(),
                    r.TrailingCount.ToString(),
                    r.RawRecord.Length.ToString()
                ]
            });
        }

        return new TableDocument
        {
            Title = node.Name,
            Summary = $"{records.Length:N0} skill records · 1504-byte main + N×8 trailing",
            Columns = ["#", "trailing count", "main bytes"],
            Rows = rows
        };
    }

    private static DecodedDocument Mobs(VfsFileNode node, ReadOnlyMemory<byte> bytes)
    {
        var records = ConfigTableParser.ParseMobsScr(bytes);
        var rows = new List<TableRow>(records.Length);
        for (var i = 0; i < records.Length && i < MaxRows; i++)
        {
            var r = records[i];
            rows.Add(new TableRow
            {
                Cells =
                [
                    (i + 1).ToString(),
                    r.Id.ToString(),
                    r.Type.ToString(),
                    r.MobLevel.ToString(),
                    r.SpawnTimer.ToString()
                ]
            });
        }

        return new TableDocument
        {
            Title = node.Name,
            Summary = $"{records.Length:N0} mob records · 488-byte stride · CP949",
            Columns = ["#", "id", "type", "level", "spawn timer"],
            Rows = rows
        };
    }
}