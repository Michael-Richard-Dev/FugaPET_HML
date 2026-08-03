using FugaPET_HML.AcessoDados.Banco;
using Npgsql;

namespace FugaPET_HML.Tests.Repositorio;

public sealed class ConfiguracaoSchemaBancoTests : IDisposable
{
    private readonly string? _conexaoAnterior =
        Environment.GetEnvironmentVariable("FUGAPET_HML_CONEXAO_POSTGRES");

    [Fact]
    public void ConfiguracaoPadrao_DeveUsarHomologacao()
    {
        ConfiguracaoBancoPostgreSql configuracao = new();

        Assert.Equal("homologacao", configuracao.Schema);
    }

    [Fact]
    public void Fabrica_DeveAplicarSchemaNoSearchPath()
    {
        ConfiguracaoBancoPostgreSql configuracao = new()
        {
            Habilitado = true,
            Servidor = "localhost",
            Porta = 5432,
            NomeBanco = "teste",
            Schema = "homologacao",
            Usuario = "teste",
            Senha = "teste"
        };

        string connectionString =
            new FabricaConexaoPostgreSql(configuracao).ObterConnectionString();
        NpgsqlConnectionStringBuilder builder = new(connectionString);

        Assert.Equal("homologacao", builder.SearchPath);
    }

    [Fact]
    public void LeitorConnectionString_DeveLerSearchPath()
    {
        Environment.SetEnvironmentVariable(
            "FUGAPET_HML_CONEXAO_POSTGRES",
            "Host=localhost;Database=teste;Username=teste;Password=teste;Search Path=homologacao");

        ConfiguracaoBancoPostgreSql configuracao =
            LeitorConfiguracaoBancoPostgreSql.Carregar();

        Assert.Equal("homologacao", configuracao.Schema);
        Assert.True(configuracao.Habilitado);
        Assert.False(configuracao.ModoDemonstracao);
        Assert.False(configuracao.AmbienteDemonstrativo);
    }

    [Fact]
    public void LeitorConnectionString_SemSearchPath_DeveUsarHomologacao()
    {
        // Sem Search Path na connection string, o fallback do leitor deve ser o schema do
        // ambiente DEV (homologacao) — nunca o legado "homologacao".
        Environment.SetEnvironmentVariable(
            "FUGAPET_HML_CONEXAO_POSTGRES",
            "Host=localhost;Database=teste;Username=teste;Password=teste");

        ConfiguracaoBancoPostgreSql configuracao =
            LeitorConfiguracaoBancoPostgreSql.Carregar();

        Assert.Equal("homologacao", configuracao.Schema);
    }

    [Fact]
    public void LeitorConnectionString_DeveRejeitarSchemaInvalido()
    {
        Environment.SetEnvironmentVariable(
            "FUGAPET_HML_CONEXAO_POSTGRES",
            "Host=localhost;Database=teste;Username=teste;Password=teste;Search Path=homologacao,public");

        Assert.Throws<InvalidOperationException>(
            LeitorConfiguracaoBancoPostgreSql.Carregar);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(
            "FUGAPET_HML_CONEXAO_POSTGRES",
            _conexaoAnterior);
    }
}
