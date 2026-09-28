using System.Globalization;
using FugaPET_HML.Tela.Processo;

namespace FugaPET_HML.Tests.Operacao;

/// <summary>
/// GATE 110B — parse decimal do PESO MANUAL da Entrada (sempre KG).
///
/// Incidente real (pedido 4500000216 / material 1000205): TryNormalizeWeight REMOVIA todos os separadores
/// e exigia inteiro, então "4155,461" (4.155,461 KG) virava "4155461" ⇒ 4.155.461 KG — erro de fator 1000
/// aceito sem um único aviso. O contrato agora INTERPRETA o separador ou BLOQUEIA; nunca descarta.
///
/// Testes puros: nenhum WinForms instanciado, nenhum banco, nenhum SAP.
/// </summary>
public sealed class EntradaPesoManualParseDecimal110BTests
{
    private static decimal Normalizar(string entrada)
    {
        Assert.True(
            ProcessoEntradaProdutoForm.TryNormalizeWeight(entrada, out string normalizado, out string? erro),
            $"esperado ACEITAR '{entrada}', mas bloqueou: {erro}");
        Assert.Null(erro);

        // O valor normalizado precisa atravessar o parser do fluxo SEM perda de escala.
        Assert.True(
            ProcessoEntradaProdutoForm.TryParsePesoKg(normalizado, out decimal peso),
            $"normalizado '{normalizado}' não foi consumido pelo fluxo");
        return peso;
    }

    private static string Bloquear(string entrada)
    {
        Assert.False(
            ProcessoEntradaProdutoForm.TryNormalizeWeight(entrada, out string normalizado, out string? erro),
            $"esperado BLOQUEAR '{entrada}', mas aceitou como '{normalizado}'");
        Assert.Equal(string.Empty, normalizado);
        Assert.False(string.IsNullOrWhiteSpace(erro), "bloqueio deve explicar o formato esperado");
        return erro!;
    }

    // ==================================================================
    // §10 INCIDENT_REGRESSION — obrigatório
    // ==================================================================

    [Fact]
    public void IncidenteReal_4155Virgula461_NuncaPodeVirar4155461()
    {
        decimal peso = Normalizar("4155,461");

        Assert.Equal(4155.461m, peso);
        Assert.NotEqual(4155461m, peso);
    }

    [Fact] // A perda de escala de fator 1000 não pode reaparecer por nenhuma das formas do incidente.
    public void IncidenteReal_NenhumaFormaProduzEscalaMilVezesMaior()
    {
        Assert.Equal(4155.461m, Normalizar("4155,461"));
        Assert.Equal(4155.461m, Normalizar("4155.461"));
        Assert.Equal(4155.461m, Normalizar("4.155,461"));
        Assert.Equal(4155.461m, Normalizar("4,155.461"));

        // A forma agrupada sem decimal é ambígua e por isso BLOQUEIA (nunca vira 4155461).
        Bloquear("4.155.461");
    }

    // ==================================================================
    // §3 CASOS VÁLIDOS MÍNIMOS
    // ==================================================================

    [Theory]
    [InlineData("4155", "4155")]
    [InlineData("4155,4", "4155.4")]
    [InlineData("4155,46", "4155.46")]
    [InlineData("4155,461", "4155.461")]
    [InlineData("4155.4", "4155.4")]
    [InlineData("4155.46", "4155.46")]
    [InlineData("4155.461", "4155.461")]
    [InlineData("  4155,461  ", "4155.461")]
    [InlineData("0,5", "0.5")]
    [InlineData("0,001", "0.001")]
    public void EntradasValidas_NormalizamParaInvariante(string entrada, string esperado)
    {
        Assert.True(
            ProcessoEntradaProdutoForm.TryNormalizeWeight(entrada, out string normalizado, out string? erro), erro);

        Assert.Equal(esperado, normalizado);
        Assert.Equal(
            decimal.Parse(esperado, CultureInfo.InvariantCulture),
            Normalizar(entrada));
    }

    // ==================================================================
    // GATE 110B-R1 §1/§3 — separador único ambíguo por agrupamento ⇒ BLOCK
    // ==================================================================

