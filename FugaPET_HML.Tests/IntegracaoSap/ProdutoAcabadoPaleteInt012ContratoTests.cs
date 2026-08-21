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

    [Fact] // §3/§4: HandlingUnitExternalID == "$1" e != CodigoPaleteLocal
    public void HandlingUnitExternalId_EhLiteralDolar1_NaoOCodigoLocal()
    {
        ResultadoPreviewProdutoAcabadoPalete preview = new ProdutoAcabadoPaletePayloadBuilder().MontarPreview(PaleteValido());

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
        ResultadoPreviewProdutoAcabadoPalete preview = new ProdutoAcabadoPaletePayloadBuilder().MontarPreview(PaleteValido());

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

        ResultadoPreviewProdutoAcabadoPalete preview = new ProdutoAcabadoPaletePayloadBuilder().MontarPreview(comPallet01);

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

        ResultadoPreviewProdutoAcabadoPalete preview = new ProdutoAcabadoPaletePayloadBuilder().MontarPreview(semMaterial);
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
