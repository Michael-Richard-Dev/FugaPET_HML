namespace FugaPET_HML.Tela.Comum;

public static class UsuarioLogadoUiHelper
{
    public static string ObterLogin()
    {
        string? login = global::FugaPET_HML.Servicos.Seguranca.EstadoSessaoUsuarioAtual.SessaoAtual?.Login;
        return string.IsNullOrWhiteSpace(login) ? "N/A" : login.Trim();
    }

    public static string ObterTextoUsuarioRodape()
    {
        return $"Usuário:  {ObterLogin()}";
    }
}
