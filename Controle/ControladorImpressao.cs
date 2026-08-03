using FugaPET_HML.Controle;
using FugaPET_HML.Modelo;
using FugaPET_HML.Servicos;

using System;
using System.Threading;
using System.Threading.Tasks;

namespace FugaPET_HML.Controle;

public sealed class ControladorImpressao
{
    private readonly ServicoImpressoraZebra _servicoImpressoraZebra;

    public ControladorImpressao()
        : this(new ServicoImpressoraZebra())
    {
    }

    public ControladorImpressao(ServicoImpressoraZebra servicoImpressoraZebra)
    {
        _servicoImpressoraZebra = servicoImpressoraZebra;
    }

    public async Task<string> ImprimirAsync(SolicitacaoImpressao solicitacao, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(solicitacao.Texto))
        {
            return "Digite um texto para imprimir.";
        }

        if (string.IsNullOrWhiteSpace(solicitacao.NomeImpressora))
        {
            return "Informe o nome da impressora.";
        }

        try
        {
            _servicoImpressoraZebra.ImprimirTexto(solicitacao.NomeImpressora.Trim(), solicitacao.Texto);
            return "Impressao enviada com sucesso.";
        }
        catch (Exception ex)
        {
            // Detalhe tecnico vai para auditoria; usuario recebe mensagem amigavel.
            try
            {
                await FabricaControladoresCadastro.CriarAuditoriaServico()
                    .RegistrarErroAsync("IMPRESSAO_ERRO", ex.ToString(), "ControladorImpressao", cancellationToken)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Auditoria nunca pode quebrar o fluxo.
            }

            return "Não foi possível imprimir. Acione o suporte.";
        }
    }
}
