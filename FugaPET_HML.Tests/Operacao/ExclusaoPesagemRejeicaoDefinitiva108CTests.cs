using System.Text.RegularExpressions;

namespace FugaPET_HML.Tests.Operacao;

/// <summary>
/// GATE 108C — exclusão local de pesagem após REJEIÇÃO SAP DETERMINÍSTICA (nenhum documento criado),
/// mantendo BLOQUEADO todo estado ativo/confirmado/indeterminado.
///
/// Incidente: pedido 4500000216 / item 10 / material 1000205. O SAP recusou de forma determinística
/// (108B: SAP_DEFINITIVE_NO_DOCUMENT), o lançamento ficou ERRO_SAP e o predicado antigo exigia
/// `status_lancamento = 'FINALIZADO_LOCAL'`, bloqueando a exclusão para sempre.
///
/// A matriz de segurança (§7) NÃO é hardcoded: as cláusulas são EXTRAÍDAS da fonte real do repositório
/// e avaliadas. Remover qualquer prova negativa de documento faz as linhas C/D/E falharem.
/// Sem banco e sem SAP.
/// </summary>
public sealed class ExclusaoPesagemRejeicaoDefinitiva108CTests
{
    // ---------------- leitura da fonte real ----------------

    private static string Fonte(params string[] partes)
    {
        string raiz = AppContext.BaseDirectory;
        for (int i = 0; i < 9 && raiz is not null; i++)
        {
            foreach (string cand in new[]
            {
                Path.Combine(raiz, Path.Combine(partes)),
                Path.Combine(raiz, "FugaPet_HML", Path.Combine(partes))
            })
            {
                if (File.Exists(cand)) return File.ReadAllText(cand);
            }
            raiz = Directory.GetParent(raiz)?.FullName!;
        }

        throw new FileNotFoundException(string.Join('/', partes));
    }

    private static string FonteRepositorio()
        => Fonte("AcessoDados", "Repositorio", "ExclusaoPesagemRepositorio.cs");

    private static string FonteController()
        => Fonte("Controle", "Processo", "EntradaProdutoController.cs");

    private static string FonteRepositorioEntrada()
        => Fonte("AcessoDados", "Repositorio", "EntradaProdutoRepositorio.cs");

    /// <summary>Remove comentários de linha: asserções negativas devem olhar CÓDIGO, não documentação.</summary>
    private static string SemComentarios(string trecho)
        => string.Join(
            '\n',
            trecho.Split('\n').Select(l =>
            {
                int c = l.IndexOf("//", StringComparison.Ordinal);
                return c >= 0 ? l[..c] : l;
            }));

    /// <summary>Recorta o corpo da DEFINIÇÃO do método (ancorado no último match, como no GATE 067).</summary>
    private static string Corpo(string src, string assinatura)
    {
        int inicio = src.LastIndexOf(assinatura, StringComparison.Ordinal);
        Assert.True(inicio >= 0, $"método não encontrado: {assinatura}");
        int prox = src.IndexOf("private ", inicio + assinatura.Length, StringComparison.Ordinal);
        if (prox < 0) prox = src.Length;
        return src.Substring(inicio, prox - inicio);
    }

    // ---------------- predicado do LANÇAMENTO extraído da fonte ----------------

