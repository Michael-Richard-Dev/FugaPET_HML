using System.Net;
using FugaPET_HML.Servicos.Seguranca;

namespace FugaPET_HML.Servicos.Auditoria;

public sealed class ContextoAuditoria
{
    public long? CodigoUsuario { get; init; }
    public string LoginUsuario { get; init; } = string.Empty;
    public string NomeMaquina { get; init; } = string.Empty;
    public IPAddress? IpOrigem { get; init; }

    public static ContextoAuditoria Atual()
    {
        SessaoUsuarioAplicacao? sessao = EstadoSessaoUsuarioAtual.SessaoAtual;
        return new ContextoAuditoria
        {
            CodigoUsuario = sessao?.IdUsuario,
            LoginUsuario = sessao?.Login ?? string.Empty,
            NomeMaquina = ObterNomeMaquinaSeguro()
        };
    }

    public static ContextoAuditoria ParaLogin(string login, long? codigoUsuario = null)
        => new()
        {
            CodigoUsuario = codigoUsuario,
            LoginUsuario = login,
            NomeMaquina = ObterNomeMaquinaSeguro()
        };

    private static string ObterNomeMaquinaSeguro()
    {
        try
        {
            return Environment.MachineName;
        }
        catch
        {
            return string.Empty;
        }
    }
}
