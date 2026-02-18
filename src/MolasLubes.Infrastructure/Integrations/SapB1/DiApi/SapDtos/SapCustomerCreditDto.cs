namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi.SapDtos;

public class SapCustomerCreditDto
{
    public string CardCode { get; set; } = "";
    public decimal CreditLimit { get; set; }
    public decimal Balance { get; set; }
}