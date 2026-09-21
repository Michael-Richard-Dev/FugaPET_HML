using FugaPET_HML.AcessoDados.Repositorio;
using FugaPET_HML.Modelo.Consumo;
using FugaPET_HML.Modelo.Processo;
using FugaPET_HML.Servicos.Operacao;

namespace FugaPET_HML.Tests.Processo;

/// <summary>
/// GATE 101J — recuperação READ-ONLY de consumo PENDENTE_SAP persistido, correspondente por MATCH
/// item-a-item à ocorrência do apontamento (OP + operação/sequência via conjunto de componentes filtrado),
/// e envio pela cerimônia/capability 261 bound ao MESMO PK. Reescreve o Code05 histórico (stale no 101E)
/// contra o CONTRATO REAL do HEAD 744d718 (resolvedor em <see cref="ConsumoMaterialConsultaServico"/> +
/// wiring da <c>ProcessoConsumoMaterialForm</c>). Fail-closed: nunca autosseleciona; ENVIANDO bloqueia.
/// </summary>
public sealed class ConsumoRecuperacaoLancamentoCode05Tests
{
    // ======================================================================================
    // MATCH item-a-item PURO (ConsumoMaterialConsultaServico.ItensCasamComComponentes)
    // ======================================================================================

    [Fact] // Base: dois itens casam 1:1 com dois componentes distintos.
    public void Match_DoisItens_CasamComComponentesDistintos()
    {
        var componentes = new List<ComponenteConsumoMaterial> { Comp("185", "1", "1000186", dep: "PP01"), Comp("185", "2", "3500024", dep: "PP02") };
        var itens = new List<ConsumoMaterialItem> { Item("185", "1", "1000186", dep: "PP01"), Item("185", "2", "3500024", dep: "PP02") };
        Assert.True(ConsumoMaterialConsultaServico.ItensCasamComComponentes(itens, componentes));
    }

    [Fact] // Candidato vazio: nenhum item → inválido.
    public void Match_SemItens_Invalido()
        => Assert.False(ConsumoMaterialConsultaServico.ItensCasamComComponentes([], new List<ComponenteConsumoMaterial> { Comp("185", "1", "1000186") }));

    [Fact] // C05: reserva divergente → rejeita.
    public void Match_ReservaDivergente_Rejeita()
        => Assert.False(ConsumoMaterialConsultaServico.ItensCasamComComponentes(
            new List<ConsumoMaterialItem> { Item("999", "1", "1000186") },
            new List<ComponenteConsumoMaterial> { Comp("185", "1", "1000186") }));

    [Fact] // C06: item da reserva divergente → rejeita.
    public void Match_ItemReservaDivergente_Rejeita()
        => Assert.False(ConsumoMaterialConsultaServico.ItensCasamComComponentes(
            new List<ConsumoMaterialItem> { Item("185", "9", "1000186") },
            new List<ComponenteConsumoMaterial> { Comp("185", "1", "1000186") }));

    [Fact] // C04: material divergente → rejeita.
    public void Match_MaterialDivergente_Rejeita()
        => Assert.False(ConsumoMaterialConsultaServico.ItensCasamComComponentes(
            new List<ConsumoMaterialItem> { Item("185", "1", "9999999") },
            new List<ComponenteConsumoMaterial> { Comp("185", "1", "1000186") }));

    [Fact] // C08: depósito divergente → rejeita.
    public void Match_DepositoDivergente_Rejeita()
        => Assert.False(ConsumoMaterialConsultaServico.ItensCasamComComponentes(
            new List<ConsumoMaterialItem> { Item("185", "1", "1000186", dep: "PP09") },
            new List<ComponenteConsumoMaterial> { Comp("185", "1", "1000186", dep: "PP01") }));

    [Fact] // C07: lote divergente → rejeita.
    public void Match_LoteDivergente_Rejeita()
        => Assert.False(ConsumoMaterialConsultaServico.ItensCasamComComponentes(
            new List<ConsumoMaterialItem> { Item("185", "1", "1000186", lote: "LOTE-B") },
            new List<ComponenteConsumoMaterial> { Comp("185", "1", "1000186", lote: "LOTE-A") }));

