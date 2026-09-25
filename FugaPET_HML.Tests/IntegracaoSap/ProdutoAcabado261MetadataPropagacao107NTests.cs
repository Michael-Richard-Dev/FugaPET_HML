using FugaPET_HML.Controle.Processo;
using FugaPET_HML.Modelo.IntegracaoSap;
using FugaPET_HML.Modelo.Processo;
using FugaPET_HML.Servicos.IntegracaoSap;
using FugaPET_HML.Servicos.Operacao;
using FugaPET_HML.Tela.Processo;

namespace FugaPET_HML.Tests.IntegracaoSap;

/// <summary>
/// GATE 107N — propagação FAIL-CLOSED da metadata SAP do componente até
/// <see cref="ProdutoAcabadoComponenteOrigem"/>, para uso do FUTURO allocator 261.
///
/// Invariante central: campo decisório ausente/nulo/inválido ⇒ null (DESCONHECIDO), NUNCA false/0.
/// Nada de fórmula, rateio ou acumulado é introduzido; o payload 261 permanece intocado.
/// Sem banco, sem SAP (mapeamento puro a partir de corpos JSON/XML literais).
/// </summary>
public sealed class ProdutoAcabado261MetadataPropagacao107NTests
{
    // ------------------------------------------------------------------
    // Corpos SAP sintéticos (A_ProductionOrder_2 com $expand de componente)
    // ------------------------------------------------------------------

    /// <summary>JSON com TODOS os 13 campos presentes e válidos.</summary>
    private const string JsonCompleto = """
    {
      "d": {
        "ManufacturingOrder": "1000173",
        "Material": "3500024",
        "ProductionPlant": "3007",
        "TotalQuantity": "100.000",
        "ProductionUnit": "KG",
        "to_ProductionOrderComponent": {
          "results": [
            {
              "ManufacturingOrder": "1000173",
              "Reservation": "185",
              "ReservationItem": "1",
              "Material": "1000186",
              "Plant": "3007",
              "StorageLocation": "PP01",
              "RequiredQuantity": "50.000",
              "BaseUnit": "KG",
              "WithdrawnQuantity": "12.500",
              "ConfirmedAvailableQuantity": "37.500",
              "GoodsMovementType": "261",
              "Batch": "L001",
              "BOMItem": "0010",
              "BOMItemCategory": "L",
              "BatchSplitType": "1",
              "ReservationIsFinallyIssued": "X",
              "IsBulkMaterialComponent": "X",
              "MatlCompIsMarkedForBackflush": "X",
              "QuantityIsFixed": "X",
              "IsNetScrap": "X",
              "ComponentScrapInPercent": "2.50",
              "OperationScrapInPercent": "1.25",
              "MaterialCompOriginalQuantity": "48.000"
            }
          ]
        }
      }
    }
    """;

    /// <summary>JSON com os campos decisórios AUSENTES (devem virar DESCONHECIDO).</summary>
    private const string JsonAusente = """
    {
      "d": {
        "ManufacturingOrder": "1000173",
        "to_ProductionOrderComponent": {
          "results": [
            {
              "Material": "1000186",
              "Plant": "3007",
              "StorageLocation": "PP01",
              "RequiredQuantity": "50.000",
              "BaseUnit": "KG",
              "GoodsMovementType": "261"
            }
          ]
        }
      }
    }
    """;

    /// <summary>JSON com nulos explícitos e decimais não numéricos (DESCONHECIDO, não 0/false).</summary>
    private const string JsonNuloEInvalido = """
    {
      "d": {
        "ManufacturingOrder": "1000173",
        "to_ProductionOrderComponent": {
          "results": [
            {
              "Material": "1000186",
              "BaseUnit": "KG",
              "QuantityIsFixed": null,
              "IsNetScrap": null,
              "ComponentScrapInPercent": null,
              "OperationScrapInPercent": "nao-numerico",
              "MaterialCompOriginalQuantity": "",
              "WithdrawnQuantity": "abc",
              "ConfirmedAvailableQuantity": null,
              "ReservationIsFinallyIssued": null,
              "MatlCompIsMarkedForBackflush": "?",
              "IsBulkMaterialComponent": null
            }
          ]
        }
      }
    }
    """;

