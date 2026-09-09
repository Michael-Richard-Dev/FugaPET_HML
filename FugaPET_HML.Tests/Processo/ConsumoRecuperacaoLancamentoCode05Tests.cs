using FugaPET_HML.AcessoDados.Repositorio;
using FugaPET_HML.Modelo.Consumo;
using FugaPET_HML.Modelo.IntegracaoSap;
using FugaPET_HML.Modelo.Processo;
using FugaPET_HML.Servicos.Operacao;
using FugaPET_HML.Servicos.Processo;
using FugaPET_HML.Tela.Processo;

namespace FugaPET_HML.Tests.Processo;

/// <summary>
/// GATE 049-E2E-CONSUMO261-CODE-05 — recuperação fail-closed de lançamento PENDENTE_SAP já persistido,
/// resolvido por MATCH item-a-item contra os componentes da ocorrência (Contexto.Operacao + Sequencia).
/// </summary>
public sealed class ConsumoRecuperacaoLancamentoCode05Tests
{
    // ---------- MATCH puro (LancamentoCasaComOcorrencia) ----------

    [Fact] // Base: dois itens casam 1:1 com dois componentes distintos.
    public void Match_DoisItens_CasamComComponentesDistintos()
    {
        var componentes = new List<ComponenteConsumoMaterial> { Comp("14391", "4", "1000037", dep: "PP01"), Comp("14391", "5", "3500024", dep: "PP02") };
        var itens = new List<ConsumoMaterialItem> { Item("14391", "4", "1000037", dep: "PP01"), Item("14391", "5", "3500024", dep: "PP02") };
        Assert.True(ConsumoMaterialConsultaServico.LancamentoCasaComOcorrencia(itens, componentes));
    }

    [Fact] // T (candidato vazio): nenhum item → inválido.
    public void Match_SemItens_Invalido()
        => Assert.False(ConsumoMaterialConsultaServico.LancamentoCasaComOcorrencia([], new List<ComponenteConsumoMaterial> { Comp("14391", "4", "1000037") }));

    [Fact] // T5: ReservationItem divergente → rejeita.
    public void Match_ReservationItemDivergente_Rejeita()
    {
        var componentes = new List<ComponenteConsumoMaterial> { Comp("14391", "4", "1000037") };
        var itens = new List<ConsumoMaterialItem> { Item("14391", "9", "1000037") };
        Assert.False(ConsumoMaterialConsultaServico.LancamentoCasaComOcorrencia(itens, componentes));
    }

    [Fact] // T6: Material divergente → rejeita.
    public void Match_MaterialDivergente_Rejeita()
    {
        var componentes = new List<ComponenteConsumoMaterial> { Comp("14391", "4", "1000037") };
        var itens = new List<ConsumoMaterialItem> { Item("14391", "4", "9999999") };
        Assert.False(ConsumoMaterialConsultaServico.LancamentoCasaComOcorrencia(itens, componentes));
    }

    [Fact] // T7: StorageLocation divergente → rejeita.
    public void Match_StorageDivergente_Rejeita()
    {
        var componentes = new List<ComponenteConsumoMaterial> { Comp("14391", "4", "1000037", dep: "PP01") };
        var itens = new List<ConsumoMaterialItem> { Item("14391", "4", "1000037", dep: "PP09") };
        Assert.False(ConsumoMaterialConsultaServico.LancamentoCasaComOcorrencia(itens, componentes));
    }

    [Fact] // T8: Batch divergente → rejeita.
    public void Match_BatchDivergente_Rejeita()
    {
        var componentes = new List<ComponenteConsumoMaterial> { Comp("14391", "4", "1000037", lote: "LOTE-A") };
        var itens = new List<ConsumoMaterialItem> { Item("14391", "4", "1000037", lote: "LOTE-B") };
        Assert.False(ConsumoMaterialConsultaServico.LancamentoCasaComOcorrencia(itens, componentes));
    }

    [Fact] // T9: campo obrigatório ausente (lote vazio) → fail-closed.
    public void Match_CampoObrigatorioAusente_FailClosed()
    {
        var componentes = new List<ComponenteConsumoMaterial> { Comp("14391", "4", "1000037", lote: "") };
        var itens = new List<ConsumoMaterialItem> { Item("14391", "4", "1000037", lote: "") };
        Assert.False(ConsumoMaterialConsultaServico.LancamentoCasaComOcorrencia(itens, componentes));
    }

    [Fact] // MovementType diferente de 261 → rejeita.
    public void Match_MovimentoNao261_Rejeita()
    {
        var componentes = new List<ComponenteConsumoMaterial> { Comp("14391", "4", "1000037") };
        var itens = new List<ConsumoMaterialItem> { Item("14391", "4", "1000037", tipoMov: "531") };
        Assert.False(ConsumoMaterialConsultaServico.LancamentoCasaComOcorrencia(itens, componentes));
    }

    [Fact] // T16: item casa com >1 componente (dois componentes idênticos) → rejeita.
    public void Match_ItemComMaisDeUmComponente_Rejeita()
    {
        var componentes = new List<ComponenteConsumoMaterial> { Comp("14391", "4", "1000037"), Comp("14391", "4", "1000037") };
        var itens = new List<ConsumoMaterialItem> { Item("14391", "4", "1000037") };
        Assert.False(ConsumoMaterialConsultaServico.LancamentoCasaComOcorrencia(itens, componentes));
    }

    [Fact] // T17: dois itens tentam usar o MESMO componente → rejeita.
    public void Match_DoisItensMesmoComponente_Rejeita()
    {
        var componentes = new List<ComponenteConsumoMaterial> { Comp("14391", "4", "1000037") };
        var itens = new List<ConsumoMaterialItem> { Item("14391", "4", "1000037"), Item("14391", "4", "1000037") };
        Assert.False(ConsumoMaterialConsultaServico.LancamentoCasaComOcorrencia(itens, componentes));
    }