    [Fact] // C10: identidade obrigatória ausente (lote vazio) → fail-closed.
    public void Match_IdentidadeObrigatoriaAusente_FailClosed()
        => Assert.False(ConsumoMaterialConsultaServico.ItensCasamComComponentes(
            new List<ConsumoMaterialItem> { Item("185", "1", "1000186", lote: "") },
            new List<ComponenteConsumoMaterial> { Comp("185", "1", "1000186", lote: "") }));

    [Fact] // C09: movimento != 261 → rejeita.
    public void Match_MovimentoNao261_Rejeita()
        => Assert.False(ConsumoMaterialConsultaServico.ItensCasamComComponentes(
            new List<ConsumoMaterialItem> { Item("185", "1", "1000186", tipoMov: "531") },
            new List<ComponenteConsumoMaterial> { Comp("185", "1", "1000186") }));

    [Fact] // Ambiguidade: item casa com >1 componente idêntico → rejeita.
    public void Match_ItemComMaisDeUmComponente_Rejeita()
        => Assert.False(ConsumoMaterialConsultaServico.ItensCasamComComponentes(
            new List<ConsumoMaterialItem> { Item("185", "1", "1000186") },
            new List<ComponenteConsumoMaterial> { Comp("185", "1", "1000186"), Comp("185", "1", "1000186") }));

    [Fact] // Ambiguidade: dois itens disputam o mesmo componente → rejeita.
    public void Match_DoisItensMesmoComponente_Rejeita()
        => Assert.False(ConsumoMaterialConsultaServico.ItensCasamComComponentes(
            new List<ConsumoMaterialItem> { Item("185", "1", "1000186"), Item("185", "1", "1000186") },
            new List<ComponenteConsumoMaterial> { Comp("185", "1", "1000186") }));

    // ======================================================================================
    // RESOLVEDOR READ-ONLY (ResolverRecuperacaoPendenteAsync) — cardinalidade/status
    // ======================================================================================

    [Fact] // C01: zero candidatos persistidos → Nenhum (fluxo novo).
    public async Task Resolver_ZeroCandidatos_Nenhum()
    {
        var repo = new FakeRepoRecuperacao();
        var res = await Servico(repo).ResolverRecuperacaoPendenteAsync(Contexto(), Ocorrencia());
        Assert.Equal(ModalidadeRecuperacaoConsumo.Nenhum, res.Modalidade);
        Assert.Null(res.CodigoLancamento);
    }

    [Fact] // C02/C19: exatamente um PENDENTE exato → recupera o PK EXATO (sem hardcode; chega por match).
    public async Task Resolver_UmPendenteExato_RecuperaPkExato()
    {
        var repo = new FakeRepoRecuperacao
        {
            Candidatos = [Resumo(8)],
            Detalhes = { [8] = Lanc(8, [Item("185", "1", "1000186", dep: "PP01")]) }
        };
        var res = await Servico(repo).ResolverRecuperacaoPendenteAsync(Contexto(), Ocorrencia());
        Assert.Equal(ModalidadeRecuperacaoConsumo.UmPendente, res.Modalidade);
        Assert.Equal(8, res.CodigoLancamento);
    }

    [Fact] // C03/C11-ambiguidade: dois PENDENTE exatos → Ambíguo, nenhum escolhido automaticamente.
    public async Task Resolver_DoisPendentesExatos_Ambiguo()
    {
        var repo = new FakeRepoRecuperacao
        {
            Candidatos = [Resumo(8), Resumo(9)],
            Detalhes =
            {
                [8] = Lanc(8, [Item("185", "1", "1000186", dep: "PP01")]),
                [9] = Lanc(9, [Item("185", "1", "1000186", dep: "PP01")])
            }
        };
        var res = await Servico(repo).ResolverRecuperacaoPendenteAsync(Contexto(), Ocorrencia());
        Assert.Equal(ModalidadeRecuperacaoConsumo.AmbiguoPendente, res.Modalidade);
        Assert.Null(res.CodigoLancamento);
        Assert.Equal(2, res.Candidatos.Count);
    }

