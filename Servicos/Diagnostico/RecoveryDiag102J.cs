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

/// <summary>Contexto do apontamento — campos allowlisted.</summary>
public readonly record struct ContextoDiag102J(
    long CodigoApontamento, string Op, string Operacao, string Sequencia, string TipoProcesso);

/// <summary>Identidade de um componente da ocorrência — campos allowlisted (identidade de reserva/matcher).</summary>
public readonly record struct ComponenteDiag102J(
    string Material, string Reservation, string ReservationItem, string StorageLocation, string Batch, string Movement);

/// <summary>Resultado do resolver — campos allowlisted.</summary>
public readonly record struct ResultadoDiag102J(
    string ModalidadeBruta, string ModalidadeEfetiva, long? CodigoLancamento, int CandidateCount);

/// <summary>Snapshot de estado — SOMENTE modalidade/PK + classificação de status + booleans de controles.</summary>
public readonly record struct SnapshotDiag102J(
    string Modalidade,
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
/// GATE 102J-C/102J-E — instrumentação TEMPORÁRIA e SANITIZADA POR CONSTRUÇÃO (allowlist estrutural).
/// DESLIGADA por padrão. Só ativa em ambiente Q (marcador ambiente.q.json) E com a flag de PROCESSO
/// <c>FUGAPET_Q_RECOVERY_DIAG_102J=1</c>. NÃO existe API que aceite texto livre para o log: cada método aceita
/// SOMENTE estruturas/enum/campos conhecidos, formatados internamente. Best-effort: falha de escrita é engolida
/// e NUNCA altera o fluxo. Não escreve banco/SAP; não registra segredos nem texto de UI (status/orientação).
/// </summary>
public static class RecoveryDiag102J
{
    internal const string NomeFlag = "FUGAPET_Q_RECOVERY_DIAG_102J";
    private const string MarcadorAmbienteQ = "ambiente.q.json";

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

    // ---------- API TIPADA (sem texto livre) ----------

    public static void LogMarco(Guid cid, PontoDiag102J ponto)
        => Emitir(cid, ponto, null);

    public static void LogContexto(Guid cid, PontoDiag102J ponto, in ContextoDiag102J ctx)
        => Emitir(cid, ponto,
            $"CodApont={ctx.CodigoApontamento};OP={ctx.Op};Op={ctx.Operacao};Seq={ctx.Sequencia};Tipo={ctx.TipoProcesso}");

    public static void LogComponente(Guid cid, PontoDiag102J ponto, int index, in ComponenteDiag102J c)
        => Emitir(cid, ponto,
            $"idx={index};Mat={c.Material};Res={c.Reservation};Item={c.ReservationItem};"
            + $"Dep={c.StorageLocation};Batch={c.Batch};Mov={c.Movement}");

    public static void LogContagemComponentes(Guid cid, PontoDiag102J ponto, int count)
        => Emitir(cid, ponto, $"count={count}");

    public static void LogResultado(Guid cid, PontoDiag102J ponto, in ResultadoDiag102J r)
        => Emitir(cid, ponto,
            $"modalidadeBruta={r.ModalidadeBruta};modalidadeEfetiva={r.ModalidadeEfetiva};"
            + $"pk={(r.CodigoLancamento?.ToString() ?? "null")};candidatos={r.CandidateCount}");

    public static void LogSnapshot(Guid cid, PontoDiag102J ponto, in SnapshotDiag102J s)
        => Emitir(cid, ponto,
            $"modalidade={s.Modalidade};pk={(s.CodigoLancamento?.ToString() ?? "null")};statusClass={s.StatusClass};"
            + $"start.En={s.StartEnabled};read.En={s.ReadEnabled};read.Vis={s.ReadVisible};"
            + $"manual.En={s.ManualEnabled};manual.Vis={s.ManualVisible};confirm.En={s.ConfirmEnabled};"
            + $"send.Vis={s.SendVisible};send.En={s.SendEnabled}");

    public static void LogSnapshotComOrientacao(Guid cid, PontoDiag102J ponto, in SnapshotDiag102J s, OrientacaoClass102J orientacao)
        => Emitir(cid, ponto,
            $"orientacaoClass={orientacao};modalidade={s.Modalidade};pk={(s.CodigoLancamento?.ToString() ?? "null")};"
            + $"statusClass={s.StatusClass};start.En={s.StartEnabled};read.En={s.ReadEnabled};read.Vis={s.ReadVisible};"
            + $"manual.En={s.ManualEnabled};manual.Vis={s.ManualVisible};confirm.En={s.ConfirmEnabled};"
            + $"send.Vis={s.SendVisible};send.En={s.SendEnabled}");

    // ---------- Formatação/escrita interna (NÃO acessível como texto livre externo) ----------

    private static void Emitir(Guid cid, PontoDiag102J ponto, string? camposEstruturados)
    {
        if (!Ativo)
        {
            return;
        }

        try
        {
            EscreverLinha(CaminhoLog(), Formatar(cid, ponto, camposEstruturados));
        }
        catch
        {
            // Best-effort: falha de diagnóstico NUNCA afeta o fluxo funcional.
        }
    }

    // internal para teste — recebe SOMENTE enum + campos já estruturados por esta classe (nunca entrada externa livre).
    internal static string Formatar(Guid cid, PontoDiag102J ponto, string? camposEstruturados)
    {
        string baseLinha =
            $"{DateTime.UtcNow:O}|PID={Environment.ProcessId}|TID={Environment.CurrentManagedThreadId}"
            + $"|CID={cid}|{ponto}";
        return camposEstruturados is null ? baseLinha : baseLinha + "|" + camposEstruturados;
    }

    internal static string CaminhoLog()
        => Path.Combine(Path.GetTempPath(), "FugaPET_Q", "102J", $"recovery_{Environment.ProcessId}.log");

    internal static void EscreverLinha(string caminho, string linha)
    {
        string? dir = Path.GetDirectoryName(caminho);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.AppendAllText(caminho, linha + Environment.NewLine);
    }
}
