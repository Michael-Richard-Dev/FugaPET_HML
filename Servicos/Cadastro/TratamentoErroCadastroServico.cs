using FugaPET_HML.AcessoDados.Banco;
using FugaPET_HML.Servicos.Auditoria;

namespace FugaPET_HML.Servicos.Cadastro;

internal static class TratamentoErroCadastroServico
{
    public static async Task<ResultadoOperacao> TratarFalhaAsync(
        AuditoriaServico auditoriaServico,
        string acao,
        Exception ex,
        string tela,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await auditoriaServico.RegistrarErroAsync(acao, ex.ToString(), tela, cancellationToken);
        }
        catch
        {
            // Nao mascarar a falha original caso a auditoria tambem falhe.
        }

        return ResultadoOperacao.Falha(ErroBancoTratado.ObterMensagemAmigavel(ex));
    }
}