    // ---------- Resolver (repo-backed) ----------

    [Fact] // T1: 0 candidatos → Zero (nada escolhido).
    public async Task Resolver_ZeroCandidatos_Zero()
    {
        var repo = new FakeRepoResolucao();
        var res = await Servico(repo).ResolverLancamentoPendenteDoContextoAsync(Contexto(), Ocorrencia());
        Assert.Equal(CardinalidadeResolucaoLancamento.Zero, res.Cardinalidade);
        Assert.Null(res.CodigoLancamento);
    }

    [Fact] // T2: 1 candidato integralmente válido → PK resolvida (chega ao 71 por match, sem hardcode).
    public async Task Resolver_UmCandidatoValido_ResolvePk()
    {
        var repo = new FakeRepoResolucao
        {
            Candidatos = [Resumo(71)],
            Detalhes = { [71] = Lanc(71, [Item("14391", "4", "1000037", dep: "PP01"), Item("14391", "5", "3500024", dep: "PP02")]) }
        };
        var res = await Servico(repo).ResolverLancamentoPendenteDoContextoAsync(Contexto(), Ocorrencia());
        Assert.Equal(CardinalidadeResolucaoLancamento.Um, res.Cardinalidade);
        Assert.Equal(71, res.CodigoLancamento);
    }

    [Fact] // T3: >1 candidatos válidos → Varios, nenhuma seleção automática.
    public async Task Resolver_VariosCandidatos_NaoEscolheAutomaticamente()
    {
        var repo = new FakeRepoResolucao
        {
            Candidatos = [Resumo(71), Resumo(72)],
            Detalhes =
            {
                [71] = Lanc(71, [Item("14391", "4", "1000037", dep: "PP01"), Item("14391", "5", "3500024", dep: "PP02")]),
                [72] = Lanc(72, [Item("14391", "4", "1000037", dep: "PP01"), Item("14391", "5", "3500024", dep: "PP02")])
            }
        };
        var res = await Servico(repo).ResolverLancamentoPendenteDoContextoAsync(Contexto(), Ocorrencia());
        Assert.Equal(CardinalidadeResolucaoLancamento.Varios, res.Cardinalidade);
        Assert.Null(res.CodigoLancamento);
        Assert.Equal(2, res.Candidatos.Count);
    }

    [Fact] // T4: candidato com item de OUTRA operação (não está na ocorrência) → rejeita.
    public async Task Resolver_ItemDeOutraOperacao_Rejeita()
    {
        var repo = new FakeRepoResolucao
        {
            Candidatos = [Resumo(71)],
            // item com reserva/material que NÃO existe entre os componentes da ocorrência (0050).
            Detalhes = { [71] = Lanc(71, [Item("99999", "1", "7777777", dep: "PP07")]) }
        };
        var res = await Servico(repo).ResolverLancamentoPendenteDoContextoAsync(Contexto(), Ocorrencia());
        Assert.Equal(CardinalidadeResolucaoLancamento.Zero, res.Cardinalidade);
    }

    [Fact] // T10: candidato ENVIANDO_SAP não é elegível (não retornado como PENDENTE) → Zero.
    public async Task Resolver_Enviando_NaoCandidato()
    {
        var repo = new FakeRepoResolucao
        {
            Candidatos = [Resumo(71, status: "ENVIANDO_SAP")],
            Detalhes = { [71] = Lanc(71, [Item("14391", "4", "1000037", dep: "PP01")], status: "ENVIANDO_SAP") }
        };
        var res = await Servico(repo).ResolverLancamentoPendenteDoContextoAsync(Contexto(), Ocorrencia());
        Assert.Equal(CardinalidadeResolucaoLancamento.Zero, res.Cardinalidade);
    }

    [Fact] // T11: CONFIRMADO_SAP não é candidato.
    public async Task Resolver_Confirmado_NaoCandidato()
    {
        var repo = new FakeRepoResolucao
        {
            Candidatos = [Resumo(71, status: "CONFIRMADO_SAP", doc: "5000000124", ex: "2026")],
            Detalhes = { [71] = Lanc(71, [Item("14391", "4", "1000037", dep: "PP01")], status: "CONFIRMADO_SAP", doc: "5000000124", ex: "2026") }
        };
        var res = await Servico(repo).ResolverLancamentoPendenteDoContextoAsync(Contexto(), Ocorrencia());
        Assert.Equal(CardinalidadeResolucaoLancamento.Zero, res.Cardinalidade);
    }

    [Fact] // T12: PENDENTE_SAP mas com documento/ano preenchido → não candidato (não fura CODE-04).
    public async Task Resolver_PendenteComDocumento_NaoCandidato()
    {
        var repo = new FakeRepoResolucao
        {
            Candidatos = [Resumo(71, doc: "5000000124", ex: "2026")],
            Detalhes = { [71] = Lanc(71, [Item("14391", "4", "1000037", dep: "PP01")], doc: "5000000124", ex: "2026") }
        };
        var res = await Servico(repo).ResolverLancamentoPendenteDoContextoAsync(Contexto(), Ocorrencia());
        Assert.Equal(CardinalidadeResolucaoLancamento.Zero, res.Cardinalidade);
    }

    [Fact] // T19: resolver NÃO faz POST/claim (repo nunca reserva/envia/confirma).
    public async Task Resolver_ZeroPostZeroClaim()
    {
        var repo = new FakeRepoResolucao
        {
            Candidatos = [Resumo(71)],
            Detalhes = { [71] = Lanc(71, [Item("14391", "4", "1000037", dep: "PP01"), Item("14391", "5", "3500024", dep: "PP02")]) }
        };
        _ = await Servico(repo).ResolverLancamentoPendenteDoContextoAsync(Contexto(), Ocorrencia());
        Assert.Equal(0, repo.Reservas);
        Assert.Equal(0, repo.Confirmacoes);
        Assert.Equal(0, repo.Falhas);
        Assert.Equal(0, repo.Saves);
    }