    /// <summary>JSON com flags FALSE EXPLÍCITO (booleano JSON, "false", "0").</summary>
    private const string JsonFalseExplicito = """
    {
      "d": {
        "ManufacturingOrder": "1000173",
        "to_ProductionOrderComponent": {
          "results": [
            {
              "Material": "1000186",
              "BaseUnit": "KG",
              "QuantityIsFixed": false,
              "IsNetScrap": false,
              "ReservationIsFinallyIssued": "false",
              "MatlCompIsMarkedForBackflush": "0",
              "IsBulkMaterialComponent": "FALSE"
            }
          ]
        }
      }
    }
    """;

    /// <summary>
    /// GATE 107N-R1: string VAZIA e WHITESPACE ⇒ DESCONHECIDO (antes eram tratados como FALSE).
    /// Ausência de conteúdo é ausência de informação, não negação.
    /// </summary>
    private const string JsonVazioEWhitespace = """
    {
      "d": {
        "ManufacturingOrder": "1000173",
        "to_ProductionOrderComponent": {
          "results": [
            {
              "Material": "1000186",
              "BaseUnit": "KG",
              "QuantityIsFixed": "",
              "IsNetScrap": "   ",
              "ReservationIsFinallyIssued": "",
              "MatlCompIsMarkedForBackflush": "\t",
              "IsBulkMaterialComponent": " "
            }
          ]
        }
      }
    }
    """;

    private const string XmlCompleto = """
    <entry xmlns="http://www.w3.org/2005/Atom"
           xmlns:m="http://schemas.microsoft.com/ado/2007/08/dataservices/metadata"
           xmlns:d="http://schemas.microsoft.com/ado/2007/08/dataservices">
      <content><m:properties>
        <d:ManufacturingOrder>1000173</d:ManufacturingOrder>
        <d:TotalQuantity>100.000</d:TotalQuantity>
        <d:ProductionUnit>KG</d:ProductionUnit>
      </m:properties></content>
      <link title="to_ProductionOrderComponent">
        <m:inline><feed>
          <entry><content><m:properties>
            <d:Material>1000186</d:Material>
            <d:BaseUnit>KG</d:BaseUnit>
            <d:RequiredQuantity>50.000</d:RequiredQuantity>
            <d:WithdrawnQuantity>12.500</d:WithdrawnQuantity>
            <d:ConfirmedAvailableQuantity>37.500</d:ConfirmedAvailableQuantity>
            <d:BOMItem>0010</d:BOMItem>
            <d:BOMItemCategory>L</d:BOMItemCategory>
            <d:BatchSplitType>1</d:BatchSplitType>
            <d:ReservationIsFinallyIssued>X</d:ReservationIsFinallyIssued>
            <d:IsBulkMaterialComponent>X</d:IsBulkMaterialComponent>
            <d:MatlCompIsMarkedForBackflush>X</d:MatlCompIsMarkedForBackflush>
            <d:QuantityIsFixed>X</d:QuantityIsFixed>
            <d:IsNetScrap>X</d:IsNetScrap>
            <d:ComponentScrapInPercent>2.50</d:ComponentScrapInPercent>
            <d:OperationScrapInPercent>1.25</d:OperationScrapInPercent>
            <d:MaterialCompOriginalQuantity>48.000</d:MaterialCompOriginalQuantity>
          </m:properties></content></entry>
        </feed></m:inline>
      </link>
    </entry>
    """;

    /// <summary>XML com FALSE explícito ("false"/"0") e com VAZIO/WHITESPACE (⇒ DESCONHECIDO em 107N-R1).</summary>
    private const string XmlFalseVazioEWhitespace = """
    <entry xmlns="http://www.w3.org/2005/Atom"
           xmlns:m="http://schemas.microsoft.com/ado/2007/08/dataservices/metadata"
           xmlns:d="http://schemas.microsoft.com/ado/2007/08/dataservices">
      <content><m:properties>
        <d:ManufacturingOrder>1000173</d:ManufacturingOrder>
      </m:properties></content>
      <link title="to_ProductionOrderComponent">
        <m:inline><feed>
          <entry><content><m:properties>
            <d:Material>1000186</d:Material>
            <d:BaseUnit>KG</d:BaseUnit>
            <d:ReservationIsFinallyIssued>false</d:ReservationIsFinallyIssued>
            <d:MatlCompIsMarkedForBackflush>0</d:MatlCompIsMarkedForBackflush>
            <d:QuantityIsFixed></d:QuantityIsFixed>
            <d:IsNetScrap>   </d:IsNetScrap>
            <d:IsBulkMaterialComponent> </d:IsBulkMaterialComponent>
          </m:properties></content></entry>
        </feed></m:inline>
      </link>
    </entry>
    """;

