using System.Text.Encodings.Web;
using System.Text.Json;
using FugaPET_HML.Modelo.IntegracaoSap;
using FugaPET_HML.Modelo.Processo;

namespace FugaPET_HML.Servicos.IntegracaoSap;

public sealed class ProdutoAcabadoPaletePayloadBuilder
{
    public ResultadoPreviewProdutoAcabadoPalete MontarPreview(ProdutoAcabadoPalete palete)
    {
        ArgumentNullException.ThrowIfNull(palete);

        if (palete.PrimeiraCaixa > palete.UltimaCaixa)
        {
            return ResultadoPreviewProdutoAcabadoPalete.Falha("Intervalo de caixas invÃ¡lido.");
        }

        if (palete.Caixas.Count == 0)
        {
            return ResultadoPreviewProdutoAcabadoPalete.Falha("Nenhuma caixa informada para o palete.");
        }

        if (palete.PesoBrutoKg <= palete.PesoLiquidoKg || palete.PesoLiquidoKg <= 0m || palete.TaraKg < 0m)
        {
            return ResultadoPreviewProdutoAcabadoPalete.Falha("Pesos do palete invÃ¡lidos.");
        }

        // Â§12/Â§19: PackagingMaterial real Ã© obrigatÃ³rio (nunca PALLET01 hardcoded). Ausente â‡’ DEPENDENCIA_ARES / fail-closed.
        if (string.IsNullOrWhiteSpace(palete.PackagingMaterial))
        {
            return ResultadoPreviewProdutoAcabadoPalete.Falha(
                "Material de embalagem do palete nÃ£o informado (DEPENDENCIA_ARES: PackagingMaterial real).");
        }

        // Â§10/Â§11/Â§18: SOMENTE caixa CONFIRMADA_SAP entra no palete.
        if (palete.Caixas.Any(caixa => caixa.StatusIntegracao != StatusIntegracaoCaixa.ConfirmadaSap))
        {
            return ResultadoPreviewProdutoAcabadoPalete.Falha(
                "Todas as caixas do palete precisam estar CONFIRMADA_SAP.");
        }

        // Â§10 (problema 2): HU SAP individual OBRIGATÃ“RIA. SEM fallback para CodigoCaixaLocal â€” HU ausente â‡’ fail-closed.
        if (palete.Caixas.Any(caixa => string.IsNullOrWhiteSpace(caixa.HandlingUnitExternalId)))
        {
            return ResultadoPreviewProdutoAcabadoPalete.Falha(
                "Caixa sem HU SAP individual: palete bloqueado (o payload sÃ³ aceita HUs SAP reais).");
        }

        ProdutoAcabadoPaleteRequest payload = new()
        {
            HandlingUnitExternalID = "$1", // GATE 046-K: contrato INT012 exige literal "$1"; CodigoPaleteLocal NAO vai neste campo
            GrossWeight = palete.PesoBrutoKg,
            NetWeight = palete.PesoLiquidoKg,
            TareWeight = palete.TaraKg,
            WeightUnit = "KG",
            Plant = palete.Plant,
            StorageLocation = palete.StorageLocation,
            PackagingMaterial = palete.PackagingMaterial,
            // Â§18/Â§27: _HandlingUnitItem contÃ©m EXATAMENTE as HUs SAP das caixas (HandlingUnitExternalId), sem fallback.
            HandlingUnitItems = palete.Caixas
                .Select(caixa => new ProdutoAcabadoPaleteItemRequest { HandlingUnit = caixa.HandlingUnitExternalId! })
                .ToArray()
        };

        string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        return ResultadoPreviewProdutoAcabadoPalete.Ok(payload, json);
    }
}

