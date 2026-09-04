using FinFlower.Application.Quotes.Dtos;
using FinFlower.Application.Reports.Export;

namespace FinFlower.Application.Abstractions;

/// <summary>
/// Desenha a proposta que vai para o cliente. Fica fora do <c>ReportDocument</c>
/// de propósito: aquele é um modelo neutro de métricas e tabelas, feito para
/// relatório interno. Uma proposta é um documento comercial — tem cabeçalho de
/// papel timbrado, bloco do cliente, condições e linha de aceite — e espremê-la
/// naquele formato tiraria justamente o que a torna apresentável.
/// </summary>
public interface IQuoteProposalWriter
{
    ReportFile Write(QuoteProposal proposal);
}