    [Fact] // C04: candidato com material divergente → não casa → Nenhum.
    public async Task Resolver_MaterialDivergente_Nenhum()
    {
        var repo = new FakeRepoRecuperacao
        {
            Candidatos = [Resumo(8)],
            Detalhes = { [8] = Lanc(8, [Item("185", "1", "9999999", dep: "PP01")]) }
        };
        var res = await Servico(repo).ResolverRecuperacaoPendenteAsync(Contexto(), Ocorrencia());
        Assert.Equal(ModalidadeRecuperacaoConsumo.Nenhum, res.Modalidade);
    }

    [Fact] // C11: PENDENTE com documento SAP preenchido → não elegível → Nenhum.
    public async Task Resolver_PendenteComDocumento_Nenhum()
    {
        var repo = new FakeRepoRecuperacao
        {
            Candidatos = [Resumo(8, doc: "4900006518")],
            Detalhes = { [8] = Lanc(8, [Item("185", "1", "1000186", dep: "PP01")], doc: "4900006518") }
        };
        var res = await Servico(repo).ResolverRecuperacaoPendenteAsync(Contexto(), Ocorrencia());
        Assert.Equal(ModalidadeRecuperacaoConsumo.Nenhum, res.Modalidade);
    }

    [Fact] // C12: PENDENTE com ano SAP preenchido → não elegível → Nenhum.
    public async Task Resolver_PendenteComAno_Nenhum()
    {
        var repo = new FakeRepoRecuperacao
        {
            Candidatos = [Resumo(8, ex: "2026")],
            Detalhes = { [8] = Lanc(8, [Item("185", "1", "1000186", dep: "PP01")], ex: "2026") }
        };
        var res = await Servico(repo).ResolverRecuperacaoPendenteAsync(Contexto(), Ocorrencia());
        Assert.Equal(ModalidadeRecuperacaoConsumo.Nenhum, res.Modalidade);
    }

    [Fact] // C13: CONFIRMADO_SAP não é candidato de envio (nem blocker) → Nenhum.
    public async Task Resolver_Confirmado_Nenhum()
    {
        var repo = new FakeRepoRecuperacao
        {
            Candidatos = [Resumo(8, status: "CONFIRMADO_SAP", doc: "4900006518", ex: "2026")],
            Detalhes = { [8] = Lanc(8, [Item("185", "1", "1000186", dep: "PP01")], status: "CONFIRMADO_SAP", doc: "4900006518", ex: "2026") }
        };
        var res = await Servico(repo).ResolverRecuperacaoPendenteAsync(Contexto(), Ocorrencia());
        Assert.Equal(ModalidadeRecuperacaoConsumo.Nenhum, res.Modalidade);
    }

    [Fact] // C14: CANCELADO_LOCAL não é candidato → Nenhum.
    public async Task Resolver_Cancelado_Nenhum()
    {
        var repo = new FakeRepoRecuperacao
        {
            Candidatos = [Resumo(8, status: "CANCELADO_LOCAL")],
            Detalhes = { [8] = Lanc(8, [Item("185", "1", "1000186", dep: "PP01")], status: "CANCELADO_LOCAL") }
        };
        var res = await Servico(repo).ResolverRecuperacaoPendenteAsync(Contexto(), Ocorrencia());
        Assert.Equal(ModalidadeRecuperacaoConsumo.Nenhum, res.Modalidade);
    }

    [Fact] // C15/C27: ENVIANDO_SAP correspondente → EnviandoReconciliacao (blocker persistente), sem PK de envio.
    public async Task Resolver_Enviando_ReconciliacaoBlocker()
    {
        var repo = new FakeRepoRecuperacao
        {
            Candidatos = [Resumo(8, status: "ENVIANDO_SAP")],
            Detalhes = { [8] = Lanc(8, [Item("185", "1", "1000186", dep: "PP01")], status: "ENVIANDO_SAP") }
        };
        var res = await Servico(repo).ResolverRecuperacaoPendenteAsync(Contexto(), Ocorrencia());
        Assert.Equal(ModalidadeRecuperacaoConsumo.EnviandoReconciliacao, res.Modalidade);
        Assert.Null(res.CodigoLancamento);
    }

