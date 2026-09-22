using FugaPET_HML.Modelo.Consumo;
using FugaPET_HML.Modelo.Processo;

namespace FugaPET_HML.Servicos.Diagnostico;

/// <summary>Ponto de diagnóstico ALLOWLISTED (enum fechado — nunca texto operacional arbitrário).</summary>
public enum PontoDiag102J
{
    A_FormConstructor,
    B_ShownEnter,
    C_ConsultarOpEnter,
    C_ReentranciaBloqueada,
    D_ComponentesSap,
    E_AposFiltroOperacao,
    E_FiltroZeroComponentes,
    F_BeforeRecoveryResolver,
    F2_ResolverInput,
    G_AfterRecoveryResolver,
    H_BeforeAplicarMaterializacao,
    I_AfterAplicarMaterializacao,
    K1_AposResolverNoConsultar,
    J_AtualizarComponenteSelecionado,
    J_AtualizarLiberacaoInicioLeitura,
    J_AtualizarApontamentoVisual,
    J_ReaplicarEstadoRecuperacao,
    J_ClearGridSelectionsEnter,
    J_ClearGridSelectionsExit,
    K_FinalSnapshot
}

/// <summary>Classificação FINITA do estado de leitura/recovery (substitui statusLabel.Text — nunca texto livre).</summary>
public enum StatusClass102J
{
    RecoveryPendente,
    Reconciliacao,
    FreshFlow,
    Outro
}

/// <summary>Classificação FINITA da orientação (substitui o texto `orientacao` — nunca texto livre).</summary>
public enum OrientacaoClass102J
{
    SemComponente,
    ComponentePresente,
    RecoveryBloqueado,
    Outro
}

/// <summary>Snapshot de estado — SOMENTE modalidade/PK + classificação de status + booleans de controles.</summary>
public readonly record struct SnapshotDiag102J(
    ModalidadeRecuperacaoConsumo Modalidade,
    long? CodigoLancamento,
    StatusClass102J StatusClass,
    bool StartEnabled,
    bool ReadEnabled,
    bool ReadVisible,
    bool ManualEnabled,
    bool ManualVisible,
    bool ConfirmEnabled,
    bool SendVisible,
    bool SendEnabled);

/// <summary>
/// GATE 102J-C/102J-E/102J-I — instrumentação TEMPORÁRIA e SANITIZADA POR CONSTRUÇÃO (allowlist estrutural
/// + de valores). DESLIGADA por padrão (só ambiente Q + flag de PROCESSO FUGAPET_Q_RECOVERY_DIAG_102J=1).
/// NÃO existe API/formatter/writer que aceite string arbitrária como payload: a linha é um tipo OPACO
/// (<see cref="LinhaDiag102J"/>) com construtor PRIVADO, produzível apenas por factories privadas TIPADAS a
/// partir de tipos/campos já aprovados. Best-effort: falha de escrita é engolida e NUNCA altera o fluxo.
/// Não escreve banco/SAP; não registra segredos nem texto de UI (status/orientação).
/// </summary>
public static class RecoveryDiag102J
{
    internal const string NomeFlag = "FUGAPET_Q_RECOVERY_DIAG_102J";
    private const string MarcadorAmbienteQ = "ambiente.q.json";
    private const string ValorInvalido = "INVALID";

    public static bool Ativo { get; } = AvaliarAtivo(
        Environment.GetEnvironmentVariable(NomeFlag, EnvironmentVariableTarget.Process),
        AmbienteQDetectado());

    internal static bool AvaliarAtivo(string? valorFlag, bool ambienteQ)
        => ambienteQ && string.Equals(valorFlag, "1", StringComparison.Ordinal);