    // ---------- Source-scan: Form + Histórico (T13, T14, T15, T18, T20) ----------

    [Fact] // T13/T14/T15: recovery envia PK direto e nunca salva/recria pesagem/lançamento.
    public void Form_EnvioRecuperado_NaoSalvaNemRecriaPesagens()
    {
        string form = Fonte("Tela", "Processo", "ProcessoConsumoMaterialForm.cs");
        string metodo = ExtrairMetodo(form, "private async Task EnviarSap261LancamentoRecuperadoAsync");
        Assert.Contains("_controller.EnviarConsumoSap261Async(codigoLancamento, usuario)", metodo, StringComparison.Ordinal);
        Assert.DoesNotContain("SalvarConsumoLocalAsync", metodo, StringComparison.Ordinal);
        Assert.DoesNotContain("_pesagensPorComponente", metodo, StringComparison.Ordinal);
        // A resolução também não reconstrói pesagens.
        string resolver = ExtrairMetodo(form, "private async Task ResolverLancamentoPendentePersistidoAsync");
        Assert.DoesNotContain("_pesagensPorComponente", resolver, StringComparison.Ordinal);
        Assert.DoesNotContain("SalvarConsumoLocalAsync", resolver, StringComparison.Ordinal);
    }

    [Fact] // T20: troca de contexto invalida o PK; envio revalida o snapshot.
    public void Form_TrocaContexto_InvalidaPk()
    {
        string form = Fonte("Tela", "Processo", "ProcessoConsumoMaterialForm.cs");
        // Reset da OP zera o recuperado.
        Assert.Contains("_codigoLancamentoRecuperado = null;", form, StringComparison.Ordinal);
        string envio = ExtrairMetodo(form, "private async Task EnviarSap261LancamentoRecuperadoAsync");
        Assert.Contains("!snapshot.Equals(SnapshotContextoAtual())", envio, StringComparison.Ordinal);
    }

    [Fact] // T18: histórico restrito só exibe/seleciona a allowlist recebida.
    public void Historico_Restrito_SubconjuntoExatoDaAllowlist()
    {
        string hist = Fonte("Tela", "Processo", "ProcessoConsumoMaterialHistoricoForm.cs");
        Assert.Contains("_modoContextualRestrito", hist, StringComparison.Ordinal);
        Assert.Contains("_codigosPermitidos.Contains(l.CodigoLancamento)", hist, StringComparison.Ordinal);
        // Seleção nunca fora da allowlist.
        Assert.Contains("if (!_codigosPermitidos.Contains(resumo.CodigoLancamento))", hist, StringComparison.Ordinal);
    }

