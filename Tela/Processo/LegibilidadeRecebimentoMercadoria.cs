using System.Drawing;
using System.Windows.Forms;

namespace FugaPET_HML.Tela.Processo;

/// <summary>
/// GATE 081 — legibilidade da tela "Recebimento de Mercadoria". Escala determinística de tipografia + geometria
/// (2.0x) preservando o tamanho de ícones/logos (PictureBox) e a espessura de bordas. A escala é uniforme
/// (fonte, posição e tamanho pelo mesmo fator), portanto o que cabia em 1x continua cabendo em 2x — sem clipping
/// nem overlap. Puro sobre Control: testável sem instanciar a tela real.
/// </summary>
public static class LegibilidadeRecebimentoMercadoria
{
    /// <summary>Fator exigido por Michael Richard: dobrar as fontes da tela.</summary>
    public const float Escala = 2.0f;

    // Tema FugaPET congelado (laranja/branco). Mesmos valores já usados no reskin aprovado.
    public static readonly Color LaranjaFugaPet = Color.FromArgb(250, 105, 26);
    public static readonly Color LaranjaFugaPetEscuro = Color.FromArgb(200, 78, 10);

    /// <summary>
    /// Escala fonte + geometria de toda a subárvore de <paramref name="raiz"/> pelo fator informado. O tamanho
    /// da própria raiz não é alterado (só o conteúdo). Ícones/logos (PictureBox) mantêm o tamanho em pixels
    /// (apenas reposicionados); bordas não são tocadas.
    /// </summary>
    public static void AplicarEscala(Control raiz, float escala)
    {
        if (raiz is null || escala <= 0f)
        {
            return;
        }

        // Snapshot das fontes ANTES de qualquer mutação: evita composição por herança (pai já escalado
        // inflaria o filho que herda). Cada controle recebe baseFont * escala uma única vez.
        Dictionary<Control, float> fontesBase = [];
        Coletar(raiz, fontesBase);
        EscalarSubarvore(raiz, escala, fontesBase);
    }

    private static void Coletar(Control controle, Dictionary<Control, float> fontesBase)
    {
        fontesBase[controle] = controle.Font.Size;
        foreach (Control filho in controle.Controls)
        {
            Coletar(filho, fontesBase);
        }
    }

    private static void EscalarSubarvore(Control controle, float escala, Dictionary<Control, float> fontesBase)
    {
        controle.SuspendLayout();

        foreach (Control filho in controle.Controls)
        {
            // Posição sempre escala (proporcional ao pai que também cresce). Tamanho escala exceto ícones/logo.
            filho.Location = new Point(Arredondar(filho.Location.X * escala), Arredondar(filho.Location.Y * escala));
            if (filho is not PictureBox)
            {
                filho.Size = new Size(Arredondar(filho.Width * escala), Arredondar(filho.Height * escala));
            }

            EscalarSubarvore(filho, escala, fontesBase);
        }

        // Fonte: a partir do snapshot (não do valor já herdado/mutado). PictureBox não tem texto relevante.
        if (controle is not PictureBox && fontesBase.TryGetValue(controle, out float baseFont) && baseFont > 0f)
        {
            controle.Font = new Font(controle.Font.FontFamily, baseFont * escala, controle.Font.Style, GraphicsUnit.Point);
        }

        if (controle is DataGridView grid && fontesBase.TryGetValue(grid, out float gridFont) && gridFont > 0f)
        {
            EscalarGrid(grid, escala, gridFont);
        }

        controle.ResumeLayout(false);
    }

    // DataGridView: linhas/headers são estilos (não controles-filho) — precisam ser escalados explicitamente.
    private static void EscalarGrid(DataGridView grid, float escala, float baseFont)
    {
        Font fonte = new(grid.Font.FontFamily, baseFont * escala, grid.Font.Style, GraphicsUnit.Point);
        grid.ColumnHeadersDefaultCellStyle.Font = fonte;
        grid.DefaultCellStyle.Font = fonte;
        grid.RowsDefaultCellStyle.Font = fonte;

        grid.ColumnHeadersHeight = Arredondar(grid.ColumnHeadersHeight * escala);
        grid.RowTemplate.Height = Arredondar(grid.RowTemplate.Height * escala);

        foreach (DataGridViewColumn coluna in grid.Columns)
        {
            if (coluna.Width > 0)
            {
                coluna.Width = Arredondar(coluna.Width * escala);
            }
            if (coluna.MinimumWidth > 0)
            {
                coluna.MinimumWidth = Arredondar(coluna.MinimumWidth * escala);
            }
        }
    }

    private static int Arredondar(float valor) => (int)Math.Round(valor, MidpointRounding.AwayFromZero);
}
