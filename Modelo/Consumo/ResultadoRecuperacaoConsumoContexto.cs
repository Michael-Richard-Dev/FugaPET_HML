namespace FugaPET_HML.Modelo.Consumo;

/// <summary>
/// GATE 101J — modalidade da resolução READ-ONLY do estado de consumo PERSISTIDO correspondente à
/// ocorrência operacional atual (OP + operação/sequência via conjunto de componentes já filtrado),
/// resolvida por MATCH item-a-item. Fail-closed: nunca escolhe "mais recente"/menor/maior PK.
/// </summary>
public enum ModalidadeRecuperacaoConsumo
{
    /// <summary>Nenhum lançamento persistido corresponde à ocorrência: fluxo novo normal.</summary>
    Nenhum = 0,

    /// <summary>Exatamente UM PENDENTE_SAP elegível (doc/ano nulos, match exato): recuperar esse PK.</summary>
    UmPendente = 1,

    /// <summary>Mais de um PENDENTE_SAP elegível para a MESMA ocorrência: AMBÍGUO, fail-closed.</summary>
    AmbiguoPendente = 2,

    /// <summary>
    /// Existe lançamento ENVIANDO_SAP correspondente: reconciliação pendente. Não recupera para envio,
    /// mas bloqueia novo consumo. Autoridade do bloqueio vem do estado persistido (sobrevive a restart).
    /// </summary>
    EnviandoReconciliacao = 3
}

/// <summary>
/// Resultado explícito e fail-closed da resolução contextual do consumo persistido. Só carrega PK quando
/// <see cref="Modalidade"/> == <see cref="ModalidadeRecuperacaoConsumo.UmPendente"/>.
/// </summary>
public sealed class ResultadoRecuperacaoConsumoContexto
{
    public ModalidadeRecuperacaoConsumo Modalidade { get; init; }

    /// <summary>PK do lançamento SOMENTE quando <see cref="Modalidade"/> == UmPendente; caso contrário null.</summary>
    public long? CodigoLancamento { get; init; }

    /// <summary>Códigos PENDENTE_SAP validados quando AmbiguoPendente (diagnóstico; nunca autosseleção).</summary>
    public IReadOnlyList<long> Candidatos { get; init; } = [];

    public static ResultadoRecuperacaoConsumoContexto Nenhum { get; } =
        new() { Modalidade = ModalidadeRecuperacaoConsumo.Nenhum };

    public static ResultadoRecuperacaoConsumoContexto UmPendente(long codigoLancamento)
        => new() { Modalidade = ModalidadeRecuperacaoConsumo.UmPendente, CodigoLancamento = codigoLancamento };

    public static ResultadoRecuperacaoConsumoContexto AmbiguoPendente(IReadOnlyList<long> candidatos)
        => new() { Modalidade = ModalidadeRecuperacaoConsumo.AmbiguoPendente, Candidatos = candidatos };

    public static ResultadoRecuperacaoConsumoContexto EnviandoReconciliacao(IReadOnlyList<long> correspondentes)
        => new() { Modalidade = ModalidadeRecuperacaoConsumo.EnviandoReconciliacao, Candidatos = correspondentes };
}
