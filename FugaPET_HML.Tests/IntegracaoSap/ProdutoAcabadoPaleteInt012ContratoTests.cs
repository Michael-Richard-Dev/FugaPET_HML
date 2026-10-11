using FugaPET_HML.Modelo.IntegracaoSap;
using FugaPET_HML.Modelo.Processo;
using FugaPET_HML.Servicos.IntegracaoSap;

namespace FugaPET_HML.Tests.IntegracaoSap;

/// <summary>
/// GATE 046-K: contrato INT012 — HandlingUnitExternalID é o literal "$1" (nunca o CodigoPaleteLocal); os itens
/// (_HandlingUnitItem) carregam exclusivamente a HU SAP individual da caixa; e a autenticação CPI usa credencial
/// DEDICADA (fail-closed sem fallback para as credenciais SAP genéricas). Nenhum POST/HTTP é executado aqui.
/// </summary>
public sealed class ProdutoAcabadoPaleteInt012ContratoTests
{
    private const string CodigoLocal = "PLT-1002024-0002-0003";

    private static ProdutoAcabadoPalete PaleteValido() => new()
    {
        CodigoPaleteLocal = CodigoLocal,
        PrimeiraCaixa = 2,
        UltimaCaixa = 3,
        PesoBrutoKg = 20m,
        PesoLiquidoKg = 18m,
        TaraKg = 2m,
        Plant = "3007",
        StorageLocation = "PP01",
        PackagingMaterial = "PACK_TESTE_046K", // valor de teste — nunca PALLET01/4000108; não há POST
        Caixas =
        [
            Caixa(2, "HU-CX-A"),
            Caixa(3, "HU-CX-B"),
        ]
    };

    private static ProdutoAcabadoCaixa Caixa(int numero, string huSap) => new()
    {
        CodigoProdutoAcabadoCaixa = numero,
        NumeroCaixa = numero,
        CodigoCaixaLocal = $"CX-LOCAL-{numero:0000}",
        StatusIntegracao = StatusIntegracaoCaixa.ConfirmadaSap,
        HandlingUnitExternalId = huSap,
        PesoBrutoKg = 10m,
        PesoLiquidoKg = 9m,
        TaraKg = 1m
    };

    /// <summary>GATE 126V: tara fisica JA RESOLVIDA, injetada no builder (que permanece puro).</summary>
    private static ResultadoTaraFisicaEmbalagem TaraFisica(decimal pesoKg)
        => ResultadoTaraFisicaEmbalagem.Resolvida(pesoKg);