    internal static bool AmbienteQDetectado()
    {
        try
        {
            return File.Exists(Path.Combine(AppContext.BaseDirectory, MarcadorAmbienteQ));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Correlation id somente-diagnóstico. NUNCA gerar quando desligado (§9).</summary>
    public static Guid NovaCorrelacao() => Ativo ? Guid.NewGuid() : Guid.Empty;

    // ---------- API TIPADA pública (assinaturas INALTERADAS — call-sites intocados) ----------

    public static void LogMarco(Guid cid, PontoDiag102J ponto)
    {
        if (!Ativo) { return; }
        Escrever(LinhaDiag102J.Marco(cid, ponto));
    }

    public static void LogContexto(Guid cid, PontoDiag102J ponto, ContextoApontamentoProcesso contexto)
    {
        if (!Ativo) { return; }
        Escrever(LinhaDiag102J.Contexto(cid, ponto, contexto));
    }

    public static void LogComponente(Guid cid, PontoDiag102J ponto, int index, ComponenteConsumoMaterial componente)
    {
        if (!Ativo) { return; }
        Escrever(LinhaDiag102J.Componente(cid, ponto, index, componente));
    }

    public static void LogContagemComponentes(Guid cid, PontoDiag102J ponto, int count)
    {
        if (!Ativo) { return; }
        Escrever(LinhaDiag102J.Contagem(cid, ponto, count));
    }

    public static void LogResultado(
        Guid cid,
        PontoDiag102J ponto,
        ModalidadeRecuperacaoConsumo modalidadeBruta,
        ModalidadeRecuperacaoConsumo modalidadeEfetiva,
        long? codigoLancamento,
        int candidateCount)
    {
        if (!Ativo) { return; }
        Escrever(LinhaDiag102J.Resultado(cid, ponto, modalidadeBruta, modalidadeEfetiva, codigoLancamento, candidateCount));
    }

    public static void LogSnapshot(Guid cid, PontoDiag102J ponto, in SnapshotDiag102J s)
    {
        if (!Ativo) { return; }
        Escrever(LinhaDiag102J.Snapshot(cid, ponto, in s));
    }

    public static void LogSnapshotComOrientacao(Guid cid, PontoDiag102J ponto, in SnapshotDiag102J s, OrientacaoClass102J orientacao)
    {
        if (!Ativo) { return; }
        Escrever(LinhaDiag102J.SnapshotComOrientacao(cid, ponto, in s, orientacao));
    }

    // ---------- Writer OPACO: recebe SOMENTE LinhaDiag102J (nunca string). ----------

    private static void Escrever(in LinhaDiag102J linha)
    {
        try
        {
            PersistirLinha(in linha);
        }
        catch
        {
            // Best-effort: falha de diagnóstico NUNCA afeta o fluxo funcional.
        }
    }

    private static void PersistirLinha(in LinhaDiag102J linha)
    {
        string caminho = CaminhoLog();
        string? dir = Path.GetDirectoryName(caminho);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        // ÚNICO ponto de conversão do tipo opaco em texto, encapsulado no writer.
        File.AppendAllText(caminho, linha.ObterTexto() + Environment.NewLine);
    }

    internal static string CaminhoLog()
        => Path.Combine(Path.GetTempPath(), "FugaPET_Q", "102J", $"recovery_{Environment.ProcessId}.log");

    // ---------- Formatters TIPADOS para teste (retornam texto para inspeção; sem string livre de entrada). ----------

    internal static string FormatarMarcoParaTeste(Guid cid, PontoDiag102J ponto)
        => LinhaDiag102J.Marco(cid, ponto).ObterTexto();

    internal static string FormatarContextoParaTeste(Guid cid, PontoDiag102J ponto, ContextoApontamentoProcesso contexto)
        => LinhaDiag102J.Contexto(cid, ponto, contexto).ObterTexto();

    internal static string FormatarComponenteParaTeste(Guid cid, PontoDiag102J ponto, int index, ComponenteConsumoMaterial componente)
        => LinhaDiag102J.Componente(cid, ponto, index, componente).ObterTexto();

    internal static string FormatarResultadoParaTeste(
        Guid cid,
        PontoDiag102J ponto,
        ModalidadeRecuperacaoConsumo modalidadeBruta,
        ModalidadeRecuperacaoConsumo modalidadeEfetiva,
        long? codigoLancamento,
        int candidateCount)
        => LinhaDiag102J.Resultado(cid, ponto, modalidadeBruta, modalidadeEfetiva, codigoLancamento, candidateCount).ObterTexto();

    // ==========================================================================================
    // Tipo OPACO da linha de diagnóstico. Construtor PRIVADO: NUNCA public/internal aceitando string.
    // Só as factories TIPADAS abaixo (que recebem tipos/valores já aprovados) podem produzi-lo.
    // ==========================================================================================
    private readonly struct LinhaDiag102J
    {
        private readonly string _texto;

        private LinhaDiag102J(string texto) => _texto = texto;

        internal string ObterTexto() => _texto;

        internal static LinhaDiag102J Marco(Guid cid, PontoDiag102J ponto)
            => new(Prefixo(cid, ponto));

        internal static LinhaDiag102J Contexto(Guid cid, PontoDiag102J ponto, ContextoApontamentoProcesso contexto)
            => new($"{Prefixo(cid, ponto)}|{CamposContexto(contexto.CodigoApontamento, contexto.NumeroOrdem, contexto.Operacao, contexto.Sequencia, contexto.TipoProcesso)}");

        internal static LinhaDiag102J Componente(Guid cid, PontoDiag102J ponto, int index, ComponenteConsumoMaterial componente)
            => new($"{Prefixo(cid, ponto)}|{CamposComponente(index, componente.CodigoMaterial, componente.NumeroReserva, componente.ItemReserva, componente.DepositoConsumo, componente.Lote, componente.TipoMovimento, componente.Operacao, componente.SequenciaOperacao)}");

        internal static LinhaDiag102J Contagem(Guid cid, PontoDiag102J ponto, int count)
            => new($"{Prefixo(cid, ponto)}|count={SanitizarNaoNegativo(count)}");

        internal static LinhaDiag102J Resultado(
            Guid cid, PontoDiag102J ponto, ModalidadeRecuperacaoConsumo modalidadeBruta,
            ModalidadeRecuperacaoConsumo modalidadeEfetiva, long? codigoLancamento, int candidateCount)
            => new($"{Prefixo(cid, ponto)}|{CamposResultado(modalidadeBruta.ToString(), modalidadeEfetiva.ToString(), codigoLancamento, candidateCount)}");

        internal static LinhaDiag102J Snapshot(Guid cid, PontoDiag102J ponto, in SnapshotDiag102J s)
            => new($"{Prefixo(cid, ponto)}|{CamposSnapshot(in s)}");

        internal static LinhaDiag102J SnapshotComOrientacao(Guid cid, PontoDiag102J ponto, in SnapshotDiag102J s, OrientacaoClass102J orientacao)
            => new($"{Prefixo(cid, ponto)}|orientacaoClass={orientacao};{CamposSnapshot(in s)}");
    }

    private static string Prefixo(Guid cid, PontoDiag102J ponto)
        => $"{DateTime.UtcNow:O}|PID={Environment.ProcessId}|TID={Environment.CurrentManagedThreadId}|CID={cid}|{ponto}";

    private static string CamposSnapshot(in SnapshotDiag102J s)
        => $"modalidade={SanitizarModalidade(s.Modalidade.ToString())};"
            + $"pk={(s.CodigoLancamento?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null")};"
            + $"statusClass={s.StatusClass};start.En={s.StartEnabled};read.En={s.ReadEnabled};read.Vis={s.ReadVisible};"
            + $"manual.En={s.ManualEnabled};manual.Vis={s.ManualVisible};confirm.En={s.ConfirmEnabled};"
            + $"send.Vis={s.SendVisible};send.En={s.SendEnabled}";