    private const string XmlAusenteEInvalido = """
    <entry xmlns="http://www.w3.org/2005/Atom"
           xmlns:m="http://schemas.microsoft.com/ado/2007/08/dataservices/metadata"
           xmlns:d="http://schemas.microsoft.com/ado/2007/08/dataservices">
      <content><m:properties>
        <d:ManufacturingOrder>1000173</d:ManufacturingOrder>
      </m:properties></content>
      <link title="to_ProductionOrderComponent">
        <m:inline><feed>
          <entry><content><m:properties>
            <d:Material>1000186</d:Material>
            <d:BaseUnit>KG</d:BaseUnit>
            <d:OperationScrapInPercent>nao-numerico</d:OperationScrapInPercent>
            <d:MaterialCompOriginalQuantity></d:MaterialCompOriginalQuantity>
            <d:MatlCompIsMarkedForBackflush>?</d:MatlCompIsMarkedForBackflush>
          </m:properties></content></entry>
        </feed></m:inline>
      </link>
    </entry>
    """;

    private static MetadataAlocacao261Sap MetadataDe(string corpo)
    {
        OrdemProducaoSap? ordem = ProductionOrderSapApiClient.MapearOrdem(corpo);
        Assert.NotNull(ordem);
        ComponenteOrdemProducaoSap componente = Assert.Single(ordem!.Componentes);
        return componente.MetadataAlocacao261;
    }

    // ==================================================================
    // §8 — mapeamento JSON: presente / false explícito / ausente / nulo / inválido
    // ==================================================================

    [Fact]
    public void Json_TodosOsCamposPresentes_MapeiaValores()
    {
        MetadataAlocacao261Sap m = MetadataDe(JsonCompleto);

        Assert.True(m.QuantidadeFixa);
        Assert.True(m.SucataLiquida);
        Assert.Equal(2.50m, m.SucataComponentePercentual);
        Assert.Equal(1.25m, m.SucataOperacaoPercentual);
        Assert.Equal(48.000m, m.QuantidadeOriginalComponente);
        Assert.True(m.ReservaFinalizada);
        Assert.True(m.Backflush);
        Assert.True(m.MaterialGranel);
        Assert.Equal(12.500m, m.QuantidadeRetirada);
        Assert.Equal(37.500m, m.QuantidadeDisponivelConfirmada);
        Assert.Equal("0010", m.ItemBOM);
        Assert.Equal("L", m.CategoriaItemBOM);
        Assert.Equal("1", m.TipoSplitLote);
        Assert.Equal("KG", m.UnidadeBaseSap);
    }

    [Fact] // FALSE EXPLÍCITO (booleano JSON / "false" / "0") é FALSE — distinto de DESCONHECIDO.
    public void Json_FlagsFalseExplicito_MapeiaFalseNaoNulo()
    {
        MetadataAlocacao261Sap m = MetadataDe(JsonFalseExplicito);

        Assert.False(m.QuantidadeFixa);      // booleano JSON false
        Assert.False(m.SucataLiquida);       // booleano JSON false
        Assert.False(m.ReservaFinalizada);   // "false"
        Assert.False(m.Backflush);           // "0"
        Assert.False(m.MaterialGranel);      // "FALSE" (case-insensitive)
    }

