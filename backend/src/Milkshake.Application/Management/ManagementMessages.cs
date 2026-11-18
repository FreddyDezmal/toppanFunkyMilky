namespace Milkshake.Application.Management;

/// <summary>User-facing management messages shared by services, API and tests.</summary>
public static class ManagementMessages
{
    /// <summary>409 title.</summary>
    public const string ConflictTitle = "This record was changed by someone else.";

    /// <summary>409 detail.</summary>
    public const string ConflictDetail = "Reload to see the latest values, then make your change again.";

    /// <summary>404 title.</summary>
    public const string NotFoundTitle = "Record not found.";

    /// <summary>404 detail.</summary>
    public const string NotFoundDetail = "It may not exist, or the link is out of date.";

    /// <summary>Duplicate name, e.g. "A flavour with this name already exists…".</summary>
    public static string DuplicateName(string label) =>
        $"A {label} with this name already exists. If it is inactive, reactivate it instead.";
}