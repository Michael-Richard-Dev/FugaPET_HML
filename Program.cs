using FugaPET_HML.AcessoDados.Banco;
using FugaPET_HML.Servicos.Ambiente;
using FugaPET_HML.Servicos.IntegracaoSap;
using FugaPET_HML.Tela;

namespace FugaPET_HML;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        // GATE 120G: perfil de ambiente lido UMA vez da configuracao local, e usado tanto para
        // determinar ENVIRONMENT=Q quanto para os defaults operacionais Q. Permite o duplo clique
        // direto no EXE sem PowerShell/BAT/setx/Registry/User/Machine.
        PerfilAmbienteLocal perfilAmbiente =
            PerfilAmbienteLocal.CarregarDoArquivo(LeitorConfiguracaoSap.CaminhoConfiguracaoLocal);

        ResultadoValidacaoAmbienteQ ambiente = ValidadorAmbienteQ.ValidarStartup(
            LeitorConfiguracaoSap.Carregar(),
            LeitorConfiguracaoBancoPostgreSql.Carregar(),
            LeitorConfiguracaoSap.ObterVariavelAmbienteSistema,
            perfilAmbiente);
        if (!ambiente.Valido)
        {
            MessageBox.Show(
                ambiente.Mensagem,
                "Ambiente Q bloqueado",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        using LoginForm loginForm = new();
        if (loginForm.ShowDialog() == DialogResult.OK)
        {
            Application.Run(new PainelInicialForm());
        }
    }
}
