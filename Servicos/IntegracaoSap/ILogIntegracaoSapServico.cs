using FugaPET_HML.Modelo.IntegracaoSap;

namespace FugaPET_HML.Servicos.IntegracaoSap;

internal interface ILogIntegracaoSapServico
{
    Task RegistrarAsync(
        RegistroLogIntegracaoSap registro,
        CancellationToken cancellationToken = default);
}
