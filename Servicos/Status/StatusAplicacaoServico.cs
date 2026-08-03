using FugaPET_HML.Modelo.Status;

namespace FugaPET_HML.Servicos.Status;

public sealed class StatusAplicacaoServico
{
    private readonly StatusIndustrialServico _statusIndustrialServico;

    public StatusAplicacaoServico()
        : this(new StatusIndustrialServico())
    {
    }

    public StatusAplicacaoServico(StatusIndustrialServico statusIndustrialServico)
    {
        _statusIndustrialServico = statusIndustrialServico;
    }

    public Task<StatusIndustrial> ObterStatusAsync(CancellationToken cancellationToken = default)
        => _statusIndustrialServico.ObterStatusAsync(cancellationToken);
}
