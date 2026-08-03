using FugaPET_HML.Tela;

namespace FugaPET_HML;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        using LoginForm loginForm = new();
        if (loginForm.ShowDialog() == DialogResult.OK)
        {
            Application.Run(new PainelInicialForm());
        }
    }
}
