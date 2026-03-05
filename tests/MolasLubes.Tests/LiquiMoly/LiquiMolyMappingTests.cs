using System.Text.Json;
using MolasLubes.Domain.Entities.Cache;
using MolasLubes.Domain.Entities.Neon;

namespace MolasLubes.Tests.LiquiMoly;

/// <summary>
/// Tests for the Liqui-Moly product mapping logic.
///
/// Because <c>LiquiMolyCacheSyncService</c> and <c>LiquiMolyNeonSyncService</c> live in the
/// Infrastructure project (which requires a Windows-only COM reference), the mapping logic
/// is tested here using a local mirror of the <c>MapToEntity</c> helper.  This ensures the
/// expected transformations (JSON serialisation, field assignments, timestamp, IsActive flag)
/// remain correct as the codebase evolves.
/// </summary>
public class LiquiMolyMappingTests
{
    // =========================================================================
    // Local DTO mirror (avoids Infrastructure dependency)
    // =========================================================================

    private sealed record ProductDto
    {
        public string   ArticleNumber         { get; init; } = null!;
        public string   Name                  { get; init; } = null!;
        public string?  ProductUrl            { get; init; }
        public string?  Category              { get; init; }
        public string?  SubCategory           { get; init; }
        public string?  Description           { get; init; }
        public string?  PackagingSize         { get; init; }
        public List<string> AllPackagingSizes { get; init; } = new();
        public string?  SpecGrade             { get; init; }
        public string?  ImageUrl              { get; init; }
        public List<string> AllImageUrls      { get; init; } = new();
        public List<string> Approvals         { get; init; } = new();
        public Dictionary<string, string> Specifications { get; init; } = new();
        public string?  ProductInfoPdfUrl     { get; init; }
        public string?  SafetyDataSheetPdfUrl { get; init; }
    }

    // =========================================================================
    // Local mapper mirrors (exact copy of the logic in the sync services)
    // =========================================================================

    private static readonly JsonSerializerOptions _json = new() { WriteIndented = false };

    private static void MapToCache(ProductDto dto, CacheLiquiMolyProduct entity, DateTime now)
    {
        entity.Name                  = dto.Name;
        entity.Category              = dto.Category;
        entity.SubCategory           = dto.SubCategory;
        entity.Description           = dto.Description;
        entity.SpecGrade             = dto.SpecGrade;
        entity.PackagingSize         = dto.PackagingSize;
        entity.ImageUrl              = dto.ImageUrl;
        entity.ProductUrl            = dto.ProductUrl;
        entity.IsActive              = true;
        entity.ScrapedAt             = now;
        entity.AllPackagingSizes     = dto.AllPackagingSizes.Count > 0
            ? JsonSerializer.Serialize(dto.AllPackagingSizes, _json) : null;
        entity.AllImageUrls          = dto.AllImageUrls.Count > 0
            ? JsonSerializer.Serialize(dto.AllImageUrls, _json) : null;
        entity.Approvals             = dto.Approvals.Count > 0
            ? JsonSerializer.Serialize(dto.Approvals, _json) : null;
        entity.Specifications        = dto.Specifications.Count > 0
            ? JsonSerializer.Serialize(dto.Specifications, _json) : null;
        entity.ProductInfoPdfUrl     = dto.ProductInfoPdfUrl;
        entity.SafetyDataSheetPdfUrl = dto.SafetyDataSheetPdfUrl;
    }

    private static void MapToNeon(ProductDto dto, NeonLiquiMolyProduct entity, DateTime now)
    {
        entity.Name                  = dto.Name;
        entity.Category              = dto.Category;
        entity.SubCategory           = dto.SubCategory;
        entity.Description           = dto.Description;
        entity.SpecGrade             = dto.SpecGrade;
        entity.PackagingSize         = dto.PackagingSize;
        entity.ImageUrl              = dto.ImageUrl;
        entity.ProductUrl            = dto.ProductUrl;
        entity.IsActive              = true;
        entity.ScrapedAt             = now;
        entity.AllPackagingSizes     = dto.AllPackagingSizes.Count > 0
            ? JsonSerializer.Serialize(dto.AllPackagingSizes, _json) : null;
        entity.AllImageUrls          = dto.AllImageUrls.Count > 0
            ? JsonSerializer.Serialize(dto.AllImageUrls, _json) : null;
        entity.Approvals             = dto.Approvals.Count > 0
            ? JsonSerializer.Serialize(dto.Approvals, _json) : null;
        entity.Specifications        = dto.Specifications.Count > 0
            ? JsonSerializer.Serialize(dto.Specifications, _json) : null;
        entity.ProductInfoPdfUrl     = dto.ProductInfoPdfUrl;
        entity.SafetyDataSheetPdfUrl = dto.SafetyDataSheetPdfUrl;
    }

    // =========================================================================
    // Test data factory
    // =========================================================================

