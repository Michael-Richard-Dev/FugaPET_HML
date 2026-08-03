using Npgsql;

namespace FugaPET_HML.AcessoDados.Banco;

public interface IFabricaConexaoBanco
{
    NpgsqlConnection CriarConexao();
    Task<NpgsqlConnection> CriarConexaoAbertaAsync(CancellationToken cancellationToken = default);
    string ObterConnectionString();
}