    /// <summary>
    /// GATE 107N-R2: número JSON como flag. Apenas 0/1 são representações booleanas válidas;
    /// a regra genérica "diferente de zero ⇒ TRUE" foi removida.
    /// </summary>
    private static string JsonComNumero(string valorNumerico) => $$"""
    {
      "d": {
        "ManufacturingOrder": "1000173",
        "to_ProductionOrderComponent": {
          "results": [
            {
              "Material": "1000186",
              "BaseUnit": "KG",
              "QuantityIsFixed": {{valorNumerico}},
              "IsNetScrap": {{valorNumerico}},
              "ReservationIsFinallyIssued": {{valorNumerico}},
              "MatlCompIsMarkedForBackflush": {{valorNumerico}},
              "IsBulkMaterialComponent": {{valorNumerico}}
            }
          ]
        }
      }
    }
    """;

    [Fact] // JSON number 0 ⇒ FALSE (negação explícita).
    public void Json_Numero0_MapeiaFalse()
    {
        MetadataAlocacao261Sap m = MetadataDe(JsonComNumero("0"));

        Assert.False(m.QuantidadeFixa);
        Assert.False(m.SucataLiquida);
        Assert.False(m.ReservaFinalizada);
        Assert.False(m.Backflush);
        Assert.False(m.MaterialGranel);
    }

    [Fact] // JSON number 1 ⇒ TRUE (afirmação explícita).
    public void Json_Numero1_MapeiaTrue()
    {
        MetadataAlocacao261Sap m = MetadataDe(JsonComNumero("1"));

        Assert.True(m.QuantidadeFixa);
        Assert.True(m.SucataLiquida);
        Assert.True(m.ReservaFinalizada);
        Assert.True(m.Backflush);
        Assert.True(m.MaterialGranel);
    }

    [Theory] // Qualquer número fora de {0,1} ⇒ DESCONHECIDO (nunca TRUE por "!= 0").
    [InlineData("2")]
    [InlineData("-1")]
    [InlineData("7")]
    [InlineData("1.5")]
    [InlineData("-0.5")]
    [InlineData("0.999")]
    public void Json_NumeroForaDeZeroUm_PreservaDesconhecido(string valorNumerico)
    {
        MetadataAlocacao261Sap m = MetadataDe(JsonComNumero(valorNumerico));

        Assert.Null(m.QuantidadeFixa);
        Assert.Null(m.SucataLiquida);
        Assert.Null(m.ReservaFinalizada);
        Assert.Null(m.Backflush);
        Assert.Null(m.MaterialGranel);
    }

    [Fact] // 0.0 e 1.0 continuam sendo 0 e 1 (equivalência numérica, não textual).
    public void Json_NumeroDecimalEquivalenteAZeroOuUm_MantemSemantica()
    {
        Assert.False(MetadataDe(JsonComNumero("0.0")).QuantidadeFixa);
        Assert.True(MetadataDe(JsonComNumero("1.0")).QuantidadeFixa);
    }

    [Fact] // GATE 107N-R1: string vazia e whitespace ⇒ DESCONHECIDO, NUNCA false.
    public void Json_FlagsVaziaOuWhitespace_PreservamDesconhecido()
    {
        MetadataAlocacao261Sap m = MetadataDe(JsonVazioEWhitespace);

        Assert.Null(m.QuantidadeFixa);     // ""
        Assert.Null(m.SucataLiquida);      // "   "
        Assert.Null(m.ReservaFinalizada);  // ""
        Assert.Null(m.Backflush);          // "\t"
        Assert.Null(m.MaterialGranel);     // " "
    }

    [Fact] // AUSENTE ⇒ DESCONHECIDO (jamais false/0).
    public void Json_CamposAusentes_PreservamDesconhecido()
    {
        MetadataAlocacao261Sap m = MetadataDe(JsonAusente);

        Assert.Null(m.QuantidadeFixa);
        Assert.Null(m.SucataLiquida);
        Assert.Null(m.SucataComponentePercentual);
        Assert.Null(m.SucataOperacaoPercentual);
        Assert.Null(m.QuantidadeOriginalComponente);
        Assert.Null(m.ReservaFinalizada);
        Assert.Null(m.Backflush);
        Assert.Null(m.MaterialGranel);
        Assert.Null(m.QuantidadeRetirada);
        Assert.Null(m.QuantidadeDisponivelConfirmada);
    }