    [Fact] // §10: ENVIANDO domina mesmo havendo um PENDENTE elegível para a ocorrência.
    public async Task Resolver_EnviandoDominaPendente_Reconciliacao()
    {
        var repo = new FakeRepoRecuperacao
        {
            Candidatos = [Resumo(8, status: "PENDENTE_SAP"), Resumo(9, status: "ENVIANDO_SAP")],
            Detalhes =
            {
                [8] = Lanc(8, [Item("185", "1", "1000186", dep: "PP01")]),
                [9] = Lanc(9, [Item("185", "1", "1000186", dep: "PP01")], status: "ENVIANDO_SAP")
            }
        };
        var res = await Servico(repo).ResolverRecuperacaoPendenteAsync(Contexto(), Ocorrencia());
        Assert.Equal(ModalidadeRecuperacaoConsumo.EnviandoReconciliacao, res.Modalidade);
    }

    [Fact] // C17/C18: a RESOLUÇÃO é READ-ONLY — zero save, zero reserva/claim, zero confirmação, zero pesagem.
    public async Task Resolver_ReadOnly_ZeroEscritaZeroClaim()
    {
        var repo = new FakeRepoRecuperacao
        {
            Candidatos = [Resumo(8)],
            Detalhes = { [8] = Lanc(8, [Item("185", "1", "1000186", dep: "PP01")]) }
        };
        _ = await Servico(repo).ResolverRecuperacaoPendenteAsync(Contexto(), Ocorrencia());
        Assert.Equal(0, repo.Saves);
        Assert.Equal(0, repo.Reservas);
        Assert.Equal(0, repo.Confirmacoes);
        Assert.Equal(0, repo.Falhas);
    }

    [Fact] // Contexto de OUTRO tipo de processo → resolvedor não atua (Nenhum).
    public async Task Resolver_TipoProcessoDiferente_Nenhum()
    {
        var repo = new FakeRepoRecuperacao
        {
            Candidatos = [Resumo(8)],
            Detalhes = { [8] = Lanc(8, [Item("185", "1", "1000186", dep: "PP01")]) }
        };
        var contexto = new ContextoApontamentoProcesso
        { NumeroOrdem = "1000170", Operacao = "0010", Sequencia = "0", TipoProcesso = "RESULTADO_APONTAMENTO" };
        var res = await Servico(repo).ResolverRecuperacaoPendenteAsync(contexto, Ocorrencia());
        Assert.Equal(ModalidadeRecuperacaoConsumo.Nenhum, res.Modalidade);
    }

    // ======================================================================================
    // WIRING DA FORM (prova estrutural: seam sem behavioral acessível numa WinForms)
    // ======================================================================================

    [Fact] // §12 passo 5: resolve SOMENTE depois de materializar OP/componentes da ocorrência.
    public void Form_ResolveDepoisDeMaterializarComponentes()
    {
        string form = Fonte("Tela", "Processo", "ProcessoConsumoMaterialForm.cs");
        string consulta = ExtrairMetodo(form, "private async Task ConsultarOrdemProducaoAsync");
        int idxPreencher = consulta.IndexOf("PreencherOrdemCarregada(resultado.Ordem, componentesOperacionais)", StringComparison.Ordinal);
        int idxResolver = consulta.IndexOf("ResolverRecuperacaoPersistidaDoContextoAsync(componentesOperacionais)", StringComparison.Ordinal);
        Assert.True(idxPreencher >= 0 && idxResolver >= 0 && idxPreencher < idxResolver,
            "A recuperação deve ocorrer DEPOIS de materializar OP/componentes.");
    }

