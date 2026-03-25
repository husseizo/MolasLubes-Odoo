namespace MolasLubes.Infrastructure.Integrations.Germax.Dtos;

public class GermaxCandidateDto
{
    public string  ProductUrl     { get; set; } = string.Empty;
    public string? Title          { get; set; }

    /// <summary>
    /// Article number extracted from the search result title using the
    /// common Germax pattern (e.g. "GL2787" from "GL2787 – Turbocharger TD6").
    /// Null when no recognisable code is found.
    /// </summary>
    public string? ArticleNumber  { get; set; }

    public string? Category       { get; set; }
    public string  SearchStrategy { get; set; } = string.Empty;
    public decimal Score          { get; set; }
}
