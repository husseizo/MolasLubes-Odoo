using System.Text.Json;
using MolasLubes.Domain.Entities.Cache;
using MolasLubes.Domain.Entities.Neon;

namespace MolasLubes.Tests.LiquiMoly;

/// <summary>
/// Unit tests for <see cref="CacheLiquiMolyProduct"/> and <see cref="NeonLiquiMolyProduct"/>
/// entity defaults, property assignments, and JSON field round-trips.
///
/// Note: Full mapping/upsert tests against the actual sync services (LiquiMolyCacheSyncService,
/// LiquiMolyNeonSyncService) require the Infrastructure project which has a Windows-only COM
/// reference (SAPbobsCOM). These entity-level tests cover the core data model behavior.
/// </summary>
public class LiquiMolyProductEntityTests
{
    // =========================================================================
    // CacheLiquiMolyProduct — defaults
    // =========================================================================

    [Fact]
    public void CacheLiquiMolyProduct_DefaultIsActive_IsTrue()
    {
        var entity = new CacheLiquiMolyProduct
        {
            ArticleNumber = "20001",
            Name          = "Leichtlauf 5W-30",
        };

        Assert.True(entity.IsActive);
    }

    [Fact]
    public void CacheLiquiMolyProduct_AllNullableFields_DefaultToNull()
    {
        var entity = new CacheLiquiMolyProduct
        {
            ArticleNumber = "20001",
            Name          = "Test",
        };

        Assert.Null(entity.Category);
        Assert.Null(entity.SubCategory);
        Assert.Null(entity.Description);
        Assert.Null(entity.SpecGrade);
        Assert.Null(entity.PackagingSize);
        Assert.Null(entity.AllPackagingSizes);
        Assert.Null(entity.ImageUrl);
        Assert.Null(entity.AllImageUrls);
        Assert.Null(entity.Approvals);
        Assert.Null(entity.Specifications);
        Assert.Null(entity.ProductInfoPdfUrl);
        Assert.Null(entity.SafetyDataSheetPdfUrl);
        Assert.Null(entity.ProductUrl);
    }

    [Fact]
    public void CacheLiquiMolyProduct_AllPackagingSizes_RoundTripsAsJson()
    {
        var sizes = new List<string> { "1 L", "5 L", "20 L" };
        var json  = JsonSerializer.Serialize(sizes);

        var entity = new CacheLiquiMolyProduct
        {
            ArticleNumber     = "20001",
            Name              = "Leichtlauf 5W-30",
            AllPackagingSizes = json,
        };

        var roundTripped = JsonSerializer.Deserialize<List<string>>(entity.AllPackagingSizes!);
        Assert.NotNull(roundTripped);
        Assert.Equal(3, roundTripped!.Count);
        Assert.Equal("1 L",  roundTripped[0]);
        Assert.Equal("5 L",  roundTripped[1]);
        Assert.Equal("20 L", roundTripped[2]);
    }

    [Fact]
    public void CacheLiquiMolyProduct_Approvals_RoundTripsAsJson()
    {
        var approvals = new List<string> { "BMW Longlife-04", "MB 229.51", "VW 504 00" };
        var json      = JsonSerializer.Serialize(approvals);

        var entity = new CacheLiquiMolyProduct
        {
            ArticleNumber = "20001",
            Name          = "Leichtlauf 5W-30",
            Approvals     = json,
        };

        var roundTripped = JsonSerializer.Deserialize<List<string>>(entity.Approvals!);
        Assert.NotNull(roundTripped);
        Assert.Equal(3, roundTripped!.Count);
        Assert.Contains("BMW Longlife-04", roundTripped);
        Assert.Contains("VW 504 00", roundTripped);
    }

    [Fact]
    public void CacheLiquiMolyProduct_Specifications_RoundTripsAsJson()
    {
        var specs = new Dictionary<string, string>
        {
            { "Viscosity class", "SAE 5W-30" },
            { "Base oil", "HC-synthesis" },
        };
        var json = JsonSerializer.Serialize(specs);

        var entity = new CacheLiquiMolyProduct
        {
            ArticleNumber  = "20001",
            Name           = "Leichtlauf 5W-30",
            Specifications = json,
        };

        var roundTripped = JsonSerializer.Deserialize<Dictionary<string, string>>(entity.Specifications!);
        Assert.NotNull(roundTripped);
        Assert.Equal("SAE 5W-30",    roundTripped!["Viscosity class"]);
        Assert.Equal("HC-synthesis", roundTripped["Base oil"]);
    }

    // =========================================================================
    // NeonLiquiMolyProduct — defaults (mirrors Cache entity)
    // =========================================================================

    [Fact]
    public void NeonLiquiMolyProduct_DefaultIsActive_IsTrue()
    {
        var entity = new NeonLiquiMolyProduct
        {
            ArticleNumber = "20001",
            Name          = "Leichtlauf 5W-30",
        };

        Assert.True(entity.IsActive);
    }

    [Fact]
    public void NeonLiquiMolyProduct_AllNullableFields_DefaultToNull()
    {
        var entity = new NeonLiquiMolyProduct
        {
            ArticleNumber = "20001",
            Name          = "Test",
        };

        Assert.Null(entity.Category);
        Assert.Null(entity.SubCategory);
        Assert.Null(entity.Description);
        Assert.Null(entity.SpecGrade);
        Assert.Null(entity.PackagingSize);
        Assert.Null(entity.AllPackagingSizes);
        Assert.Null(entity.ImageUrl);
        Assert.Null(entity.AllImageUrls);
        Assert.Null(entity.Approvals);
        Assert.Null(entity.Specifications);
        Assert.Null(entity.ProductInfoPdfUrl);
        Assert.Null(entity.SafetyDataSheetPdfUrl);
        Assert.Null(entity.ProductUrl);
    }

    [Fact]
    public void NeonLiquiMolyProduct_AllPackagingSizes_RoundTripsAsJson()
    {
        var sizes = new List<string> { "500 ml", "1 L", "5 L" };
        var json  = JsonSerializer.Serialize(sizes);

        var entity = new NeonLiquiMolyProduct
        {
            ArticleNumber     = "20001",
            Name              = "Test",
            AllPackagingSizes = json,
        };

        var roundTripped = JsonSerializer.Deserialize<List<string>>(entity.AllPackagingSizes!);
        Assert.NotNull(roundTripped);
        Assert.Equal(3, roundTripped!.Count);
        Assert.Equal("500 ml", roundTripped[0]);
    }

    // =========================================================================
    // Entity parity: Cache and Neon entities share the same field set
    // =========================================================================

    [Fact]
    public void CacheAndNeon_HaveEquivalentLiquiMolyFields()
    {
        var cacheProps = typeof(CacheLiquiMolyProduct)
            .GetProperties()
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToList();

        var neonProps = typeof(NeonLiquiMolyProduct)
            .GetProperties()
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToList();

        // Both entities should expose the same set of columns
        Assert.Equal(cacheProps, neonProps);
    }
}