    [Fact] // NULO explícito e decimal não numérico ⇒ DESCONHECIDO.
    public void Json_NuloOuInvalido_PreservamDesconhecido()
    {
        MetadataAlocacao261Sap m = MetadataDe(JsonNuloEInvalido);

        Assert.Null(m.QuantidadeFixa);                 // null JSON
        Assert.Null(m.SucataLiquida);                  // null JSON
        Assert.Null(m.SucataComponentePercentual);     // null JSON
        Assert.Null(m.SucataOperacaoPercentual);       // "nao-numerico"
        Assert.Null(m.QuantidadeOriginalComponente);   // ""
        Assert.Null(m.QuantidadeRetirada);             // "abc"
        Assert.Null(m.QuantidadeDisponivelConfirmada); // null JSON
        Assert.Null(m.ReservaFinalizada);              // null JSON
        Assert.Null(m.Backflush);                      // "?" não interpretável
        Assert.Null(m.MaterialGranel);                 // null JSON
    }

    // ==================================================================
    // §8 — mapeamento XML/Atom
    // ==================================================================

    [Fact]
    public void Xml_TodosOsCamposPresentes_MapeiaValores()
    {
        MetadataAlocacao261Sap m = MetadataDe(XmlCompleto);

        Assert.True(m.QuantidadeFixa);
        Assert.True(m.SucataLiquida);
        Assert.Equal(2.50m, m.SucataComponentePercentual);
        Assert.Equal(1.25m, m.SucataOperacaoPercentual);
        Assert.Equal(48.000m, m.QuantidadeOriginalComponente);
        Assert.True(m.ReservaFinalizada);
        Assert.True(m.Backflush);
        Assert.True(m.MaterialGranel);
        Assert.Equal(12.500m, m.QuantidadeRetirada);
        Assert.Equal(37.500m, m.QuantidadeDisponivelConfirmada);
        Assert.Equal("0010", m.ItemBOM);
        Assert.Equal("L", m.CategoriaItemBOM);
        Assert.Equal("1", m.TipoSplitLote);
    }

    [Fact] // GATE 107N-R1: XML — "false"/"0" ⇒ FALSE; vazio/whitespace ⇒ DESCONHECIDO.
    public void Xml_FalseExplicitoVersusVazioOuWhitespace()
    {
        MetadataAlocacao261Sap m = MetadataDe(XmlFalseVazioEWhitespace);

        // negação EXPLÍCITA
        Assert.False(m.ReservaFinalizada);  // "false"
        Assert.False(m.Backflush);          // "0"

        // ausência de conteúdo ⇒ DESCONHECIDO (nunca false)
        Assert.Null(m.QuantidadeFixa);      // elemento vazio
        Assert.Null(m.SucataLiquida);       // "   "
        Assert.Null(m.MaterialGranel);      // " "
    }

    [Fact]
    public void Xml_AusenteOuInvalido_PreservamDesconhecido()
    {
        MetadataAlocacao261Sap m = MetadataDe(XmlAusenteEInvalido);

        Assert.Null(m.QuantidadeFixa);                 // elemento ausente
        Assert.Null(m.SucataLiquida);                  // ausente
        Assert.Null(m.SucataComponentePercentual);     // ausente
        Assert.Null(m.SucataOperacaoPercentual);       // "nao-numerico"
        Assert.Null(m.QuantidadeOriginalComponente);   // elemento vazio
        Assert.Null(m.ReservaFinalizada);              // ausente
        Assert.Null(m.Backflush);                      // "?" não interpretável
        Assert.Null(m.MaterialGranel);                 // ausente
        Assert.Null(m.QuantidadeRetirada);             // ausente
        Assert.Null(m.QuantidadeDisponivelConfirmada); // ausente
    }

    // ==================================================================
    // §2 — OperationScrapInPercent vem do COMPONENTE (sem join de operação)
    // ==================================================================

    [Fact]
    public void OperationScrapInPercent_VemDoComponente_SemConsultaDeOperacao()
    {
        // O corpo NÃO possui to_ProductionOrderOperation; ainda assim o valor é lido do componente.
        MetadataAlocacao261Sap m = MetadataDe(JsonCompleto);
        Assert.Equal(1.25m, m.SucataOperacaoPercentual);

        OrdemProducaoSap? ordem = ProductionOrderSapApiClient.MapearOrdem(JsonCompleto);
        Assert.Empty(ordem!.Operacoes); // prova que não houve dependência da entidade de operação
    }