    private static string CamposContexto(
        long codigoApontamento, string? op, string? operacao, string? sequencia, string? tipoProcesso)
        => $"CodApont={SanitizarPositivo(codigoApontamento)};OP={SanitizarNumerico(op, 20)};"
            + $"Op={SanitizarTecnico(operacao, 20)};Seq={SanitizarTecnico(sequencia, 20)};"
            + $"Tipo={SanitizarTipoProcesso(tipoProcesso)}";

    private static string CamposComponente(
        int index, string? material, string? reserva, string? itemReserva, string? deposito,
        string? lote, string? movimento, string? operacao, string? sequencia)
        => $"idx={SanitizarNaoNegativo(index)};Mat={SanitizarTecnico(material, 40)};"
            + $"Res={SanitizarNumerico(reserva, 20)};Item={SanitizarNumerico(itemReserva, 10)};"
            + $"Dep={SanitizarTecnico(deposito, 10)};Batch={SanitizarTecnico(lote, 40)};"
            + $"Mov={SanitizarNumerico(movimento, 4)};Op={SanitizarTecnico(operacao, 20)};"
            + $"Seq={SanitizarTecnico(sequencia, 20)}";

    private static string CamposResultado(
        string? modalidadeBruta, string? modalidadeEfetiva, long? codigoLancamento, int candidateCount)
        => $"modalidadeBruta={SanitizarModalidade(modalidadeBruta)};"
            + $"modalidadeEfetiva={SanitizarModalidade(modalidadeEfetiva)};"
            + $"pk={SanitizarOpcionalPositivo(codigoLancamento)};candidatos={SanitizarNaoNegativo(candidateCount)}";

