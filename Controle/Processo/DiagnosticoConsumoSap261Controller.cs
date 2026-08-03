using FugaPET_HML.Modelo.Diagnostico;
using FugaPET_HML.Servicos.Diagnostico;

namespace FugaPET_HML.Controle.Processo;

public sealed class DiagnosticoConsumoSap261Controller
{
    private readonly DiagnosticoConsumoSap261Servico _servico;

    public DiagnosticoConsumoSap261Controller()
        : this(new DiagnosticoConsumoSap261Servico())
    {
    }

    internal DiagnosticoConsumoSap261Controller(DiagnosticoConsumoSap261Servico servico)
    {
        _servico = servico;
    }

    public Task<DiagnosticoConsumoSap261Resultado> ExecutarAsync(CancellationToken cancellationToken = default)
        => _servico.ExecutarAsync(cancellationToken);
}
