using FugaPET_HML.Modelo.Entrada;

namespace FugaPET_HML.Servicos.Entrada;

/// <summary>
/// GATE R5: provider OPERACIONAL do P5. Coleta as tres evidencias (exclusividade duravel, recebimentos
/// locais e historico 101/102 no SAP), delega a decisao ao motor puro
/// <see cref="AvaliadorEvidenciaRecebimentoItemPedido"/> e expoe o resultado no tri-state que o
/// controller ja consome desde o R2.
/// <para>
/// NAO contem regra de negocio propria: a precedencia vive no motor puro. Nao lanca para o chamador —
/// qualquer excecao de uma fonte vira evidencia indisponivel, que BLOQUEIA.
/// </para>
/// <para>
/// Cancelamento NAO e capturado: continua subindo como cancelamento, preservando o contrato do R3B.
/// </para>
/// </summary>
public sealed class ProvedorEvidenciaRecebimentoItemPedido
{
    private readonly IExclusividadeItemPedidoServico _exclusividade;
    private readonly IEvidenciaRecebimentoLocalServico _local;
    private readonly IHistoricoMaterialDocumentSapServico _historico;
    private readonly Action<string>? _registrarDiagnostico;

    public ProvedorEvidenciaRecebimentoItemPedido(
        IExclusividadeItemPedidoServico? exclusividade = null,
        IEvidenciaRecebimentoLocalServico? local = null,
        IHistoricoMaterialDocumentSapServico? historico = null,
        Action<string>? registrarDiagnostico = null)
    {
        // Defaults FAIL-CLOSED: enquanto as fontes aprovadas nao existirem, o P5 permanece bloqueando.
        _exclusividade = exclusividade ?? new ExclusividadeItemPedidoIndisponivelServico();
        _local = local ?? new EvidenciaRecebimentoLocalIndisponivelServico();
        _historico = historico ?? new HistoricoMaterialDocumentSapIndisponivelServico();
        _registrarDiagnostico = registrarDiagnostico;
    }

    /// <summary>Avaliacao COMPLETA, com o cenario nomeado (para diagnostico e teste).</summary>
    public async Task<ResultadoEvidenciaRecebimento> AvaliarAsync(
        string numeroPedido,
        string numeroItemPedido,
        long codigoLancamento,
        CancellationToken cancellationToken = default)
    {
        ResultadoExclusividadeItemPedido exclusividade = await ObterAsync(
            () => _exclusividade.TentarObterAsync(numeroPedido, numeroItemPedido, codigoLancamento, cancellationToken),
            ex => ResultadoExclusividadeItemPedido.Indisponivel(
                "falha ao obter exclusividade duravel (" + ex.GetType().Name + ")"));

        ResultadoRecebimentoLocal local = await ObterAsync(
            () => _local.ConsultarAsync(numeroPedido, numeroItemPedido, codigoLancamento, cancellationToken),
            ex => ResultadoRecebimentoLocal.Indisponivel(
                "falha na consulta local (" + ex.GetType().Name + ")"));

        ResultadoHistoricoMaterialSap historico = await ObterAsync(
            () => _historico.ConsultarPorPedidoItemAsync(numeroPedido, numeroItemPedido, cancellationToken),
            ex => ResultadoHistoricoMaterialSap.Falha(
                "falha na consulta do historico SAP (" + ex.GetType().Name + ")"));

        ResultadoEvidenciaRecebimento resultado =
            AvaliadorEvidenciaRecebimentoItemPedido.Avaliar(exclusividade, local, historico);

        _registrarDiagnostico?.Invoke(
            $"P5 pedido {numeroPedido} item {numeroItemPedido} lancamento {codigoLancamento}: "
            + $"CENARIO={resultado.Cenario}; LIBERA={resultado.Libera}; "
            + $"EXCLUSIVIDADE={exclusividade.Cenario}; LOCAL={local.Cenario}; "
            + $"HISTORICO={historico.Cenario} (101={historico.Documentos101}, 102={historico.Movimentos102}, "
            + $"paginacaoCompleta={historico.PaginacaoCompleta}, abrangencia={historico.AbrangenciaComprovada}).");

        return resultado;
    }

    /// <summary>
    /// Adaptador para o seam do R2 (<c>existeRecebimentoConfirmadoItemPedido</c>): false = libera,
    /// true = ja recebido, null = desconhecido (bloqueia).
    /// </summary>
    public async Task<bool?> ExisteRecebimentoConfirmadoAsync(
        string numeroPedido,
        string numeroItemPedido,
        long codigoLancamento,
        CancellationToken cancellationToken = default)
        => (await AvaliarAsync(numeroPedido, numeroItemPedido, codigoLancamento, cancellationToken))
            .ComoTriState();

    private static async Task<T> ObterAsync<T>(Func<Task<T>> consulta, Func<Exception, T> emFalha)
    {
        try
        {
            return await consulta();
        }
        catch (OperationCanceledException)
        {
            // Cancelamento NAO vira evidencia: sobe e e tratado pela fronteira do R3B.
            throw;
        }
        catch (Exception ex)
        {
            return emFalha(ex);
        }
    }
}