    private static string SanitizarNumerico(string? valor, int tamanhoMaximo)
        => ValorPermitido(valor, tamanhoMaximo, char.IsAsciiDigit);

    private static string SanitizarTecnico(string? valor, int tamanhoMaximo)
        => ValorPermitido(valor, tamanhoMaximo, char.IsAsciiLetterOrDigit);

    private static string ValorPermitido(string? valor, int tamanhoMaximo, Func<char, bool> caracterePermitido)
    {
        if (string.IsNullOrEmpty(valor) || valor.Length > tamanhoMaximo)
        {
            return ValorInvalido;
        }

        return valor.All(caracterePermitido) ? valor : ValorInvalido;
    }

    private static string SanitizarTipoProcesso(string? tipoProcesso)
        => tipoProcesso switch
        {
            TipoProcessoOperacao.ConsumoMateriaPrima => TipoProcessoOperacao.ConsumoMateriaPrima,
            TipoProcessoOperacao.ConsumoQuimicos => TipoProcessoOperacao.ConsumoQuimicos,
            TipoProcessoOperacao.SemiAcabado => TipoProcessoOperacao.SemiAcabado,
            TipoProcessoOperacao.ProdutoAcabado => TipoProcessoOperacao.ProdutoAcabado,
            TipoProcessoOperacao.ResultadoApontamento => TipoProcessoOperacao.ResultadoApontamento,
            TipoProcessoOperacao.SemDestinoConfigurado => TipoProcessoOperacao.SemDestinoConfigurado,
            _ => ValorInvalido
        };

    private static string SanitizarModalidade(string? modalidade)
        => Enum.TryParse(modalidade, ignoreCase: false, out ModalidadeRecuperacaoConsumo valor)
            && Enum.IsDefined(valor)
                ? valor.ToString()
                : ValorInvalido;

    private static string SanitizarPositivo(long valor)
        => valor > 0 ? valor.ToString(System.Globalization.CultureInfo.InvariantCulture) : ValorInvalido;

    private static string SanitizarOpcionalPositivo(long? valor)
        => valor is null ? "null" : SanitizarPositivo(valor.Value);

    private static string SanitizarNaoNegativo(int valor)
        => valor >= 0 ? valor.ToString(System.Globalization.CultureInfo.InvariantCulture) : ValorInvalido;
}