    [Fact] // §3/§4: HandlingUnitExternalID == "$1" e != CodigoPaleteLocal
    public void HandlingUnitExternalId_EhLiteralDolar1_NaoOCodigoLocal()
    {
        ResultadoPreviewProdutoAcabadoPalete preview = new ProdutoAcabadoPaletePayloadBuilder().MontarPreview(PaleteValido(), TaraFisica(1m));

        Assert.True(preview.Sucesso, preview.Mensagem);
        Assert.NotNull(preview.Payload);
        Assert.Equal("$1", preview.Payload!.HandlingUnitExternalID);
        Assert.NotEqual(CodigoLocal, preview.Payload.HandlingUnitExternalID);
        Assert.Contains("\"HandlingUnitExternalID\": \"$1\"", preview.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain(CodigoLocal, preview.PayloadJson, StringComparison.Ordinal); // código local NÃO viaja no payload
    }

    [Fact] // §4: _HandlingUnitItem[].HandlingUnit = HU SAP individual da caixa, sem fallback para código local
    public void ItemHandlingUnit_UsaHuSapDaCaixa_SemFallbackLocal()
    {
        ResultadoPreviewProdutoAcabadoPalete preview = new ProdutoAcabadoPaletePayloadBuilder().MontarPreview(PaleteValido(), TaraFisica(1m));

        Assert.True(preview.Sucesso, preview.Mensagem);
        Assert.Equal(["HU-CX-A", "HU-CX-B"], preview.Payload!.HandlingUnitItems.Select(i => i.HandlingUnit));
        Assert.DoesNotContain("CX-LOCAL-0002", preview.PayloadJson, StringComparison.Ordinal);
        Assert.DoesNotContain("CX-LOCAL-0003", preview.PayloadJson, StringComparison.Ordinal);
    }

    [Fact] // GATE 046-K REV2 §9: PALLET01 é o PackagingMaterial oficial do MVP (INT012 v1.00) — ACEITO e serializado.
    public void PackagingMaterial_Pallet01_Aceito_ESerializado()
    {
        ProdutoAcabadoPalete palete = PaleteValido();
        ProdutoAcabadoPalete comPallet01 = new()
        {
            CodigoPaleteLocal = palete.CodigoPaleteLocal,
            PrimeiraCaixa = palete.PrimeiraCaixa,
            UltimaCaixa = palete.UltimaCaixa,
            PesoBrutoKg = palete.PesoBrutoKg,
            PesoLiquidoKg = palete.PesoLiquidoKg,
            TaraKg = palete.TaraKg,
            Plant = palete.Plant,
            StorageLocation = palete.StorageLocation,
            PackagingMaterial = "PALLET01",
            Caixas = palete.Caixas
        };

        ResultadoPreviewProdutoAcabadoPalete preview = new ProdutoAcabadoPaletePayloadBuilder().MontarPreview(comPallet01, TaraFisica(1m));

        Assert.True(preview.Sucesso, preview.Mensagem);
        Assert.Equal("PALLET01", preview.Payload!.PackagingMaterial);
        Assert.Contains("\"PackagingMaterial\": \"PALLET01\"", preview.PayloadJson, StringComparison.Ordinal);
    }

    [Theory] // fail-closed preservado: vazio/branco/null seguem bloqueados
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void PackagingMaterial_AusenteVazioBranco_Bloqueado(string? material)
    {
        ProdutoAcabadoPalete p = PaleteValido();
        ProdutoAcabadoPalete semMaterial = new()
        {
            CodigoPaleteLocal = p.CodigoPaleteLocal,
            PrimeiraCaixa = p.PrimeiraCaixa,
            UltimaCaixa = p.UltimaCaixa,
            PesoBrutoKg = p.PesoBrutoKg,
            PesoLiquidoKg = p.PesoLiquidoKg,
            TaraKg = p.TaraKg,
            Plant = p.Plant,
            StorageLocation = p.StorageLocation,
            PackagingMaterial = material ?? string.Empty,
            Caixas = p.Caixas
        };

        ResultadoPreviewProdutoAcabadoPalete preview = new ProdutoAcabadoPaletePayloadBuilder().MontarPreview(semMaterial, TaraFisica(1m));
        Assert.False(preview.Sucesso);
    }

    [Fact] // §5/§6: credencial CPI DEDICADA ausente ⇒ gateway NÃO autorizado, mesmo com credencial SAP presente (sem fallback)
    public void Gateway_SemCredencialCpiDedicada_FailClosed_SemFallbackSap()
    {
        ConfiguracaoSap config = ConfigCpi(comCredencialCpi: false, writeHabilitado: true);
        IProdutoAcabadoPaleteInt012Gateway gateway = FabricaProdutoAcabadoPaleteInt012Gateway.Criar(config);
        Assert.False(gateway.EnvioAutorizado);
    }

    [Fact] // §6: com credencial CPI dedicada + write + url + host ⇒ autorizado
    public void Gateway_ComCredencialCpiDedicada_Autoriza()
    {
        ConfiguracaoSap config = ConfigCpi(comCredencialCpi: true, writeHabilitado: true);
        IProdutoAcabadoPaleteInt012Gateway gateway = FabricaProdutoAcabadoPaleteInt012Gateway.Criar(config);
        Assert.True(gateway.EnvioAutorizado);
    }

    [Fact] // §9: write desabilitado ⇒ fail-closed mesmo com credencial CPI presente
    public void Gateway_SemWrite_FailClosed()
    {
        ConfiguracaoSap config = ConfigCpi(comCredencialCpi: true, writeHabilitado: false);
        IProdutoAcabadoPaleteInt012Gateway gateway = FabricaProdutoAcabadoPaleteInt012Gateway.Criar(config);
        Assert.False(gateway.EnvioAutorizado);
    }

    // ========================================================================================
    // GATE 126V: composicao da tara FISICA da embalagem do palete EXCLUSIVAMENTE no outbound.
    // Cenario aprovado: PLANT=3007, PACKAGING_MATERIAL=PALLET01, PHYSICAL_TARE=1.000 KG,
    // local 28.800 / 28.700 / 0.100 ⇒ outbound 29.800 / 28.700 / 1.100.
    // ========================================================================================

    private const string MandanteQ = "110";
    private const string CentroQ = "3007";
    private const string MaterialPalete = "PALLET01";

    /// <summary>Palete 1 do piloto: pesos LOCAIS persistidos, sem qualquer tara de embalagem fisica.</summary>
    private static ProdutoAcabadoPalete PaletePiloto1() => new()
    {
        CodigoPaleteLocal = "1",
        PrimeiraCaixa = 1,
        UltimaCaixa = 2,
        PesoBrutoKg = 28.800m,
        PesoLiquidoKg = 28.700m,
        TaraKg = 0.100m,
        Plant = CentroQ,
        StorageLocation = "PA01",
        PackagingMaterial = MaterialPalete,
        Caixas =
        [
            CaixaPiloto(1, "300000100"),
            CaixaPiloto(2, "300000101"),
        ]
    };

    private static ProdutoAcabadoCaixa CaixaPiloto(int numero, string huSap) => new()
    {
        CodigoProdutoAcabadoCaixa = numero,
        NumeroCaixa = numero,
        CodigoCaixaLocal = $"CX-LOCAL-{numero:0000}",
        StatusIntegracao = StatusIntegracaoCaixa.ConfirmadaSap,
        HandlingUnitExternalId = huSap,
        PesoBrutoKg = 14.400m,
        PesoLiquidoKg = 14.350m,
        TaraKg = 0.050m
    };

    /// <summary>Mapeamento APROVADO: mandante 110 + centro 3007 + PALLET01 ⇒ 1.000 KG.</summary>
    private static IReadOnlyList<TaraFisicaEmbalagemConfigurada> MapeamentoAprovado() =>
    [
        new()
        {
            SapClient = MandanteQ,
            Centro = CentroQ,
            MaterialEmbalagem = MaterialPalete,
            PesoKg = 1.000m,
            Unidade = "KG"
        }
    ];

    private static ResultadoTaraFisicaEmbalagem ResolverPiloto(
        IReadOnlyList<TaraFisicaEmbalagemConfigurada>? mapeamentos,
        string mandante = MandanteQ,
        string centro = CentroQ,
        string material = MaterialPalete)
        => ResolvedorTaraFisicaEmbalagemPalete.Resolver(mapeamentos, mandante, centro, material);

    [Fact] // T1 + T12: palete 1 produz 29.800 / 28.700 / 1.100 no payload e no JSON serializado, em KG
    public void T1_Palete1_ProduzPesosCompostos_ComUnidadeKg()
    {
        ProdutoAcabadoPalete palete = PaletePiloto1();

        ResultadoPreviewProdutoAcabadoPalete preview = new ProdutoAcabadoPaletePayloadBuilder()
            .MontarPreview(palete, ResolverPiloto(MapeamentoAprovado()));

        Assert.True(preview.Sucesso, preview.Mensagem);
        Assert.Equal(29.800m, preview.Payload!.GrossWeight);
        Assert.Equal(28.700m, preview.Payload.NetWeight);
        Assert.Equal(1.100m, preview.Payload.TareWeight);
        Assert.Equal("KG", preview.Payload.WeightUnit);
        // T12: os pesos corretos chegam ao corpo serializado que o gateway envia.
        Assert.Contains("\"GrossWeight\": 29.800", preview.PayloadJson, StringComparison.Ordinal);
        Assert.Contains("\"NetWeight\": 28.700", preview.PayloadJson, StringComparison.Ordinal);
        Assert.Contains("\"TareWeight\": 1.100", preview.PayloadJson, StringComparison.Ordinal);
        Assert.Contains("\"WeightUnit\": \"KG\"", preview.PayloadJson, StringComparison.Ordinal);
        // A tara das caixas (0.100) NAO foi perdida: 1.100 = 0.100 + 1.000.
        Assert.Equal(palete.TaraKg + 1.000m, preview.Payload.TareWeight);
    }

    [Fact] // T2 + T11: tara ausente (nenhum mapeamento) bloqueia o outbound ANTES de qualquer payload
    public void T2_TaraAusente_BloqueiaOutbound_SemPayload()
    {
        ResultadoTaraFisicaEmbalagem tara = ResolverPiloto(mapeamentos: []);
        Assert.Equal(CenarioTaraFisicaEmbalagem.NaoConfigurada, tara.Cenario);
        Assert.Null(tara.PesoKg); // ausencia NAO e zero

        ResultadoPreviewProdutoAcabadoPalete preview = new ProdutoAcabadoPaletePayloadBuilder()
            .MontarPreview(PaletePiloto1(), tara);

        Assert.False(preview.Sucesso);
        Assert.Null(preview.Payload);
        // Nenhum fallback silencioso para os pesos locais.
        Assert.DoesNotContain("28.800", preview.PayloadJson, StringComparison.Ordinal);
    }

    [Fact] // T3: tara negativa e INVALIDA e bloqueia o outbound
    public void T3_TaraNegativa_BloqueiaOutbound()
    {
        ResultadoTaraFisicaEmbalagem tara = ResolverPiloto(
        [
            new() { SapClient = MandanteQ, Centro = CentroQ, MaterialEmbalagem = MaterialPalete, PesoKg = -1.000m, Unidade = "KG" }
        ]);

        Assert.Equal(CenarioTaraFisicaEmbalagem.Invalida, tara.Cenario);
        Assert.Null(tara.PesoKg);

        ResultadoPreviewProdutoAcabadoPalete preview = new ProdutoAcabadoPaletePayloadBuilder()
            .MontarPreview(PaletePiloto1(), tara);

        Assert.False(preview.Sucesso);
        Assert.Null(preview.Payload);
    }

    [Fact] // T4: zero EXPLICITO e valido e distinto de desconhecido — e nao substitui o valor de PALLET01/3007
    public void T4_TaraZeroExplicita_EhDistintaDeDesconhecida_ENaoSubstituiOValorAprovado()
    {
        // Zero explicito: cenario Disponivel, com peso materialmente 0m.
        ResultadoTaraFisicaEmbalagem zero = ResolverPiloto(
        [
            new() { SapClient = MandanteQ, Centro = CentroQ, MaterialEmbalagem = "EMB_SEM_PESO", PesoKg = 0m, Unidade = "KG" }
        ], material: "EMB_SEM_PESO");

        Assert.Equal(CenarioTaraFisicaEmbalagem.Disponivel, zero.Cenario);
        Assert.True(zero.Disponivel);
        Assert.Equal(0m, zero.PesoKg);

        // Desconhecida: cenario diferente e peso null — jamais confundidos.
        ResultadoTaraFisicaEmbalagem desconhecida = ResolverPiloto(mapeamentos: []);
        Assert.NotEqual(zero.Cenario, desconhecida.Cenario);
        Assert.False(desconhecida.Disponivel);
        Assert.Null(desconhecida.PesoKg);

        // O zero de OUTRO material nao vale para PALLET01/3007: lá continua valendo 1.000.
        ResultadoTaraFisicaEmbalagem aprovada = ResolverPiloto(MapeamentoAprovado());
        Assert.Equal(1.000m, aprovada.PesoKg);
    }

    [Fact] // T5: NetWeight permanece invariavel — o liquido das caixas nunca e alterado
    public void T5_NetWeight_PermaneceInvariavel()
    {
        ProdutoAcabadoPalete palete = PaletePiloto1();

        foreach (decimal taraFisica in new[] { 0m, 1.000m, 7.500m })
        {
            ResultadoPreviewProdutoAcabadoPalete preview = new ProdutoAcabadoPaletePayloadBuilder()
                .MontarPreview(palete, ResultadoTaraFisicaEmbalagem.Resolvida(taraFisica));

            Assert.True(preview.Sucesso, preview.Mensagem);
            Assert.Equal(palete.PesoLiquidoKg, preview.Payload!.NetWeight);
            Assert.Equal(28.700m, preview.Payload.NetWeight);
        }
    }

    [Fact] // T6: dois previews consecutivos produzem pesos IDENTICOS (composicao exatamente uma vez)
    public void T6_DoisPreviews_ProduzemPesosIdenticos()
    {
        ProdutoAcabadoPalete palete = PaletePiloto1();
        ProdutoAcabadoPaletePayloadBuilder builder = new();

        ResultadoPreviewProdutoAcabadoPalete primeiro = builder.MontarPreview(palete, ResolverPiloto(MapeamentoAprovado()));
        ResultadoPreviewProdutoAcabadoPalete segundo = builder.MontarPreview(palete, ResolverPiloto(MapeamentoAprovado()));

        Assert.True(primeiro.Sucesso, primeiro.Mensagem);
        Assert.True(segundo.Sucesso, segundo.Mensagem);
        Assert.Equal(primeiro.Payload!.GrossWeight, segundo.Payload!.GrossWeight);
        Assert.Equal(primeiro.Payload.NetWeight, segundo.Payload.NetWeight);
        Assert.Equal(primeiro.Payload.TareWeight, segundo.Payload.TareWeight);
        Assert.Equal(29.800m, segundo.Payload.GrossWeight); // nao acumulou para 30.800
        Assert.Equal(1.100m, segundo.Payload.TareWeight);   // nao acumulou para 2.100
    }

    [Fact] // T7: o objeto de dominio recebido NAO e mutado — o rascunho local permanece intacto
    public void T7_PaleteRecebido_NaoEhMutado()
    {
        ProdutoAcabadoPalete palete = PaletePiloto1();

        ResultadoPreviewProdutoAcabadoPalete preview = new ProdutoAcabadoPaletePayloadBuilder()
            .MontarPreview(palete, ResolverPiloto(MapeamentoAprovado()));

        Assert.True(preview.Sucesso, preview.Mensagem);
        Assert.Equal(28.800m, palete.PesoBrutoKg);
        Assert.Equal(28.700m, palete.PesoLiquidoKg);
        Assert.Equal(0.100m, palete.TaraKg);
        // As caixas tambem permanecem intactas.
        Assert.Equal(14.350m, palete.Caixas[0].PesoLiquidoKg);
        Assert.Equal(0.050m, palete.Caixas[0].TaraKg);
    }

    [Fact] // T9: invariantes GROSS = NET + TARE e GROSS > NET > TARE sao verificadas apos a composicao
    public void T9_Invariantes_DePeso_SaoVerificadas()
    {
        // GROSS = NET + TARE no cenario aprovado.
        ResultadoPreviewProdutoAcabadoPalete ok = new ProdutoAcabadoPaletePayloadBuilder()
            .MontarPreview(PaletePiloto1(), ResolverPiloto(MapeamentoAprovado()));
        Assert.True(ok.Sucesso, ok.Mensagem);
        Assert.Equal(ok.Payload!.GrossWeight, ok.Payload.NetWeight + ok.Payload.TareWeight);
        Assert.True(ok.Payload.GrossWeight > ok.Payload.NetWeight);
        Assert.True(ok.Payload.NetWeight > ok.Payload.TareWeight);

        // Tara fisica tao grande que TARE >= NET viola a ordem exigida ⇒ bloqueia.
        ResultadoPreviewProdutoAcabadoPalete violada = new ProdutoAcabadoPaletePayloadBuilder()
            .MontarPreview(PaletePiloto1(), ResultadoTaraFisicaEmbalagem.Resolvida(100.000m));
        Assert.False(violada.Sucesso);
        Assert.Null(violada.Payload);
    }

    [Fact] // T10: mapeamento de OUTRO centro/material/mandante nao autoriza PALLET01/3007
    public void T10_MapeamentoDeOutraChave_NaoAutorizaPallet01_3007()
    {
        // Centro diferente.
        Assert.Equal(CenarioTaraFisicaEmbalagem.NaoConfigurada, ResolverPiloto(
        [
            new() { SapClient = MandanteQ, Centro = "3008", MaterialEmbalagem = MaterialPalete, PesoKg = 1.000m, Unidade = "KG" }
        ]).Cenario);

        // Material diferente.
        Assert.Equal(CenarioTaraFisicaEmbalagem.NaoConfigurada, ResolverPiloto(
        [
            new() { SapClient = MandanteQ, Centro = CentroQ, MaterialEmbalagem = "PALLET02", PesoKg = 1.000m, Unidade = "KG" }
        ]).Cenario);

        // Mandante diferente: o arquivo copiado para outro ambiente NAO autoriza a composicao.
        Assert.Equal(CenarioTaraFisicaEmbalagem.NaoConfigurada, ResolverPiloto(
            MapeamentoAprovado(), mandante: "100").Cenario);

        // Mandante ativo ausente ⇒ fail-closed.
        Assert.Equal(CenarioTaraFisicaEmbalagem.NaoConfigurada, ResolverPiloto(
            MapeamentoAprovado(), mandante: "   ").Cenario);
    }

    [Fact] // T11: configuracao invalida ou ambigua bloqueia ANTES de qualquer POST
    public void T11_ConfiguracaoInvalidaOuAmbigua_Bloqueia()
    {
        // Unidade diferente de KG ⇒ INVALIDA (nunca convertida).
        Assert.Equal(CenarioTaraFisicaEmbalagem.Invalida, ResolverPiloto(
        [
            new() { SapClient = MandanteQ, Centro = CentroQ, MaterialEmbalagem = MaterialPalete, PesoKg = 1000m, Unidade = "G" }
        ]).Cenario);

        // Unidade ausente ⇒ INVALIDA.
        Assert.Equal(CenarioTaraFisicaEmbalagem.Invalida, ResolverPiloto(
        [
            new() { SapClient = MandanteQ, Centro = CentroQ, MaterialEmbalagem = MaterialPalete, PesoKg = 1.000m, Unidade = string.Empty }
        ]).Cenario);

        // Precisao acima de 3 casas ⇒ INVALIDA.
        Assert.Equal(CenarioTaraFisicaEmbalagem.Invalida, ResolverPiloto(
        [
            new() { SapClient = MandanteQ, Centro = CentroQ, MaterialEmbalagem = MaterialPalete, PesoKg = 1.00049m, Unidade = "KG" }
        ]).Cenario);

        // Duas entradas para a MESMA chave logica ⇒ AMBIGUA, nunca resolvida por escolha.
        Assert.Equal(CenarioTaraFisicaEmbalagem.Ambigua, ResolverPiloto(
        [
            new() { SapClient = MandanteQ, Centro = CentroQ, MaterialEmbalagem = MaterialPalete, PesoKg = 1.000m, Unidade = "KG" },
            new() { SapClient = MandanteQ, Centro = CentroQ, MaterialEmbalagem = MaterialPalete, PesoKg = 1.500m, Unidade = "KG" }
        ]).Cenario);

        // Todos os cenarios acima resultam em bloqueio no builder, sem payload.
        foreach (ResultadoTaraFisicaEmbalagem tara in new[]
        {
            ResultadoTaraFisicaEmbalagem.Invalida("teste"),
            ResultadoTaraFisicaEmbalagem.Ambigua("teste"),
            ResultadoTaraFisicaEmbalagem.NaoConfigurada("teste")
        })
        {
            ResultadoPreviewProdutoAcabadoPalete preview = new ProdutoAcabadoPaletePayloadBuilder()
                .MontarPreview(PaletePiloto1(), tara);
            Assert.False(preview.Sucesso);
            Assert.Null(preview.Payload);
        }
    }

    [Fact] // 126V: match tolerante a espacos e caixa, sem afrouxar a chave logica
    public void Resolucao_ToleraEspacosECaixa_SemAfrouxarChave()
    {
        ResultadoTaraFisicaEmbalagem tara = ResolvedorTaraFisicaEmbalagemPalete.Resolver(
        [
            new() { SapClient = " 110 ", Centro = " 3007 ", MaterialEmbalagem = " pallet01 ", PesoKg = 1.000m, Unidade = " kg " }
        ], " 110 ", " 3007 ", " PALLET01 ");

        Assert.True(tara.Disponivel);
        Assert.Equal(1.000m, tara.PesoKg);
    }

    private static ConfiguracaoSap ConfigCpi(bool comCredencialCpi, bool writeHabilitado) => new()
    {
        // Credenciais SAP genéricas PREENCHIDAS de propósito, para provar que NÃO há fallback para elas.
        Usuario = "sap-generico-fake",
        Senha = "sap-generico-fake",
        PalletWriteHabilitado = writeHabilitado,
        PalletInt012BaseUrl = "https://cpi.exemplo.test",
        PalletInt012HostsPermitidos = ["cpi.exemplo.test"],
        PalletInt012Usuario = comCredencialCpi ? "cpi-usuario-fake" : string.Empty,
        PalletInt012Senha = comCredencialCpi ? "cpi-senha-fake" : string.Empty,
        TimeoutSegundos = 30
    };
}
