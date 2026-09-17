namespace MolasLubes.Infrastructure.Integrations.SapB1.DiApi;

public record SapTransferDocumentDto(
    int     DocEntry,
    string  DocType,        // "OWTR" | "OWTQ"
    int     DocNum,
    DateTime  DocDate,
    DateTime? TaxDate,
    DateTime? DocDueDate,
    string  FromWhsCode,
    string  ToWhsCode,
    string? Comments,
    string  DocStatus,      // "O" = Open, "C" = Closed
    int?    UserSign,
    decimal DocTotal,
    IReadOnlyList<SapTransferLineDto> Lines);

public record SapTransferLineDto(
    int     DocEntry,
    int     LineNum,
    string  ItemCode,
    string? Description,
    decimal Quantity,
    decimal OpenQty,
    string? UomCode,
    string? FromWhsCode,
    string? ToWhsCode,
    decimal? Price,
    decimal? LineTotal,
    int?    BaseType,
    int?    BaseEntry,
    int?    BaseLine);
