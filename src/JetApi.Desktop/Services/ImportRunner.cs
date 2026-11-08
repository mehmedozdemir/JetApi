using JetApi.Desktop.Localization;
using JetApi.Import;

namespace JetApi.Desktop.Services;

/// <summary>What happened to one imported file.</summary>
/// <param name="FileName">The file's name.</param>
/// <param name="Succeeded">Something was imported from it.</param>
/// <param name="Summary">"Shop API: 10 requests in 2 folders", or why nothing was imported.</param>
/// <param name="Issues">What was not carried over, or needs attention.</param>
public sealed record ImportFileReport(string FileName, bool Succeeded, string Summary, IReadOnlyList<ImportIssue> Issues);

/// <summary>The outcome of importing one or more files, shown in the import report.</summary>
/// <param name="Files">One entry per file.</param>
/// <param name="CollectionKeys">Keys of the imported collections, to reveal them in the tree.</param>
public sealed record ImportReport(IReadOnlyList<ImportFileReport> Files, IReadOnlyList<string> CollectionKeys)
{
    /// <summary>Every file was imported and nothing needs attention.</summary>
    public bool IsClean => Files.All(f => f.Succeeded && f.Issues.Count == 0);
}

/// <summary>Imports Postman collections and environments and OpenAPI/Swagger documents into an open workspace.</summary>
public static class ImportRunner
{
    /// <summary>Reads, converts and saves each file; a bad file is reported and does not stop the others.</summary>
    public static async Task<ImportReport> ImportFilesAsync(WorkspaceSession session, IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(paths);
        var files = new List<ImportFileReport>();
        var collectionKeys = new List<string>();

        foreach (var path in paths)
        {
            var fileName = Path.GetFileName(path);
            string json;
            try
            {
                json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                files.Add(new ImportFileReport(fileName, false, Loc.Format("Import_ReadFailed", exception.Message), []));
                continue;
            }

            try
            {
                if (OpenApiImporter.LooksLikeOpenApi(json))
                {
                    var api = OpenApiImporter.Import(json, session.Workspace.Collections.Select(c => c.Key));
                    if (api.Value is { } importedApi)
                    {
                        await session.ImportCollectionAsync(importedApi.Collection, cancellationToken).ConfigureAwait(true);
                        collectionKeys.Add(importedApi.Collection.Key);
                        files.Add(new ImportFileReport(fileName, true, Loc.Format("Import_CollectionSummary", importedApi.Collection.Name, importedApi.RequestCount, importedApi.FolderCount), api.Issues));
                    }
                    else
                    {
                        files.Add(new ImportFileReport(fileName, false, api.Error ?? string.Empty, api.Issues));
                    }

                    continue;
                }

                switch (PostmanImporter.Detect(json))
                {
                    case ImportKind.PostmanCollection:
                        var collection = PostmanImporter.ImportCollection(json, session.Workspace.Collections.Select(c => c.Key));
                        if (collection.Value is { } imported)
                        {
                            await session.ImportCollectionAsync(imported.Collection, cancellationToken).ConfigureAwait(true);
                            collectionKeys.Add(imported.Collection.Key);
                            files.Add(new ImportFileReport(fileName, true, Loc.Format("Import_CollectionSummary", imported.Collection.Name, imported.RequestCount, imported.FolderCount), collection.Issues));
                        }
                        else
                        {
                            files.Add(new ImportFileReport(fileName, false, collection.Error ?? string.Empty, collection.Issues));
                        }

                        break;

                    case ImportKind.PostmanEnvironment:
                        var environment = PostmanImporter.ImportEnvironment(json, session.Workspace.Environments.Select(e => e.Key));
                        if (environment.Value is { } importedEnvironment)
                        {
                            await session.SaveEnvironmentAsync(importedEnvironment, cancellationToken).ConfigureAwait(true);
                            files.Add(new ImportFileReport(fileName, true, Loc.Format("Import_EnvironmentSummary", importedEnvironment.Name, importedEnvironment.Variables.Count), environment.Issues));
                        }
                        else
                        {
                            files.Add(new ImportFileReport(fileName, false, environment.Error ?? string.Empty, environment.Issues));
                        }

                        break;

                    default:
                        files.Add(new ImportFileReport(fileName, false, Loc.Get("Import_UnknownFile"), []));
                        break;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JetApi.Storage.WorkspaceStorageException)
            {
                files.Add(new ImportFileReport(fileName, false, Loc.Format("Error_Save", exception.Message), []));
            }
        }

        // What needs attention comes first; notes about the same place stay together.
        var ordered = files.Select(f => f with
        {
            Issues = [.. f.Issues.OrderBy(i => i.Severity == ImportIssueSeverity.Warning ? 0 : 1).ThenBy(i => i.Location, StringComparer.CurrentCulture)],
        }).ToList();
        return new ImportReport(ordered, collectionKeys);
    }
}