    [Fact] // C19/C23/C25: recovery envia o PK RECUPERADO pela MESMA cerimônia/seam 261 (uma única).
    public void Form_EnvioRecuperado_UsaSeamUnicoComMesmoPk()
    {
        string form = Fonte("Tela", "Processo", "ProcessoConsumoMaterialForm.cs");
        string metodo = ExtrairMetodo(form, "private async Task EnviarSap261RecuperadoAsync");
        // Uma única cerimônia (AutorizarEEnviarSap261Async), com o PK recuperado.
        Assert.Contains("AutorizarEEnviarSap261Async(codigoLancamento, usuario)", metodo, StringComparison.Ordinal);
        Assert.Equal(1, ContarOcorrencias(metodo, "AutorizarEEnviarSap261Async"));
        // Não implementa segundo writer nem salva/cria pesagem no recovery.
        Assert.DoesNotContain("SalvarConsumoLocalAsync", metodo, StringComparison.Ordinal);
        Assert.DoesNotContain("_pesagensPorComponente", metodo, StringComparison.Ordinal);
        Assert.DoesNotContain("RegistrarPesagem", metodo, StringComparison.Ordinal);
    }

    [Fact] // C20: troca de contexto invalida o PK recuperado antes do envio (zero claim/POST).
    public void Form_TrocaContexto_InvalidaPkRecuperado()
    {
        string form = Fonte("Tela", "Processo", "ProcessoConsumoMaterialForm.cs");
        string metodo = ExtrairMetodo(form, "private async Task EnviarSap261RecuperadoAsync");
        Assert.Contains("!snapshot.Equals(SnapshotContextoRecuperacaoAtual())", metodo, StringComparison.Ordinal);
        Assert.Contains("LimparEstadoRecuperacao();", metodo, StringComparison.Ordinal);
    }

    [Fact] // C21/C22: recusa humana OU falha de auditoria (seam retorna null) ⇒ return, sem conclusão/HTTP extra.
    public void Form_RecusaOuAuditoriaFalha_NaoConclui()
    {
        string form = Fonte("Tela", "Processo", "ProcessoConsumoMaterialForm.cs");
        string metodo = ExtrairMetodo(form, "private async Task EnviarSap261RecuperadoAsync");
        int idxNull = metodo.IndexOf("if (resultado is null)", StringComparison.Ordinal);
        int idxReturn = idxNull >= 0 ? metodo.IndexOf("return;", idxNull, StringComparison.Ordinal) : -1;
        Assert.True(idxNull >= 0 && idxReturn > idxNull, "Seam null (recusa/auditoria) deve retornar sem confirmar.");
    }

    [Fact] // C26: happy path materializa ConfirmadoSap com vínculo = MESMO PK recuperado.
    public void Form_HappyPath_ConfirmadoSapMesmoPk()
    {
        string form = Fonte("Tela", "Processo", "ProcessoConsumoMaterialForm.cs");
        string metodo = ExtrairMetodo(form, "private async Task EnviarSap261RecuperadoAsync");
        Assert.Contains("ResultadoExecucaoProcessoApontamento.ConfirmadoSap, codigoLancamento", metodo, StringComparison.Ordinal);
        Assert.Contains("confirmadoSap: true", metodo, StringComparison.Ordinal);
    }

    [Fact] // C16: recovery ativo bloqueia CONFIRMAR CONSUMO (não cria PK concorrente).
    public void Form_RecoveryAtivo_BloqueiaNovoConsumo()
    {
        string form = Fonte("Tela", "Processo", "ProcessoConsumoMaterialForm.cs");
        string metodo = ExtrairMetodo(form, "private async Task ConfirmarConsumoAsync");
        Assert.Contains("if (RecuperacaoBloqueiaNovoConsumo())", metodo, StringComparison.Ordinal);

        string iniciar = ExtrairMetodo(form, "private bool PodeIniciarLeituraConsumo");
        Assert.Contains("!RecuperacaoBloqueiaNovoConsumo()", iniciar, StringComparison.Ordinal);
    }