    [Fact] // REV1 R1: PK recuperado vira ação visual estável de envio 261.
    public void REV1_Form_ResolvedorUm_MaterializaPkStatusEBotao()
    {
        string form = Fonte("Tela", "Processo", "ProcessoConsumoMaterialForm.cs");
        string resolver = ExtrairMetodo(form, "private async Task ResolverLancamentoPendentePersistidoAsync");
        Assert.Contains("_codigoLancamentoRecuperado = pk;", resolver, StringComparison.Ordinal);
        Assert.Contains("_contextoResolucaoSnapshot = SnapshotContextoAtual();", resolver, StringComparison.Ordinal);
        Assert.Contains("AplicarMaterializacaoRecovery(pk);", resolver, StringComparison.Ordinal);

        string materializacao = ExtrairMetodo(form, "private void AplicarMaterializacaoRecovery");
        Assert.Contains("PENDENTE_SAP", materializacao, StringComparison.Ordinal);
        Assert.Contains("ENVIAR SAP 261", materializacao, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("_enviarSap261Button.Visible = true;", materializacao, StringComparison.Ordinal);
        Assert.Contains("_enviarSap261Button.Enabled = !_enviandoSap;", materializacao, StringComparison.Ordinal);
    }

    [Fact] // REV1 R2: AtualizarBotaoConfirmar não esconde mais o ENVIAR SAP 261 recuperado.
    public void REV1_Form_AtualizarBotaoConfirmar_PreservaEnviarSap261NoRecovery()
    {
        string form = Fonte("Tela", "Processo", "ProcessoConsumoMaterialForm.cs");
        string metodo = ExtrairMetodo(form, "private void AtualizarBotaoConfirmar");
        Assert.Contains("bool recoveryAtivo = RecoveryAtivoNoContextoAtual();", metodo, StringComparison.Ordinal);
        Assert.Contains("bool finalizacaoAtiva = FinalizacaoLocalAtivaNoContextoAtual();", metodo, StringComparison.Ordinal);
        Assert.Contains("bool acaoPrimariaRecovery = recoveryAtivo || finalizacaoAtiva;", metodo, StringComparison.Ordinal);
        Assert.Contains("_enviarSap261Button.Visible = acaoPrimariaRecovery;", metodo, StringComparison.Ordinal);
        Assert.Contains("_enviarSap261Button.Enabled = acaoPrimariaRecovery && !_enviandoSap;", metodo, StringComparison.Ordinal);
        Assert.Contains("_enviarSap261Button.Text = finalizacaoAtiva ? \"Finalizar Atividade\" : \"Enviar SAP 261\";", metodo, StringComparison.Ordinal);

        string materializacaoPendente = ExtrairMetodo(form, "private void AplicarMaterializacaoRecovery");
        Assert.Contains("PENDENTE_SAP", materializacaoPendente, StringComparison.Ordinal);
        Assert.Contains("ENVIAR SAP 261", materializacaoPendente, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("_enviarSap261Button.Visible = true;", materializacaoPendente, StringComparison.Ordinal);
        Assert.Contains("_enviarSap261Button.Enabled = !_enviandoSap;", materializacaoPendente, StringComparison.Ordinal);

        string materializacaoConfirmado = ExtrairMetodo(form, "private void AplicarMaterializacaoFinalizacao");
        Assert.Contains("CONFIRMADO_SAP", materializacaoConfirmado, StringComparison.Ordinal);
        Assert.Contains("Finalizar Atividade", metodo, StringComparison.Ordinal);
        Assert.DoesNotContain("EnviarSap261LancamentoRecuperadoAsync", materializacaoConfirmado, StringComparison.Ordinal);
    }

    [Fact] // REV1 R3: recovery ativo remove Confirmar Consumo como ação primária concorrente.
    public void REV1_Form_RecoveryAtivo_NaoMantemConfirmarConsumoComoPrimario()
    {
        string form = Fonte("Tela", "Processo", "ProcessoConsumoMaterialForm.cs");
        string metodo = ExtrairMetodo(form, "private void AtualizarBotaoConfirmar");
        Assert.Contains("&& !recoveryAtivo", metodo, StringComparison.Ordinal);

        string materializacao = ExtrairMetodo(form, "private void AplicarMaterializacaoRecovery");
        Assert.Contains("_confirmarConsumoButton.Visible = false;", materializacao, StringComparison.Ordinal);
        Assert.Contains("_confirmarConsumoButton.Enabled = false;", materializacao, StringComparison.Ordinal);
    }

    [Fact] // REV1 R4: fluxo normal de Confirmar Consumo permanece condicionado à pesagem nova.
    public void REV1_Form_SemRecovery_ConfirmarConsumoPermaneceFluxoNormal()
    {
        string form = Fonte("Tela", "Processo", "ProcessoConsumoMaterialForm.cs");
        string metodo = ExtrairMetodo(form, "private void AtualizarBotaoConfirmar");
        Assert.Contains("PossuiPesagemPendenteParaNovoApontamento()", metodo, StringComparison.Ordinal);
        Assert.Contains("&& !_consumoSalvoNaSessao", metodo, StringComparison.Ordinal);
        Assert.Contains("_confirmarConsumoButton.Enabled = _confirmarConsumoButton.Visible && !_salvandoConsumo;", metodo, StringComparison.Ordinal);
    }

    [Fact] // REV1 R5: atualizações visuais posteriores reaplicam o recovery validado.
    public void REV1_Form_AtualizacaoVisual_ReaplicaRecoveryAtivo()
    {
        string form = Fonte("Tela", "Processo", "ProcessoConsumoMaterialForm.cs");
        string metodo = ExtrairMetodo(form, "private void AtualizarApontamentoVisual");
        Assert.Contains("RecoveryAtivoNoContextoAtual()", metodo, StringComparison.Ordinal);
        Assert.Contains("AplicarMaterializacaoRecovery(codigoRecuperado);", metodo, StringComparison.Ordinal);
    }

    [Fact] // REV1 R6: cadeia integrada abre OP, preenche componentes e só depois resolve recovery.
    public void REV1_Form_CadeiaIntegrada_PreparaContextoAntesDoRecovery()
    {
        string form = Fonte("Tela", "Processo", "ProcessoConsumoMaterialForm.cs");
        string aplicar = ExtrairMetodo(form, "private void AplicarContextoApontamento");
        Assert.Contains("Shown += async (_, _) =>", aplicar, StringComparison.Ordinal);
        Assert.Contains("await ConsultarOrdemProducaoAsync(exibirAvisoOrdemObrigatoria: false);", aplicar, StringComparison.Ordinal);

        string consulta = ExtrairMetodo(form, "private async Task ConsultarOrdemProducaoAsync");
        Assert.True(
            consulta.IndexOf("PreencherOrdemCarregada(resultado.Ordem, componentesOperacionais)", StringComparison.Ordinal)
            < consulta.IndexOf("ResolverLancamentoPendentePersistidoAsync(componentesOperacionais)", StringComparison.Ordinal),
            "A recuperação deve ocorrer depois de materializar a OP/componentes na tela.");
    }

    [Fact] // REV1 R7: sequência vazia e "0" são equivalentes somente como default/zero.
    public async Task REV1_SequenciaVaziaVsZero_EquivalenteComoDefault()
    {
        var repo = new FakeRepoResolucao
        {
            Candidatos = [Resumo(71)],
            Detalhes = { [71] = Lanc(71, [Item("14391", "4", "1000037", dep: "PP01"), Item("14391", "5", "3500024", dep: "PP02")]) }
        };

        var res = await Servico(repo).ResolverLancamentoPendenteDoContextoAsync(Contexto(sequencia: "0"), Ocorrencia(seq: ""));
        Assert.Equal(CardinalidadeResolucaoLancamento.Um, res.Cardinalidade);
        Assert.Equal(71, res.CodigoLancamento);
    }

    [Fact] // REV1 R8: sequência "1" contra "0" rejeita.
    public async Task REV1_SequenciaUmVsZero_Rejeita()
    {
        var repo = new FakeRepoResolucao
        {
            Candidatos = [Resumo(71)],
            Detalhes = { [71] = Lanc(71, [Item("14391", "4", "1000037", dep: "PP01"), Item("14391", "5", "3500024", dep: "PP02")]) }
        };

        var res = await Servico(repo).ResolverLancamentoPendenteDoContextoAsync(Contexto(sequencia: "0"), Ocorrencia(seq: "1"));
        Assert.Equal(CardinalidadeResolucaoLancamento.Zero, res.Cardinalidade);
    }

    [Fact] // REV1 R9: sequência "1" contra vazio rejeita; branco não vira wildcard.
    public async Task REV1_SequenciaUmVsVazio_Rejeita()
    {
        var repo = new FakeRepoResolucao
        {
            Candidatos = [Resumo(71)],
            Detalhes = { [71] = Lanc(71, [Item("14391", "4", "1000037", dep: "PP01"), Item("14391", "5", "3500024", dep: "PP02")]) }
        };

        var res = await Servico(repo).ResolverLancamentoPendenteDoContextoAsync(Contexto(sequencia: ""), Ocorrencia(seq: "1"));
        Assert.Equal(CardinalidadeResolucaoLancamento.Zero, res.Cardinalidade);
    }

    [Fact] // REV1 R10: falha no resolver é fail-closed e produz diagnóstico sanitizado.
    public void REV1_Form_ExceptionResolver_FailClosedComDiagnosticoSanitizado()
    {
        string form = Fonte("Tela", "Processo", "ProcessoConsumoMaterialForm.cs");
        string resolver = ExtrairMetodo(form, "private async Task ResolverLancamentoPendentePersistidoAsync");
        Assert.Contains("catch (Exception ex)", resolver, StringComparison.Ordinal);
        Assert.Contains("RegistrarFalhaResolucaoRecovery(ex);", resolver, StringComparison.Ordinal);

        string falha = ExtrairMetodo(form, "private void RegistrarFalhaResolucaoRecovery");
        Assert.Contains("_codigoLancamentoRecuperado = null;", falha, StringComparison.Ordinal);
        Assert.Contains("_contextoResolucaoSnapshot = null;", falha, StringComparison.Ordinal);
        Assert.Contains("AtualizarBotaoConfirmar();", falha, StringComparison.Ordinal);
        Assert.Contains("TraceWarning", falha, StringComparison.Ordinal);
    }

    [Fact] // REV1 R11: falha no resolver continua zero save/claim/post.
    public void REV1_Form_ExceptionResolver_ZeroSaveClaimPost()
    {
        string form = Fonte("Tela", "Processo", "ProcessoConsumoMaterialForm.cs");
        string falha = ExtrairMetodo(form, "private void RegistrarFalhaResolucaoRecovery");
        Assert.DoesNotContain("SalvarConsumoLocalAsync", falha, StringComparison.Ordinal);
        Assert.DoesNotContain("EnviarConsumoSap261Async", falha, StringComparison.Ordinal);
        Assert.DoesNotContain("TentarReservar", falha, StringComparison.Ordinal);
        Assert.DoesNotContain("Post", falha, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // REV1 R12: invalidação por troca de contexto preserva fail-closed do PK recuperado.
    public void REV1_Form_InvalidacaoContexto_PkNaoSobreviveTroca()
    {
        string form = Fonte("Tela", "Processo", "ProcessoConsumoMaterialForm.cs");
        Assert.Contains("private bool RecoveryAtivoNoContextoAtual()", form, StringComparison.Ordinal);
        Assert.Contains("_contextoResolucaoSnapshot is { } snapshot", form, StringComparison.Ordinal);
        Assert.Contains("snapshot.Equals(SnapshotContextoAtual())", form, StringComparison.Ordinal);

        string envio = ExtrairMetodo(form, "private async Task EnviarSap261LancamentoRecuperadoAsync");
        Assert.Contains("_codigoLancamentoRecuperado = null;", envio, StringComparison.Ordinal);
        Assert.Contains("_contextoResolucaoSnapshot = null;", envio, StringComparison.Ordinal);
    }

    // ---------- REV1-SANITIZACAO: diagnóstico fail-closed do recovery (S1–S12) ----------

    // Segredos que NUNCA podem aparecer no diagnóstico sanitizado, mesmo vindos de Exception arbitrária.
    public static IEnumerable<object[]> ExcecoesComSegredo()
    {
        yield return ["token=SEGREDO_TOKEN_123", "SEGREDO_TOKEN_123"];                          // S1
        yield return ["Bearer eyJSEGREDO", "eyJSEGREDO"];                                        // S2
        yield return ["Authorization: Bearer SEGREDO_AUTH", "SEGREDO_AUTH"];                     // S3
        yield return ["client_secret=SUPER_SECRET", "SUPER_SECRET"];                             // S4
        yield return ["access_token=ACCESS_SECRET", "ACCESS_SECRET"];                            // S5
        yield return ["payload={\"password\":\"SEGREDO_PAYLOAD\"}", "SEGREDO_PAYLOAD"];          // S6
        yield return ["Password=MinhaSenhaSecreta", "MinhaSenhaSecreta"];                        // S7
        yield return ["pwd=MinhaSenhaPwd", "MinhaSenhaPwd"];                                     // S8
        yield return ["ConnectionString=Host=x;User Id=y;Password=zSECRET", "zSECRET"];          // S9
        yield return ["AcCeSs_ToKeN=SEGREDO_MIXED", "SEGREDO_MIXED"];                            // S11 (mixed-case)
    }

    [Theory] // S1–S9, S11: nenhum segredo da mensagem chega ao diagnóstico controlado.
    [MemberData(nameof(ExcecoesComSegredo))]
    public void Sanitizacao_MensagemComSegredo_NaoVaza(string mensagem, string segredo)
    {
        string diag = ProcessoConsumoMaterialForm.SanitizarDiagnosticoRecovery(new InvalidOperationException(mensagem));

        Assert.DoesNotContain(segredo, diag, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(mensagem, diag, StringComparison.Ordinal);           // mensagem bruta não aparece
        Assert.Contains("EXCEPTION_TYPE=InvalidOperationException", diag, StringComparison.Ordinal);
        Assert.Contains("EVENTO=FALHA_RESOLUCAO_LANCAMENTO_PENDENTE", diag, StringComparison.Ordinal);
    }

    [Fact] // S10: segredo em InnerException também não vaza (só tipo/HRESULT do topo).
    public void Sanitizacao_InnerExceptionComSegredo_NaoVaza()
    {
        var inner = new InvalidOperationException("access_token=INNER_SECRET_9999");
        var ex = new AggregateException("Bearer OUTER_SECRET", inner);

        string diag = ProcessoConsumoMaterialForm.SanitizarDiagnosticoRecovery(ex);

        Assert.DoesNotContain("INNER_SECRET_9999", diag, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OUTER_SECRET", diag, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("access_token", diag, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("EXCEPTION_TYPE=AggregateException", diag, StringComparison.Ordinal);
    }

    [Fact] // S12: mensagem comum SEM segredo — ainda assim não copia texto arbitrário da exception.
    public void Sanitizacao_MensagemComum_NaoCopiaTextoArbitrario()
    {
        const string mensagem = "Falha ao consultar o repositório de consumo no contexto atual.";
        string diag = ProcessoConsumoMaterialForm.SanitizarDiagnosticoRecovery(new TimeoutException(mensagem));

        Assert.DoesNotContain(mensagem, diag, StringComparison.Ordinal);
        Assert.Contains("EXCEPTION_TYPE=TimeoutException", diag, StringComparison.Ordinal);
        Assert.Contains("HRESULT=0x", diag, StringComparison.Ordinal);
    }

    [Fact] // Prova estrutural: o sanitizador NÃO referencia Message/ToString/StackTrace/InnerException.
    public void Sanitizacao_NaoReferenciaFontesSensiveis()
    {
        string form = Fonte("Tela", "Processo", "ProcessoConsumoMaterialForm.cs");
        string metodo = ExtrairMetodo(form, "internal static string SanitizarDiagnosticoRecovery");

        Assert.DoesNotContain(".Message", metodo, StringComparison.Ordinal);
        Assert.DoesNotContain(".ToString()", metodo, StringComparison.Ordinal);
        Assert.DoesNotContain(".StackTrace", metodo, StringComparison.Ordinal);
        Assert.DoesNotContain(".InnerException", metodo, StringComparison.Ordinal);
        Assert.Contains("ex.GetType().Name", metodo, StringComparison.Ordinal);
    }

    // ---------- GATE 050 RECOVERY-CONCLUSAO: finalização local de lançamento CONFIRMADO_SAP (C1–C22) ----------

    [Fact] // C2: CONFIRMADO_SAP + doc/ano + match 1:1 → recupera como finalização local (PK).
    public async Task Confirmado_ComEvidenciaEMatch_ResolvePk()
    {
        var repo = new FakeRepoResolucao
        {
            Candidatos = [Resumo(71, status: "CONFIRMADO_SAP", doc: "4900006518", ex: "2026")],
            Detalhes = { [71] = Lanc(71, [Item("14391", "4", "1000037", dep: "PP01"), Item("14391", "5", "3500024", dep: "PP02")], status: "CONFIRMADO_SAP", doc: "4900006518", ex: "2026") }
        };
        var res = await Servico(repo).ResolverLancamentoConfirmadoDoContextoAsync(Contexto(), Ocorrencia());
        Assert.Equal(CardinalidadeResolucaoLancamento.Um, res.Cardinalidade);
        Assert.Equal(71, res.CodigoLancamento);
    }

    [Fact] // C3: CONFIRMADO_SAP sem documento → fail-closed (Zero).
    public async Task Confirmado_SemDocumento_FailClosed()
    {
        var repo = new FakeRepoResolucao
        {
            Candidatos = [Resumo(71, status: "CONFIRMADO_SAP", doc: null, ex: "2026")],
            Detalhes = { [71] = Lanc(71, [Item("14391", "4", "1000037", dep: "PP01"), Item("14391", "5", "3500024", dep: "PP02")], status: "CONFIRMADO_SAP", doc: null, ex: "2026") }
        };
        var res = await Servico(repo).ResolverLancamentoConfirmadoDoContextoAsync(Contexto(), Ocorrencia());
        Assert.Equal(CardinalidadeResolucaoLancamento.Zero, res.Cardinalidade);
    }

    [Fact] // C4: documento presente mas status PENDENTE (incompatível com finalização) → Zero.
    public async Task Confirmado_StatusIncompativel_FailClosed()
    {
        var repo = new FakeRepoResolucao
        {
            Candidatos = [Resumo(71, status: "PENDENTE_SAP", doc: "4900006518", ex: "2026")],
            Detalhes = { [71] = Lanc(71, [Item("14391", "4", "1000037", dep: "PP01")], status: "PENDENTE_SAP", doc: "4900006518", ex: "2026") }
        };
        var res = await Servico(repo).ResolverLancamentoConfirmadoDoContextoAsync(Contexto(), Ocorrencia());
        Assert.Equal(CardinalidadeResolucaoLancamento.Zero, res.Cardinalidade);
    }

    [Fact] // C5: match 0 (itens de outra ocorrência) → não finaliza.
    public async Task Confirmado_SemMatch_Zero()
    {
        var repo = new FakeRepoResolucao
        {
            Candidatos = [Resumo(71, status: "CONFIRMADO_SAP", doc: "4900006518", ex: "2026")],
            Detalhes = { [71] = Lanc(71, [Item("99999", "1", "7777777", dep: "PP07")], status: "CONFIRMADO_SAP", doc: "4900006518", ex: "2026") }
        };
        var res = await Servico(repo).ResolverLancamentoConfirmadoDoContextoAsync(Contexto(), Ocorrencia());
        Assert.Equal(CardinalidadeResolucaoLancamento.Zero, res.Cardinalidade);
    }

    [Fact] // C6: match N → não escolhe automaticamente.
    public async Task Confirmado_VariosValidos_NaoEscolhe()
    {
        var repo = new FakeRepoResolucao
        {
            Candidatos = [Resumo(71, status: "CONFIRMADO_SAP", doc: "4900006518", ex: "2026"), Resumo(72, status: "CONFIRMADO_SAP", doc: "4900006519", ex: "2026")],
            Detalhes =
            {
                [71] = Lanc(71, [Item("14391", "4", "1000037", dep: "PP01"), Item("14391", "5", "3500024", dep: "PP02")], status: "CONFIRMADO_SAP", doc: "4900006518", ex: "2026"),
                [72] = Lanc(72, [Item("14391", "4", "1000037", dep: "PP01"), Item("14391", "5", "3500024", dep: "PP02")], status: "CONFIRMADO_SAP", doc: "4900006519", ex: "2026")
            }
        };
        var res = await Servico(repo).ResolverLancamentoConfirmadoDoContextoAsync(Contexto(), Ocorrencia());
        Assert.Equal(CardinalidadeResolucaoLancamento.Varios, res.Cardinalidade);
        Assert.Null(res.CodigoLancamento);
    }

    [Fact] // C1: resolver de finalização IGNORA PENDENTE_SAP (esse é caminho de ENVIAR SAP 261).
    public async Task Confirmado_IgnoraPendente()
    {
        var repo = new FakeRepoResolucao
        {
            Candidatos = [Resumo(71, status: "PENDENTE_SAP")],
            Detalhes = { [71] = Lanc(71, [Item("14391", "4", "1000037", dep: "PP01")]) }
        };
        var res = await Servico(repo).ResolverLancamentoConfirmadoDoContextoAsync(Contexto(), Ocorrencia());
        Assert.Equal(CardinalidadeResolucaoLancamento.Zero, res.Cardinalidade);
    }

    [Fact] // C16: agregado conclui a ocorrência via processo terminal CONFIRMADO_SAP mesmo com Withdrawn=0<Required.
    public void Agregado_ConcluiPorProcessoTerminal_SemWithdrawn()
    {
        var requisitos = new List<ComponenteOrdemProducaoSap>
        {
            new() { Reserva = "14391", ItemReserva = "4", QuantidadeNecessaria = 54.920m, QuantidadeRetirada = 0m },
            new() { Reserva = "14391", ItemReserva = "5", QuantidadeNecessaria = 76.020m, QuantidadeRetirada = 0m }
        };
        var vinculos = new List<ApontamentoProcesso>
        {
            new() { TipoProcesso = "CONSUMO_MATERIA_PRIMA", StatusLancamento = "CONFIRMADO_SAP", DocumentoMaterialSap = "4900006518", ExercicioMaterialSap = "2026", Reservation = "14391", ReservationItem = "4" },
            new() { TipoProcesso = "CONSUMO_MATERIA_PRIMA", StatusLancamento = "CONFIRMADO_SAP", DocumentoMaterialSap = "4900006518", ExercicioMaterialSap = "2026", Reservation = "14391", ReservationItem = "5" }
        };

        Assert.Equal(EstadoConclusaoAgregada.Concluida, AvaliadorConclusaoAgregada.Avaliar(requisitos, vinculos));
    }

    [Fact] // C7/C8: confirmado recuperado ⇒ FINALIZAR ATIVIDADE (rótulo distinto), nunca "Enviar SAP 261".
    public void Form_Confirmado_UsaFinalizarAtividade()
    {
        string form = Fonte("Tela", "Processo", "ProcessoConsumoMaterialForm.cs");
        string materializa = ExtrairMetodo(form, "private void AplicarMaterializacaoFinalizacao");
        Assert.Contains("\"Finalizar Atividade\"", materializa, StringComparison.Ordinal);
        Assert.Contains("CONFIRMADO_SAP", materializa, StringComparison.Ordinal);
        // A ação de finalização precede o envio no Click e nunca chama o caminho 261.
        string click = ExtrairMetodo(form, "private async Task EnviarSap261Async");
        Assert.Contains("_codigoLancamentoConfirmadoRecuperado is long codigoConfirmado", click, StringComparison.Ordinal);
        Assert.Contains("FinalizarAtividadeLocal(codigoConfirmado)", click, StringComparison.Ordinal);
    }

    [Fact] // C9–C14, C22: FINALIZAR ATIVIDADE registra resultado terminal; zero POST/save/pesagem/lançamento.
    public void Form_FinalizarAtividade_LocalPuroRegistraResultado()
    {
        string form = Fonte("Tela", "Processo", "ProcessoConsumoMaterialForm.cs");
        string metodo = ExtrairMetodo(form, "private void FinalizarAtividadeLocal");

        Assert.Contains("RegistrarResultadoApontamento(", metodo, StringComparison.Ordinal);
        Assert.Contains("ResultadoExecucaoProcessoApontamento.ConfirmadoSap", metodo, StringComparison.Ordinal);
        Assert.Contains("codigoLancamento", metodo, StringComparison.Ordinal);
        Assert.Contains("confirmadoSap: true", metodo, StringComparison.Ordinal);
        // Zero SAP / save / pesagem.
        Assert.DoesNotContain("EnviarConsumoSap261Async", metodo, StringComparison.Ordinal);
        Assert.DoesNotContain("SalvarConsumoLocalAsync", metodo, StringComparison.Ordinal);
        Assert.DoesNotContain("_pesagensPorComponente", metodo, StringComparison.Ordinal);
        // C21: não depende de flag de escrita SAP.
        Assert.DoesNotContain("EscritaHabilitada", metodo, StringComparison.Ordinal);
        Assert.DoesNotContain("WRITE_ENABLED", metodo, StringComparison.Ordinal);
    }

    // ---------- helpers ----------

    private static ConsumoMaterialConsultaServico Servico(FakeRepoResolucao repo)
        => new(() => repo, new ConsumoMaterialServico());

    private static ContextoApontamentoProcesso Contexto(string sequencia = "0")
        => new() { NumeroOrdem = "1002101", Operacao = "0050", Sequencia = sequencia, TipoProcesso = "CONSUMO_MATERIA_PRIMA" };

    private static List<ComponenteConsumoMaterial> Ocorrencia(string seq = "0")
        => [Comp("14391", "4", "1000037", dep: "PP01", seq: seq), Comp("14391", "5", "3500024", dep: "PP02", seq: seq)];

    private static ComponenteConsumoMaterial Comp(
        string reserva, string itemReserva, string material,
        string centro = "3007", string dep = "PP01", string lote = "L1",
        string operacao = "0050", string seq = "0", string tipoMov = "261")
        => new()
        {
            NumeroReserva = reserva, ItemReserva = itemReserva, CodigoMaterial = material,
            Centro = centro, DepositoConsumo = dep, Lote = lote,
            Operacao = operacao, SequenciaOperacao = seq, TipoMovimento = tipoMov
        };

    private static ConsumoMaterialItem Item(
        string reserva, string itemReserva, string material,
        string centro = "3007", string dep = "PP01", string lote = "L1", string tipoMov = "261")
        => new()
        {
            NumeroReserva = reserva, ItemReserva = itemReserva, CodigoMaterial = material,
            Centro = centro, DepositoConsumo = dep, Lote = lote, TipoMovimentoSap = tipoMov
        };

    private static ResumoConsumoMaterialLancamento Resumo(long codigo, string status = "PENDENTE_SAP", string? doc = null, string? ex = null)
        => new() { CodigoLancamento = codigo, NumeroOrdem = "1002101", StatusLancamento = status, DocumentoMaterialSap = doc, ExercicioDocumentoMaterialSap = ex };

    private static ConsumoMaterialLancamento Lanc(long codigo, List<ConsumoMaterialItem> itens, string status = "PENDENTE_SAP", string? doc = null, string? ex = null)
        => new() { Codigo = codigo, NumeroOrdem = "1002101", StatusLancamento = status, DocumentoMaterialSap = doc, ExercicioDocumentoMaterialSap = ex, Itens = itens };

    private static string Fonte(params string[] partes)
    {
        string raiz = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && raiz is not null; i++)
        {
            string candidato = Path.Combine(raiz, Path.Combine(partes));
            if (File.Exists(candidato))
            {
                return File.ReadAllText(candidato);
            }

            string projeto = Path.Combine(raiz, "FugaPet_HML", Path.Combine(partes));
            if (File.Exists(projeto))
            {
                return File.ReadAllText(projeto);
            }

            raiz = Directory.GetParent(raiz)?.FullName!;
        }

        throw new FileNotFoundException($"Fonte não encontrada: {string.Join('/', partes)}");
    }

    private static string ExtrairMetodo(string fonte, string assinatura)
    {
        int inicio = fonte.IndexOf(assinatura, StringComparison.Ordinal);
        Assert.True(inicio >= 0, $"Assinatura não encontrada: {assinatura}");
        int abre = fonte.IndexOf('{', inicio);
        int profundidade = 0;
        for (int i = abre; i < fonte.Length; i++)
        {
            if (fonte[i] == '{') profundidade++;
            else if (fonte[i] == '}')
            {
                profundidade--;
                if (profundidade == 0)
                {
                    return fonte.Substring(inicio, i - inicio + 1);
                }
            }
        }

        return fonte[inicio..];
    }

    private sealed class FakeRepoResolucao : IConsumoMaterialRepositorio
    {
        public IReadOnlyList<ResumoConsumoMaterialLancamento> Candidatos { get; init; } = [];
        public Dictionary<long, ConsumoMaterialLancamento> Detalhes { get; init; } = new();
        public int Reservas { get; private set; }
        public int Confirmacoes { get; private set; }
        public int Falhas { get; private set; }
        public int Saves { get; private set; }

        public Task<long> SalvarConsumoLocalAsync(ConsumoMaterialLancamento lancamento, CancellationToken cancellationToken = default)
        {
            Saves++;
            return Task.FromResult(1L);
        }

        public Task<ConsumoMaterialLancamento?> ObterPorCodigoAsync(long codigoLancamento, CancellationToken cancellationToken = default)
            => Task.FromResult(Detalhes.TryGetValue(codigoLancamento, out ConsumoMaterialLancamento? l) ? l : null);

        public Task<IReadOnlyList<ResumoConsumoMaterialLancamento>> ConsultarLancamentosAsync(
            ConsultaConsumoMaterialFiltro filtro, CancellationToken cancellationToken = default)
            => Task.FromResult(Candidatos);

        public Task<ConsumoMaterialLancamento?> ObterDetalheCompletoAsync(long codigoLancamento, CancellationToken cancellationToken = default)
            => Task.FromResult(Detalhes.TryGetValue(codigoLancamento, out ConsumoMaterialLancamento? l) ? l : null);

        public Task<bool> TentarReservarEnvioSapAsync(long codigoLancamento, DateTime reservadoEmUtc, CancellationToken cancellationToken = default)
        {
            Reservas++;
            return Task.FromResult(false);
        }

        public Task MarcarFalhaSapAsync(long codigoLancamento, CancellationToken cancellationToken = default)
        {
            Falhas++;
            return Task.CompletedTask;
        }

        public Task MarcarConsumoConfirmadoSapAsync(long codigoLancamento, string? documentoMaterialSap, string? exercicioDocumentoMaterialSap, DateTime enviadoSapEmUtc, CancellationToken cancellationToken = default)
        {
            Confirmacoes++;
            return Task.CompletedTask;
        }
    }
}
