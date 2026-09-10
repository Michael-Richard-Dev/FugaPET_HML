using System.Drawing;
using System.Windows.Forms;

namespace FugaPET_HML.Tela.Processo;

/// <summary>
/// GATE 081/082 — legibilidade da tela "Recebimento de Mercadoria". Escala SOMENTE a tipografia (2.0x),
/// NUNCA a geometria: a tela é montada com TableLayoutPanel + Anchor/Dock + AutoScaleMode.Font e janela
/// Maximizada, então o layout se readapta ao viewport (1920x1080 / 100%) sozinho quando a fonte cresce —
/// sem barras de rolagem e sem estourar a área de trabalho. Ícones/logos (PictureBox) e bordas não são
/// tocados (§9). Puro sobre Control: testável sem instanciar a tela real.
///
/// GATE 082-FIX1: removida a escala de GEOMETRIA do 081 (Location/Size ×2), que empurrava os controles
/// para fora do viewport dentro das células de layout e forçava AutoScroll/barras. A fonte 2.0x aprovada
/// por Michael Richard é preservada.
/// </summary>
public static class LegibilidadeRecebimentoMercadoria
{
    /// <summary>Fator exigido por Michael Richard: dobrar as fontes da tela (validado em 1920x1080/100%).</summary>
    public const float Escala = 2.0f;

    // Tema FugaPET congelado (laranja/branco). Mesmos valores já usados no reskin aprovado.
    public static readonly Color LaranjaFugaPet = Color.FromArgb(250, 105, 26);
    public static readonly Color LaranjaFugaPetEscuro = Color.FromArgb(200, 78, 10);

    /// <summary>
    /// Multiplica a fonte de toda a subárvore de <paramref name="raiz"/> pelo fator informado, SEM alterar
    /// Location/Size (a geometria é responsabilidade do layout responsivo da tela). Snapshot das fontes ANTES
    /// de mutar evita composição por herança (pai já escalado inflaria o filho que herda).
    /// </summary>
    public static void AplicarEscalaFonte(Control raiz, float escala)
    {
        if (raiz is null || escala <= 0f)
        {
            return;
        }

        Dictionary<Control, float> fontesBase = [];
        Coletar(raiz, fontesBase);
        Aplicar(raiz, escala, fontesBase);
    }

    private static void Coletar(Control controle, Dictionary<Control, float> fontesBase)
    {
        fontesBase[controle] = controle.Font.Size;
        foreach (Control filho in controle.Controls)
        {
            Coletar(filho, fontesBase);
        }
    }

    private static void Aplicar(Control controle, float escala, Dictionary<Control, float> fontesBase)
    {
        controle.SuspendLayout();

        foreach (Control filho in controle.Controls)
        {
            Aplicar(filho, escala, fontesBase);
        }

        // Fonte: a partir do snapshot (não do valor já herdado/mutado). PictureBox não tem texto relevante.
        if (controle is not PictureBox && fontesBase.TryGetValue(controle, out float baseFont) && baseFont > 0f)
        {
            controle.Font = new Font(controle.Font.FontFamily, baseFont * escala, controle.Font.Style, GraphicsUnit.Point);
        }

        if (controle is DataGridView grid && fontesBase.TryGetValue(grid, out float gridFont) && gridFont > 0f)
        {
            EscalarGridFonte(grid, escala, gridFont);
        }

        controle.ResumeLayout(false);
    }

    // DataGridView é a região elástica da tela (Anchor/Dock): escala a FONTE de header/células e a altura de
    // linha/header para acomodar o texto. NÃO escala larguras de coluna (evita overflow horizontal; as colunas
    // usam preenchimento/redistribuição). Se faltar espaço, a própria grade rola internamente — nunca a janela.
    private static void EscalarGridFonte(DataGridView grid, float escala, float baseFont)
    {
        Font fonte = new(grid.Font.FontFamily, baseFont * escala, grid.Font.Style, GraphicsUnit.Point);
        grid.ColumnHeadersDefaultCellStyle.Font = fonte;
        grid.DefaultCellStyle.Font = fonte;
        grid.RowsDefaultCellStyle.Font = fonte;

        grid.ColumnHeadersHeight = Arredondar(grid.ColumnHeadersHeight * escala);
        grid.RowTemplate.Height = Arredondar(grid.RowTemplate.Height * escala);
    }

    private static int Arredondar(float valor) => (int)Math.Round(valor, MidpointRounding.AwayFromZero);
}
