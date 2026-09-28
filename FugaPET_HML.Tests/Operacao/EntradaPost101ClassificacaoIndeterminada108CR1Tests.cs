using System.Net;
using FugaPET_HML.Controle.Processo;
using FugaPET_HML.Modelo.IntegracaoSap;
using FugaPET_HML.Servicos.IntegracaoSap;

namespace FugaPET_HML.Tests.Operacao;

/// <summary>
/// GATE 108C-R1 — classificação FAIL-CLOSED do resultado do POST 101.
///
/// Defeito (Hermes 108D): o client captura HttpRequestException / timeout não provocado pelo caller /
/// IOException do POST em <c>FalhaRede(EtapaPost, ex)</c> e devolve Sucesso=false com StatusHttp=null.
/// Essas falhas NUNCA chegam ao catch de exceção do fluxo, então caíam na regra genérica
/// `Sucesso=false ⇒ ERRO_SAP` — apesar de o POST poder ter sido entregue e o documento criado.
///
/// Sem banco e sem SAP: o classificador é puro e o contrato do client é exercitado com handler falso.
/// </summary>
public sealed class EntradaPost101ClassificacaoIndeterminada108CR1Tests
{
    private static ResultadoMaterialDocumentSap FalhaNaEtapa(string etapa, int? statusHttp)
        => new()
        {
            Sucesso = false,
            Etapa = etapa,
            StatusHttp = statusHttp,
            MensagemSanitizada = $"Etapa {etapa}: falha."
        };

    // ==================================================================
    // §3 / §8 — matriz de classificação pós-POST
    // ==================================================================

    [Fact] // (1) A causa real do incidente: rede/TLS/timeout no POST ⇒ sem resposta HTTP.
    public void PostSemRespostaHttp_EhIndeterminado()
        => Assert.True(EntradaProdutoController.EhResultadoIndeterminadoAposPost(
            FalhaNaEtapa(MaterialDocumentSapApiClient.EtapaPost, null)));

    [Theory] // (2)(3)(4) 408 e 5xx: o SAP pode ter processado.
    [InlineData(408)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public void PostComStatusIndeterminado_EhIndeterminado(int status)
        => Assert.True(EntradaProdutoController.EhResultadoIndeterminadoAposPost(
            FalhaNaEtapa(MaterialDocumentSapApiClient.EtapaPost, status)));

    [Theory] // (5)(6) 4xx exceto 408: recusa comprovada, nenhum documento criado ⇒ ERRO_SAP permitido.
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(422)]
    public void PostCom4xxDeterministico_EhRejeicaoDefinitiva(int status)
        => Assert.False(EntradaProdutoController.EhResultadoIndeterminadoAposPost(
            FalhaNaEtapa(MaterialDocumentSapApiClient.EtapaPost, status)));

    [Fact] // 408 é 4xx mas NÃO é determinístico: a ordem das regras importa.
    public void Post408_NaoEhTratadoComoRejeicaoDefinitiva()
    {
        Assert.True(EntradaProdutoController.EhResultadoIndeterminadoAposPost(
            FalhaNaEtapa(MaterialDocumentSapApiClient.EtapaPost, 408)));
        Assert.False(EntradaProdutoController.EhResultadoIndeterminadoAposPost(
            FalhaNaEtapa(MaterialDocumentSapApiClient.EtapaPost, 409)));
    }

    [Theory] // (7) §6: falhas comprovadamente ANTERIORES ao dispatch não postaram nada.
    [InlineData(MaterialDocumentSapApiClient.EtapaCsrfFetch, null)]
    [InlineData(MaterialDocumentSapApiClient.EtapaCsrfFetch, 500)]
    [InlineData(MaterialDocumentSapApiClient.EtapaCsrfFetch, 403)]
    [InlineData(MaterialDocumentSapApiClient.EtapaValidacaoUrl, null)]
    [InlineData(MaterialDocumentSapApiClient.EtapaMontagemRequisicao, null)]
    [InlineData(MaterialDocumentSapApiClient.EtapaAuthHeader, null)]
    [InlineData(MaterialDocumentSapApiClient.EtapaClienteConstrucao, null)]
    public void FalhaAntesDoPost_NaoEhIndeterminadaPorEfeitoSap(string etapa, int? status)
        => Assert.False(EntradaProdutoController.EhResultadoIndeterminadoAposPost(
            FalhaNaEtapa(etapa, status)));

