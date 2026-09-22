using FugaPET_HML.Servicos.Diagnostico;

namespace FugaPET_HML.Tests.Diagnostico;

/// <summary>
/// GATE 102J-C — testes da instrumentação temporária: gating (Q + flag), sanitização e escrita best-effort.
/// A instrumentação está DESLIGADA por padrão (sem Q ou sem flag), garantindo zero efeito no fluxo normal.
/// </summary>
public sealed class RecoveryDiag102JTests
{
    // ---------- Gating: SÓ ativa com Q + flag == "1" ----------

    [Theory]
    [InlineData(null, true, false)]     // sem flag, em Q → OFF
    [InlineData("", true, false)]       // flag vazia → OFF
    [InlineData("0", true, false)]      // flag != 1 → OFF
    [InlineData("true", true, false)]   // flag não é exatamente "1" → OFF
    [InlineData("1", false, false)]     // flag on, mas NÃO é Q → OFF
    [InlineData("1", true, true)]       // Q + flag "1" → ON
    public void AvaliarAtivo_MatrizGating(string? flag, bool ambienteQ, bool esperado)
        => Assert.Equal(esperado, RecoveryDiag102J.AvaliarAtivo(flag, ambienteQ));

    [Fact] // Por padrão (ambiente de teste: sem marcador Q, sem flag) a instrumentação está DESLIGADA.
    public void Ativo_PorPadrao_Desligado()
        => Assert.False(RecoveryDiag102J.Ativo);

    // ---------- Sanitização: a linha só contém identificadores funcionais + estados ----------

    [Fact]
    public void Formatar_ContemMetadadosDiagnosticos_ESemSegredos()
    {
        Guid cid = Guid.NewGuid();
        string linha = RecoveryDiag102J.Formatar(cid, "G_AFTER_RECOVERY_RESOLVER", "modalidade=UmPendente;pk=8");

        Assert.Contains("PID=", linha, StringComparison.Ordinal);
        Assert.Contains("TID=", linha, StringComparison.Ordinal);
        Assert.Contains($"CID={cid}", linha, StringComparison.Ordinal);
        Assert.Contains("G_AFTER_RECOVERY_RESOLVER", linha, StringComparison.Ordinal);
        Assert.Contains("modalidade=UmPendente;pk=8", linha, StringComparison.Ordinal);

        foreach (string proibido in new[] { "senha", "password", "Authorization", "Bearer", "token", "connectionstring", "Host=", "User Id", "pwd=" })
        {
            Assert.DoesNotContain(proibido, linha, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact] // Sem detalhe: só a linha base (sem sufixo).
    public void Formatar_SemDetalhe_NaoAdicionaPipeFinal()
    {
        string linha = RecoveryDiag102J.Formatar(Guid.Empty, "B_SHOWN_ENTER", null);
        Assert.EndsWith("|B_SHOWN_ENTER", linha, StringComparison.Ordinal);
    }

    [Fact] // Caminho do log é temporário, exclusivo do gate e por PID.
    public void CaminhoLog_EhTemporarioExclusivoPorPid()
    {
        string caminho = RecoveryDiag102J.CaminhoLog();
        Assert.Contains(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), caminho, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Path.Combine("FugaPET_Q", "102J"), caminho, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"recovery_{Environment.ProcessId}.log", caminho, StringComparison.Ordinal);
    }

    // ---------- Escrita best-effort ----------

    [Fact] // EscreverLinha cria o diretório e o arquivo e faz append (ON path).
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
            Assert.Equal("linha-1", linhas[0]);
            Assert.Equal("linha-2", linhas[1]);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* limpeza best-effort */ }
        }
    }

    [Fact] // DIAG OFF (padrão): Log é no-op e NÃO cria o arquivo do gate.
    public void Log_ComDiagDesligado_NaoCriaArquivo()
    {
        string caminho = RecoveryDiag102J.CaminhoLog();
        try { if (File.Exists(caminho)) File.Delete(caminho); } catch { /* ignore */ }

        RecoveryDiag102J.Log(Guid.NewGuid(), "PONTO_TESTE", "detalhe");

        Assert.False(RecoveryDiag102J.Ativo);          // confirma OFF neste ambiente
        Assert.False(File.Exists(caminho));            // nenhum arquivo criado
    }
}
