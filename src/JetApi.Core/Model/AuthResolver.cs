namespace JetApi.Core.Model;

/// <summary>Determines the auth that actually applies to a request.</summary>
public static class AuthResolver
{
    /// <summary>
    /// Walks from the request up through its folders to the collection and returns the first
    /// auth that is not <see cref="InheritAuth"/>.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="ancestors">Folders containing the request, closest first.</param>
    /// <param name="collection">The owning collection.</param>
    public static AuthConfig Resolve(RequestNode request, IEnumerable<Folder> ancestors, ApiCollection collection)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(ancestors);
        ArgumentNullException.ThrowIfNull(collection);

        if (request.Auth is not InheritAuth)
        {
            return request.Auth;
        }

        foreach (var folder in ancestors)
        {
            if (folder.Auth is not InheritAuth)
            {
                return folder.Auth;
            }
        }

        return collection.Auth is InheritAuth ? AuthConfig.None : collection.Auth;
    }
}
