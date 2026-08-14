namespace MolasLubes.Domain.Orders;

/// <summary>
/// Pure static logic for building the SAP Sales Order line description prefix.
/// No external dependencies — fully unit-testable.
///
/// Format: "U_ItemName/U_Manufacturer/OriginalDescription"
///
/// Rules
/// ─────
/// • Both values present   → "HOSE/VIKA/8K0121101M"
/// • Only ItemName         → "HOSE/8K0121101M"
/// • Only Manufacturer     → "VIKA/8K0121101M"
/// • Both blank            → original description unchanged
/// • Already prefixed      → original description unchanged (idempotent)
/// • Never produces        → double-slash, leading slash, or re-prefix
/// </summary>
public static class SalesOrderDescriptionBuilder
{
    /// <summary>
    /// Maximum characters written to RDR1.Dscription.
    /// SAP B1 base schema: 100. Extended installations may use up to 254.
    /// Adjust to match your system's column definition.
    /// </summary>
    public const int MaxDescriptionLength = 100;

    // ──────────────────────────────────────────────────────────
    // BuildPrefix
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the "ItemName/Manufacturer" prefix from the two UDF values.
    /// Blank/null parts are omitted — no double-slash or leading slash.
    /// Returns empty string when both parts are blank.
    /// </summary>
    /// <example>
    /// BuildPrefix("HOSE", "VIKA")  → "HOSE/VIKA"
    /// BuildPrefix("HOSE", null)    → "HOSE"
    /// BuildPrefix(null,   "VIKA")  → "VIKA"
    /// BuildPrefix(null,   null)    → ""
    /// BuildPrefix("",     "  ")    → ""
    /// </example>
    public static string BuildPrefix(string? itemName, string? manufacturer)
    {
        var parts = new List<string>(2);

        if (!string.IsNullOrWhiteSpace(itemName))
            parts.Add(itemName.Trim());

        if (!string.IsNullOrWhiteSpace(manufacturer))
            parts.Add(manufacturer.Trim());

        return string.Join("/", parts);
    }

    // ──────────────────────────────────────────────────────────
    // BuildDescription
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Applies the prefix to <paramref name="currentDescription"/> and
    /// returns the final string ready to store in RDR1.Dscription.
    ///
    /// Idempotency guarantee:
    ///   If <paramref name="currentDescription"/> already begins with
    ///   "ItemName/Manufacturer/" (case-insensitive), it is returned
    ///   unchanged — the prefix is NEVER added twice.
    ///
    /// Truncation:
    ///   The result is safely capped at <see cref="MaxDescriptionLength"/>
    ///   characters. No exception is thrown; excess characters are dropped.
    ///
    /// Call this BEFORE order.Lines.Add() on new orders so the correct
    /// description is stored in a single SAP transaction.
    /// </summary>
    public static string BuildDescription(
        string? itemName,
        string? manufacturer,
        string  currentDescription)
    {
        string prefix = BuildPrefix(itemName, manufacturer);

        // Nothing to prefix — leave description as-is
        if (string.IsNullOrEmpty(prefix))
            return currentDescription;

        string prefixSlash = prefix + "/";

        // Already formatted — do NOT add the prefix again
        if (currentDescription.StartsWith(prefixSlash, StringComparison.OrdinalIgnoreCase))
            return currentDescription;

        return SafeTruncate(prefixSlash + currentDescription, MaxDescriptionLength);
    }

    // ──────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────

    private static string SafeTruncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
