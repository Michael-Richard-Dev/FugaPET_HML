namespace FugaPET_HML.Modelo.IntegracaoSap;

/// <summary>
/// GATE 107N: metadata SAP do COMPONENTE destinada EXCLUSIVAMENTE à decisão do futuro allocator 261
/// (Produto Acabado). Origem: A_ProductionOrderComponent_2.
///
/// CONTRATO TRI-STATE (fail-closed, 107N-R1): todo campo decisório é anulável e <c>null</c> significa
/// <b>DESCONHECIDO</b> — ausente na resposta, nulo, VAZIO/whitespace, ou não interpretável. NUNCA é
/// convertido para <c>false</c>/<c>0</c>. Somente uma afirmação ("X"/"true"/"1") ou uma negação
/// ("false"/"0", ou booleano JSON) explícitas produzem TRUE/FALSE. O allocator futuro deve BLOQUEAR
/// diante de DESCONHECIDO; este contrato existe justamente para que a incerteza não seja silenciada.
///
/// Este tipo é um CARREGADOR de decisão: não entra no payload SAP (o command 261 permanece contrato de
/// payload) e não altera nenhuma propriedade já consumida por outros fluxos (Consumo/Apontamentos), que
/// seguem com os tipos originais no DTO compartilhado.
/// </summary>
public sealed record MetadataAlocacao261Sap
{
    // ---- 5 campos novos (sem consumidor legado: anuláveis nativos) ----

    /// <summary>QuantityIsFixed — quantidade fixa do componente (não escala com a ordem). null = DESCONHECIDO.</summary>
    public bool? QuantidadeFixa { get; init; }

    /// <summary>IsNetScrap — indicador de sucata líquida. null = DESCONHECIDO.</summary>
    public bool? SucataLiquida { get; init; }

    /// <summary>ComponentScrapInPercent — sucata do componente (%). null = DESCONHECIDO.</summary>
    public decimal? SucataComponentePercentual { get; init; }

    /// <summary>
    /// OperationScrapInPercent — sucata da operação (%), lida DIRETAMENTE de
    /// A_ProductionOrderComponent_2 (sem join/consulta à entidade de operação). null = DESCONHECIDO.
    /// </summary>
    public decimal? SucataOperacaoPercentual { get; init; }

    /// <summary>MaterialCompOriginalQuantity — quantidade original do componente. null = DESCONHECIDO.</summary>
    public decimal? QuantidadeOriginalComponente { get; init; }

    // ---- 8 campos já lidos, agora com incerteza PRESERVADA no caminho PA ----
    // As propriedades equivalentes em ComponenteOrdemProducaoSap (bool/decimal não anuláveis) permanecem
    // INTACTAS para os fluxos existentes; aqui viajam as versões tri-state usadas pela decisão.

    /// <summary>ReservationIsFinallyIssued. null = DESCONHECIDO.</summary>
    public bool? ReservaFinalizada { get; init; }

    /// <summary>MatlCompIsMarkedForBackflush. null = DESCONHECIDO.</summary>
    public bool? Backflush { get; init; }

    /// <summary>IsBulkMaterialComponent. null = DESCONHECIDO.</summary>
    public bool? MaterialGranel { get; init; }

    /// <summary>WithdrawnQuantity. null = DESCONHECIDO (distinto de 0 retirado).</summary>
    public decimal? QuantidadeRetirada { get; init; }

    /// <summary>ConfirmedAvailableQuantity. null = DESCONHECIDO (distinto de 0 disponível).</summary>
    public decimal? QuantidadeDisponivelConfirmada { get; init; }

    /// <summary>BOMItem. Vazio = não informado pelo SAP.</summary>
    public string ItemBOM { get; init; } = string.Empty;

    /// <summary>BOMItemCategory. Vazio = não informado pelo SAP.</summary>
    public string CategoriaItemBOM { get; init; } = string.Empty;

    /// <summary>BatchSplitType. Vazio = não informado pelo SAP.</summary>
    public string TipoSplitLote { get; init; } = string.Empty;

    // ---- unidade quantitativa (sem fallback literal) ----

    /// <summary>
    /// BaseUnit do COMPONENTE exatamente como o SAP devolveu, SEM fallback "KG" e sem normalização de
    /// conveniência. Vazio = DESCONHECIDO. O caminho quantitativo do allocator usa esta propriedade —
    /// nunca a unidade de exibição da UI (que mantém o fallback histórico).
    /// </summary>
    public string UnidadeBaseSap { get; init; } = string.Empty;
}