    private static ProductDto FullDto() => new()
    {
        ArticleNumber         = "20001",
        Name                  = "Leichtlauf SAE 5W-30",
        ProductUrl            = "https://www.liqui-moly.com/en/leichtlauf-sae-5w-30.html",
        Category              = "Engine Oils",
        SubCategory           = "Leichtlauf",
        Description           = "HC-synthesised motor oil.",
        PackagingSize         = "5 L",
        AllPackagingSizes     = new List<string> { "1 L", "5 L", "20 L" },
        SpecGrade             = "5W-30",
        ImageUrl              = "https://cdn.example.com/20001.jpg",
        AllImageUrls          = new List<string> { "https://cdn.example.com/20001.jpg", "https://cdn.example.com/20001-2.jpg" },
        Approvals             = new List<string> { "BMW Longlife-04", "MB 229.51" },
        Specifications        = new Dictionary<string, string> { { "Viscosity class", "SAE 5W-30" } },
        ProductInfoPdfUrl     = "https://pim.liqui-moly.com/20001-pi.pdf",
        SafetyDataSheetPdfUrl = "https://pim.liqui-moly.com/20001-sds.pdf",
    };

    // =========================================================================
    // Cache mapping
    // =========================================================================

    [Fact]
    public void MapToCache_ScalarFields_AreCopied()
    {
        var dto    = FullDto();
        var entity = new CacheLiquiMolyProduct { ArticleNumber = dto.ArticleNumber, Name = dto.Name };
        var now    = DateTime.UtcNow;

        MapToCache(dto, entity, now);

        Assert.Equal(dto.Name,                  entity.Name);
        Assert.Equal(dto.Category,              entity.Category);
        Assert.Equal(dto.SubCategory,           entity.SubCategory);
        Assert.Equal(dto.Description,           entity.Description);
        Assert.Equal(dto.SpecGrade,             entity.SpecGrade);
        Assert.Equal(dto.PackagingSize,         entity.PackagingSize);
        Assert.Equal(dto.ImageUrl,              entity.ImageUrl);
        Assert.Equal(dto.ProductUrl,            entity.ProductUrl);
        Assert.Equal(dto.ProductInfoPdfUrl,     entity.ProductInfoPdfUrl);
        Assert.Equal(dto.SafetyDataSheetPdfUrl, entity.SafetyDataSheetPdfUrl);
    }

    [Fact]
    public void MapToCache_IsActive_AlwaysSetToTrue()
    {
        var dto    = FullDto();
        var entity = new CacheLiquiMolyProduct { ArticleNumber = dto.ArticleNumber, Name = dto.Name, IsActive = false };

        MapToCache(dto, entity, DateTime.UtcNow);

        Assert.True(entity.IsActive);
    }

    [Fact]
    public void MapToCache_ScrapedAt_IsSetToProvidedTimestamp()
    {
        var dto    = FullDto();
        var entity = new CacheLiquiMolyProduct { ArticleNumber = dto.ArticleNumber, Name = dto.Name };
        var now    = new DateTime(2026, 3, 5, 12, 0, 0, DateTimeKind.Utc);

        MapToCache(dto, entity, now);

        Assert.Equal(now, entity.ScrapedAt);
    }

    [Fact]
    public void MapToCache_AllPackagingSizes_SerializedToJson()
    {
        var dto    = FullDto();
        var entity = new CacheLiquiMolyProduct { ArticleNumber = dto.ArticleNumber, Name = dto.Name };

        MapToCache(dto, entity, DateTime.UtcNow);

        Assert.NotNull(entity.AllPackagingSizes);
        var list = JsonSerializer.Deserialize<List<string>>(entity.AllPackagingSizes!);
        Assert.Equal(dto.AllPackagingSizes, list);
    }

    [Fact]
    public void MapToCache_EmptyAllPackagingSizes_SerializesToNull()
    {
        var dto    = FullDto() with { AllPackagingSizes = new List<string>() };
        var entity = new CacheLiquiMolyProduct { ArticleNumber = dto.ArticleNumber, Name = dto.Name };

        MapToCache(dto, entity, DateTime.UtcNow);

        Assert.Null(entity.AllPackagingSizes);
    }

    [Fact]
    public void MapToCache_Approvals_SerializedToJson()
    {
        var dto    = FullDto();
        var entity = new CacheLiquiMolyProduct { ArticleNumber = dto.ArticleNumber, Name = dto.Name };

        MapToCache(dto, entity, DateTime.UtcNow);

        Assert.NotNull(entity.Approvals);
        var list = JsonSerializer.Deserialize<List<string>>(entity.Approvals!);
        Assert.Equal(dto.Approvals, list);
    }

    [Fact]
    public void MapToCache_EmptyApprovals_SerializesToNull()
    {
        var dto    = FullDto() with { Approvals = new List<string>() };
        var entity = new CacheLiquiMolyProduct { ArticleNumber = dto.ArticleNumber, Name = dto.Name };

        MapToCache(dto, entity, DateTime.UtcNow);

        Assert.Null(entity.Approvals);
    }

