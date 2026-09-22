using System.Reflection;
using FugaPET_HML.Servicos.Diagnostico;

namespace FugaPET_HML.Tests.Diagnostico;

/// <summary>
/// GATE 102J-C/102J-E — testes da instrumentação: gating (Q + flag), ALLOWLIST ESTRUTURAL (sem texto livre),
/// sanitização por construção, correlation id lazy (não materializa quando OFF) e escrita best-effort.
/// </summary>
public sealed class RecoveryDiag102JTests
{
    private const BindingFlags TodosMetodos =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;

    // ---------- Gating ----------

    [Theory]
    [InlineData(null, true, false)]
    [InlineData("", true, false)]
    [InlineData("0", true, false)]
    [InlineData("true", true, false)]
    [InlineData("1", false, false)]
    [InlineData("1", true, true)]
    public void AvaliarAtivo_MatrizGating(string? flag, bool ambienteQ, bool esperado)
        => Assert.Equal(esperado, RecoveryDiag102J.AvaliarAtivo(flag, ambienteQ));

    [Fact]
    public void Ativo_PorPadrao_Desligado() => Assert.False(RecoveryDiag102J.Ativo);

    // ---------- §9: DIAG OFF não materializa correlation id ----------

    [Fact]
    public void NovaCorrelacao_ComDiagDesligado_RetornaGuidVazio()
        => Assert.Equal(Guid.Empty, RecoveryDiag102J.NovaCorrelacao());

    // ---------- §3/§4/§10.A: ALLOWLIST ESTRUTURAL — não existe API de texto livre para o log ----------

    [Fact] // A antiga API livre Log(Guid, string ponto, string detalhe) NÃO existe mais.
    public void NaoExisteApiDeTextoLivreParaLog()
    {
        MethodInfo? logLivre = typeof(RecoveryDiag102J).GetMethod(
            "Log", TodosMetodos, binder: null,
            types: [typeof(Guid), typeof(string), typeof(string)], modifiers: null);
        Assert.Null(logLivre);

        // Nenhum método público aceita 'string ponto' — o ponto é SEMPRE o enum allowlisted PontoDiag102J.
        foreach (MethodInfo m in typeof(RecoveryDiag102J).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            ParameterInfo[] ps = m.GetParameters();
            if (ps.Length >= 2 && ps[0].ParameterType == typeof(Guid))
            {
                Assert.Equal(typeof(PontoDiag102J), ps[1].ParameterType);
            }
        }
    }

    [Fact] // §8: o ponto é enum fechado (allowlist), nunca string operacional arbitrária.
    public void PontoDiagnostico_EhEnumFechado()
    {
        Assert.True(typeof(PontoDiag102J).IsEnum);
        Assert.Contains(PontoDiag102J.G_AfterRecoveryResolver, Enum.GetValues<PontoDiag102J>());
    }

    [Fact] // §6/§7: status e orientação são CLASSIFICAÇÕES FINITAS (enum), nunca texto de UI.
    public void StatusEOrientacao_SaoEnumsFinitos()
    {
        Assert.True(typeof(StatusClass102J).IsEnum);
        Assert.True(typeof(OrientacaoClass102J).IsEnum);

        // O snapshot NÃO possui campo de texto de status/orientação — só StatusClass (enum) + booleans + modalidade.
        string[] campos = typeof(SnapshotDiag102J).GetProperties().Select(p => p.Name).ToArray();
        Assert.Contains(nameof(SnapshotDiag102J.StatusClass), campos);
        Assert.DoesNotContain("StatusText", campos);
        Assert.DoesNotContain("Orientacao", campos);
        Assert.DoesNotContain("Texto", campos);

        // LogSnapshotComOrientacao recebe OrientacaoClass102J (enum), não string.
        MethodInfo? m = typeof(RecoveryDiag102J).GetMethod(nameof(RecoveryDiag102J.LogSnapshotComOrientacao));
        Assert.NotNull(m);
        Assert.Contains(m!.GetParameters(), p => p.ParameterType == typeof(OrientacaoClass102J));
    }

