using FugaPET_HML.Modelo.Cadastro;

namespace FugaPET_HML.Servicos.Cadastro;

public sealed class CadastroTaraServico
{
    private readonly TaraServico _taraServico;

    public CadastroTaraServico(TaraServico taraServico)
    {
        _taraServico = taraServico;
    }

    public Task<IReadOnlyList<TaraCadastro>> ListarAsync(CancellationToken cancellationToken = default)
        => _taraServico.ListarAsync(cancellationToken);

    public Task<ResultadoOperacao> InserirAsync(TaraCadastro tara, CancellationToken cancellationToken = default)
        => _taraServico.InserirAsync(tara, cancellationToken);

    public Task<ResultadoOperacao> AtualizarAsync(TaraCadastro tara, CancellationToken cancellationToken = default)
        => _taraServico.AtualizarAsync(tara, cancellationToken);

    public Task<ResultadoOperacao> ExcluirAsync(long codigoTara, CancellationToken cancellationToken = default)
        => _taraServico.ExcluirAsync(codigoTara, cancellationToken);

    public Task<ResultadoOperacao> ReativarAsync(long codigoTara, CancellationToken cancellationToken = default)
        => _taraServico.ReativarAsync(codigoTara, cancellationToken);
}
