using FugaPET_HML.Modelo.IntegracaoSap;

namespace FugaPET_HML.Servicos.IntegracaoSap;

internal sealed class LogIntegracaoSapNuloServico : ILogIntegracaoSapServico
{
    public static LogIntegracaoSapNuloServico Instancia { get; } = new();

    private LogIntegracaoSapNuloServico()
    {
    }

    public Task RegistrarAsync(
        RegistroLogIntegracaoSap registro,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
