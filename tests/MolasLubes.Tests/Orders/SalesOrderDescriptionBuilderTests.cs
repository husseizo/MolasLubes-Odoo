using MolasLubes.Domain.Orders;

namespace MolasLubes.Tests.Orders;

/// <summary>
/// Unit tests for SalesOrderDescriptionBuilder.
///
/// Inajaribu hali zote:
///  - Zote mbili zipo        → prefix inawekwa
///  - Moja tu                → prefix moja tu
///  - Zote blank             → hakuna mabadiliko
///  - Tayari ina prefix      → HAIRUDIWI (idempotency)
///  - Case sensitivity       → case-insensitive check
///  - Truncation             → haizidi MaxDescriptionLength
///  - Double-slash / slashes za ziada → haziwekwi
/// </summary>
public class SalesOrderDescriptionBuilderTests
{
    // ═══════════════════════════════════════════════════════════════
    // BuildPrefix — prefix construction
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BuildPrefix_BothValues_ReturnsCombined()
    {
        var result = SalesOrderDescriptionBuilder.BuildPrefix("HOSE", "VIKA");
        Assert.Equal("HOSE/VIKA", result);
    }

    [Fact]
    public void BuildPrefix_OnlyItemName_ReturnsItemName()
    {
        var result = SalesOrderDescriptionBuilder.BuildPrefix("HOSE", null);
        Assert.Equal("HOSE", result);
    }

    [Fact]
    public void BuildPrefix_OnlyManufacturer_ReturnsManufacturer()
    {
        var result = SalesOrderDescriptionBuilder.BuildPrefix(null, "VIKA");
        Assert.Equal("VIKA", result);
    }

