using FugaPET_HML.Modelo.IntegracaoSap;
using FugaPET_HML.Modelo.Processo;

namespace FugaPET_HML.Servicos.Operacao;

/// <summary>GATE 118B: desfecho do alocador. SkipItem vale por COMPONENTE (delta zero), nunca por OP.</summary>
public enum CenarioAllocator261
{
    /// <summary>Ha pelo menos um componente com delta &gt; 0 e TODOS os guards passaram.</summary>
    Ok,

    /// <summary>Todos os componentes elegiveis fecharam com delta exatamente zero: nada a enviar.</summary>
    NadaAEnviar,

    /// <summary>Guard acionado. Nenhum POST pode ocorrer.</summary>
    Bloqueado
}

/// <summary>GATE 118B: componente FRESCO do SAP, em tri-state. null significa AUSENTE, nunca 0/false.</summary>
public sealed record Componente261Fresco
{
    public string NumeroOrdem { get; init; } = string.Empty;
    public string Reservation { get; init; } = string.Empty;
    public string ReservationItem { get; init; } = string.Empty;
    public string Material { get; init; } = string.Empty;
    public string Plant { get; init; } = string.Empty;
    public string StorageLocation { get; init; } = string.Empty;
    public string Batch { get; init; } = string.Empty;
    public string BaseUnit { get; init; } = string.Empty;
    public string TipoMovimento { get; init; } = string.Empty;

    public decimal? RequiredQuantity { get; init; }
    public decimal? WithdrawnQuantity { get; init; }
    public decimal? ConfirmedAvailableQuantity { get; init; }
    public bool? QuantityIsFixed { get; init; }

    /// <summary>Identidade canonica: a KEY do EntityType A_ProductionOrderComponent_2.</summary>
    public string Identidade => $"{Reservation}/{ReservationItem}";

    public bool IdentidadeDeterministica =>
        !string.IsNullOrWhiteSpace(Reservation) && !string.IsNullOrWhiteSpace(ReservationItem);
}

/// <summary>GATE 118B: entrada completa do alocador. Toda ela vem de leitura FRESCA + ledger local.</summary>
public sealed record Entrada261Cumulativa
{
    public string NumeroOrdem { get; init; } = string.Empty;

    /// <summary>OP ecoada pela leitura do ITEM (STEP 1), para checagem cruzada com a dos componentes.</summary>
    public string NumeroOrdemItemFresco { get; init; } = string.Empty;

    /// <summary>MfgOrderItemPlannedTotalQty fresco. null = INDETERMINADO.</summary>
    public decimal? PlannedProductionOp { get; init; }

    /// <summary>MfgOrderItemGoodsReceiptQty fresco. null = INDETERMINADO (nunca 0 por omissao).</summary>
    public decimal? PriorProducedConfirmed { get; init; }

    /// <summary>Producao REAL da caixa corrente, ja resolvida na ProductionUnit da OP. null = INDETERMINADO.</summary>
    public decimal? CurrentBoxProduction { get; init; }

    /// <summary>ProductionUnit da OP (unidade de PlannedProduction/PriorProduced/CurrentBox).</summary>
    public string ProductionUnit { get; init; } = string.Empty;

    public IReadOnlyList<Componente261Fresco> Componentes { get; init; } = [];

    /// <summary>true somente quando a completude da lista foi COMPROVADA (__count == recebidos).</summary>
    public bool ComponentesCompletosComprovado { get; init; }

    /// <summary>
    /// Ledger LOCAL confirmado por identidade de componente ("Reservation/ReservationItem"): quanto o
    /// pipeline considera ja consumido. Obrigatorio. null = ledger indisponivel => bloqueio.
    /// </summary>
    public IReadOnlyDictionary<string, decimal>? ConsumoLocalConfirmadoPorComponente { get; init; }
}

/// <summary>GATE 118B: uma linha de saida = um item do 261, com o DELTA (nunca o cumulativo).</summary>
public sealed record Item261Alocado
{
    public string Reservation { get; init; } = string.Empty;
    public string ReservationItem { get; init; } = string.Empty;
    public string Material { get; init; } = string.Empty;
    public string Plant { get; init; } = string.Empty;
    public string StorageLocation { get; init; } = string.Empty;
    public string Batch { get; init; } = string.Empty;
    public string Unidade { get; init; } = string.Empty;

