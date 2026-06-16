using System.Globalization;
using System.Text;
using MartialHeroes.Assets.Parsers.Models;

namespace MartialHeroes.Assets.Parsers;

/// <summary>
/// Parser for <c>data/script/items.csv</c> — the flat comma-delimited item catalogue.
/// </summary>
/// <remarks>
/// spec: Docs/RE/formats/items_csv.md
/// <para>
/// Format: CP949 encoding (no BOM), LF-only line endings, no header row.
/// HAZARD A — item_name (col 0) and item_description (col 2) are unquoted CP949 strings that
/// may contain literal comma characters. A naive Split(',') corrupts column alignment.
/// CORRECT rule: numeric-anchor field splitting (spec: Docs/RE/formats/items_csv.md §2.HAZARD-A).
/// spec: Docs/RE/formats/items_csv.md §2 — Hazard A (embedded commas): CONFIRMED; CRITICAL.
/// </para>
/// <para>
/// HAZARD B — at least one numeric column is a float (period decimal separator, invariant culture).
/// A tokenizer that treats period as a separator inserts a phantom column for that row.
/// CORRECT rule: parse numeric tail with InvariantCulture; treat tokens containing a period as float.
/// spec: Docs/RE/formats/items_csv.md §2 — Hazard B (float column): CONFIRMED; HIGH.
/// </para>
/// <para>
/// LF-only line endings: split on '\n'; trim trailing '\r' defensively.
/// spec: Docs/RE/formats/items_csv.md §2 — Secondary note (LF-only): CONFIRMED; MEDIUM.
/// </para>
/// <para>
/// ZERO rendering/engine dependencies.
/// </para>
/// </remarks>
public static class ItemsCsvParser
{
    /// <summary>
    /// Parses <c>data/script/items.csv</c> into an array of <see cref="ItemCsvRow"/> records.
    /// </summary>
    /// <param name="data">Raw file bytes (CP949 encoding, LF-only line endings).</param>
    /// <returns>Array of decoded item rows, one per data line.</returns>
    /// <remarks>
    /// spec: Docs/RE/formats/items_csv.md §Identification — "Encoding: CP949": CONFIRMED.
    /// spec: Docs/RE/formats/items_csv.md §Identification — "Line ending: LF only": CONFIRMED.
    /// spec: Docs/RE/formats/items_csv.md §Identification — "Header row: NONE": CONFIRMED.
    /// </remarks>
    public static ItemCsvRow[] Parse(ReadOnlyMemory<byte> data) =>
        Parse(data.Span);

    /// <inheritdoc cref="Parse(ReadOnlyMemory{byte})"/>
    public static ItemCsvRow[] Parse(ReadOnlySpan<byte> span)
    {
        // Decode as CP949.
        // spec: Docs/RE/formats/items_csv.md §Identification — "Encoding: CP949 / EUC-KR": CONFIRMED.
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
        var rows = new List<ItemCsvRow>();

        // Split on LF only (CONFIRMED line ending).
        // spec: Docs/RE/formats/items_csv.md §Identification — "Line ending: LF only (0x0A)": CONFIRMED.
        // Trim trailing \r defensively in case a CRLF crept in.
        // spec: Docs/RE/formats/items_csv.md §2 — Secondary note (LF-only line endings): CONFIRMED.
        int start = 0;
        int len = text.Length;
        while (start < len)
        {
            int lf = text.IndexOf('\n', start);
            int lineEnd = lf >= 0 ? lf : len;
            ReadOnlySpan<char> lineSpan = text.AsSpan(start, lineEnd - start);

            // Trim trailing \r.
            if (lineSpan.Length > 0 && lineSpan[^1] == '\r')
                lineSpan = lineSpan[..^1];

            if (lineSpan.Length > 0)
            {
                string line = new string(lineSpan);
                ItemCsvRow? row = ParseLine(line);
                if (row is not null)
                    rows.Add(row);
            }

            start = lf >= 0 ? lf + 1 : len;
        }

        return rows.ToArray();
    }