    [Fact]
    public void BuildPrefix_BothNull_ReturnsEmpty()
    {
        var result = SalesOrderDescriptionBuilder.BuildPrefix(null, null);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void BuildPrefix_BothWhitespace_ReturnsEmpty()
    {
        var result = SalesOrderDescriptionBuilder.BuildPrefix("   ", "  ");
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void BuildPrefix_TrimsWhitespace()
    {
        var result = SalesOrderDescriptionBuilder.BuildPrefix("  HOSE  ", "  VIKA  ");
        Assert.Equal("HOSE/VIKA", result);
    }

    // ═══════════════════════════════════════════════════════════════
    // BuildDescription — happy path
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BuildDescription_BothValues_PrependsPrefix()
    {
        var result = SalesOrderDescriptionBuilder.BuildDescription(
            "HOSE", "VIKA", "8K0121101M");

        Assert.Equal("HOSE/VIKA/8K0121101M", result);
    }

    [Fact]
    public void BuildDescription_OnlyItemName_PrependsItemName()
    {
        var result = SalesOrderDescriptionBuilder.BuildDescription(
            "HOSE", null, "8K0121101M");

        Assert.Equal("HOSE/8K0121101M", result);
    }

    [Fact]
    public void BuildDescription_OnlyManufacturer_PrependsManufacturer()
    {
        var result = SalesOrderDescriptionBuilder.BuildDescription(
            null, "VIKA", "8K0121101M");

        Assert.Equal("VIKA/8K0121101M", result);
    }

    [Fact]
    public void BuildDescription_BothBlank_ReturnsOriginal()
    {
        var result = SalesOrderDescriptionBuilder.BuildDescription(
            null, null, "8K0121101M");

        Assert.Equal("8K0121101M", result);
    }

    // ═══════════════════════════════════════════════════════════════
    // IDEMPOTENCY — hii ndiyo jambo muhimu zaidi
    // Ikiwa description tayari ina prefix → HAIRUDIWI
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BuildDescription_AlreadyHasPrefix_DoesNotRepeat()
    {
        // Tayari ina "HOSE/VIKA/" — hairudii kuandika tena
        var result = SalesOrderDescriptionBuilder.BuildDescription(
            "HOSE", "VIKA", "HOSE/VIKA/8K0121101M");

        Assert.Equal("HOSE/VIKA/8K0121101M", result);
    }

    [Fact]
    public void BuildDescription_AlreadyHasPrefix_CaseInsensitive_DoesNotRepeat()
    {
        // SAP inaweza kuandika uppercase au mixed-case — bado hairudii
        var result = SalesOrderDescriptionBuilder.BuildDescription(
            "HOSE", "VIKA", "hose/vika/8K0121101M");

        Assert.Equal("hose/vika/8K0121101M", result);
    }

    [Fact]
    public void BuildDescription_AlreadyHasPrefix_MixedCase_DoesNotRepeat()
    {
        var result = SalesOrderDescriptionBuilder.BuildDescription(
            "hose", "vika", "HOSE/VIKA/8K0121101M");

        Assert.Equal("HOSE/VIKA/8K0121101M", result);
    }

    [Fact]
    public void BuildDescription_CalledTwice_SameResult()
    {
        // Simulate job running twice on the same order
        string first = SalesOrderDescriptionBuilder.BuildDescription(
            "HOSE", "VIKA", "8K0121101M");

        string second = SalesOrderDescriptionBuilder.BuildDescription(
            "HOSE", "VIKA", first);

        // Second call must not add the prefix again
        Assert.Equal(first, second);
        Assert.Equal("HOSE/VIKA/8K0121101M", second);
    }

    [Fact]
    public void BuildDescription_CalledThreeTimes_SameResult()
    {
        // Job ni ya kila siku — lazima haibadiliki baada ya run ya kwanza
        string once  = SalesOrderDescriptionBuilder.BuildDescription("HOSE", "VIKA", "8K0121101M");
        string twice = SalesOrderDescriptionBuilder.BuildDescription("HOSE", "VIKA", once);
        string three = SalesOrderDescriptionBuilder.BuildDescription("HOSE", "VIKA", twice);

        Assert.Equal("HOSE/VIKA/8K0121101M", once);
        Assert.Equal("HOSE/VIKA/8K0121101M", twice);
        Assert.Equal("HOSE/VIKA/8K0121101M", three);
    }

    // ═══════════════════════════════════════════════════════════════
    // No double-slash / bad formatting
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BuildDescription_DoesNotProduceDoubleSlash()
    {
        var result = SalesOrderDescriptionBuilder.BuildDescription(
            "HOSE", "VIKA", "8K0121101M");

        Assert.DoesNotContain("//", result);
    }

    [Fact]
    public void BuildDescription_DoesNotStartWithSlash()
    {
        var result = SalesOrderDescriptionBuilder.BuildDescription(
            null, "VIKA", "8K0121101M");

        Assert.False(result.StartsWith("/"));
    }

    [Fact]
    public void BuildDescription_BothBlank_DoesNotStartWithSlash()
    {
        var result = SalesOrderDescriptionBuilder.BuildDescription(
            null, null, "8K0121101M");

        Assert.False(result.StartsWith("/"));
    }

    // ═══════════════════════════════════════════════════════════════
    // Truncation
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BuildDescription_LongResult_IsTruncated()
    {
        string longDesc = new string('X', 200);

        var result = SalesOrderDescriptionBuilder.BuildDescription(
            "HOSE", "VIKA", longDesc);

        Assert.True(result.Length <= SalesOrderDescriptionBuilder.MaxDescriptionLength);
    }

    [Fact]
    public void BuildDescription_ShortResult_IsNotTruncated()
    {
        var result = SalesOrderDescriptionBuilder.BuildDescription(
            "HOSE", "VIKA", "8K0121101M");

        Assert.Equal("HOSE/VIKA/8K0121101M", result);
        Assert.True(result.Length <= SalesOrderDescriptionBuilder.MaxDescriptionLength);
    }

    // ═══════════════════════════════════════════════════════════════
    // Real-world SAP examples from the spec
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void BuildDescription_SpecExample_Full()
    {
        // U_ItemName=HOSE, U_Manufacturer=VIKA, OriginalDescription=8K0121101M/11211853001
        var result = SalesOrderDescriptionBuilder.BuildDescription(
            "HOSE", "VIKA", "8K0121101M/11211853001");

        Assert.Equal("HOSE/VIKA/8K0121101M/11211853001", result);
    }

    [Fact]
    public void BuildDescription_SpecExample_AlreadyFormatted_NoChange()
    {
        // Order imekwisha created na description sahihi — job hairudii kuandika
        var result = SalesOrderDescriptionBuilder.BuildDescription(
            "HOSE", "VIKA", "HOSE/VIKA/8K0121101M/11211853001");

        Assert.Equal("HOSE/VIKA/8K0121101M/11211853001", result);
    }
}