    /// <summary>Consumo cumulativo ALVO para a producao cumulativa atual (arredondado a 3 casas).</summary>
    public decimal TargetCumulative { get; init; }

    /// <summary>WithdrawnQuantity fresco usado como base do delta.</summary>
    public decimal WithdrawnFresco { get; init; }

    /// <summary>Quantidade a enviar no 261: TargetCumulative - WithdrawnFresco. Sempre &gt; 0 aqui.</summary>
    public decimal Delta261 { get; init; }
}

/// <summary>GATE 118B: resultado do alocador. Bloqueado => ZERO itens, logo zero POST possivel.</summary>
public sealed record Resultado261Cumulativo(
    CenarioAllocator261 Cenario,
    string Mensagem,
    IReadOnlyList<Item261Alocado> Itens,
    decimal? ProducedCumulative,
    IReadOnlyList<string> ComponentesIgnoradosPorDeltaZero)
{
    public bool Sucesso => Cenario == CenarioAllocator261.Ok;

    public static Resultado261Cumulativo Bloqueado(string mensagem)
        => new(CenarioAllocator261.Bloqueado, mensagem, [], null, []);
}

/// <summary>
/// GATE 118B: alocador CUMULATIVO do movimento 261 do Produto Acabado.
/// <para>
/// Corrige o defeito do incidente: a quantidade por caixa NUNCA e a RequiredQuantity integral da OP.
/// O consumo e derivado da producao CUMULATIVA realmente confirmada no SAP
/// (MfgOrderItemGoodsReceiptQty) somada a producao da caixa corrente, rateada sobre a necessidade
/// total da OP, e o que se envia e o DELTA contra o WithdrawnQuantity FRESCO.
/// </para>
/// <para>
/// Puro: sem HTTP, sem banco, sem relogio, sem estado. Fail-closed por construcao — qualquer guard
/// acionado devolve ZERO itens, de modo que nenhum blocker pode alcancar o POST 261.
/// Nao converte unidades: a razao producao/planejado e adimensional e o alvo permanece na BaseUnit
/// do componente.
/// </para>
/// </summary>
public static class ProdutoAcabado261AllocatorCumulativo
{
    /// <summary>Escala do contrato SAP: Decimal(13,3) em RequiredQuantity/WithdrawnQuantity.</summary>
    public const int CasasDecimaisContratoSap = 3;

    /// <summary>Tolerancia de reconciliacao: menor unidade representavel na escala 3 do contrato.</summary>
    public const decimal ToleranciaReconciliacao = 0.001m;

    public const string MotivoPlanejadoInvalido =
        "Quantidade planejada da OP (MfgOrderItemPlannedTotalQty) ausente ou <= 0: BLOCK (nenhum POST).";

    public const string MotivoProduzidoAnteriorInvalido =
        "Producao anterior confirmada (MfgOrderItemGoodsReceiptQty) ausente ou negativa: BLOCK (nenhum POST).";

    public const string MotivoCaixaCorrenteInvalida =
        "Producao da caixa corrente indeterminada ou <= 0: BLOCK (nenhum POST).";

    public const string MotivoProduzidoMaiorQuePlanejado =
        "Producao cumulativa maior que a planejada da OP: BLOCK (nenhum POST).";

    public const string MotivoUnidadeProducaoIndeterminada =
        "ProductionUnit da OP indeterminada: BLOCK (nenhum POST).";

    public const string MotivoListaComponentesIncompleta =
        "Completude da lista de componentes NAO comprovada ($inlinecount ausente ou divergente): BLOCK (nenhum POST).";

    public const string MotivoSemComponentes =
        "Nenhum componente 261 elegivel na leitura fresca: BLOCK (nenhum POST).";

    public const string MotivoLedgerIndisponivel =
        "Ledger local de consumo indisponivel: BLOCK_RECONCILIATION_REQUIRED (nenhum POST).";