    // ==================================================================
    // §5/§8 — propagação END-TO-END até ProdutoAcabadoComponenteOrigem
    // ==================================================================

    private static ProdutoAcabadoComponenteOrigem PropagarAteOrigem(string corpo)
    {
        OrdemProducaoSap? ordemSap = ProductionOrderSapApiClient.MapearOrdem(corpo);
        Assert.NotNull(ordemSap);

        // hop 3: Controller (DTO SAP → domínio PA)
        ProdutoAcabadoOrdem ordemPa = EntradaDominioProdutoAcabado(ordemSap!);
        ProdutoAcabadoComponenteOrdem componentePa = Assert.Single(ordemPa.Componentes);
        Assert.NotNull(componentePa.MetadataAlocacao261);

        // hop 4: Form (domínio PA → origem do pipeline)
        ProdutoAcabadoPipelineOrigem origem = ProcessoProdutoAcabadoForm.MontarOrigemPipelineRuntime(
            new ProdutoAcabadoCaixa
            {
                CodigoProdutoAcabadoCaixa = 1,
                NumeroOrdemProducao = ordemPa.NumeroOrdem,
                Material = ordemPa.MaterialProduzido,
                Centro = ordemPa.Centro,
                Deposito = ordemPa.DepositoDestino,
                ItemOrdemProducao = ordemPa.ItemOrdem,
                Lote = ordemPa.Lote,
                UnidadeQuantidade = ordemPa.Unidade,
                QuantidadeProdutos = 1
            },
            ordemPa,
            new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc));

