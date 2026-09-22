using System.Reflection;
using FugaPET_HML.Modelo.Consumo;
using FugaPET_HML.Modelo.Processo;
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

    [Fact] // §5: componente entra como domínio; não existe DTO intermediário com strings persistíveis.
    public void ComponenteDiag_RecebeDominioSemDtoLivre()
    {
        Assert.Null(typeof(RecoveryDiag102J).Assembly.GetType(
            "FugaPET_HML.Servicos.Diagnostico.ComponenteDiag102J"));
        MethodInfo metodo = typeof(RecoveryDiag102J).GetMethod(nameof(RecoveryDiag102J.LogComponente))!;
        Assert.Contains(metodo.GetParameters(), p => p.ParameterType == typeof(ComponenteConsumoMaterial));
        Assert.DoesNotContain(metodo.GetParameters(), p => p.ParameterType == typeof(string));
    }

    // ---------- Sanitização por construção da linha ----------

    [Fact]
    public void Formatar_ContemMetadados_ESemSegredos()
    {
        Guid cid = Guid.NewGuid();
        string linha = RecoveryDiag102J.FormatarResultadoParaTeste(
            cid,
            PontoDiag102J.G_AfterRecoveryResolver,
            ModalidadeRecuperacaoConsumo.UmPendente,
            ModalidadeRecuperacaoConsumo.UmPendente,
            8,
            0);

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
        string linha = RecoveryDiag102J.FormatarMarcoParaTeste(Guid.Empty, PontoDiag102J.B_ShownEnter);
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
    public void PersistirLinha_EhExclusivamentePrivado()
    {
        MethodInfo? metodo = typeof(RecoveryDiag102J).GetMethod(
            "PersistirLinha", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(metodo);
        Assert.True(metodo!.IsPrivate);
        Assert.Null(typeof(RecoveryDiag102J).GetMethod(
            "EscreverLinha", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));
    }

    [Fact] // §10.D: DIAG OFF → nenhum arquivo criado por LogSnapshot/LogMarco.
    public void Log_ComDiagDesligado_NaoCriaArquivo()
    {
        string caminho = RecoveryDiag102J.CaminhoLog();
        try { if (File.Exists(caminho)) File.Delete(caminho); } catch { /* ignore */ }

        RecoveryDiag102J.LogMarco(Guid.NewGuid(), PontoDiag102J.K_FinalSnapshot);
        RecoveryDiag102J.LogSnapshot(Guid.NewGuid(), PontoDiag102J.I_AfterAplicarMaterializacao,
            new SnapshotDiag102J(ModalidadeRecuperacaoConsumo.UmPendente, 8,
                StatusClass102J.RecoveryPendente, false, false, false, false, false, false, true, true));

        Assert.False(RecoveryDiag102J.Ativo);
        Assert.False(File.Exists(caminho));
    }

    // ---------- §3/§4/§6/§7/§8: SINK TIPADO OPACO — nenhum caminho de string arbitrária ----------

    [Fact]
    public void SinkOpaco_SemCaminhoDeStringArbitraria()
    {
        Type t = typeof(RecoveryDiag102J);

        // Os antigos pipelines textuais genéricos NÃO existem mais.
        Assert.Null(t.GetMethod("Emitir", TodosMetodos));
        Assert.Null(t.GetMethod("FormatarLinha", TodosMetodos));

        // WRITER_ACCEPTS_ARBITRARY_STRING=NAO — PersistirLinha recebe SOMENTE o tipo opaco (nenhum string).
        MethodInfo persistir = t.GetMethod("PersistirLinha", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.DoesNotContain(persistir.GetParameters(), p => p.ParameterType == typeof(string));

        // FORMATTER_ACCEPTS_ARBITRARY_STRING=NAO — nenhum método "Formatar*" recebe string como payload.
        foreach (MethodInfo m in t.GetMethods(TodosMetodos).Where(x => x.Name.StartsWith("Formatar", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain(m.GetParameters(), p => p.ParameterType == typeof(string));
        }

        // Tipo OPACO privado com construtor PRIVADO (nunca public/internal aceitando string).
        Type? opaco = t.GetNestedType("LinhaDiag102J", BindingFlags.NonPublic);
        Assert.NotNull(opaco);
        Assert.True(opaco!.IsNestedPrivate);

        foreach (ConstructorInfo ctor in opaco.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (ctor.GetParameters().Any(p => p.ParameterType == typeof(string)))
            {
                Assert.True(ctor.IsPrivate, "Construtor do tipo opaco que aceita string DEVE ser privado.");
            }
        }

        // ARBITRARY_VALUE_PERSISTENCE_PATH_COUNT=0 — as factories do tipo opaco recebem SOMENTE tipos aprovados.
        foreach (MethodInfo f in opaco.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                     .Where(x => x.ReturnType == opaco))
        {
            Assert.DoesNotContain(f.GetParameters(), p => p.ParameterType == typeof(string));
        }
    }

    [Fact] // §8: os valores funcionais válidos continuam aparecendo corretamente no log.
    public void ValoresFuncionais_ContinuamAparecendo()
    {
        var contexto = new ContextoApontamentoProcesso
        {
            CodigoApontamento = 4,
            NumeroOrdem = "1000170",
            Operacao = "0010",
            Sequencia = "0",
            TipoProcesso = "CONSUMO_MATERIA_PRIMA"
        };
        var componente = new ComponenteConsumoMaterial
        {
            CodigoMaterial = "1000186",
            NumeroReserva = "185",
            ItemReserva = "1",
            DepositoConsumo = "PP01",
            Lote = "0000000222",
            TipoMovimento = "261",
            Operacao = "0010",
            SequenciaOperacao = "0"
        };

        string ctx = RecoveryDiag102J.FormatarContextoParaTeste(Guid.NewGuid(), PontoDiag102J.A_FormConstructor, contexto);
        string comp = RecoveryDiag102J.FormatarComponenteParaTeste(Guid.NewGuid(), PontoDiag102J.D_ComponentesSap, 0, componente);

        Assert.Contains("OP=1000170", ctx, StringComparison.Ordinal);
        Assert.Contains("Op=0010", ctx, StringComparison.Ordinal);
        Assert.Contains("Tipo=CONSUMO_MATERIA_PRIMA", ctx, StringComparison.Ordinal);

        Assert.Contains("Mat=1000186", comp, StringComparison.Ordinal);
        Assert.Contains("Res=185", comp, StringComparison.Ordinal);
        Assert.Contains("Item=1", comp, StringComparison.Ordinal);
        Assert.Contains("Dep=PP01", comp, StringComparison.Ordinal);
        Assert.Contains("Batch=0000000222", comp, StringComparison.Ordinal);
        Assert.Contains("Mov=261", comp, StringComparison.Ordinal);
    }
}
