namespace FugaPET_HML.Servicos.Diagnostico;

/// <summary>
/// GATE 102J-C — instrumentação TEMPORÁRIA e SANITIZADA do caminho de recovery da ProcessoConsumoMaterialForm.
/// DESLIGADA por padrão. Só ativa em ambiente Q (marcador ambiente.q.json no diretório do app) E com a flag
/// de PROCESSO <c>FUGAPET_Q_RECOVERY_DIAG_102J=1</c>. Fora disso: ZERO log, ZERO efeito. Best-effort: qualquer
/// falha de escrita é engolida e NUNCA altera o fluxo funcional nem lança para a Form. Não escreve em banco/SAP;
/// não registra segredos (só identificadores funcionais e estados de UI).
/// </summary>
public static class RecoveryDiag102J
{
    internal const string NomeFlag = "FUGAPET_Q_RECOVERY_DIAG_102J";
    private const string MarcadorAmbienteQ = "ambiente.q.json";

    /// <summary>Avaliado UMA vez no carregamento do tipo. Cache imutável: custo nulo quando desligado.</summary>
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

    /// <summary>Correlation id somente-diagnóstico por instância de Form.</summary>
    public static Guid NovaCorrelacao() => Guid.NewGuid();

    /// <summary>Registra uma linha sanitizada. No-op quando desligado; best-effort quando ligado.</summary>
    public static void Log(Guid correlacao, string ponto, string? detalhe = null)
    {
        if (!Ativo)
        {
            return;
        }

        try
        {
            EscreverLinha(CaminhoLog(), Formatar(correlacao, ponto, detalhe));
        }
        catch
        {
            // Best-effort: falha de diagnóstico NUNCA afeta o fluxo funcional.
        }
    }

    internal static string Formatar(Guid correlacao, string ponto, string? detalhe)
    {
        string baseLinha =
            $"{DateTime.UtcNow:O}|PID={Environment.ProcessId}|TID={Environment.CurrentManagedThreadId}"
            + $"|CID={correlacao}|{ponto}";
        return detalhe is null ? baseLinha : baseLinha + "|" + detalhe;
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
