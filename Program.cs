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

        ResultadoValidacaoAmbienteQ ambiente = ValidadorAmbienteQ.ValidarStartup(
            LeitorConfiguracaoSap.Carregar(),
            LeitorConfiguracaoBancoPostgreSql.Carregar(),
            LeitorConfiguracaoSap.ObterVariavelAmbienteSistema);
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
