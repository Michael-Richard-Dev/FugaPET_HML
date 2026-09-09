using FugaPET_HML.Modelo.IntegracaoSap;

namespace FugaPET_HML.Servicos.IntegracaoSap;

public sealed record ResultadoResolucaoWorkCenterSap(
    bool Sucesso,
    string Mensagem,
    IReadOnlyList<OperacaoOrdemProducaoSap> Operacoes);

/// <summary>Resolve master data de WorkCenter em lote por OP. Somente leitura.</summary>
public interface IWorkCenterSapServico
{
    Task<ResultadoResolucaoWorkCenterSap> ResolverWorkCentersAsync(
        OrdemProducaoSap ordem,
        IReadOnlyList<OperacaoOrdemProducaoSap> operacoes,
        CancellationToken cancellationToken = default);
}
