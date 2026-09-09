using FugaPET_HML.Servicos.IntegracaoSap;

namespace FugaPET_HML.Servicos.Seguranca;

public static class EstadoSessaoUsuarioAtual
{
    public static SessaoUsuarioAplicacao? SessaoAtual { get; private set; }

    public static void Definir(SessaoUsuarioAplicacao sessao)
    {
        // 12E-E-C: toda troca de sessão (login) desarma a capability de escrita SAP 101 (in-memory, sem
        // persistência). Impede que uma capability armada por um usuário sobreviva para outro no MESMO processo.
        RuntimeSapWriteCapability.Instancia.Desabilitar();
        SessaoAtual = sessao;
    }

    public static void Limpar()
    {
        // 12E-E-C: logout/fim de sessão desarma a capability de escrita SAP 101 (cross-user fail-closed).
        RuntimeSapWriteCapability.Instancia.Desabilitar();
        SessaoAtual = null;
    }

    public static void MarcarSenhaAlterada()
    {
        if (SessaoAtual is null)
        {
            return;
        }

        SessaoAtual = new SessaoUsuarioAplicacao
        {
            IdUsuario = SessaoAtual.IdUsuario,
            Login = SessaoAtual.Login,
            Nome = SessaoAtual.Nome,
            IdSetorPadrao = SessaoAtual.IdSetorPadrao,
            PerfisCodigo = SessaoAtual.PerfisCodigo,
            Permissoes = SessaoAtual.Permissoes,
            IntegracaoBancoHabilitada = SessaoAtual.IntegracaoBancoHabilitada,
            DeveTrocarSenha = false
        };
    }
}