    [Theory] // (8) 2xx sem MaterialDocument/Year (etapa PARSE): regra preexistente preservada.
    [InlineData(200)]
    [InlineData(201)]
    [InlineData(204)]
    public void Parse2xxSemDocumento_ContinuaIndeterminado(int status)
        => Assert.True(EntradaProdutoController.EhResultadoIndeterminadoAposPost(
            FalhaNaEtapa(MaterialDocumentSapApiClient.EtapaParse, status)));

    [Fact] // Sucesso nunca é indeterminado.
    public void Sucesso_NaoEhIndeterminado()
        => Assert.False(EntradaProdutoController.EhResultadoIndeterminadoAposPost(new ResultadoMaterialDocumentSap
        {
            Sucesso = true,
            Etapa = MaterialDocumentSapApiClient.EtapaParse,
            StatusHttp = 201,
            MaterialDocument = "5000123",
            MaterialDocumentYear = "2026"
        }));

    [Fact] // Fail-closed: status fora das faixas provadas (ex.: 3xx) não é recusa comprovada.
    public void PostComStatusNaoProvadoComoRecusa_EhIndeterminado()
    {
        Assert.True(EntradaProdutoController.EhResultadoIndeterminadoAposPost(
            FalhaNaEtapa(MaterialDocumentSapApiClient.EtapaPost, 302)));
        Assert.True(EntradaProdutoController.EhResultadoIndeterminadoAposPost(
            FalhaNaEtapa(MaterialDocumentSapApiClient.EtapaPost, 100)));
    }

    // ==================================================================
    // §9 — CONTRATO REAL DO CLIENT (o defeito exato do Hermes)
    // ==================================================================

