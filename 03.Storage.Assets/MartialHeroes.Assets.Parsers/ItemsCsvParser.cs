using System.Text;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parser for <c>data/script/items.csv</c> — the plain-text item catalogue.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/config_tables.md §4 items.csv
/// <para>
/// Format: CP949 encoding (no BOM), LF line endings, no header row, 139 columns per row.
/// RFC 4180 quoting (description fields may contain commas and are double-quote quoted).
/// <c>\\</c> inside field text represents an in-game newline.
/// Total rows: 89,712 confirmed. Columns: 139 confirmed.
/// spec: Docs/RE/formats/config_tables.md §4.1: CONFIRMED.
/// </para>
/// <para>
/// ZERO rendering/engine dependencies.
/// </para>
/// </remarks>
public static class ItemsCsvParser
{
    // Column count: 139 (0-based indices 0..138). CONFIRMED.
    // spec: Docs/RE/formats/config_tables.md §4.1 — "Columns per row: 139 (fixed, 0-based 0..138)": CONFIRMED.
    private const int ExpectedColumnCount = 139;

    /// <summary>
    /// Parses <c>data/script/items.csv</c> into an array of <see cref="ItemCsvRow"/> records.
    /// </summary>
    /// <param name="data">Raw file bytes (CP949 encoding, LF line endings).</param>
    /// <returns>Array of decoded item rows, one per CSV data row.</returns>
    /// <remarks>
    /// CP949 encoding: CONFIRMED. LF line endings: CONFIRMED. No header row: CONFIRMED.
    /// spec: Docs/RE/formats/config_tables.md §4.1.
    /// RFC 4180 quoting required for description fields that contain commas: CONFIRMED.
    /// </remarks>
    public static ItemCsvRow[] Parse(ReadOnlyMemory<byte> data) =>
        Parse(data.Span);

    /// <inheritdoc cref="Parse(ReadOnlyMemory{byte})"/>
    public static ItemCsvRow[] Parse(ReadOnlySpan<byte> span)
    {
        // Decode as CP949.
        // spec: Docs/RE/formats/config_tables.md §4.1 — "Encoding: CP949 / EUC-KR (no BOM)": CONFIRMED.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cp949 = Encoding.GetEncoding(949);
        string text = cp949.GetString(span);

        return ParseText(text);
    }

    /// <summary>
    /// Overload accepting pre-decoded text (for testing with known UTF-16 strings).
    /// </summary>
    public static ItemCsvRow[] ParseText(string text)
    {
        var rows = new List<ItemCsvRow>(89712); // pre-size for expected row count
        var fields = new List<string>(ExpectedColumnCount);
        int pos = 0;

        while (pos < text.Length)
        {
            // Skip empty lines (bare LF after CRLF or lone LF).
            if (text[pos] == '\n')
            {
                pos++;
                continue;
            }

            if (text[pos] == '\r' && pos + 1 < text.Length && text[pos + 1] == '\n')
            {
                pos += 2;
                continue;
            }

            // Parse one RFC 4180 CSV line.
            // spec: Docs/RE/formats/config_tables.md §4.1 — "Quoting: RFC 4180 double-quote quoting": CONFIRMED.
            fields.Clear();
            ParseLine(text, ref pos, fields);

            if (fields.Count == 0) continue;
            if (fields.Count != ExpectedColumnCount)
                throw new InvalidDataException(
                    $"items.csv parse error: expected {ExpectedColumnCount} columns, got {fields.Count} " +
                    $"at character offset {pos}. " +
                    "spec: Docs/RE/formats/config_tables.md §4.1.");

            rows.Add(BuildRow(fields));
        }

        return rows.ToArray();
    }

    private static void ParseLine(string text, ref int pos, List<string> fields)
    {
        int len = text.Length;

        while (pos < len)
        {
            char c = text[pos];

            if (c == '\n')
            {
                pos++;
                return;
            }

            if (c == '\r')
            {
                if (pos + 1 < len && text[pos + 1] == '\n') pos++;
                pos++;
                return;
            }

            // RFC 4180 quoted field.
            if (c == '"')
            {
                pos++; // skip opening quote
                var sb = new System.Text.StringBuilder();
                while (pos < len)
                {
                    char fc = text[pos];
                    if (fc == '"')
                    {
                        pos++;
                        if (pos < len && text[pos] == '"')
                        {
                            sb.Append('"');
                            pos++;
                        } // escaped ""
                        else break; // closing quote
                    }
                    else
                    {
                        sb.Append(fc);
                        pos++;
                    }
                }

                fields.Add(sb.ToString());
                // Expect comma or line-end after closing quote.
                if (pos < len && text[pos] == ',') pos++;
            }
            else
            {
                // Unquoted field — read until comma or line-end.
                int start = pos;
                while (pos < len && text[pos] != ',' && text[pos] != '\n' && text[pos] != '\r')
                    pos++;
                fields.Add(text.Substring(start, pos - start));
                if (pos < len && text[pos] == ',') pos++;
            }
        }
    }