    /// <summary>
    /// Parses one line from items.csv using the numeric-anchor split rule.
    /// Returns null when the line cannot produce a valid record (e.g. no numeric id found).
    /// </summary>
    /// <remarks>
    /// Implements the hazard-safe parsing recipe from:
    /// spec: Docs/RE/formats/items_csv.md §3 — Implementable parsing recipe (hazard-safe).
    /// <para>
    /// Step 1: Split on commas into raw tokens.
    /// Step 2: Find col 1 (item_id) — the first purely-numeric (integer or float) token.
    ///         Everything before it is col 0 (item_name), commas included.
    ///         spec: Docs/RE/formats/items_csv.md §2.HAZARD-A — numeric-anchor field splitting: CONFIRMED.
    /// Step 3: Find the start of the numeric tail after the description (col 2).
    ///         The description runs from after item_id to the token before the first numeric tail token.
    ///         spec: Docs/RE/formats/items_csv.md §3 step 3.
    /// Step 4: Parse the numeric tail under InvariantCulture (float-safe).
    ///         spec: Docs/RE/formats/items_csv.md §2.HAZARD-B — float column, InvariantCulture: CONFIRMED.
    /// </para>
    /// </remarks>
    private static ItemCsvRow? ParseLine(string line)
    {
        // Split the entire line on raw commas.
        // The name (col 0) and description (col 2) may contain embedded commas, so we cannot use
        // the resulting token count as a column count. We must re-assemble col 0 and col 2 by anchor.
        // spec: Docs/RE/formats/items_csv.md §2 — Hazard A: CONFIRMED; CRITICAL.
        string[] tokens = line.Split(',');

        // Step 2: Find col 1 (item_id) — first purely-numeric token.
        // spec: Docs/RE/formats/items_csv.md §3 step 2 — "first token that is purely an integer is item_id".
        // A "purely numeric" token is one that parses successfully as integer or float-with-period.
        // (Including float because §2 warns: "treat a token containing a period as numeric" to avoid
        //  re-opening the description boundary on a float token.)
        // spec: Docs/RE/formats/items_csv.md §2.HAZARD-A — implementation caution.
        //
        // KNOWN LIMITATION (spec: items_csv.md §HAZARD-A): if a name segment is itself a purely-numeric
        // token, the numeric-anchor scan can misidentify it as item_id; safe only when item names never
        // start with a digit-only token.
        int idTokenIndex = -1;
        for (int t = 0; t < tokens.Length; t++)
        {
            if (IsNumericToken(tokens[t].Trim()))
            {
                idTokenIndex = t;
                break;
            }
        }

        if (idTokenIndex < 0)
            return null; // No numeric id found — skip malformed line.

        // Reconstruct col 0 (item_name): join tokens[0..idTokenIndex-1] with commas.
        // spec: Docs/RE/formats/items_csv.md §3 step 2 — "everything before the preceding comma is item_name".
        string itemName = string.Join(",", tokens, 0, idTokenIndex);

        // Col 1 (item_id): the purely-numeric token at idTokenIndex.
        // spec: Docs/RE/formats/items_csv.md §1 col 1 — item_id u32: HIGH.
        uint itemId = ParseUInt(tokens[idTokenIndex]);

        // Step 3: Find col 2 (description) — from idTokenIndex+1 up to the first numeric tail token.
        // spec: Docs/RE/formats/items_csv.md §3 step 3 — "continue scanning until the first numeric token".
        int descStart = idTokenIndex + 1;
        int numericTailStart = -1;
        for (int t = descStart; t < tokens.Length; t++)
        {
            if (IsNumericToken(tokens[t].Trim()))
            {
                numericTailStart = t;
                break;
            }
        }

        string itemDesc;
        string[] numericTokens;

        if (numericTailStart < 0)
        {
            // No numeric tail — the rest of the line is description, no stats.
            itemDesc = string.Join(",", tokens, descStart, tokens.Length - descStart);
            numericTokens = [];
        }
        else
        {
            // Reconstruct col 2 (description): join tokens[descStart..numericTailStart-1] with commas.
            itemDesc = string.Join(",", tokens, descStart, numericTailStart - descStart);

            // Cols 3..N: the numeric tail tokens from numericTailStart onward.
            int tailCount = tokens.Length - numericTailStart;
            numericTokens = new string[tailCount];
            Array.Copy(tokens, numericTailStart, numericTokens, 0, tailCount);
        }

        // Build typed row. Numeric tail columns are 0-based relative to col 3 of the full row,
        // which is tail index 0 here.
        // spec: Docs/RE/formats/items_csv.md §1 — col layout (0-indexed from start of row).
        return BuildRow(itemName, itemId, itemDesc, numericTokens);
    }

    /// <summary>
    /// Returns true when the token is a valid integer or float (digits, optional single leading '-',
    /// optional single period for floating-point). A bare "-" with no digits is NOT numeric.
    /// Empty string is not numeric.
    /// </summary>
    /// <remarks>
    /// spec: Docs/RE/formats/items_csv.md §2.HAZARD-A — implementation caution: "treat a token
    /// containing a period as numeric too (it is the float field)".
    /// spec: Docs/RE/formats/items_csv.md §HAZARD-A — signed values guarded defensively.
    /// </remarks>
    private static bool IsNumericToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return false;