        return Assert.Single(origem.Componentes);
    }

    /// <summary>Reproduz o hop do Controller usando o mapeamento REAL (MapearOrdem é privado estático).</summary>
    private static ProdutoAcabadoOrdem EntradaDominioProdutoAcabado(OrdemProducaoSap ordemSap)
    {
        System.Reflection.MethodInfo metodo = typeof(ProdutoAcabadoController).GetMethod(
            "MapearOrdem",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        return (ProdutoAcabadoOrdem)metodo.Invoke(null, [ordemSap])!;
    }

    [Fact] // Os 13 campos chegam íntegros à origem do pipeline.
    public void EndToEnd_Metadata_ChegaNaOrigemDoPipeline()
    {
        ProdutoAcabadoComponenteOrigem origem = PropagarAteOrigem(JsonCompleto);
        MetadataAlocacao261Sap m = origem.MetadataAlocacao261;

        Assert.True(m.QuantidadeFixa);
        Assert.True(m.SucataLiquida);
        Assert.Equal(2.50m, m.SucataComponentePercentual);
        Assert.Equal(1.25m, m.SucataOperacaoPercentual);
        Assert.Equal(48.000m, m.QuantidadeOriginalComponente);
        Assert.True(m.ReservaFinalizada);
        Assert.True(m.Backflush);
        Assert.True(m.MaterialGranel);
        Assert.Equal(12.500m, m.QuantidadeRetirada);
        Assert.Equal(37.500m, m.QuantidadeDisponivelConfirmada);
        Assert.Equal("0010", m.ItemBOM);
        Assert.Equal("L", m.CategoriaItemBOM);
        Assert.Equal("1", m.TipoSplitLote);
        Assert.Equal("KG", m.UnidadeBaseSap);
    }

    [Fact] // A INCERTEZA também atravessa a cadeia inteira (fail-closed preservado até a decisão).
    public void EndToEnd_Desconhecido_ChegaComoDesconhecido()
    {
        ProdutoAcabadoComponenteOrigem origem = PropagarAteOrigem(JsonAusente);
        MetadataAlocacao261Sap m = origem.MetadataAlocacao261;

        Assert.Null(m.QuantidadeFixa);
        Assert.Null(m.SucataLiquida);
        Assert.Null(m.SucataComponentePercentual);
        Assert.Null(m.SucataOperacaoPercentual);
        Assert.Null(m.QuantidadeOriginalComponente);
        Assert.Null(m.ReservaFinalizada);
        Assert.Null(m.Backflush);
        Assert.Null(m.MaterialGranel);
        Assert.Null(m.QuantidadeRetirada);
        Assert.Null(m.QuantidadeDisponivelConfirmada);
    }

    // ==================================================================
    // §6 — autoridade de quantidade/unidade preservada para o futuro allocator
    // ==================================================================

    [Fact]
    public void AutoridadeItem_MfgOrderItemPlannedTotalQty_TemPrecedenciaSobreHeader()
    {
        const string comItem = """
        {
          "d": {
            "ManufacturingOrder": "1000173",
            "TotalQuantity": "100.000",
            "ProductionUnit": "KG",
            "to_ProductionOrderItem": {
              "results": [
                {
                  "ManufacturingOrderItem": "0001",
                  "Material": "3500024",
                  "MfgOrderItemPlannedTotalQty": "80.000",
                  "MfgOrderItemActualDeliveryQty": "10.000",
                  "ProductionUnit": "KG"
                }
              ]
            }
          }
        }
        """;

        OrdemProducaoSap? ordemSap = ProductionOrderSapApiClient.MapearOrdem(comItem);
        ProdutoAcabadoOrdem ordemPa = EntradaDominioProdutoAcabado(ordemSap!);

        // ITEM é autoritativo (80), não o header (100).
        Assert.Equal(80.000m, ordemPa.QuantidadePlanejada);
        Assert.Equal(10.000m, ordemPa.QuantidadeEntregue);
        Assert.Equal(70.000m, ordemPa.QuantidadePendente);
        Assert.Equal("KG", ordemPa.Unidade);
        // Header permanece DISPONÍVEL no DTO SAP como fallback futuro (não selecionado aqui).
        Assert.Equal(100.000m, ordemSap!.QuantidadePrevista);
    }

    [Fact] // §7: o caminho quantitativo não herda o fallback literal "KG" da UI.
    public void CaminhoQuantitativo_NaoUsaFallbackLiteralKg()
    {
        const string semUnidade = """
        {
          "d": {
            "ManufacturingOrder": "1000173",
            "to_ProductionOrderComponent": {
              "results": [
                { "Material": "1000186", "RequiredQuantity": "50.000" }
              ]
            }
          }
        }
        """;

        ProdutoAcabadoComponenteOrigem origem = PropagarAteOrigem(semUnidade);

        // SAP não informou BaseUnit ⇒ o campo quantitativo fica VAZIO (DESCONHECIDO), sem virar "KG".
        Assert.Equal(string.Empty, origem.MetadataAlocacao261.UnidadeBaseSap);
    }

    // ==================================================================
    // §5/§11 — payload 261 intocado e nenhum cálculo introduzido
    // ==================================================================

    [Fact]
    public void Payload261ItemCommand_NaoRecebeMetadataDecisoria()
    {
        string[] propriedades = typeof(ProdutoAcabadoMovimento261ItemCommand)
            .GetProperties()
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        // Contrato de payload EXATO (8 campos) — nenhuma metadata decisória entrou.
        Assert.Equal(
            ["Batch", "Material", "Plant", "Quantidade", "Reservation", "ReservationItem", "StorageLocation", "Unidade"],
            propriedades);
        Assert.DoesNotContain("MetadataAlocacao261", propriedades);
    }

    [Fact] // A quantidade do componente NÃO é recalculada/rateada neste gate.
    public void Quantidade_PermaneceRequiredQuantitySemRateio()
    {
        ProdutoAcabadoComponenteOrigem origem = PropagarAteOrigem(JsonCompleto);

        // Mesmo com caixa de 1 KG e OP de 100 KG, a quantidade segue igual ao RequiredQuantity (50).
        // Corrigir isso é papel do FUTURO allocator — proibido neste gate.
        Assert.Equal(50.000m, origem.Quantidade);
    }

    [Fact] // Nenhum allocator foi criado.
    public void Allocator_NaoFoiImplementado()
    {
        Assert.DoesNotContain(
            typeof(ProdutoAcabadoComponenteOrigem).Assembly.GetTypes(),
            t => t.Name.Contains("261Allocator", StringComparison.OrdinalIgnoreCase)
                 || t.Name.Contains("Allocator261", StringComparison.OrdinalIgnoreCase));
    }
}
