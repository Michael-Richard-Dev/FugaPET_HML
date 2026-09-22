using System.Reflection;
using FugaPET_HML.Modelo.Consumo;
using FugaPET_HML.Modelo.Processo;
using FugaPET_HML.Servicos.Diagnostico;

namespace FugaPET_HML.Tests.Diagnostico;

public sealed class RecoveryDiag102JValueAllowlistTests
{
    [Fact]
    public void Material_ComAuthorizationBearer_PersisteSomenteInvalid()
    {
        ComponenteConsumoMaterial componente = ComponenteValido();
        componente.CodigoMaterial = "Authorization=Bearer ABC";

        string linha = RecoveryDiag102J.FormatarComponenteParaTeste(
            Guid.NewGuid(), PontoDiag102J.D_ComponentesSap, 0, componente);

        Assert.Contains("Mat=INVALID", linha, StringComparison.Ordinal);
        AssertNaoContemOriginal(linha, "Authorization", "Bearer", "ABC");
    }

    [Fact]
    public void Batch_ComPassword_PersisteSomenteInvalid()
    {
        ComponenteConsumoMaterial componente = ComponenteValido();
        componente.Lote = "password=abc";

        string linha = RecoveryDiag102J.FormatarComponenteParaTeste(
            Guid.NewGuid(), PontoDiag102J.D_ComponentesSap, 0, componente);

        Assert.Contains("Batch=INVALID", linha, StringComparison.Ordinal);
        AssertNaoContemOriginal(linha, "password", "abc");
    }

    [Fact]
    public void StorageLocation_ComUserId_PersisteSomenteInvalid()
    {
        ComponenteConsumoMaterial componente = ComponenteValido();
        componente.DepositoConsumo = "User Id=foo";

        string linha = RecoveryDiag102J.FormatarComponenteParaTeste(
            Guid.NewGuid(), PontoDiag102J.D_ComponentesSap, 0, componente);

        Assert.Contains("Dep=INVALID", linha, StringComparison.Ordinal);
        AssertNaoContemOriginal(linha, "User Id", "foo");
    }

    [Fact]
    public void Op_ComHost_PersisteSomenteInvalid()
    {
        ContextoApontamentoProcesso contexto = ContextoValido("Host=bar");

        string linha = RecoveryDiag102J.FormatarContextoParaTeste(
            Guid.NewGuid(), PontoDiag102J.A_FormConstructor, contexto);

        Assert.Contains("OP=INVALID", linha, StringComparison.Ordinal);
        AssertNaoContemOriginal(linha, "Host", "bar");
    }

    [Theory]
    [InlineData("token=xyz", "token", "xyz")]
    [InlineData("Server=foo", "Server", "foo")]
    [InlineData("pwd=abc", "pwd", "abc")]
    [InlineData("connectionstring=abc", "connectionstring", "abc")]
    public void OutrosSegredosEmValorTecnico_PersistemSomenteInvalid(
        string valorInjetado, string marcador, string segredo)
    {
        ComponenteConsumoMaterial componente = ComponenteValido();
        componente.CodigoMaterial = valorInjetado;

        string linha = RecoveryDiag102J.FormatarComponenteParaTeste(
            Guid.NewGuid(), PontoDiag102J.D_ComponentesSap, 0, componente);

        Assert.Contains("Mat=INVALID", linha, StringComparison.Ordinal);
        AssertNaoContemOriginal(linha, marcador, segredo);
    }

    [Fact]
    public void ModalidadeLivre_NaoEhPossivelPeloContrato()
    {
        Assert.Equal(typeof(ModalidadeRecuperacaoConsumo), typeof(SnapshotDiag102J).GetProperty("Modalidade")!.PropertyType);
        MethodInfo resultado = typeof(RecoveryDiag102J).GetMethod(nameof(RecoveryDiag102J.LogResultado))!;
        Assert.Equal(2, resultado.GetParameters().Count(p => p.ParameterType == typeof(ModalidadeRecuperacaoConsumo)));
        Assert.DoesNotContain(resultado.GetParameters(), p => p.ParameterType == typeof(string));
    }

    [Fact]
    public void NenhumaApiPublicaOuInternalAceitaLinhaOuDetalheLivre()
    {
        MethodInfo[] expostos = typeof(RecoveryDiag102J).GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Where(m => !m.IsPrivate)
            .Where(m => m.Name.Contains("Log", StringComparison.Ordinal)
                || m.Name.Contains("Format", StringComparison.Ordinal)
                || m.Name.Contains("Emit", StringComparison.Ordinal)
                || m.Name.Contains("Escrever", StringComparison.Ordinal))
            .ToArray();

        Assert.DoesNotContain(expostos, m => m.GetParameters().Any(p => p.ParameterType == typeof(string)));
        Assert.Null(typeof(RecoveryDiag102J).GetMethod("EscreverLinha", BindingFlags.NonPublic | BindingFlags.Static));
        Assert.Null(typeof(RecoveryDiag102J).GetMethod("Formatar", BindingFlags.NonPublic | BindingFlags.Static));
    }

    [Fact]
    public void ValoresFuncionaisValidos_ContinuamMaterializados()
    {
        string contexto = RecoveryDiag102J.FormatarContextoParaTeste(
            Guid.NewGuid(), PontoDiag102J.A_FormConstructor, ContextoValido("1000170"));
        string componente = RecoveryDiag102J.FormatarComponenteParaTeste(
            Guid.NewGuid(), PontoDiag102J.D_ComponentesSap, 0, ComponenteValido());
        string resultado = RecoveryDiag102J.FormatarResultadoParaTeste(
            Guid.NewGuid(), PontoDiag102J.G_AfterRecoveryResolver,
            ModalidadeRecuperacaoConsumo.UmPendente, ModalidadeRecuperacaoConsumo.UmPendente, 8, 1);

        Assert.Contains("OP=1000170", contexto, StringComparison.Ordinal);
        Assert.Contains("Op=0010", contexto, StringComparison.Ordinal);
        Assert.Contains("Mat=1000186", componente, StringComparison.Ordinal);
        Assert.Contains("Res=185", componente, StringComparison.Ordinal);
        Assert.Contains("Item=1", componente, StringComparison.Ordinal);
        Assert.Contains("Dep=PP01", componente, StringComparison.Ordinal);
        Assert.Contains("Batch=0000000222", componente, StringComparison.Ordinal);
        Assert.Contains("Mov=261", componente, StringComparison.Ordinal);
        Assert.Contains("modalidadeBruta=UmPendente", resultado, StringComparison.Ordinal);
        Assert.Contains("pk=8", resultado, StringComparison.Ordinal);
    }

    private static ContextoApontamentoProcesso ContextoValido(string ordem)
        => new()
        {
            CodigoApontamento = 2,
            NumeroOrdem = ordem,
            Operacao = "0010",
            Sequencia = "000000",
            TipoProcesso = TipoProcessoOperacao.ConsumoMateriaPrima
        };

    private static ComponenteConsumoMaterial ComponenteValido()
        => new()
        {
            CodigoMaterial = "1000186",
            NumeroReserva = "185",
            ItemReserva = "1",
            DepositoConsumo = "PP01",
            Lote = "0000000222",
            TipoMovimento = "261",
            Operacao = "0010",
            SequenciaOperacao = "000000"
        };

    private static void AssertNaoContemOriginal(string linha, params string[] partes)
    {
        foreach (string parte in partes)
        {
            Assert.DoesNotContain(parte, linha, StringComparison.OrdinalIgnoreCase);
        }
    }
}
