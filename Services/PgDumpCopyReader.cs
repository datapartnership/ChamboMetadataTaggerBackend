using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MetadataTagging.Services;

/// <summary>A table's rows as read from a <c>COPY ... FROM stdin</c> block of a plain-SQL pg_dump.</summary>
public class CopyTable(string name, IReadOnlyList<string> columns)
{
    public string Name { get; } = name;
    public List<string?[]> Rows { get; } = new();

    private readonly Dictionary<string, int> _index =
        columns.Select((c, i) => (c, i)).ToDictionary(x => x.c, x => x.i);

    public string? Get(string?[] row, string column) =>
        _index.TryGetValue(column, out var i)
            ? row[i]
            : throw new FormatException($"Column \"{column}\" is missing from the {Name} data in the dump.");

    public string Required(string?[] row, string column) =>
        Get(row, column) ?? throw new FormatException($"{Name}.{column} is NULL in the dump.");

    public int Int(string?[] row, string column) => int.Parse(Required(row, column), CultureInfo.InvariantCulture);
    public int? NullableInt(string?[] row, string column) =>
        Get(row, column) is { } v ? int.Parse(v, CultureInfo.InvariantCulture) : null;
    public long Long(string?[] row, string column) => long.Parse(Required(row, column), CultureInfo.InvariantCulture);
    public double? NullableDouble(string?[] row, string column) =>
        Get(row, column) is { } v ? double.Parse(v, CultureInfo.InvariantCulture) : null;
    public bool Bool(string?[] row, string column) => Required(row, column) == "t";
    public DateTime Timestamp(string?[] row, string column) => ParseTimestamp(Required(row, column));
    public DateTime? NullableTimestamp(string?[] row, string column) =>
        Get(row, column) is { } v ? ParseTimestamp(v) : null;

    private static readonly string[] TimestampFormats =
    [
        "yyyy-MM-dd HH:mm:ss.FFFFFFzz",
        "yyyy-MM-dd HH:mm:ss.FFFFFFzzz",
    ];

    // timestamptz values are written as e.g. "2026-04-29 07:48:26.900473+00".
    private static DateTime ParseTimestamp(string value) =>
        DateTimeOffset.ParseExact(value, TimestampFormats, CultureInfo.InvariantCulture, DateTimeStyles.None).UtcDateTime;
}

public static partial class PgDumpCopyReader
{
    [GeneratedRegex("""^COPY public\."(?<table>[^"]+)" \((?<columns>.*)\) FROM stdin;$""")]
    private static partial Regex CopyHeader();

    /// <summary>
    /// Reads the data of the requested tables from a plain-SQL pg_dump. Other tables are
    /// skipped without being buffered.
    /// </summary>
    public static async Task<Dictionary<string, CopyTable>> ReadAsync(
        Stream dump, IReadOnlyCollection<string> tables, CancellationToken ct = default)
    {
        var result = new Dictionary<string, CopyTable>();
        using var reader = new StreamReader(dump, Encoding.UTF8);

        var first = await reader.ReadLineAsync(ct);
        if (first != null && first.StartsWith("PGDMP", StringComparison.Ordinal))
            throw new FormatException("This is a custom-format dump. Export it as plain SQL (pg_dump -Fp) and try again.");

        CopyTable? current = null;
        var skipping = false;
        for (var line = first; line != null; line = await reader.ReadLineAsync(ct))
        {
            if (current != null || skipping)
            {
                if (line == "\\.")
                {
                    current = null;
                    skipping = false;
                }
                else
                {
                    current?.Rows.Add(line.Split('\t').Select(Unescape).ToArray());
                }
                continue;
            }

            var header = CopyHeader().Match(line);
            if (!header.Success)
                continue;

            var table = header.Groups["table"].Value;
            if (!tables.Contains(table))
            {
                skipping = true;
                continue;
            }

            var columns = header.Groups["columns"].Value.Split(", ").Select(c => c.Trim('"')).ToList();
            current = new CopyTable(table, columns);
            result[table] = current;
        }

        return result;
    }

    // COPY text format: \N is NULL; backslash escapes for control characters, octal and hex bytes.
    private static string? Unescape(string field)
    {
        if (field == "\\N")
            return null;
        if (!field.Contains('\\'))
            return field;

        var bytes = new List<byte>(field.Length);
        for (var i = 0; i < field.Length; i++)
        {
            var c = field[i];
            if (c != '\\' || i + 1 >= field.Length)
            {
                var length = char.IsHighSurrogate(c) && i + 1 < field.Length ? 2 : 1;
                bytes.AddRange(Encoding.UTF8.GetBytes(field.Substring(i, length)));
                i += length - 1;
                continue;
            }

            var next = field[++i];
            switch (next)
            {
                case 'b': bytes.Add(0x08); break;
                case 'f': bytes.Add(0x0C); break;
                case 'n': bytes.Add(0x0A); break;
                case 'r': bytes.Add(0x0D); break;
                case 't': bytes.Add(0x09); break;
                case 'v': bytes.Add(0x0B); break;
                case >= '0' and <= '7':
                {
                    var start = i;
                    while (i + 1 < field.Length && i - start < 2 && field[i + 1] is >= '0' and <= '7') i++;
                    bytes.Add(Convert.ToByte(field[start..(i + 1)], 8));
                    break;
                }
                case 'x' when i + 1 < field.Length && Uri.IsHexDigit(field[i + 1]):
                {
                    var start = i + 1;
                    i = start;
                    if (i + 1 < field.Length && Uri.IsHexDigit(field[i + 1])) i++;
                    bytes.Add(Convert.ToByte(field[start..(i + 1)], 16));
                    break;
                }
                default:
                    bytes.AddRange(Encoding.UTF8.GetBytes(next.ToString()));
                    break;
            }
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
}