    [Fact] // §13: ONE_PENDING materializa "Enviar SAP 261" e bloqueia ações concorrentes.
    public void Form_UmPendente_MaterializaEnvioEBloqueia()
    {
        string form = Fonte("Tela", "Processo", "ProcessoConsumoMaterialForm.cs");
        string metodo = ExtrairMetodo(form, "private void AplicarMaterializacaoRecuperacaoPendente");
        Assert.Contains("_enviarSap261Button.Visible = true;", metodo, StringComparison.Ordinal);
        Assert.Contains("BloquearAcoesConcorrentesRecuperacao();", metodo, StringComparison.Ordinal);

        string bloqueio = ExtrairMetodo(form, "private void BloquearAcoesConcorrentesRecuperacao");
        Assert.Contains("iniciarLeituraButton.Enabled = false;", bloqueio, StringComparison.Ordinal);
        Assert.Contains("SetReadWeightEnabled(false);", bloqueio, StringComparison.Ordinal);
    }

    [Fact] // §10/§11: ambíguo/ENVIANDO bloqueiam novo consumo e NÃO materializam envio.
    public void Form_AmbiguoOuEnviando_BloqueiaSemEnvio()
    {
        string form = Fonte("Tela", "Processo", "ProcessoConsumoMaterialForm.cs");
        string metodo = ExtrairMetodo(form, "private void AplicarBloqueioReconciliacao");
        Assert.Contains("_enviarSap261Button.Visible = false;", metodo, StringComparison.Ordinal);
        Assert.Contains("BloquearAcoesConcorrentesRecuperacao();", metodo, StringComparison.Ordinal);
    }

    [Fact] // C28: recovery de CONFIRMADO_SAP existente segue intacto no Controle de Apontamentos.
    public void ControleApontamentos_RecoveryConfirmado_Preservado()
    {
        string servico = Fonte("Servicos", "Processo", "ProcessoControleApontamentosServico.cs");
        Assert.Contains("public async Task<bool> RecuperarConsumoConfirmadoAsync(", servico, StringComparison.Ordinal);
        Assert.Contains("TentarRecuperarConsumoConfirmadoAsync(", servico, StringComparison.Ordinal);
    }

    // ======================================================================================
    // helpers
    // ======================================================================================

    private static ConsumoMaterialConsultaServico Servico(FakeRepoRecuperacao repo)
        => new(() => repo, new ConsumoMaterialServico());

    private static ContextoApontamentoProcesso Contexto()
        => new() { NumeroOrdem = "1000170", Operacao = "0010", Sequencia = "0", TipoProcesso = "CONSUMO_MATERIA_PRIMA" };

    private static List<ComponenteConsumoMaterial> Ocorrencia()
        => [Comp("185", "1", "1000186", dep: "PP01")];

    private static ComponenteConsumoMaterial Comp(
        string reserva, string itemReserva, string material,
        string dep = "PP01", string lote = "0000000222")
        => new()
        {
            CodigoMaterial = material, NumeroReserva = reserva, ItemReserva = itemReserva,
            DepositoConsumo = dep, Lote = lote, TipoMovimento = "261"
        };

    private static ConsumoMaterialItem Item(
        string reserva, string itemReserva, string material,
        string dep = "PP01", string lote = "0000000222", string tipoMov = "261")
        => new()
        {
            CodigoMaterial = material, NumeroReserva = reserva, ItemReserva = itemReserva,
            DepositoConsumo = dep, Lote = lote, TipoMovimentoSap = tipoMov
        };

    private static ResumoConsumoMaterialLancamento Resumo(long codigo, string status = "PENDENTE_SAP", string? doc = null, string? ex = null)
        => new() { CodigoLancamento = codigo, NumeroOrdem = "1000170", StatusLancamento = status, DocumentoMaterialSap = doc, ExercicioDocumentoMaterialSap = ex };

    private static ConsumoMaterialLancamento Lanc(long codigo, List<ConsumoMaterialItem> itens, string status = "PENDENTE_SAP", string? doc = null, string? ex = null)
        => new() { Codigo = codigo, NumeroOrdem = "1000170", StatusLancamento = status, DocumentoMaterialSap = doc, ExercicioDocumentoMaterialSap = ex, Itens = itens };

    private static int ContarOcorrencias(string fonte, string agulha)
    {
        int total = 0, i = 0;
        while ((i = fonte.IndexOf(agulha, i, StringComparison.Ordinal)) >= 0) { total++; i += agulha.Length; }
        return total;
    }

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

    private sealed class FakeRepoRecuperacao : IConsumoMaterialRepositorio
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