    [Fact] // §5: identidade de componente só expõe os 6 campos allowlisted (sem texto livre).
    public void ComponenteDiag_SoTemCamposAllowlisted()
    {
        string[] campos = typeof(ComponenteDiag102J).GetProperties().Select(p => p.Name).OrderBy(x => x).ToArray();
        Assert.Equal(
            new[] { "Batch", "Material", "Movement", "Reservation", "ReservationItem", "StorageLocation" },
            campos);
    }

    // ---------- Sanitização por construção da linha ----------

    [Fact]
    public void Formatar_ContemMetadados_ESemSegredos()
    {
        Guid cid = Guid.NewGuid();
        // 'camposEstruturados' aqui é o que os métodos tipados produzem (enum/valores allowlisted) — nunca texto de UI.
        string linha = RecoveryDiag102J.Formatar(cid, PontoDiag102J.G_AfterRecoveryResolver,
            "modalidadeBruta=UmPendente;modalidadeEfetiva=UmPendente;pk=8;candidatos=0");

        Assert.Contains("PID=", linha, StringComparison.Ordinal);
        Assert.Contains("TID=", linha, StringComparison.Ordinal);
        Assert.Contains($"CID={cid}", linha, StringComparison.Ordinal);
        Assert.Contains("G_AfterRecoveryResolver", linha, StringComparison.Ordinal);

        foreach (string proibido in new[] { "senha", "password", "Authorization", "Bearer", "token", "connectionstring", "Host=", "User Id", "pwd=" })
        {
            Assert.DoesNotContain(proibido, linha, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Formatar_SemDetalhe_NaoAdicionaPipeFinal()
    {
        string linha = RecoveryDiag102J.Formatar(Guid.Empty, PontoDiag102J.B_ShownEnter, null);
        Assert.EndsWith("|B_ShownEnter", linha, StringComparison.Ordinal);
    }

    [Fact]
    public void CaminhoLog_EhTemporarioExclusivoPorPid()
    {
        string caminho = RecoveryDiag102J.CaminhoLog();
        Assert.Contains(Path.Combine("FugaPET_Q", "102J"), caminho, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"recovery_{Environment.ProcessId}.log", caminho, StringComparison.Ordinal);
    }

    [Fact]
    public void EscreverLinha_CriaArquivoEAppenda()
    {
        string dir = Path.Combine(Path.GetTempPath(), "FugaPET_Q_102J_TESTE", Guid.NewGuid().ToString("N"));
        string caminho = Path.Combine(dir, "recovery_teste.log");
        try
        {
            RecoveryDiag102J.EscreverLinha(caminho, "linha-1");
            RecoveryDiag102J.EscreverLinha(caminho, "linha-2");
            Assert.True(File.Exists(caminho));
            string[] linhas = File.ReadAllLines(caminho);
            Assert.Equal(2, linhas.Length);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
        }
    }

    [Fact] // §10.D: DIAG OFF → nenhum arquivo criado por LogSnapshot/LogMarco.
    public void Log_ComDiagDesligado_NaoCriaArquivo()
    {
        string caminho = RecoveryDiag102J.CaminhoLog();
        try { if (File.Exists(caminho)) File.Delete(caminho); } catch { /* ignore */ }

        RecoveryDiag102J.LogMarco(Guid.NewGuid(), PontoDiag102J.K_FinalSnapshot);
        RecoveryDiag102J.LogSnapshot(Guid.NewGuid(), PontoDiag102J.I_AfterAplicarMaterializacao,
            new SnapshotDiag102J("UmPendente", 8, StatusClass102J.RecoveryPendente, false, false, false, false, false, false, true, true));

        Assert.False(RecoveryDiag102J.Ativo);
        Assert.False(File.Exists(caminho));
    }
}