    private sealed class PostFalhaHandler(Exception excecaoNoPost) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post)
            {
                throw excecaoNoPost;
            }

            // CSRF Fetch (GET) bem-sucedido: garante que a falha ocorre DEPOIS do dispatch do POST.
            HttpResponseMessage fetch = new(HttpStatusCode.OK);
            fetch.Headers.TryAddWithoutValidation("X-CSRF-Token", "token-ok");
            return Task.FromResult(fetch);
        }
    }

    private static MaterialDocumentSapRequest Requisicao()
        => new()
        {
            GoodsMovementCode = "01",
            PostingDate = new DateTime(2024, 1, 1),
            DocumentDate = new DateTime(2024, 1, 1),
            MaterialDocumentHeaderText = "Entrada Pedido 4500000216 via FugaPET",
            Itens =
            [
                new MaterialDocumentSapItemRequest
                {
                    Material = "1000205",
                    Plant = "3007",
                    StorageLocation = "PP01",
                    GoodsMovementType = "101",
                    GoodsMovementRefDocType = "B",
                    QuantityInEntryUnit = "5.500",
                    EntryUnit = "KG",
                    PurchaseOrder = "4500000216",
                    PurchaseOrderItem = "00010"
                }
            ]
        };

    private static ConfiguracaoSap Configuracao()
        => new()
        {
            BaseUrl = "https://sap.exemplo.local/odata",
            MaterialDocumentBaseUrl = "https://sap.exemplo.local/sap/opu/odata/sap/API_MATERIAL_DOCUMENT_SRV/",
            Usuario = "usuario-teste",
            Senha = "senha-teste",
            SapClient = "110",
            HostsPermitidos = ["sap.exemplo.local"],
            EscritaHabilitada = true
        };

    public static TheoryData<Exception> FalhasPosDispatch() =>
    [
        new HttpRequestException("conexao encerrada"),
        new IOException("stream interrompido"),
        new TaskCanceledException("timeout do HttpClient", new TimeoutException())
    ];

    [Theory]
    [MemberData(nameof(FalhasPosDispatch))]
    public async Task ClientReal_FalhaDeRedeNoPost_ProduzEtapaPostComStatusNullEEhClassificadaIndeterminada(
        Exception falha)
    {
        using HttpClient http = new(new PostFalhaHandler(falha));
        MaterialDocumentSapApiClient cliente = new(Configuracao(), http);

        ResultadoMaterialDocumentSap resultado = await cliente.CriarDocumentoMaterialAsync(Requisicao());

        // Contrato real do client (é exatamente por isso que o catch do controller não é alcançado).
        Assert.False(resultado.Sucesso);
        Assert.Equal(MaterialDocumentSapApiClient.EtapaPost, resultado.Etapa);
        Assert.Null(resultado.StatusHttp);

        // E o novo classificador cobre esse resultado real.
        Assert.True(EntradaProdutoController.EhResultadoIndeterminadoAposPost(resultado));
    }

    [Theory] // O client devolve o status HTTP real do POST; a classificação deriva dele.
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.UnprocessableContent, false)]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, true)]
    public async Task ClientReal_StatusDoPost_ClassificadoConformeMatriz(HttpStatusCode status, bool indeterminado)
    {
        HttpResponseMessage fetch = new(HttpStatusCode.OK);
        fetch.Headers.TryAddWithoutValidation("X-CSRF-Token", "token-ok");
        using HttpClient http = new(new RespostasHandler(fetch, new HttpResponseMessage(status)));
        MaterialDocumentSapApiClient cliente = new(Configuracao(), http);

        ResultadoMaterialDocumentSap resultado = await cliente.CriarDocumentoMaterialAsync(Requisicao());

        Assert.False(resultado.Sucesso);
        Assert.Equal(MaterialDocumentSapApiClient.EtapaPost, resultado.Etapa);
        Assert.Equal((int)status, resultado.StatusHttp);
        Assert.Equal(
            indeterminado, EntradaProdutoController.EhResultadoIndeterminadoAposPost(resultado));
    }

    private sealed class RespostasHandler(params HttpResponseMessage[] respostas) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _fila = new(respostas);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_fila.Count > 0 ? _fila.Dequeue() : new HttpResponseMessage(HttpStatusCode.OK));
    }

    // ==================================================================
    // §4 — consequências fail-closed no fluxo (contrato de source)
    // ==================================================================

    private static string FonteController()
    {
        string raiz = AppContext.BaseDirectory;
        for (int i = 0; i < 9 && raiz is not null; i++)
        {
            foreach (string cand in new[]
            {
                Path.Combine(raiz, "Controle", "Processo", "EntradaProdutoController.cs"),
                Path.Combine(raiz, "FugaPet_HML", "Controle", "Processo", "EntradaProdutoController.cs")
            })
            {
                if (File.Exists(cand)) return File.ReadAllText(cand);
            }
            raiz = Directory.GetParent(raiz)?.FullName!;
        }

        throw new FileNotFoundException("EntradaProdutoController.cs");
    }

    private static string SemComentarios(string trecho)
        => string.Join(
            '\n',
            trecho.Split('\n').Select(l =>
            {
                int c = l.IndexOf("//", StringComparison.Ordinal);
                return c >= 0 ? l[..c] : l;
            }));

    /// <summary>Bloco que trata o resultado indeterminado, do if do classificador até seu fechamento.</summary>
    private static string BlocoIndeterminado()
    {
        string src = FonteController();
        int inicio = src.IndexOf("if (EhResultadoIndeterminadoAposPost(resultadoSap))", StringComparison.Ordinal);
        Assert.True(inicio > 0, "guarda do classificador não encontrada no fluxo de envio");
        int fim = src.IndexOf("List<ResultadoItemEnvioSap> resultados", inicio, StringComparison.Ordinal);
        Assert.True(fim > inicio, "fim do bloco não encontrado");
        return src.Substring(inicio, fim - inicio);
    }

    [Fact] // O classificador é consultado ANTES de montar resultados/atualizar status.
    public void Fluxo_ClassificaAntesDeAtualizarStatus()
    {
        string src = SemComentarios(FonteController());
        int guarda = src.IndexOf("if (EhResultadoIndeterminadoAposPost(resultadoSap))", StringComparison.Ordinal);
        int atualizacao = src.IndexOf("await _atualizarStatusAposEnvioSap(", StringComparison.Ordinal);

        Assert.True(guarda > 0 && atualizacao > guarda, "classificação deve preceder a atualização de status");
    }

    [Fact] // Indeterminado: nunca atualiza status, nunca grava ERRO_SAP, retém ENVIADO_SAP.
    public void Indeterminado_NaoAtualizaStatusENaoGravaErroSap()
    {
        string codigo = SemComentarios(BlocoIndeterminado());

        Assert.DoesNotContain("_atualizarStatusAposEnvioSap", codigo, StringComparison.Ordinal);
        Assert.DoesNotContain("ERRO_SAP", codigo, StringComparison.Ordinal);
        Assert.DoesNotContain("CenarioEnvioSapEntrada.Falha,", codigo, StringComparison.Ordinal);
        Assert.Contains("return new ResultadoEnvioSapEntrada", codigo, StringComparison.Ordinal);
        Assert.Contains("CenarioEnvioSapEntrada.FalhaPersistenciaLocal", codigo, StringComparison.Ordinal);
        Assert.Contains("StatusLocalAtualizado = false", codigo, StringComparison.Ordinal);
    }

    [Fact] // Indeterminado: reconciliação armada, sem retry cego.
    public void Indeterminado_ArmaReconciliacao()
    {
        string codigo = SemComentarios(BlocoIndeterminado());

        Assert.Contains("_capability.MarcarReconciliacao()", codigo, StringComparison.Ordinal);
        Assert.Contains("RegistrarFalhaStatusLocalAposSapAsync", codigo, StringComparison.Ordinal);
        Assert.DoesNotContain("MarcarRearm", codigo, StringComparison.Ordinal);
    }

    [Fact] // Sem duplicação: um único caminho indeterminado (o 2xx-sem-documento foi absorvido).
    public void Indeterminado_CaminhoUnicoSemDuplicacao()
    {
        string codigo = SemComentarios(FonteController());

        Assert.Equal(1, Contagem(codigo, "if (EhResultadoIndeterminadoAposPost(resultadoSap))"));
        Assert.DoesNotContain("MaterialDocumentSapApiClient.EtapaParse, StringComparison.Ordinal)\n            && resultadoSap.StatusHttp", codigo, StringComparison.Ordinal);
    }

    private static int Contagem(string texto, string agulha)
    {
        int total = 0;
        for (int i = texto.IndexOf(agulha, StringComparison.Ordinal); i >= 0;
             i = texto.IndexOf(agulha, i + agulha.Length, StringComparison.Ordinal))
        {
            total++;
        }

        return total;
    }

    // ==================================================================
    // §8 (9)(10) — o 108C continua válido
    // ==================================================================

    private static string FonteRepositorioExclusao()
    {
        string raiz = AppContext.BaseDirectory;
        for (int i = 0; i < 9 && raiz is not null; i++)
        {
            foreach (string cand in new[]
            {
                Path.Combine(raiz, "AcessoDados", "Repositorio", "ExclusaoPesagemRepositorio.cs"),
                Path.Combine(raiz, "FugaPet_HML", "AcessoDados", "Repositorio", "ExclusaoPesagemRepositorio.cs")
            })
            {
                if (File.Exists(cand)) return File.ReadAllText(cand);
            }
            raiz = Directory.GetParent(raiz)?.FullName!;
        }

        throw new FileNotFoundException("ExclusaoPesagemRepositorio.cs");
    }

    [Fact] // (9) ERRO_SAP sem evidência SAP continua elegível; (10) ENVIADO_SAP continua bloqueado.
    public void Exclusao108C_PermaneceIntacta()
    {
        string src = FonteRepositorioExclusao();

        Assert.Contains("status_lancamento IN ('FINALIZADO_LOCAL', 'ERRO_SAP')", src, StringComparison.Ordinal);
        Assert.Contains("documento_material_sap IS NULL", src, StringComparison.Ordinal);
        Assert.Contains("exercicio_documento_material_sap IS NULL", src, StringComparison.Ordinal);
        Assert.Contains("enviado_sap_em IS NULL", src, StringComparison.Ordinal);

        // ENVIADO_SAP nunca entra no IN de elegibilidade ⇒ indeterminado nunca libera exclusão.
        Assert.DoesNotContain("'ENVIADO_SAP'", src, StringComparison.Ordinal);
        Assert.DoesNotContain("'CONFIRMADO_SAP'", src, StringComparison.Ordinal);
    }
}
