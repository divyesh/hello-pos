using System.Text;

namespace HelloPos.Api.Features.Catalog.Import;

/// <summary>
/// A small RFC 4180 reader (research R8): comma separated, double-quoted fields may hold commas, line
/// breaks and doubled quotes; CRLF or LF line ends; a UTF-8 byte-order mark is ignored. Blank lines
/// are skipped. Returns the rows with their line numbers, or the line of an unterminated quote.
/// </summary>
public static class CsvReader
{
    public sealed record Row(int Line, IReadOnlyList<string> Fields);

    public static (List<Row> Rows, int? UnterminatedQuoteLine) Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 0 && text[0] == '﻿')
        {
            text = text[1..];
        }

        var rows = new List<Row>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var line = 1;
        var rowLine = 1;
        var quoted = false;
        var quoteStart = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    if (c == '\n')
                    {
                        line++;
                    }

                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"' when field.Length == 0:
                    quoted = true;
                    quoteStart = line;
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    EndRow();
                    line++;
                    rowLine = line;
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (quoted)
        {
            return (rows, quoteStart);
        }

        EndRow();
        return (rows, null);

        void EndRow()
        {
            fields.Add(field.ToString());
            field.Clear();
            if (fields.Count > 1 || fields[0].Length > 0)
            {
                rows.Add(new Row(rowLine, fields.ToArray()));
            }

            fields.Clear();
        }
    }

    /// <summary>
    /// A cell ready for a spreadsheet: quoted when needed, and a leading <c>=</c>, <c>+</c>, <c>-</c> or
    /// <c>@</c> prefixed with an apostrophe so spreadsheets do not run it as a formula (CSV injection).
    /// </summary>
    public static string Cell(string? value)
    {
        var text = value ?? string.Empty;
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@')
        {
            text = "'" + text;
        }

        return text.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"" : text;
    }

    /// <summary>Undoes <see cref="Cell"/>'s formula guard when a file comes back in.</summary>
    public static string Uncell(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Length > 1 && value[0] == '\'' && value[1] is '=' or '+' or '-' or '@' ? value[1..] : value;
    }
}
