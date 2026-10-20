namespace SadrApp.ViewModels;

/// <summary>Common row shape for the shared grid: Id, title, code, description.</summary>
public class RowBase
{
    public int Id { get; set; }

    /// <summary>1-based display position within the current (possibly filtered) view.
    /// Set by ListTableControl — the users never see the raw entity Id.</summary>
    public int RowNumber { get; set; }

    public virtual string Title { get; set; } = "";
    public virtual string Code { get; set; } = "";
    public virtual string Description { get; set; } = "";

    /// <summary>Optional extra column (hidden unless the page enables it).</summary>
    public virtual string Extra { get; set; } = "";
}