    private static ItemCsvRow BuildRow(List<string> f)
    {
        // spec: Docs/RE/formats/config_tables.md §4.3 Column index table.
        // Only typed columns with CONFIRMED or HIGH confidence are decoded to their stated types.
        // UNVERIFIED columns are exposed raw as strings.

        return new ItemCsvRow
        {
            // ── Identity (cols 0–6) ─────────────────────────────────────────
            // col0: name_cp949 string CONFIRMED
            NameCp949 = f[0],
            // col1: item_id uint32 CONFIRMED
            ItemId = ParseUInt(f[1]),
            // col2: description_cp949 string CONFIRMED (\\=in-game newline preserved)
            DescriptionCp949 = f[2],
            // col3: linked_item_id uint32 HIGH
            LinkedItemId = ParseUInt(f[3]),
            // col4: base_ref_id uint32 HIGH
            BaseRefId = ParseUInt(f[4]),
            // col5: secondary_ref_id uint32 HIGH
            SecondaryRefId = ParseUInt(f[5]),
            // col6: item_subtype uint32 CONFIRMED
            ItemSubtype = ParseUInt(f[6]),

            // ── Flags and meta (cols 7–18) ──────────────────────────────────
            BonusFlagA = ParseByte(f[7]),
            BonusFlagB = ParseByte(f[8]),
            // col9: reserved_09 uint8 HIGH (always 0)
            // col10: enhancement_size uint8 HIGH
            EnhancementSize = ParseByte(f[10]),
            // col16: sell_price uint32 CONFIRMED
            SellPrice = ParseUInt(f[16]),
            // col17: npc_purchaseable uint8 HIGH
            NpcPurchaseable = ParseByte(f[17]),
            // col18: enabled uint8 CONFIRMED
            Enabled = ParseByte(f[18]),

            // ── Stacking, tier, durability (cols 19–23) ─────────────────────
            // col19: max_stack uint16 CONFIRMED
            MaxStack = ParseUShort(f[19]),
            // col22: item_tier_rank uint16 CONFIRMED
            ItemTierRank = ParseUShort(f[22]),
            // col23: max_durability uint16 HIGH
            MaxDurability = ParseUShort(f[23]),

            // ── Required stats (cols 24–28) ─────────────────────────────────
            ReqStr = ParseUShort(f[24]),
            ReqCon = ParseUShort(f[25]),
            ReqAgi = ParseUShort(f[26]),
            ReqInt = ParseUShort(f[27]),
            ReqChi = ParseUShort(f[28]),

            // ── Class restriction flags (cols 29–32) ────────────────────────
            ClassYi = ParseByte(f[29]),
            ClassYe = ParseByte(f[30]),
            ClassIn = ParseByte(f[31]),
            ClassJi = ParseByte(f[32]),

            // ── Enchant and socket block (cols 47–48) ───────────────────────
            EnchantLevel = ParseByte(f[47]),
            GemPower = ParseByte(f[48]),

            // ── Bonus stat block A (cols 64–65, 68) ─────────────────────────
            BonusAtk = ParseUInt(f[64]),
            BonusHp = ParseUInt(f[65]),
            BonusExtAtk = ParseUInt(f[68]),

            // ── Float rate block (cols 75, 78) ──────────────────────────────
            AttackSpeed = ParseFloat(f[75]),
            DodgeRate = ParseFloat(f[78]),

            // ── Bonus stat block B (cols 84–87, 90, 93–96) ──────────────────
            BonusChi = ParseUInt(f[84]),
            WeaponStatA = ParseUInt(f[85]),
            WeaponStatB = ParseUInt(f[86]),
            MinAttack = ParseUInt(f[87]),
            MaxAttack = ParseUInt(f[90]),
            BonusDefenseA = ParseUInt(f[93]),
            PhysDefense = ParseUInt(f[94]),
            ArmorDefense = ParseUInt(f[96]),

            // ── Model / visual IDs (cols 117–118) ───────────────────────────
            ModelSetId = ParseUShort(f[117]),
            ModelType = ParseByte(f[118]),

            // ── Consumable block (cols 112, 113, 119–120, 127–130) ──────────
            DurationMinutes = ParseUInt(f[112]),
            ExpireMode = ParseByte(f[113]),
            ConsumableValue = ParseUInt(f[119]),
            IsConsumable = ParseByte(f[120]),
            GemCategory = ParseByte(f[127]),
            EquippableFlag = ParseByte(f[128]),
            HasEffect = ParseByte(f[129]),
            EffectType = ParseByte(f[130]),
            EffectStrength = ParseUShort(f[131]),

            // ── All 139 raw columns (zero-alloc for consumers that need untyped access) ──
            RawColumns = f.ToArray(),
        };
    }

    private static uint ParseUInt(string s) =>
        uint.TryParse(s.Trim(), out var v) ? v : 0;

    private static ushort ParseUShort(string s) =>
        ushort.TryParse(s.Trim(), out var v) ? v : (ushort)0;

    private static byte ParseByte(string s) =>
        byte.TryParse(s.Trim(), out var v) ? v : (byte)0;

    private static float ParseFloat(string s) =>
        float.TryParse(s.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v
            : 0f;
}