        int start = 0;
        // Accept an optional single leading '-' ONLY for float tokens (those containing a period).
        // Integer (unsigned) tokens with a leading '-' are NOT recognised as numeric because
        // ParseUInt/ParseUShort/ParseByte use NumberStyles.None (no AllowLeadingSign) and would
        // silently return 0 for a negative value, causing a recognition/decoding asymmetry.
        // A bare "-" (no following digits) is never numeric.
        // spec: Docs/RE/formats/items_csv.md §HAZARD-A — signed values guarded defensively.
        // spec: Docs/RE/formats/items_csv.md §HAZARD-B — float column allows sign (NumberStyles.Float).
        bool hasLeadingMinus = token[0] == '-';
        if (hasLeadingMinus)
        {
            if (token.Length == 1) return false; // bare "-" is not numeric
            start = 1;
        }

        // A numeric token contains only ASCII digits and at most one period.
        bool hasPeriod = false;
        for (int i = start; i < token.Length; i++)
        {
            char c = token[i];
            if (c == '.')
            {
                if (hasPeriod) return false; // two periods = not numeric
                hasPeriod = true;
            }
            else if (c < '0' || c > '9')
            {
                return false;
            }
        }

        // Reject negative integer tokens: '-' is only valid when a period is also present (float).
        // This aligns IsNumericToken with ParseUInt/ParseUShort/ParseByte (NumberStyles.None = no sign).
        if (hasLeadingMinus && !hasPeriod)
            return false;