    // GATE 118B-R4: a contradicao entre leituras deixou de ter UM motivo generico. Sao quatro
    // condicoes materialmente diferentes, cada uma com codigo proprio, porque "o SAP nao devolveu a
    // OP" e um defeito de contrato e "o SAP devolveu OUTRA OP" e um erro de correlacao.
    public const string MotivoItemOrdemAusente =
        "ITEM_MANUFACTURING_ORDER_MISSING: o item retornado pelo SAP nao traz ManufacturingOrder. "
        + "Correlacao com a OP solicitada nao comprovavel. BLOCK (nenhum POST).";

    public const string MotivoItemOrdemDivergente =
        "ITEM_MANUFACTURING_ORDER_MISMATCH: a OP do item retornado pelo SAP difere da OP solicitada. "
        + "BLOCK (nenhum POST).";

    public const string MotivoComponenteOrdemAusente =
        "COMPONENT_MANUFACTURING_ORDER_MISSING: componente retornado sem ManufacturingOrder. "
        + "BLOCK (nenhum POST).";

    public const string MotivoComponenteOrdemDivergente =
        "COMPONENT_MANUFACTURING_ORDER_MISMATCH: a OP do componente difere da OP solicitada/do item. "
        + "BLOCK (nenhum POST).";

    public static Resultado261Cumulativo Calcular(Entrada261Cumulativa entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (string.IsNullOrWhiteSpace(entrada.NumeroOrdem))
        {
            return Resultado261Cumulativo.Bloqueado("Alocador 261 sem OP: BLOCK (nenhum POST).");
        }

        // ---- §5 STEP 3 / R4 §5: consistencia CRUZADA real entre as duas leituras.
        // NumeroOrdemItemFresco precisa ser a OP MATERIALMENTE RETORNADA no item pelo SAP. Se vier do
        // eco da request, esta comparacao e tautologica e nao prova nada (blocker do 118E).
        // Nao ha snapshot atomico: por isso a checagem e obrigatoria aqui, antes de qualquer item.
        string opSolicitada = entrada.NumeroOrdem.Trim();
        string opItem = entrada.NumeroOrdemItemFresco?.Trim() ?? string.Empty;

        if (opItem.Length == 0)
        {
            return Resultado261Cumulativo.Bloqueado(MotivoItemOrdemAusente);
        }

        if (!string.Equals(opItem, opSolicitada, StringComparison.OrdinalIgnoreCase))
        {
            return Resultado261Cumulativo.Bloqueado(MotivoItemOrdemDivergente);
        }

        if (string.IsNullOrWhiteSpace(entrada.ProductionUnit))
        {
            return Resultado261Cumulativo.Bloqueado(MotivoUnidadeProducaoIndeterminada);
        }

        // ---- §11: guards de producao, todos em tri-state (null NUNCA vira 0) ----
        if (entrada.PlannedProductionOp is not decimal planejado || planejado <= 0m)
        {
            return Resultado261Cumulativo.Bloqueado(MotivoPlanejadoInvalido);
        }

        if (entrada.PriorProducedConfirmed is not decimal produzidoAnterior || produzidoAnterior < 0m)
        {
            return Resultado261Cumulativo.Bloqueado(MotivoProduzidoAnteriorInvalido);
        }

        if (entrada.CurrentBoxProduction is not decimal caixaCorrente || caixaCorrente <= 0m)
        {
            return Resultado261Cumulativo.Bloqueado(MotivoCaixaCorrenteInvalida);
        }

        decimal produzidoCumulativo = produzidoAnterior + caixaCorrente;
        if (produzidoCumulativo > planejado)
        {
            return Resultado261Cumulativo.Bloqueado(MotivoProduzidoMaiorQuePlanejado);
        }

        // ---- §12: completude da lista. Ausencia de prova NAO e prova de completude ----
        if (!entrada.ComponentesCompletosComprovado)
        {
            return Resultado261Cumulativo.Bloqueado(MotivoListaComponentesIncompleta);
        }

        // ---- §9: ledger local obrigatorio ----
        if (entrada.ConsumoLocalConfirmadoPorComponente is not { } ledger)
        {
            return Resultado261Cumulativo.Bloqueado(MotivoLedgerIndisponivel);
        }

        foreach (Componente261Fresco componente in entrada.Componentes)
        {
            string opComponente = componente.NumeroOrdem?.Trim() ?? string.Empty;
            if (opComponente.Length == 0)
            {
                return Resultado261Cumulativo.Bloqueado(
                    $"{MotivoComponenteOrdemAusente} Componente {componente.Identidade}.");
            }

            if (!string.Equals(opComponente, opSolicitada, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(opComponente, opItem, StringComparison.OrdinalIgnoreCase))
            {
                return Resultado261Cumulativo.Bloqueado(
                    $"{MotivoComponenteOrdemDivergente} Componente {componente.Identidade}.");
            }
        }

        IReadOnlyList<Componente261Fresco> elegiveis = entrada.Componentes
            .Where(EhComponente261)
            .ToList();

        if (elegiveis.Count == 0)
        {
            return Resultado261Cumulativo.Bloqueado(MotivoSemComponentes);
        }

        List<Item261Alocado> itens = [];
        List<string> deltaZero = [];

        foreach (Componente261Fresco componente in elegiveis)
        {
            // §4: identidade deterministica obrigatoria (Reservation + ReservationItem).
            if (!componente.IdentidadeDeterministica)
            {
                return Resultado261Cumulativo.Bloqueado(
                    "Componente sem Reservation/ReservationItem: identidade nao deterministica. BLOCK (nenhum POST).");
            }

            // §3: tri-state de QuantityIsFixed. true E null bloqueiam, por motivos DIFERENTES.
            if (componente.QuantityIsFixed is not bool fixa)
            {
                return Resultado261Cumulativo.Bloqueado(
                    $"Componente {componente.Identidade}: QuantityIsFixed AUSENTE/INDETERMINADO. "
                    + "Proporcionalidade nao comprovada. BLOCK (nenhum POST).");
            }

            if (fixa)
            {
                return Resultado261Cumulativo.Bloqueado(
                    $"Componente {componente.Identidade}: QuantityIsFixed=true (quantidade CONSTANTE). "
                    + "Rateio proporcional nao se aplica. BLOCK (nenhum POST).");
            }

            if (string.IsNullOrWhiteSpace(componente.BaseUnit))
            {
                return Resultado261Cumulativo.Bloqueado(
                    $"Componente {componente.Identidade}: BaseUnit indeterminada. BLOCK (nenhum POST).");
            }

            if (componente.RequiredQuantity is not decimal necessidadeOp || necessidadeOp <= 0m)
            {
                return Resultado261Cumulativo.Bloqueado(
                    $"Componente {componente.Identidade}: RequiredQuantity ausente ou <= 0. BLOCK (nenhum POST).");
            }

            if (componente.WithdrawnQuantity is not decimal retiradoFresco || retiradoFresco < 0m)
            {
                return Resultado261Cumulativo.Bloqueado(
                    $"Componente {componente.Identidade}: WithdrawnQuantity ausente/indeterminada. BLOCK (nenhum POST).");
            }

            // §9: reconciliacao SAP x ledger local, por identidade.
            if (!ledger.TryGetValue(componente.Identidade, out decimal consumidoLocal))
            {
                return Resultado261Cumulativo.Bloqueado(
                    $"Componente {componente.Identidade} ausente no ledger local: "
                    + "BLOCK_RECONCILIATION_REQUIRED (nenhum POST).");
            }

            if (Math.Abs(retiradoFresco - consumidoLocal) > ToleranciaReconciliacao)
            {
                return Resultado261Cumulativo.Bloqueado(
                    $"Divergencia material no componente {componente.Identidade}: WithdrawnQuantity fresco do SAP "
                    + "difere do consumo confirmado localmente. BLOCK_RECONCILIATION_REQUIRED (nenhum POST).");
            }

            decimal alvoCumulativo = CalcularAlvoCumulativo(
                necessidadeOp, produzidoCumulativo, planejado);

            decimal delta = alvoCumulativo - retiradoFresco;

            if (delta < 0m)
            {
                return Resultado261Cumulativo.Bloqueado(
                    $"Componente {componente.Identidade}: delta 261 NEGATIVO (alvo cumulativo menor que o ja "
                    + "retirado no SAP). BLOCK (nenhum POST).");
            }

            if (delta == 0m)
            {
                deltaZero.Add(componente.Identidade);
                continue;
            }

            itens.Add(new Item261Alocado
            {
                Reservation = componente.Reservation.Trim(),
                ReservationItem = componente.ReservationItem.Trim(),
                Material = componente.Material.Trim(),
                Plant = componente.Plant.Trim(),
                StorageLocation = componente.StorageLocation.Trim(),
                Batch = componente.Batch.Trim(),
                Unidade = componente.BaseUnit.Trim().ToUpperInvariant(),
                TargetCumulative = alvoCumulativo,
                WithdrawnFresco = retiradoFresco,
                Delta261 = delta
            });
        }

        return itens.Count == 0
            ? new Resultado261Cumulativo(
                CenarioAllocator261.NadaAEnviar,
                "Todos os componentes ja estao no alvo cumulativo (delta zero): nada a enviar no 261.",
                [], produzidoCumulativo, deltaZero)
            : new Resultado261Cumulativo(
                CenarioAllocator261.Ok,
                $"Alocador 261 cumulativo: {itens.Count} componente(s) com delta a enviar.",
                itens, produzidoCumulativo, deltaZero);
    }

    /// <summary>
    /// §10 e §15: alvo cumulativo do componente. Fechamento EXATO quando a producao cumulativa iguala
    /// a planejada — nesse caso o alvo e a RequiredQuantity integral, sem arredondamento, o que
    /// garante que a soma dos deltas fecha a necessidade total sem residuo.
    /// </summary>
    internal static decimal CalcularAlvoCumulativo(
        decimal necessidadeOp, decimal produzidoCumulativo, decimal planejado)
        => produzidoCumulativo == planejado
            ? necessidadeOp
            : Math.Round(
                necessidadeOp * produzidoCumulativo / planejado,
                CasasDecimaisContratoSap,
                MidpointRounding.AwayFromZero);

    /// <summary>
    /// §9: expectativa LOCAL de consumo cumulativo por componente, derivada do MESMO contrato do
    /// alocador para a producao JA confirmada no SAP (PriorProducedConfirmed).
    /// <para>
    /// Por que derivada e nao lida de tabela: nao existe hoje ledger PERSISTIDO por componente, e criar
    /// um exige schema novo — expressamente fora do escopo deste gate. Esta derivacao e a autoridade
    /// LOCAL do pipeline sobre "quanto deveria estar consumido" e serve APENAS de comparador de
    /// reconciliacao; NUNCA e usada como autoridade de producao (essa e o MfgOrderItemGoodsReceiptQty).
    /// Divergencia material contra o WithdrawnQuantity fresco bloqueia antes do POST — foi exatamente
    /// o caso do incidente, em que o SAP tinha a necessidade INTEGRAL retirada.
    /// </para>
    /// Entradas invalidas devolvem null, de modo que o alocador bloqueia por ledger indisponivel.
    /// </summary>
    public static IReadOnlyDictionary<string, decimal>? DerivarLedgerLocalEsperado(
        IReadOnlyList<Componente261Fresco> componentes,
        decimal? produzidoAnteriorConfirmado,
        decimal? planejadoOp)
    {
        ArgumentNullException.ThrowIfNull(componentes);

        if (produzidoAnteriorConfirmado is not decimal anterior || anterior < 0m)
        {
            return null;
        }

        if (planejadoOp is not decimal planejado || planejado <= 0m)
        {
            return null;
        }

        Dictionary<string, decimal> ledger = [];
        foreach (Componente261Fresco componente in componentes.Where(EhComponente261))
        {
            if (!componente.IdentidadeDeterministica || componente.RequiredQuantity is not decimal necessidade)
            {
                continue; // sem identidade/necessidade: o alocador bloqueia por guard proprio.
            }

            ledger[componente.Identidade] = CalcularAlvoCumulativo(necessidade, anterior, planejado);
        }

        return ledger;
    }

    /// <summary>Componente de consumo 261. Tipo de movimento vazio nao exclui (contrato vigente).</summary>
    internal static bool EhComponente261(Componente261Fresco componente)
        => string.IsNullOrWhiteSpace(componente.TipoMovimento)
           || string.Equals(componente.TipoMovimento.Trim(), "261", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// §7: resolve a producao da caixa corrente NA ProductionUnit da OP, sem converter KG &lt;-&gt; UN.
    /// A unidade da OP e comparada com a unidade de QUANTIDADE e com a de PESO da caixa; se nao casar
    /// com nenhuma das duas, devolve null (INDETERMINADO) para o alocador bloquear.
    /// </summary>
    public static decimal? ResolverProducaoCaixaCorrente(ProdutoAcabadoCaixa caixa, string? productionUnit)
    {
        ArgumentNullException.ThrowIfNull(caixa);

        string unidadeOp = productionUnit?.Trim() ?? string.Empty;
        if (unidadeOp.Length == 0)
        {
            return null;
        }

        if (Equivale(unidadeOp, caixa.UnidadeQuantidade))
        {
            return caixa.QuantidadeProdutos;
        }

        return Equivale(unidadeOp, caixa.UnidadePeso) ? caixa.PesoLiquidoKg : null;
    }

    private static bool Equivale(string a, string? b)
        => !string.IsNullOrWhiteSpace(b) && string.Equals(a, b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// GATE 118B-R4: monta a entrada do alocador a partir da leitura FRESCA + caixa corrente. E o
    /// MESMO caminho usado em runtime pelo Controller (seam unico, nao via paralela de teste), para
    /// que o wiring da OP retornada possa ser exercitado de ponta a ponta.
    /// <para>
    /// NumeroOrdemItemFresco vem EXCLUSIVAMENTE de <c>fresca.Item.NumeroOrdem</c> — a OP
    /// materialmente retornada pelo SAP. NUNCA de <c>fresca.NumeroOrdemConsultada</c>, que e apenas
    /// o eco da request e tornaria a checagem cruzada tautologica.
    /// </para>
    /// </summary>
    public static Entrada261Cumulativa MontarEntrada(
        LeituraFrescaOrdem261 fresca,
        ProdutoAcabadoCaixa caixa,
        string numeroOrdemSolicitada)
    {
        ArgumentNullException.ThrowIfNull(fresca);
        ArgumentNullException.ThrowIfNull(caixa);

        IReadOnlyList<Componente261Fresco> componentes = ProjetarComponentes(fresca);

        return new Entrada261Cumulativa
        {
            NumeroOrdem = numeroOrdemSolicitada?.Trim() ?? string.Empty,
            NumeroOrdemItemFresco = fresca.Item?.NumeroOrdem ?? string.Empty,
            PlannedProductionOp = fresca.Item?.QuantidadePrevistaSap,
            PriorProducedConfirmed = fresca.Item?.QuantidadeRecebidaSap,
            CurrentBoxProduction = ResolverProducaoCaixaCorrente(caixa, fresca.Item?.Unidade),
            ProductionUnit = fresca.Item?.Unidade ?? string.Empty,
            Componentes = componentes,
            ComponentesCompletosComprovado = fresca.ComponentesCompletos,
            ConsumoLocalConfirmadoPorComponente = DerivarLedgerLocalEsperado(
                componentes, fresca.Item?.QuantidadeRecebidaSap, fresca.Item?.QuantidadePrevistaSap)
        };
    }

    /// <summary>
    /// Projeta a leitura FRESCA do cliente SAP nos componentes tri-state do alocador, preservando
    /// WithdrawnQuantity e QuantityIsFixed sem coalescencia.
    /// </summary>
    public static IReadOnlyList<Componente261Fresco> ProjetarComponentes(LeituraFrescaOrdem261 leitura)
    {
        ArgumentNullException.ThrowIfNull(leitura);

        return leitura.Componentes
            .Select(c => new Componente261Fresco
            {
                NumeroOrdem = c.NumeroOrdem,
                Reservation = c.Reserva,
                ReservationItem = c.ItemReserva,
                Material = c.Material,
                Plant = c.Centro,
                StorageLocation = c.Deposito,
                Batch = c.Lote,
                BaseUnit = c.UnidadeBase,
                TipoMovimento = c.TipoMovimento,
                RequiredQuantity = c.QuantidadeNecessariaSap,
                WithdrawnQuantity = c.QuantidadeRetiradaSap,
                ConfirmedAvailableQuantity = c.QuantidadeDisponivelConfirmadaSap,
                QuantityIsFixed = c.QuantidadeFixa
            })
            .ToList();
    }
}
