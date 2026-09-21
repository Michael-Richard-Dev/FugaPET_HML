using System.Reflection;
using FugaPET_HML.AcessoDados.Repositorio;
using FugaPET_HML.Controle.Processo;
using FugaPET_HML.Modelo.Consumo;
using FugaPET_HML.Modelo.Processo;
using FugaPET_HML.Servicos.Operacao;
using FugaPET_HML.Tela.Processo;

namespace FugaPET_HML.Tests.Tela;

/// <summary>
/// GATE 101W — PROVA COMPORTAMENTAL (não source-scan) do recovery pós-retomada na Form REAL.
/// Instancia <see cref="ProcessoConsumoMaterialForm"/> em thread STA, injeta um resolvedor READ-ONLY fake
/// (sem banco/SAP), executa o CÓDIGO PRODUTIVO de resolução/materialização e depois o entrypoint REAL do
/// refresh de seleção (<c>AtualizarComponenteSelecionadoDoGrid</c> — o mesmo chamado pelos eventos do grid),
/// e afere o estado REAL dos controles. Reproduz o incidente OP1000170/0010/PK8.
/// </summary>
public sealed class ConsumoRecuperacaoFormPosRetomadaBehavioralTests
{
    // ---------------- infraestrutura STA + reflection ----------------

    private static void RunSta(Action corpo)
    {
        Exception? capturada = null;
        var t = new Thread(() =>
        {
            try { corpo(); }
            catch (Exception e) { capturada = e; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        t.Join();
        if (capturada is not null)
        {
            throw new Xunit.Sdk.XunitException("Falha no corpo STA: " + capturada);
        }
    }

    private const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static void SetField(object alvo, string nome, object? valor)
        => ObterCampo(alvo.GetType(), nome).SetValue(alvo, valor);

    private static T? GetField<T>(object alvo, string nome)
        => (T?)ObterCampo(alvo.GetType(), nome).GetValue(alvo);

    private static FieldInfo ObterCampo(Type? tipo, string nome)
    {
        for (Type? t = tipo; t is not null; t = t.BaseType)
        {
            FieldInfo? f = t.GetField(nome, Priv);
            if (f is not null) return f;
        }

        throw new MissingFieldException(tipo?.FullName, nome);
    }

    private static object? Invoke(object alvo, string metodo, params object?[] args)
    {
        for (Type? t = alvo.GetType(); t is not null; t = t.BaseType)
        {
            MethodInfo? m = t.GetMethod(metodo, Priv);
            if (m is not null)
            {
                object? r = m.Invoke(alvo, args);
                if (r is Task tarefa) tarefa.GetAwaiter().GetResult();
                return r;
            }
        }

        throw new MissingMethodException(alvo.GetType().FullName, metodo);
    }

    private static bool ControleEnabled(object form, string campo)
        => GetField<System.Windows.Forms.Control>(form, campo)?.Enabled ?? false;

    // Control.Visible (getter) percorre a cadeia de pais e é sempre false enquanto o Form não é exibido.
    // Para provar a intenção REAL do código produtivo (Visible = true/false que ELE setou), lemos o bit
    // intrínseco de estado via Control.GetState(States.Visible), sem depender da exibição da janela.
    private static readonly Type StatesType =
        typeof(System.Windows.Forms.Control).GetNestedType("States", BindingFlags.NonPublic)!;

    private static readonly MethodInfo GetStateMethod =
        typeof(System.Windows.Forms.Control).GetMethod("GetState", Priv, [StatesType])!;

    private static bool ControleVisible(object form, string campo)
    {
        var c = GetField<System.Windows.Forms.Control>(form, campo);
        if (c is null) return false;
        object visible = Enum.ToObject(StatesType, 0x00000002); // States.Visible
        return (bool)GetStateMethod.Invoke(c, [visible])!;
    }

    private static string StatusText(object form)
        => GetField<System.Windows.Forms.Control>(form, "statusLabel")?.Text ?? string.Empty;

    // ---------------- cenário/fixture ----------------

    private static ContextoApontamentoProcesso ContextoOp1000170()
        => new() { NumeroOrdem = "1000170", Operacao = "0010", Sequencia = "0", TipoProcesso = "CONSUMO_MATERIA_PRIMA" };

    private static List<ComponenteConsumoMaterial> ComponentesOcorrencia()
        => [new()
        {
            CodigoMaterial = "1000186", NumeroReserva = "185", ItemReserva = "1",
            DepositoConsumo = "PP01", Lote = "0000000222", TipoMovimento = "261", PesagemLiberada = true,
            QuantidadePendente = 2271.698m, UnidadeMedida = "KG"
        }];

    private static ConsumoMaterialItem ItemPk8()
        => new()
        {
            CodigoMaterial = "1000186", NumeroReserva = "185", ItemReserva = "1",
            DepositoConsumo = "PP01", Lote = "0000000222", TipoMovimentoSap = "261"
        };

    /// <summary>Injeta no controller da Form um ConsumoMaterialConsultaServico com repositório FAKE (sem banco).</summary>
    private static FakeRepo InjetarResolvedorFake(object form, FakeRepo repo)
    {
        object controller = GetField<object>(form, "_controller")!;
        var consulta = new ConsumoMaterialConsultaServico(() => repo, new ConsumoMaterialServico());
        SetField(controller, "_consultaServico", consulta);
        return repo;
    }

    private static object CriarFormComContexto()
        => new ProcessoConsumoMaterialForm(ModoConsumoMaterial.MateriaPrima, ContextoOp1000170());

    private static void ResolverEReceber(object form, FakeRepo repo)
    {
        InjetarResolvedorFake(form, repo);
        // Executa o CÓDIGO PRODUTIVO real de resolução + aplicação da modalidade (switch/materialização).
        Invoke(form, "ResolverRecuperacaoPersistidaDoContextoAsync", ComponentesOcorrencia());
    }

    // ==================================================================
    // PROVA PRINCIPAL — UmPendente / PK8 sobrevive ao refresh tardio (P01–P08 comportamental)
    // ==================================================================

    [Fact]
    public void UmPendentePk8_RefreshTardioNaoReabilitaFreshFlow()
    {
        RunSta(() =>
        {
            object form = CriarFormComContexto();

            // Save de sessão propositalmente DIVERGENTE, para provar que o envio de recovery NÃO o usa.
            SetField(form, "_ultimoCodigoLancamentoSalvo", 999L);

            var repo = new FakeRepo
            {
                Candidatos = [new() { CodigoLancamento = 8, NumeroOrdem = "1000170", StatusLancamento = "PENDENTE_SAP" }],
                Detalhes = { [8] = new() { Codigo = 8, NumeroOrdem = "1000170", StatusLancamento = "PENDENTE_SAP", Itens = [ItemPk8()] } }
            };

            // (C/D) resolve + materializa via código produtivo real.
            ResolverEReceber(form, repo);

            // Estado imediatamente após materialização.
            Assert.Equal(ModalidadeRecuperacaoConsumo.UmPendente, GetField<ModalidadeRecuperacaoConsumo>(form, "_modalidadeRecuperacao"));
            Assert.Equal(8L, GetField<long?>(form, "_codigoLancamentoRecuperado"));

            // (E) REFRESH TARDIO real: o MESMO método que os eventos SelectionChanged/CurrentCellChanged/RowEnter chamam.
            Invoke(form, "AtualizarComponenteSelecionadoDoGrid");

            // ---- asserts APÓS o refresh (estado REAL dos controles) ----
            Assert.Equal(ModalidadeRecuperacaoConsumo.UmPendente, GetField<ModalidadeRecuperacaoConsumo>(form, "_modalidadeRecuperacao"));
            Assert.Equal(8L, GetField<long?>(form, "_codigoLancamentoRecuperado"));

            Assert.True((bool)Invoke(form, "RecuperacaoBloqueiaNovoConsumo")!);
            Assert.False((bool)Invoke(form, "PodeIniciarLeituraConsumo")!);      // fresh-flow bloqueado

            Assert.False(ControleEnabled(form, "iniciarLeituraButton"));          // INICIAR LEITURA
            Assert.False(ControleEnabled(form, "lerEtiquetaButton"));             // LER PESO
            Assert.False(ControleEnabled(form, "leituraManualButton"));          // DIGITAR PESO
            Assert.False(ControleEnabled(form, "_confirmarConsumoButton"));      // CONFIRMAR CONSUMO

            Assert.True(ControleVisible(form, "_enviarSap261Button"));           // Enviar SAP 261 disponível

            // Status permanece semanticamente recovery/PENDENTE — NÃO fresh-flow.
            string status = StatusText(form);
            Assert.Contains("PENDENTE_SAP", status, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Inicie a leitura", status, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Pronto para leitura", status, StringComparison.OrdinalIgnoreCase);

            // (§8) PK do envio de recovery = 8 (recuperado), nunca o save de sessão (999).
            long? pkEnvio = (long?)Invoke(form, "PkEnvioRecuperadoAtual");
            Assert.Equal(8L, pkEnvio);
            Assert.NotEqual(GetField<long?>(form, "_ultimoCodigoLancamentoSalvo"), pkEnvio);

            // (§9) materialização/refresh READ-ONLY: zero efeitos colaterais no repositório.
            Assert.Equal(0, repo.Saves);
            Assert.Equal(0, repo.Reservas);
            Assert.Equal(0, repo.Confirmacoes);
            Assert.Equal(0, repo.Falhas);
        });
    }

    // ==================================================================
    // CONTRAPONTO — Nenhum preserva fresh-flow (guard não bloqueia globalmente)
    // ==================================================================

    [Fact]
    public void Nenhum_RefreshNaoEhBloqueadoPeloRecovery()
    {
        RunSta(() =>
        {
            object form = CriarFormComContexto();
            var repo = new FakeRepo(); // zero candidatos → Nenhum
            ResolverEReceber(form, repo);

            Assert.Equal(ModalidadeRecuperacaoConsumo.Nenhum, GetField<ModalidadeRecuperacaoConsumo>(form, "_modalidadeRecuperacao"));

            // marca sentinela para detectar o caminho tomado pelo refresh.
            GetField<System.Windows.Forms.Control>(form, "statusLabel")!.Text = "SENTINELA_101W";
            Invoke(form, "AtualizarComponenteSelecionadoDoGrid");

            // O guard de recovery NÃO engata: fresh-flow preservado.
            Assert.False((bool)Invoke(form, "RecuperacaoBloqueiaNovoConsumo")!);
            // Não materializa bloqueio de reconciliação (status não vira mensagem de recovery/bloqueio).
            string status = StatusText(form);
            Assert.DoesNotContain("reconciliação", status, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PENDENTE_SAP", status, StringComparison.OrdinalIgnoreCase);
            // Envio de recovery não acionável em Nenhum.
            Assert.Null((long?)Invoke(form, "PkEnvioRecuperadoAtual"));
        });
    }

    // ==================================================================
    // OUTRAS MODALIDADES — bloqueio sobrevive ao refresh (executado pela Form)
    // ==================================================================

    [Fact]
    public void AmbiguoPendente_RefreshMantemBloqueio()
    {
        RunSta(() =>
        {
            object form = CriarFormComContexto();
            var repo = new FakeRepo
            {
                Candidatos =
                [
                    new() { CodigoLancamento = 8, NumeroOrdem = "1000170", StatusLancamento = "PENDENTE_SAP" },
                    new() { CodigoLancamento = 9, NumeroOrdem = "1000170", StatusLancamento = "PENDENTE_SAP" }
                ],
                Detalhes =
                {
                    [8] = new() { Codigo = 8, NumeroOrdem = "1000170", StatusLancamento = "PENDENTE_SAP", Itens = [ItemPk8()] },
                    [9] = new() { Codigo = 9, NumeroOrdem = "1000170", StatusLancamento = "PENDENTE_SAP", Itens = [ItemPk8()] }
                }
            };
            ResolverEReceber(form, repo);
            Assert.Equal(ModalidadeRecuperacaoConsumo.AmbiguoPendente, GetField<ModalidadeRecuperacaoConsumo>(form, "_modalidadeRecuperacao"));

            Invoke(form, "AtualizarComponenteSelecionadoDoGrid");

            Assert.True((bool)Invoke(form, "RecuperacaoBloqueiaNovoConsumo")!);
            Assert.False(ControleEnabled(form, "iniciarLeituraButton"));
            Assert.False(ControleVisible(form, "_enviarSap261Button"));  // ambíguo NÃO materializa envio
            Assert.Null((long?)Invoke(form, "PkEnvioRecuperadoAtual"));
        });
    }

    [Fact]
    public void EnviandoReconciliacao_RefreshMantemBloqueio()
    {
        RunSta(() =>
        {
            object form = CriarFormComContexto();
            var repo = new FakeRepo
            {
                Candidatos = [new() { CodigoLancamento = 8, NumeroOrdem = "1000170", StatusLancamento = "ENVIANDO_SAP" }],
                Detalhes = { [8] = new() { Codigo = 8, NumeroOrdem = "1000170", StatusLancamento = "ENVIANDO_SAP", Itens = [ItemPk8()] } }
            };
            ResolverEReceber(form, repo);
            Assert.Equal(ModalidadeRecuperacaoConsumo.EnviandoReconciliacao, GetField<ModalidadeRecuperacaoConsumo>(form, "_modalidadeRecuperacao"));

            Invoke(form, "AtualizarComponenteSelecionadoDoGrid");

            Assert.True((bool)Invoke(form, "RecuperacaoBloqueiaNovoConsumo")!);
            Assert.False(ControleEnabled(form, "iniciarLeituraButton"));
            Assert.False(ControleVisible(form, "_enviarSap261Button"));
            Assert.Null((long?)Invoke(form, "PkEnvioRecuperadoAtual"));
        });
    }

    [Fact]
    public void FalhaResolucaoPersistida_RefreshMantemBloqueio()
    {
        RunSta(() =>
        {
            object form = CriarFormComContexto();
            var repo = new FakeRepo { Lancar = true }; // exceção → catch → FalhaResolucaoPersistida
            ResolverEReceber(form, repo);
            Assert.Equal(ModalidadeRecuperacaoConsumo.FalhaResolucaoPersistida, GetField<ModalidadeRecuperacaoConsumo>(form, "_modalidadeRecuperacao"));

            Invoke(form, "AtualizarComponenteSelecionadoDoGrid");

            Assert.True((bool)Invoke(form, "RecuperacaoBloqueiaNovoConsumo")!);
            Assert.False(ControleEnabled(form, "iniciarLeituraButton"));
            Assert.False(ControleVisible(form, "_enviarSap261Button"));
            Assert.Null((long?)Invoke(form, "PkEnvioRecuperadoAtual"));
        });
    }

    [Fact]
    public void ModalidadeDesconhecida_RefreshMantemBloqueio()
    {
        RunSta(() =>
        {
            object form = CriarFormComContexto();
            // valor de enum fora do domínio, injetado diretamente no estado da Form.
            SetField(form, "_modalidadeRecuperacao", (ModalidadeRecuperacaoConsumo)999);
            SetField(form, "_codigoLancamentoRecuperado", null);

            Invoke(form, "AtualizarComponenteSelecionadoDoGrid");

            Assert.True((bool)Invoke(form, "RecuperacaoBloqueiaNovoConsumo")!);
            Assert.False(ControleEnabled(form, "iniciarLeituraButton"));
            Assert.Null((long?)Invoke(form, "PkEnvioRecuperadoAtual"));
        });
    }

    // ---------------- fake repo READ-ONLY (sem banco/SAP) ----------------

    private sealed class FakeRepo : IConsumoMaterialRepositorio
    {
        public IReadOnlyList<ResumoConsumoMaterialLancamento> Candidatos { get; init; } = [];
        public Dictionary<long, ConsumoMaterialLancamento> Detalhes { get; init; } = new();
        public bool Lancar { get; init; }
        public int Saves { get; private set; }
        public int Reservas { get; private set; }
        public int Confirmacoes { get; private set; }
        public int Falhas { get; private set; }

        public Task<long> SalvarConsumoLocalAsync(ConsumoMaterialLancamento lancamento, CancellationToken cancellationToken = default)
        { Saves++; return Task.FromResult(1L); }

        public Task<ConsumoMaterialLancamento?> ObterPorCodigoAsync(long codigoLancamento, CancellationToken cancellationToken = default)
            => Task.FromResult(Detalhes.TryGetValue(codigoLancamento, out ConsumoMaterialLancamento? l) ? l : null);

        public Task<IReadOnlyList<ResumoConsumoMaterialLancamento>> ConsultarLancamentosAsync(ConsultaConsumoMaterialFiltro filtro, CancellationToken cancellationToken = default)
        {
            if (Lancar) throw new InvalidOperationException("falha simulada de consulta");
            return Task.FromResult(Candidatos);
        }

        public Task<ConsumoMaterialLancamento?> ObterDetalheCompletoAsync(long codigoLancamento, CancellationToken cancellationToken = default)
            => Task.FromResult(Detalhes.TryGetValue(codigoLancamento, out ConsumoMaterialLancamento? l) ? l : null);

        public Task<bool> TentarReservarEnvioSapAsync(long codigoLancamento, DateTime reservadoEmUtc, CancellationToken cancellationToken = default)
        { Reservas++; return Task.FromResult(false); }

        public Task MarcarFalhaSapAsync(long codigoLancamento, CancellationToken cancellationToken = default)
        { Falhas++; return Task.CompletedTask; }

        public Task MarcarConsumoConfirmadoSapAsync(long codigoLancamento, string? documentoMaterialSap, string? exercicioDocumentoMaterialSap, DateTime enviadoSapEmUtc, CancellationToken cancellationToken = default)
        { Confirmacoes++; return Task.CompletedTask; }
    }
}
