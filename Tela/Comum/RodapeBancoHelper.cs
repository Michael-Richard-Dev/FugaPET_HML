namespace FugaPET_HML.Tela.Comum;

public static class RodapeBancoHelper
{
    public static string ObterTextoBancoDados()
    {
        var configuracao = global::FugaPET_HML.AcessoDados.Banco.LeitorConfiguracaoBancoPostgreSql.Carregar();
        string nomeBanco = string.IsNullOrWhiteSpace(configuracao.NomeBanco)
            ? "N/A"
            : configuracao.NomeBanco.Trim();

        return $"Banco de Dados:  {nomeBanco}";
    }
}
