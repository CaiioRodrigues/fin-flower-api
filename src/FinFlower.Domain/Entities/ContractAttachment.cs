using FinFlower.Domain.Common;

namespace FinFlower.Domain.Entities;

/// <summary>
/// O PDF do contrato, guardado no próprio banco. Um backup leva tudo junto e não
/// existe arquivo órfão nem pasta para configurar.
/// </summary>
public sealed class ContractAttachment : Entity
{
    public const int MaxSizeInBytes = 10 * 1024 * 1024;
    public const string PdfContentType = "application/pdf";

    /// <summary>Assinatura do formato PDF: "%PDF".</summary>
    private static readonly byte[] PdfSignature = [0x25, 0x50, 0x44, 0x46];

    private ContractAttachment() { } // EF Core

    internal ContractAttachment(Guid contractId, string fileName, byte[] content, DateTimeOffset uploadedAt)
    {
        if (content.Length == 0)
            throw new DomainException("O arquivo está vazio.");

        if (content.Length > MaxSizeInBytes)
            throw new DomainException($"O arquivo deve ter no máximo {MaxSizeInBytes / (1024 * 1024)} MB.");

        // Confere a assinatura do arquivo, não a extensão nem o content-type que o
        // cliente declara: os dois são escolhidos por quem envia.
        if (!content.Take(PdfSignature.Length).SequenceEqual(PdfSignature))
            throw new DomainException("O arquivo precisa ser um PDF.");

        ContractId = contractId;
        FileName = Guard.AgainstNullOrWhiteSpace(SanitizeFileName(fileName), "nome do arquivo", 255);
        Content = content;
        SizeInBytes = content.Length;
        UploadedAt = uploadedAt;
    }

    public Guid ContractId { get; private set; }
    public string FileName { get; private set; } = null!;
    public byte[] Content { get; private set; } = null!;
    public int SizeInBytes { get; private set; }
    public DateTimeOffset UploadedAt { get; private set; }

    /// <summary>
    /// Guarda só o nome do arquivo, sem caminho. Um nome como
    /// "../../web.config" viraria travessia de diretório se algum dia o
    /// arquivo for gravado em disco.
    /// </summary>
    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return "contrato.pdf";

        var name = Path.GetFileName(fileName.Trim());
        foreach (var invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');

        return string.IsNullOrWhiteSpace(name) ? "contrato.pdf" : name;
    }
}