    /// <summary>Estados aceitos pelo IN(...) do predicado, lidos da fonte.</summary>
    private static HashSet<string> EstadosAceitos(string corpo, string coluna)
    {
        Match m = Regex.Match(corpo, coluna + @"\s+IN\s*\(([^)]*)\)", RegexOptions.Singleline);
        Assert.True(m.Success, $"predicado IN(...) não encontrado para {coluna}");
        return Regex.Matches(m.Groups[1].Value, @"'([A-Z_]+)'")
            .Select(x => x.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    private sealed record EstadoLancamento(
        string Status, bool Situacao, string? Documento, string? Exercicio, string? EnviadoEm);

    /// <summary>
    /// Avalia a elegibilidade EXATAMENTE como o SQL da fonte: pertinência ao IN(...) extraído E as três
    /// provas negativas, cada uma exigida somente se a cláusula existir de fato na fonte.
    /// </summary>
    private static bool Elegivel(EstadoLancamento e)
    {
        string corpo = Corpo(FonteRepositorio(), "BloquearLancamentoAsync(");
        if (!EstadosAceitos(corpo, "status_lancamento").Contains(e.Status)) return false;

        if (corpo.Contains("situacao_entrada_produto_lancamento = true", StringComparison.Ordinal)
            && !e.Situacao) return false;
        if (corpo.Contains("documento_material_sap IS NULL", StringComparison.Ordinal)
            && e.Documento is not null) return false;
        if (corpo.Contains("exercicio_documento_material_sap IS NULL", StringComparison.Ordinal)
            && e.Exercicio is not null) return false;
        if (corpo.Contains("enviado_sap_em IS NULL", StringComparison.Ordinal)
            && e.EnviadoEm is not null) return false;

        return true;
    }

    // ==================================================================
    // §7 — MATRIZ DE SEGURANÇA (A..G) avaliada contra o predicado REAL
    // ==================================================================

    [Fact] // A) nunca enviado ⇒ ALLOW (regra histórica preservada)
    public void A_FinalizadoLocal_SemEvidenciaSap_Allow()
        => Assert.True(Elegivel(new EstadoLancamento("FINALIZADO_LOCAL", true, null, null, null)));

    [Fact] // B) rejeição determinística sem documento ⇒ ALLOW (o delta deste gate)
    public void B_ErroSap_SemEvidenciaSap_Allow()
        => Assert.True(Elegivel(new EstadoLancamento("ERRO_SAP", true, null, null, null)));

    [Fact] // C) ERRO_SAP com documento ⇒ BLOCK
    public void C_ErroSap_ComDocumento_Block()
        => Assert.False(Elegivel(new EstadoLancamento("ERRO_SAP", true, "5000123", null, null)));

    [Fact] // D) ERRO_SAP com exercício ⇒ BLOCK
    public void D_ErroSap_ComExercicio_Block()
        => Assert.False(Elegivel(new EstadoLancamento("ERRO_SAP", true, null, "2026", null)));

    [Fact] // E) ERRO_SAP com enviado_sap_em ⇒ BLOCK
    public void E_ErroSap_ComEnviadoSapEm_Block()
        => Assert.False(Elegivel(new EstadoLancamento("ERRO_SAP", true, null, null, "2026-09-28T10:00:00Z")));

    [Fact] // F) ENVIADO_SAP (envio ativo / indeterminado retido) ⇒ BLOCK
    public void F_EnviadoSap_Block()
        => Assert.False(Elegivel(new EstadoLancamento("ENVIADO_SAP", true, null, null, null)));

    [Fact] // G) CONFIRMADO_SAP ⇒ BLOCK (mesmo hipoteticamente sem documento: fora do IN)
    public void G_ConfirmadoSap_Block()
    {
        Assert.False(Elegivel(new EstadoLancamento("CONFIRMADO_SAP", true, "5000123", "2026", "2026-09-28T10:00:00Z")));
        Assert.False(Elegivel(new EstadoLancamento("CONFIRMADO_SAP", true, null, null, null)));
    }

    [Theory] // demais estados do domínio permanecem BLOCK
    [InlineData("ABERTO")]
    [InlineData("EM_PESAGEM")]
    [InlineData("CANCELADO")]
    public void OutrosEstados_Block(string status)
        => Assert.False(Elegivel(new EstadoLancamento(status, true, null, null, null)));

    [Fact] // H) inativo logicamente ⇒ BLOCK (situação booleana preservada)
    public void H_SituacaoFalsa_Block()
        => Assert.False(Elegivel(new EstadoLancamento("ERRO_SAP", false, null, null, null)));

    // ==================================================================
    // §3 — contrato textual do predicado do lançamento
    // ==================================================================

    [Fact]
    public void Lancamento_AceitaErroSap_ComAsTresProvasNegativas()
    {
        string corpo = Corpo(FonteRepositorio(), "BloquearLancamentoAsync(");

        Assert.Equal(
            new[] { "ERRO_SAP", "FINALIZADO_LOCAL" },
            EstadosAceitos(corpo, "status_lancamento").OrderBy(x => x, StringComparer.Ordinal).ToArray());

        // As três provas negativas são OBRIGATÓRIAS e não podem ser removidas.
        Assert.Contains("documento_material_sap IS NULL", corpo, StringComparison.Ordinal);
        Assert.Contains("exercicio_documento_material_sap IS NULL", corpo, StringComparison.Ordinal);
        Assert.Contains("enviado_sap_em IS NULL", corpo, StringComparison.Ordinal);
        Assert.Contains("situacao_entrada_produto_lancamento = true", corpo, StringComparison.Ordinal);
        Assert.Contains("FOR UPDATE", corpo, StringComparison.Ordinal);

        // Estados ativos/confirmados nunca entram no IN.
        Assert.DoesNotContain("'ENVIADO_SAP'", corpo, StringComparison.Ordinal);
        Assert.DoesNotContain("'CONFIRMADO_SAP'", corpo, StringComparison.Ordinal);
    }

    // ==================================================================
    // §6 — item ALINHADO (provado) e lote INALTERADO (sem simetria estética)
    // ==================================================================

    [Fact] // O item vai a ERRO_SAP na rejeição ⇒ alinhamento necessário, com prova negativa mantida.
    public void Item_AceitaErroSap_MantendoProvaNegativaDeDocumento()
    {
        string corpo = Corpo(FonteRepositorio(), "BloquearItemElegivelAsync(");

        Assert.Equal(
            new[] { "ERRO_SAP", "FINALIZADO_LOCAL" },
            EstadosAceitos(corpo, "status_item").OrderBy(x => x, StringComparer.Ordinal).ToArray());
        Assert.Contains("documento_material_item IS NULL", corpo, StringComparison.Ordinal);
        Assert.Contains("situacao_entrada_produto_item = true", corpo, StringComparison.Ordinal);
        Assert.DoesNotContain("'CONFIRMADO_SAP'", corpo, StringComparison.Ordinal);
    }

    [Fact] // O pós-envio NÃO altera status_lote ⇒ o lote permanece FINALIZADO_LOCAL ⇒ guard INALTERADO.
    public void Lote_PermaneceApenasFinalizadoLocal_GuardNaoAlterado()
    {
        string corpo = Corpo(FonteRepositorio(), "BloquearLoteElegivelAsync(");

        Assert.Contains("status_lote = 'FINALIZADO_LOCAL'", corpo, StringComparison.Ordinal);
        Assert.DoesNotContain("status_lote IN", corpo, StringComparison.Ordinal);
        Assert.DoesNotContain("'ERRO_SAP'", corpo, StringComparison.Ordinal);

        // Prova da premissa: AtualizarStatusAposEnvioSapAsync não toca status_lote.
        string posEnvio = Corpo(FonteRepositorioEntrada(), "AtualizarStatusAposEnvioSapAsync(");
        Assert.DoesNotContain("status_lote", posEnvio, StringComparison.Ordinal);
    }

    // ==================================================================
    // §5 — rejeição determinística continua gravando ERRO_SAP (inalterado)
    // ==================================================================

    [Fact]
    public void RejeicaoDeterministica_ContinuaGravandoErroSap()
    {
        string posEnvio = Corpo(FonteRepositorioEntrada(), "AtualizarStatusAposEnvioSapAsync(");

        // status por item e por lançamento: sucesso ⇒ CONFIRMADO_SAP; falha ⇒ ERRO_SAP.
        Assert.Contains("resultado.Sucesso ? \"CONFIRMADO_SAP\" : \"ERRO_SAP\"", posEnvio, StringComparison.Ordinal);
        Assert.Contains(
            "cenario == CenarioEnvioSapEntrada.Enviado ? \"CONFIRMADO_SAP\" : \"ERRO_SAP\"",
            posEnvio,
            StringComparison.Ordinal);

        // Rastreabilidade só no sucesso ⇒ na rejeição os campos de documento permanecem NULL.
        Assert.Contains("cenario == CenarioEnvioSapEntrada.Enviado", posEnvio, StringComparison.Ordinal);
        Assert.Contains("gravarRastreio", posEnvio, StringComparison.Ordinal);
    }

    // ==================================================================
    // §4 / achado B2 — timeout/transporte INDETERMINADO não vira ERRO_SAP
    // ==================================================================

    /// <summary>Recorta o catch de exceção do POST do documento material no controller.</summary>
    private static string CatchTransporteIndeterminado()
    {
        string src = FonteController();
        int post = src.IndexOf("Sap.CriarDocumentoMaterialEntradaAsync(", StringComparison.Ordinal);
        Assert.True(post > 0, "chamada do POST não encontrada");
        int inicioCatch = src.IndexOf("catch (Exception ex)", post, StringComparison.Ordinal);
        Assert.True(inicioCatch > 0, "catch do POST não encontrado");
        int fim = src.IndexOf("bool sucesso = resultadoSap.Sucesso;", inicioCatch, StringComparison.Ordinal);
        Assert.True(fim > inicioCatch, "fim do bloco não encontrado");
        return src.Substring(inicioCatch, fim - inicioCatch);
    }

    [Fact] // Mantém ENVIADO_SAP: não cai no caminho que grava ERRO_SAP.
    public void Timeout_NaoConverteParaErroSap_MantemEnviadoSap()
    {
        string bloco = CatchTransporteIndeterminado();
        string codigo = SemComentarios(bloco);

        // Retorna ANTES de qualquer atualização de status pós-envio (único caminho que gravaria ERRO_SAP).
        Assert.Contains("return new ResultadoEnvioSapEntrada", codigo, StringComparison.Ordinal);
        Assert.Contains("CenarioEnvioSapEntrada.FalhaPersistenciaLocal", codigo, StringComparison.Ordinal);
        Assert.DoesNotContain("AtualizarStatusAposEnvioSap", codigo, StringComparison.Ordinal);
        Assert.DoesNotContain("ERRO_SAP", codigo, StringComparison.Ordinal);

        // Decisão documentada na própria fonte (retenção em ENVIADO_SAP).
        Assert.Contains("ENVIADO_SAP", bloco, StringComparison.Ordinal);
    }

    [Fact] // Reconciliação armada e sem autorização de reenvio cego.
    public void Timeout_ArmaReconciliacao_ESinalizaCritico()
    {
        string bloco = CatchTransporteIndeterminado();

        Assert.Contains("_capability.MarcarReconciliacao()", bloco, StringComparison.Ordinal);
        Assert.Contains("RegistrarFalhaStatusLocalAposSapAsync", bloco, StringComparison.Ordinal);
        Assert.Contains("StatusLocalAtualizado = false", bloco, StringComparison.Ordinal);
        Assert.Contains("MensagemCritica", bloco, StringComparison.Ordinal);
    }

    [Fact] // I) consequência: com ENVIADO_SAP retido, a exclusão permanece BLOQUEADA (fail-closed).
    public void I_TimeoutMantemExclusaoBloqueada()
        => Assert.False(Elegivel(new EstadoLancamento("ENVIADO_SAP", true, null, null, null)));

    // ==================================================================
    // §8 / §10 — mecânica de exclusão e outbox NÃO alteradas
    // ==================================================================

    [Fact]
    public void MecanicaDeExclusao_Preservada()
    {
        string src = FonteRepositorio();

        Assert.Contains("IsolationLevel.Serializable", src, StringComparison.Ordinal);
        Assert.Contains("DefinirUsuarioAppAsync", src, StringComparison.Ordinal);
        Assert.Contains("\"40001\" or \"40P01\"", src, StringComparison.Ordinal);
        Assert.Contains("status_pesagem = 'CANCELADA'", src, StringComparison.Ordinal);
        Assert.Contains("RecalcularQuantidadeItemAsync", src, StringComparison.Ordinal);
        Assert.Contains("CancelarLoteSeVazioAsync", src, StringComparison.Ordinal);
        Assert.Contains("CancelarItemSeVazioAsync", src, StringComparison.Ordinal);
        Assert.Contains("CancelarLancamentoSeVazioAsync", src, StringComparison.Ordinal);
        Assert.DoesNotContain("DELETE FROM", src, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // §10: guard de outbox/tentativa intocado; §9: sem reset automático ERRO_SAP → FINALIZADO_LOCAL.
    public void OutboxGuardIntocado_ESemResetAutomaticoDeErroSap()
    {
        string src = FonteRepositorio();

        Assert.Contains("fn_entrada_produto_sap_guard_counts", src, StringComparison.Ordinal);
        Assert.Contains("outbox != 0 || tentativa != 0", src, StringComparison.Ordinal);

        // Nenhum UPDATE que promova ERRO_SAP de volta a FINALIZADO_LOCAL.
        Assert.DoesNotContain("status_lancamento = 'FINALIZADO_LOCAL'", src, StringComparison.Ordinal);
        Assert.DoesNotContain("status_item = 'FINALIZADO_LOCAL'", src, StringComparison.Ordinal);
    }
}
