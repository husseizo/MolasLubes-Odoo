namespace MolasLubes.Domain.Entities.Cache;

public class BrandRoleMapping
{
    public int Id { get; set; }
    public string Brand { get; set; } = null!;
    public string Role { get; set; } = null!;
    public string SapUserCode { get; set; } = null!;
}
