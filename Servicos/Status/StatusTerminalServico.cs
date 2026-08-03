using FugaPET_HML.Modelo.Status;

namespace FugaPET_HML.Servicos.Status;

public sealed class StatusTerminalServico
{
    private readonly StatusTerminalLocalServico _statusTerminalLocalServico;

    public StatusTerminalServico()
        : this(new StatusTerminalLocalServico())
    {
    }

    public StatusTerminalServico(StatusTerminalLocalServico statusTerminalLocalServico)
    {
        _statusTerminalLocalServico = statusTerminalLocalServico;
    }

    public ItemStatusIndustrial ObterStatus()
        => _statusTerminalLocalServico.ObterStatus();
}