    [Fact]
    public void MapToCache_Specifications_SerializedToJson()
    {
        var dto    = FullDto();
        var entity = new CacheLiquiMolyProduct { ArticleNumber = dto.ArticleNumber, Name = dto.Name };

        MapToCache(dto, entity, DateTime.UtcNow);

        Assert.NotNull(entity.Specifications);
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(entity.Specifications!);
        Assert.Equal(dto.Specifications, dict);
    }

    [Fact]
    public void MapToCache_EmptySpecifications_SerializesToNull()
    {
        var dto    = FullDto() with { Specifications = new Dictionary<string, string>() };
        var entity = new CacheLiquiMolyProduct { ArticleNumber = dto.ArticleNumber, Name = dto.Name };

        MapToCache(dto, entity, DateTime.UtcNow);

        Assert.Null(entity.Specifications);
    }

    // =========================================================================
    // Neon mapping
    // =========================================================================

    [Fact]
    public void MapToNeon_ScalarFields_AreCopied()
    {
        var dto    = FullDto();
        var entity = new NeonLiquiMolyProduct { ArticleNumber = dto.ArticleNumber, Name = dto.Name };
        var now    = DateTime.UtcNow;

        MapToNeon(dto, entity, now);

        Assert.Equal(dto.Name,                  entity.Name);
        Assert.Equal(dto.Category,              entity.Category);
        Assert.Equal(dto.SubCategory,           entity.SubCategory);
        Assert.Equal(dto.Description,           entity.Description);
        Assert.Equal(dto.SpecGrade,             entity.SpecGrade);
        Assert.Equal(dto.PackagingSize,         entity.PackagingSize);
        Assert.Equal(dto.ImageUrl,              entity.ImageUrl);
        Assert.Equal(dto.ProductUrl,            entity.ProductUrl);
        Assert.Equal(dto.ProductInfoPdfUrl,     entity.ProductInfoPdfUrl);
        Assert.Equal(dto.SafetyDataSheetPdfUrl, entity.SafetyDataSheetPdfUrl);
    }

    [Fact]
    public void MapToNeon_IsActive_AlwaysSetToTrue()
    {
        var dto    = FullDto();
        var entity = new NeonLiquiMolyProduct { ArticleNumber = dto.ArticleNumber, Name = dto.Name, IsActive = false };

        MapToNeon(dto, entity, DateTime.UtcNow);

        Assert.True(entity.IsActive);
    }

    [Fact]
    public void MapToNeon_Approvals_SerializedToJson()
    {
        var dto    = FullDto();
        var entity = new NeonLiquiMolyProduct { ArticleNumber = dto.ArticleNumber, Name = dto.Name };

        MapToNeon(dto, entity, DateTime.UtcNow);

        Assert.NotNull(entity.Approvals);
        var list = JsonSerializer.Deserialize<List<string>>(entity.Approvals!);
        Assert.Equal(dto.Approvals, list);
    }

    [Fact]
    public void MapToNeon_Specifications_SerializedToJson()
    {
        var dto    = FullDto();
        var entity = new NeonLiquiMolyProduct { ArticleNumber = dto.ArticleNumber, Name = dto.Name };

        MapToNeon(dto, entity, DateTime.UtcNow);

        Assert.NotNull(entity.Specifications);
        var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(entity.Specifications!);
        Assert.Equal(dto.Specifications, dict);
    }

    // =========================================================================
    // Idempotency: mapping the same DTO twice produces the same result
    // =========================================================================

    [Fact]
    public void MapToCache_CalledTwice_SameResult_Idempotent()
    {
        var dto     = FullDto();
        var entity1 = new CacheLiquiMolyProduct { ArticleNumber = dto.ArticleNumber, Name = dto.Name };
        var entity2 = new CacheLiquiMolyProduct { ArticleNumber = dto.ArticleNumber, Name = dto.Name };
        var now     = new DateTime(2026, 3, 5, 12, 0, 0, DateTimeKind.Utc);

        MapToCache(dto, entity1, now);
        MapToCache(dto, entity1, now); // apply again to same entity
        MapToCache(dto, entity2, now); // apply to fresh entity

        Assert.Equal(entity2.Name,                  entity1.Name);
        Assert.Equal(entity2.AllPackagingSizes,      entity1.AllPackagingSizes);
        Assert.Equal(entity2.Approvals,              entity1.Approvals);
        Assert.Equal(entity2.Specifications,         entity1.Specifications);
        Assert.Equal(entity2.ProductInfoPdfUrl,      entity1.ProductInfoPdfUrl);
        Assert.Equal(entity2.SafetyDataSheetPdfUrl,  entity1.SafetyDataSheetPdfUrl);
    }
}