        return true;
    }

    private static ItemCsvRow BuildRow(string name, uint itemId, string desc, string[] tail)
    {
        // Numeric tail columns are 0-based (tail[0] = full-row col 3, tail[1] = col 4, etc.).
        // spec: Docs/RE/formats/items_csv.md §1 — col 3+ are the numeric tail.
        // spec: Docs/RE/formats/items_csv.md §2.HAZARD-B — parse numeric tail with InvariantCulture: CONFIRMED.

        // perf: static local functions — no closure allocation; the array is passed explicitly.
        // spec: items_csv.md §HAZARD-A — signed values guarded defensively (ParseUInt/ParseFloat cover negatives).
        static uint GetU(string[] t, int i) => i < t.Length ? ParseUInt(t[i]) : 0u;
        static ushort GetS(string[] t, int i) => i < t.Length ? ParseUShort(t[i]) : (ushort)0;
        static byte GetB(string[] t, int i) => i < t.Length ? ParseByte(t[i]) : (byte)0;
        static float GetF(string[] t, int i) => i < t.Length ? ParseFloat(t[i]) : 0f;

        // Build the raw columns array: col0=name, col1=id (as string), col2=desc, col3+=tail.
        // Materialised for consumers that need untyped access.
        var rawColumns = new string[3 + tail.Length];
        rawColumns[0] = name;
        rawColumns[1] = itemId.ToString(CultureInfo.InvariantCulture);
        rawColumns[2] = desc;
        tail.CopyTo(rawColumns, 3);

        // Column offsets below are FULL ROW indices (col 0 = name, col 1 = id, col 2 = desc).
        // Tail index = full-row index - 3.
        // spec: Docs/RE/formats/items_csv.md §1 — column layout PARTIAL (leading cols HIGH, tail UNVERIFIED).
        return new ItemCsvRow
        {
            // ── Identity (cols 0–6) ─────────────────────────────────────────
            NameCp949 = name, // col 0 — item_name CP949: HIGH
            ItemId = itemId, // col 1 — item_id u32: HIGH
            DescriptionCp949 = desc, // col 2 — item_description CP949: HIGH
            LinkedItemId = GetU(tail, 0), // col 3 — u32 (small int, observed 0): LOW
            BaseRefId = GetU(tail, 1), // col 4 — base_item_id / archetype id (9-digit): HIGH
            SecondaryRefId = GetU(tail, 2), // col 5 — secondary type id (9-digit): MEDIUM
            ItemSubtype = GetU(tail, 3), // col 6 — small flag (1 observed): LOW

            // ── Flags and meta (cols 7–18) ──────────────────────────────────
            BonusFlagA = GetB(tail, 4), // col 7  — bonus flag a: UNVERIFIED
            BonusFlagB = GetB(tail, 5), // col 8  — bonus flag b: UNVERIFIED
            // col 9 (tail[6]): reserved_09 u8 HIGH (always 0) — not decoded
            EnhancementSize = GetB(tail, 7), // col 10 — enhancement_size u8: HIGH
            SellPrice = GetU(tail, 13), // col 16 — sell_price u32: CONFIRMED
            NpcPurchaseable = GetB(tail, 14), // col 17 — npc_purchaseable u8: HIGH
            Enabled = GetB(tail, 15), // col 18 — enabled u8: CONFIRMED

            // ── Stacking, tier, durability (cols 19–23) ─────────────────────
            MaxStack = GetS(tail, 16), // col 19 — max_stack u16: CONFIRMED
            ItemTierRank = GetS(tail, 19), // col 22 — item_tier_rank u16: CONFIRMED
            MaxDurability = GetS(tail, 20), // col 23 — max_durability u16: HIGH

            // ── Required stats (cols 24–28) ─────────────────────────────────
            ReqStr = GetS(tail, 21), // col 24 — req_str u16
            ReqCon = GetS(tail, 22), // col 25 — req_con u16
            ReqAgi = GetS(tail, 23), // col 26 — req_agi u16
            ReqInt = GetS(tail, 24), // col 27 — req_int u16
            ReqChi = GetS(tail, 25), // col 28 — req_chi u16

            // ── Class restriction flags (cols 29–32) ────────────────────────
            ClassYi = GetB(tail, 26), // col 29 — class_yi u8
            ClassYe = GetB(tail, 27), // col 30 — class_ye u8
            ClassIn = GetB(tail, 28), // col 31 — class_in u8
            ClassJi = GetB(tail, 29), // col 32 — class_ji u8

            // ── Enchant and socket block (cols 47–48) ───────────────────────
            EnchantLevel = GetB(tail, 44), // col 47 — enchant_level u8
            GemPower = GetB(tail, 45), // col 48 — gem_power u8

            // ── Bonus stat block A (cols 64–65, 68) ─────────────────────────
            BonusAtk = GetU(tail, 61), // col 64 — bonus_atk u32
            BonusHp = GetU(tail, 62), // col 65 — bonus_hp u32
            BonusExtAtk = GetU(tail, 65), // col 68 — bonus_ext_atk u32

            // ── Float rate block (cols 75, 78) ──────────────────────────────
            // spec: Docs/RE/formats/items_csv.md §2.HAZARD-B — float column, InvariantCulture: CONFIRMED.
            AttackSpeed = GetF(tail, 72), // col 75 — attack_speed f32
            DodgeRate = GetF(tail, 75), // col 78 — dodge_rate f32

            // ── Bonus stat block B (cols 84–87, 90, 93–96) ──────────────────
            BonusChi = GetU(tail, 81), // col 84 — bonus_chi u32
            WeaponStatA = GetU(tail, 82), // col 85 — weapon_stat_a u32
            WeaponStatB = GetU(tail, 83), // col 86 — weapon_stat_b u32
            MinAttack = GetU(tail, 84), // col 87 — min_attack u32
            MaxAttack = GetU(tail, 87), // col 90 — max_attack u32
            BonusDefenseA = GetU(tail, 90), // col 93 — bonus_defense_a u32
            PhysDefense = GetU(tail, 91), // col 94 — phys_defense u32
            ArmorDefense = GetU(tail, 93), // col 96 — armor_defense u32

            // ── Model / visual IDs (cols 117–118) ───────────────────────────
            ModelSetId = GetS(tail, 114), // col 117 — model_set_id u16
            ModelType = GetB(tail, 115), // col 118 — model_type u8

            // ── Consumable block (cols 112, 113, 119–120, 127–130) ──────────
            DurationMinutes = GetU(tail, 109), // col 112 — duration_minutes u32
            ExpireMode = GetB(tail, 110), // col 113 — expire_mode u8
            ConsumableValue = GetU(tail, 116), // col 119 — consumable_value u32
            IsConsumable = GetB(tail, 117), // col 120 — is_consumable u8
            GemCategory = GetB(tail, 124), // col 127 — gem_category u8
            EquippableFlag = GetB(tail, 125), // col 128 — equippable_flag u8
            HasEffect = GetB(tail, 126), // col 129 — has_effect u8
            EffectType = GetB(tail, 127), // col 130 — effect_type u8
            EffectStrength = GetS(tail, 128), // col 131 — effect_strength u16

            // ── All raw columns ──────────────────────────────────────────────
            RawColumns = rawColumns,
        };
    }

    // ─── scalar parsers — always use InvariantCulture ────────────────────────
    // spec: Docs/RE/formats/items_csv.md §2.HAZARD-B — "use invariant culture explicitly": CONFIRMED.

    private static uint ParseUInt(string s) =>
        uint.TryParse(s.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static ushort ParseUShort(string s) =>
        ushort.TryParse(s.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : (ushort)0;

    private static byte ParseByte(string s) =>
        byte.TryParse(s.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : (byte)0;

    private static float ParseFloat(string s) =>
        float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
}