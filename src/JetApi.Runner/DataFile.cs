using System.Text;
using System.Text.Json;

namespace JetApi.Runner;

/// <summary>The rows of a data file: one iteration per row, each column a variable.</summary>
/// <param name="Columns">Column (variable) names, in file order.</param>
/// <param name="Rows">The rows; a missing value is empty.</param>
/// <param name="Error">Why the file could not be read; <c>null</c> on success.</param>
public sealed record DataFileResult(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyDictionary<string, string>> Rows, string? Error)
{
    /// <summary>The file was read.</summary>
    public bool IsSuccess => Error is null;

    internal static DataFileResult Failure(string error) => new([], [], error);
}

/// <summary>
/// Reads data files for data-driven runs: CSV (RFC 4180; the first line names the columns; comma or semicolon, chosen
/// from the first line) or JSON (an array of objects).
/// </summary>
public static class DataFile
{
    /// <summary>Reads a <c>.csv</c> or <c>.json</c> file (by extension; otherwise by content).</summary>
    public static async Task<DataFileResult> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(path);
        string text;
        try
        {
            text = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return DataFileResult.Failure(Messages.Format("Data_ReadFailed", exception.Message));
        }

        var json = Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase)
            || (!Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase) && text.TrimStart().StartsWith('['));
        return json ? ParseJson(text) : ParseCsv(text);
    }

    /// <summary>Reads JSON: an array of objects; strings are taken as they are, other values as JSON text.</summary>
    public static DataFileResult ParseJson(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return DataFileResult.Failure(Messages.Get("Data_NotArray"));
            }

            var columns = new List<string>();
            var rows = new List<IReadOnlyDictionary<string, string>>();
            var index = 0;
            foreach (var item in document.RootElement.EnumerateArray())
            {
                index++;
                if (item.ValueKind != JsonValueKind.Object)
                {
                    return DataFileResult.Failure(Messages.Format("Data_RowNotObject", index));
                }

                var row = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var property in item.EnumerateObject())
                {
                    row[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : property.Value.GetRawText();
                    if (!columns.Contains(property.Name, StringComparer.Ordinal))
                    {
                        columns.Add(property.Name);
                    }
                }

                rows.Add(row);
            }

            return rows.Count == 0 ? DataFileResult.Failure(Messages.Get("Data_Empty")) : new DataFileResult(columns, rows, null);
        }
        catch (JsonException exception)
        {
            return DataFileResult.Failure(Messages.Format("Data_InvalidJson", exception.LineNumber + 1, exception.BytePositionInLine + 1));
        }
    }

    /// <summary>Reads CSV: quoted fields may hold separators, quotes (doubled) and line breaks; blank lines are skipped.</summary>
    public static DataFileResult ParseCsv(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 0 && text[0] == '﻿')
        {
            text = text[1..];
        }

        var separator = ChooseSeparator(text);
        var records = new List<List<string>>();
        var field = new StringBuilder();
        var record = new List<string>();
        var quoted = false;
        var line = 1;
        var recordLine = 1;
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
                    line += c == '\n' ? 1 : 0;
                    field.Append(c);
                }
            }
            else if (c == '"' && field.Length == 0)
            {
                quoted = true;
            }
            else if (c == separator)
            {
                record.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                EndRecord();
                line++;
                recordLine = line;
            }
            else
            {
                field.Append(c);
            }
        }

        if (quoted)
        {
            return DataFileResult.Failure(Messages.Format("Data_UnclosedQuote", recordLine));
        }

        EndRecord();
        if (records.Count == 0)
        {
            return DataFileResult.Failure(Messages.Get("Data_Empty"));
        }

        var columns = records[0].Select(c => c.Trim()).ToList();
        if (columns.Any(c => c.Length == 0))
        {
            return DataFileResult.Failure(Messages.Get("Data_NoHeader"));
        }

        var rows = records.Skip(1)
            .Select(values => (IReadOnlyDictionary<string, string>)columns.Select((name, index) => (name, value: index < values.Count ? values[index] : string.Empty))
                .ToDictionary(p => p.name, p => p.value, StringComparer.Ordinal))
            .ToList();
        return rows.Count == 0 ? DataFileResult.Failure(Messages.Get("Data_Empty")) : new DataFileResult(columns, rows, null);

        void EndRecord()
        {
            record.Add(field.ToString());
            field.Clear();
            if (record.Count > 1 || record[0].Length > 0)
            {
                records.Add(record);
            }

            record = [];
        }
    }

    // Semicolon when the header line has more semicolons than commas (e.g. Excel in Turkish), otherwise comma.
    private static char ChooseSeparator(string text)
    {
        var end = text.IndexOfAny(['\r', '\n']);
        var header = end < 0 ? text : text[..end];
        return header.Count(c => c == ';') > header.Count(c => c == ',') ? ';' : ',';
    }
}