    [Theory] // Forma que também é inteiro agrupado legítimo ⇒ BLOQUEIA (não escolher por conta própria).
    [InlineData("4.155")]
    [InlineData("4,155")]
    [InlineData("12.345")]
    [InlineData("12,345")]
    [InlineData("999.999")]
    [InlineData("999,999")]
    [InlineData("1.000")]
    [InlineData("1,000")]
    public void SeparadorUnicoComTresDigitos_AmbiguoPorMilhar_Bloqueia(string entrada)
    {
        string erro = Bloquear(entrada);

        Assert.Contains("ambíguo", erro, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("separador de milhar", erro, StringComparison.OrdinalIgnoreCase);
    }

    [Theory] // NÃO é ambíguo: o grupo inicial tem 4+ dígitos, logo não é agrupamento válido de milhar.
    [InlineData("4155.461", "4155.461")]
    [InlineData("4155,461", "4155.461")]
    [InlineData("12345.678", "12345.678")]
    [InlineData("99999,999", "99999.999")]
    public void SeparadorUnicoComGrupoInicialDeQuatroDigitos_EhDecimal(string entrada, string esperado)
    {
        Assert.True(
            ProcessoEntradaProdutoForm.TryNormalizeWeight(entrada, out string normalizado, out string? erro), erro);

        Assert.Equal(esperado, normalizado);
    }

    [Theory] // NÃO é ambíguo: menos de 3 casas decimais não tem leitura como agrupamento.
    [InlineData("4.15", "4.15")]
    [InlineData("4,1", "4.1")]
    [InlineData("12.5", "12.5")]
    public void SeparadorUnicoComMenosDeTresDigitos_EhDecimal(string entrada, string esperado)
    {
        Assert.True(
            ProcessoEntradaProdutoForm.TryNormalizeWeight(entrada, out string normalizado, out string? erro), erro);

        Assert.Equal(esperado, normalizado);
    }

    [Theory] // Zero à esquerda não é agrupamento legítimo de milhar ⇒ permanece decimal.
    [InlineData("0,001", "0.001")]
    [InlineData("0.001", "0.001")]
    [InlineData("0,500", "0.5")]
    public void PrimeiroGrupoComZeroAEsquerda_NaoEhAmbiguo(string entrada, string esperado)
    {
        Assert.True(
            ProcessoEntradaProdutoForm.TryNormalizeWeight(entrada, out string normalizado, out string? erro), erro);

        Assert.Equal(decimal.Parse(esperado, CultureInfo.InvariantCulture), Normalizar(entrada));
        Assert.False(string.IsNullOrEmpty(normalizado));
    }

    [Fact] // Desambiguação explícita: com agrupamento declarado, o valor é aceito.
    public void FormaExplicitaComAgrupamento_ResolveAAmbiguidade()
    {
        Bloquear("4.155");
        Assert.Equal(4155m, Normalizar("4155"));
        Assert.Equal(4155.461m, Normalizar("4.155,461"));
    }

    // ==================================================================
    // §4 AMBIGUIDADE
    // ==================================================================

    [Theory] // Dois tipos de separador: o de última ocorrência é o decimal; o outro precisa ser agrupamento válido.
    [InlineData("4.155,461", "4155.461")]
    [InlineData("4,155.461", "4155.461")]
    [InlineData("1.234.567,89", "1234567.89")]
    [InlineData("1,234,567.89", "1234567.89")]
    [InlineData("12.345,6", "12345.6")]
    public void AgrupamentoInequivoco_EhAceito(string entrada, string esperado)
    {
        Assert.True(
            ProcessoEntradaProdutoForm.TryNormalizeWeight(entrada, out string normalizado, out string? erro), erro);

        Assert.Equal(esperado, normalizado);
    }

    [Theory] // Agrupamento inválido ou combinação impossível ⇒ BLOCK (nunca "adivinhar").
    [InlineData("4.15,461")]     // grupo de 2 no agrupamento
    [InlineData("4.1555,461")]   // grupo de 4 no agrupamento
    [InlineData("4.155.461")]    // mesmo separador repetido, sem decimal
    [InlineData("4,155,461")]
    [InlineData("4.155,461,2")]  // decimal repetido
    [InlineData("4,5.6,7")]
    [InlineData(",461")]         // sem parte inteira
    [InlineData("4155,")]        // sem parte decimal
    [InlineData("4155.")]
    [InlineData(",")]
    [InlineData(".")]
    public void SeparadoresAmbiguosOuInvalidos_Bloqueiam(string entrada)
        => Bloquear(entrada);

    [Fact] // "4,155,461.5" é agrupamento VÁLIDO com decimal — confirma que a regra não é excessiva.
    public void AgrupamentoMultiploComDecimal_EhAceito()
        => Assert.Equal(4155461.5m, Normalizar("4,155,461.5"));

    // ==================================================================
    // §5 NEGATIVO / ZERO
    // ==================================================================

    [Theory]
    [InlineData("-10")]
    [InlineData("-10,5")]
    [InlineData("-0,001")]
    [InlineData("10-")]
    [InlineData("+10")]
    public void SinalNoPeso_Bloqueia(string entrada)
        => Bloquear(entrada);

    [Fact] // O defeito exato: "-10" NÃO pode virar 10 em nenhuma das duas camadas.
    public void PesoNegativo_NuncaViraPositivo()
    {
        Assert.False(
            ProcessoEntradaProdutoForm.TryNormalizeWeight("-10", out string normalizado, out string? erro));
        Assert.Equal(string.Empty, normalizado);
        Assert.Contains("negativo", erro!, StringComparison.OrdinalIgnoreCase);

        // Camada auxiliar compartilhada (também usada pela leitura de balança).
        Assert.False(ProcessoEntradaProdutoForm.TryParsePesoKg("-10", out decimal peso));
        Assert.Equal(0m, peso);
        Assert.False(ProcessoEntradaProdutoForm.TryParsePesoKg("-10,5", out _));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0,0")]
    [InlineData("0,000")]
    [InlineData("00")]
    public void PesoZero_Bloqueia(string entrada)
        => Bloquear(entrada);

    // ==================================================================
    // §6 PRECISÃO
    // ==================================================================

    [Theory]
    [InlineData("4155,4610")]
    [InlineData("4155,4615")]
    [InlineData("0,0001")]
    [InlineData("1.234,5678")]
    public void MaisDeTresCasasDecimais_Bloqueia(string entrada)
    {
        string erro = Bloquear(entrada);

        Assert.Contains("3 casas decimais", erro, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // Não arredondar em silêncio: a 4ª casa BLOQUEIA, mesmo sendo zero.
    public void QuartaCasaZero_NaoEhArredondadaSilenciosamente()
    {
        Bloquear("4155,4610");
        Assert.Equal(4155.461m, Normalizar("4155,461"));
    }

    [Fact] // Alinhado a numeric(14,3): até 11 dígitos inteiros; acima disso, bloqueio de PRECISÃO.
    public void PrecisaoInteira_RespeitaNumeric14_3()
    {
        Assert.Equal(99999999999m, Normalizar("99999999999"));      // 11 dígitos
        Assert.Equal(99999999999.999m, Normalizar("99999999999,999"));
        Bloquear("999999999999");                                    // 12 dígitos
    }

    // ==================================================================
    // §10 ENTRADAS INVÁLIDAS
    // ==================================================================

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("4155 kg")]
    [InlineData("4155kg")]
    [InlineData("4 155")]
    [InlineData("1e3")]
    [InlineData("4155,4a")]
    public void EntradasInvalidas_Bloqueiam(string entrada)
        => Bloquear(entrada);

    // ==================================================================
    // §7 UX
    // ==================================================================

    [Fact]
    public void FormatoEsperado_InformaUnidadeECasasDecimais()
    {
        string formato = ProcessoEntradaProdutoForm.FormatoPesoManualEsperado;

        Assert.Contains("KG", formato, StringComparison.Ordinal);
        Assert.Contains("3 casas decimais", formato, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("4155,461", formato, StringComparison.Ordinal);
        // GATE 110B-R1 (§4): o operador precisa ser instruído a NÃO usar separador de milhar.
        Assert.Contains("separador de milhar", formato, StringComparison.OrdinalIgnoreCase);
    }

    [Theory] // Toda mensagem de erro orienta o operador com o formato esperado (sem jargão técnico).
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("-10")]
    [InlineData("0")]
    [InlineData("4155,4610")]
    [InlineData("4.155.461")]
    public void MensagensDeErro_ExplicamOFormato(string entrada)
    {
        string erro = Bloquear(entrada);

        Assert.Contains("KG", erro, StringComparison.Ordinal);
        Assert.Contains("casas decimais", erro, StringComparison.OrdinalIgnoreCase);
    }

    // ==================================================================
    // §2 A SEMÂNTICA ANTIGA ESTÁ PROIBIDA (contrato de source)
    // ==================================================================

    [Fact]
    public void FonteNaoRemoveMaisOsSeparadores()
    {
        string raiz = AppContext.BaseDirectory;
        string? fonte = null;
        for (int i = 0; i < 9 && raiz is not null && fonte is null; i++)
        {
            foreach (string cand in new[]
            {
                Path.Combine(raiz, "Tela", "Processo", "ProcessoEntradaProdutoForm.cs"),
                Path.Combine(raiz, "FugaPet_HML", "Tela", "Processo", "ProcessoEntradaProdutoForm.cs")
            })
            {
                if (File.Exists(cand)) { fonte = File.ReadAllText(cand); break; }
            }
            raiz = Directory.GetParent(raiz)?.FullName!;
        }

        Assert.NotNull(fonte);
        int inicio = fonte!.LastIndexOf("internal static bool TryNormalizeWeight(", StringComparison.Ordinal);
        Assert.True(inicio > 0);
        string corpo = fonte[inicio..];
        int fim = corpo.IndexOf("private static bool TrySepararParteInteiraEDecimal(", StringComparison.Ordinal);
        Assert.True(fim > 0, "helper de separação não encontrado após TryNormalizeWeight");
        corpo = corpo[..fim];

        Assert.DoesNotContain("Replace(\",\", string.Empty)", corpo, StringComparison.Ordinal);
        Assert.DoesNotContain("Replace(\".\", string.Empty)", corpo, StringComparison.Ordinal);
        Assert.DoesNotContain("int.TryParse", corpo, StringComparison.Ordinal);
    }
}
