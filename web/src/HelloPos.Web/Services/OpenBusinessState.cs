namespace HelloPos.Web.Services;

/// <summary>
/// The business open in this tab (feature 005, FR-015): shown in the layout and sent by pages as the
/// stale-tab header. The session cookie decides the business; this only remembers what this tab saw.
/// </summary>
public sealed class OpenBusinessState(IOwnerAccountService accounts, IBusinessService businesses)
{
    public Guid? Id { get; private set; }

    public string? Name { get; private set; }

    public event Action? Changed;

    /// <summary>Reads the open business from the session (after sign-in or a page reload).</summary>
    public async Task LoadAsync()
    {
        var session = await accounts.GetSessionAsync().ConfigureAwait(false);
        Id = session?.CurrentBusinessId;
        Name = null;
        if (Id is { } id && await businesses.GetCurrentAsync(id).ConfigureAwait(false) is CurrentBusinessResult.Loaded loaded)
        {
            Name = loaded.Settings.Name;
        }

        Changed?.Invoke();
    }

    public async Task<OwnerActionResult> OpenAsync(Guid id, string name)
    {
        var result = await businesses.OpenAsync(id).ConfigureAwait(false);
        if (result is OwnerActionResult.Succeeded)
        {
            Set(id, name);
        }

        return result;
    }

    /// <summary>After a rename of the open business.</summary>
    public void Renamed(string name)
    {
        if (Id is { } id)
        {
            Set(id, name);
        }
    }

    public void Clear()
    {
        Id = null;
        Name = null;
        Changed?.Invoke();
    }

    private void Set(Guid id, string name)
    {
        Id = id;
        Name = name;
        Changed?.Invoke();
    }
}
