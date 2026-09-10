using System.Drawing;
using System.Windows.Forms;
using FugaPET_HML.Tela.Processo;

namespace FugaPET_HML.Tests.Tela;

/// <summary>
/// GATE 081/082-FIX1 — legibilidade "Recebimento de Mercadoria": prova que a escala é de FONTE apenas (2.0x),
/// NUNCA de geometria (que quebrava o viewport 1920x1080/100% do 081), e verifica a fiação na tela (branding
/// FugaPET, ícone laranja/traços brancos, status SAP destacado, sem AutoScroll, janela Maximizada, estrutura
/// responsiva TableLayoutPanel/Anchor preservada, escopo restrito ao modo, sem tocar regra funcional).
/// </summary>
public sealed class LegibilidadeRecebimentoMercadoria081Tests
{
    private static void ExecutarEmSta(Action acao)
    {
        Exception? erro = null;
        Thread t = new(() =>
        {
            try { acao(); }
            catch (Exception ex) { erro = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (erro is not null) throw erro;
    }

    private static string Ler(string arquivo)
    {
        string raiz = AppContext.BaseDirectory;
        for (int i = 0; i < 9; i++)
        {
            foreach (string cand in new[]
            {
                Path.Combine(raiz, "Tela", "Processo", arquivo),
                Path.Combine(raiz, "FugaPet_HML", "Tela", "Processo", arquivo)
            })
            {
                if (File.Exists(cand)) return File.ReadAllText(cand);
            }
            raiz = Directory.GetParent(raiz)?.FullName ?? raiz;
        }
        throw new FileNotFoundException(arquivo);
    }

    private static string FonteTela() => Ler("ProcessoEntradaProdutoForm.cs");
    private static string FonteDesigner() => Ler("ProcessoEntradaProdutoForm.Designer.cs");
    private static string FonteHelper() => Ler("LegibilidadeRecebimentoMercadoria.cs");

    // ---------------- escalador puro (FONTE apenas) ----------------

    // TEST_12: fonte 2.0x preservada. §6/§7: geometria NÃO é escalada (adapta-se pelo layout responsivo).
    [Fact]
    public void Escala_DobraFonte_PreservaGeometriaEIcone()
        => ExecutarEmSta(() =>
        {
            using Panel raiz = new() { Size = new Size(400, 300) };
            Label titulo = new() { Location = new Point(10, 10), Size = new Size(200, 24), Font = new Font("Segoe UI", 12F, FontStyle.Bold), AutoSize = false };
            Button botao = new() { Location = new Point(10, 50), Size = new Size(120, 30), Font = new Font("Segoe UI", 9F), AutoSize = false };
            TextBox campo = new() { Location = new Point(10, 90), Size = new Size(150, 24), Font = new Font("Segoe UI", 10F) };
            PictureBox icone = new() { Location = new Point(300, 10), Size = new Size(29, 29) };
            raiz.Controls.AddRange([titulo, botao, campo, icone]);

            LegibilidadeRecebimentoMercadoria.AplicarEscalaFonte(raiz, 2.0f);

            // Fonte dobrou...
            Assert.Equal(24F, titulo.Font.Size, 3);
            Assert.Equal(18F, botao.Font.Size, 3);
            Assert.Equal(20F, campo.Font.Size, 3);
            // ...mas geometria NÃO mudou (nem posição, nem tamanho).
            Assert.Equal(new Point(10, 10), titulo.Location);
            Assert.Equal(new Size(200, 24), titulo.Size);
            Assert.Equal(new Point(10, 50), botao.Location);
            Assert.Equal(new Point(300, 10), icone.Location);
            Assert.Equal(new Size(29, 29), icone.Size);
        });

    // TEST_12 (grid): fonte 2.0x + altura de linha/header; largura de coluna PRESERVADA (evita overflow horizontal).
    [Fact]
    public void Escala_DataGridView_FonteEAlturasDobram_LarguraColunaPreservada()
        => ExecutarEmSta(() =>
        {
            using Panel raiz = new() { Size = new Size(400, 300) };
            DataGridView grid = new()
            {
                Location = new Point(5, 5),
                Size = new Size(300, 200),
                Font = new Font("Segoe UI", 8F),
                ColumnHeadersHeight = 30
            };
            grid.RowTemplate.Height = 22;
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "c1", Width = 100 });
            raiz.Controls.Add(grid);

            LegibilidadeRecebimentoMercadoria.AplicarEscalaFonte(raiz, 2.0f);

            Assert.Equal(16F, grid.Font.Size, 3);
            Assert.Equal(16F, grid.DefaultCellStyle.Font!.Size, 3);
            Assert.Equal(16F, grid.ColumnHeadersDefaultCellStyle.Font!.Size, 3);
            Assert.Equal(60, grid.ColumnHeadersHeight);   // 30 -> 60
            Assert.Equal(44, grid.RowTemplate.Height);    // 22 -> 44
            Assert.Equal(100, grid.Columns[0].Width);     // largura preservada
            Assert.Equal(new Size(300, 200), grid.Size);  // geometria do grid preservada (elástica no runtime)
        });

    [Fact]
    public void Escala_NaoCompoePorHeranca()
        => ExecutarEmSta(() =>
        {
            using Panel raiz = new() { Font = new Font("Segoe UI", 10F) };
            Label herdeiro = new();
            raiz.Controls.Add(herdeiro);

            LegibilidadeRecebimentoMercadoria.AplicarEscalaFonte(raiz, 2.0f);

            Assert.Equal(20F, herdeiro.Font.Size, 3); // 10 -> 20, não 40
        });

    [Fact] // TEST_12: alvo de escala é 2.0x.
    public void Escala_ConstanteEhTwoX()
        => Assert.Equal(2.0f, LegibilidadeRecebimentoMercadoria.Escala);

    // ---------------- fiação na tela ----------------

    [Fact] // TEST_02/14/15: fonte 2.0x SEM geometria, SEM AutoScroll, janela Maximizada (cabe no viewport).
    public void Tela_FonteApenas_SemAutoScroll_Maximizada()
    {
        string src = FonteTela();
        Assert.Contains("LegibilidadeRecebimentoMercadoria.AplicarEscalaFonte(this, LegibilidadeRecebimentoMercadoria.Escala);", src, StringComparison.Ordinal);
        Assert.Contains("AutoScroll = false;", src, StringComparison.Ordinal);
        Assert.Contains("WindowState = FormWindowState.Maximized;", src, StringComparison.Ordinal);
        // regressão do defeito 081: nada de escala de geometria nem AutoScroll ligado / crescimento de janela.
        Assert.DoesNotContain("AplicarEscala(this", src, StringComparison.Ordinal);
        Assert.DoesNotContain("AutoScroll = true;", src, StringComparison.Ordinal);
        Assert.DoesNotContain("AutoScrollMinSize", src, StringComparison.Ordinal);
    }

    [Fact] // helper não mexe em Location/Size (só fonte + alturas de grid).
    public void Helper_NaoAlteraLocationNemSizeDeControles()
    {
        string h = FonteHelper();
        Assert.DoesNotContain(".Location = new Point", h, StringComparison.Ordinal);
        Assert.DoesNotContain(".Size = new Size", h, StringComparison.Ordinal);
        Assert.Contains("public const float Escala = 2.0f;", h, StringComparison.Ordinal);
    }

    [Fact] // TEST_01/03..11: a estrutura responsiva que garante o fit está preservada (Maximized + TLP + grid elástica).
    public void Designer_EstruturaResponsivaPreservada()
    {
        string d = FonteDesigner();
        Assert.Contains("AutoScaleMode = AutoScaleMode.Font;", d, StringComparison.Ordinal);
        Assert.Contains("WindowState = FormWindowState.Maximized;", d, StringComparison.Ordinal);
        Assert.Contains("rootTableLayoutPanel.Dock = DockStyle.Fill;", d, StringComparison.Ordinal);
        Assert.Contains("productionDataGridView.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;", d, StringComparison.Ordinal);
        // MinimumSize não excede o viewport alvo (1920x1080).
        Assert.Contains("MinimumSize = new Size(1180, 648);", d, StringComparison.Ordinal);
    }

    [Fact] // TEST_16/branding, TEST_19/20 escopo: só RecebimentoMercadoria; regra funcional intacta.
    public void Tela_Branding_Escopo_FuncionalIntacto()
    {
        string src = FonteTela();
        Assert.Contains("if (_modoEntrada != global::FugaPET_HML.Modelo.Processo.ModoEntradaMaterial.RecebimentoMercadoria)", src, StringComparison.Ordinal);
        Assert.Contains("companyLogoPictureBox.Image = global::FugaPET_HML.Properties.Resources.fuga_2026_logo;", src, StringComparison.Ordinal);
        Assert.Contains("_controller.ConsultarPedidoAsync(numeroPedido, _modoEntrada", src, StringComparison.Ordinal);
    }

    [Fact] // TEST_17: ícone laranja + traços brancos; sem vermelho/rosa.
    public void Tela_IconeLaranjaTracosBrancos_SemVermelhoRosa()
    {
        string src = FonteTela();
        int ini = src.IndexOf("private void AplicarLegibilidadeRecebimentoMercadoria()", StringComparison.Ordinal);
        int fim = src.IndexOf("PerformLayout();", ini, StringComparison.Ordinal);
        string corpo = src[ini..fim];
        Assert.Contains("headerTitleIconPanel.FillColor = LegibilidadeRecebimentoMercadoria.LaranjaFugaPet;", corpo, StringComparison.Ordinal);
        Assert.Contains("headerTitleIconPictureBox.Image = global::FugaPET_HML.Properties.Resources.production_title_icon;", corpo, StringComparison.Ordinal);
        Assert.DoesNotContain("Red", corpo, StringComparison.Ordinal);
        Assert.DoesNotContain("Pink", corpo, StringComparison.Ordinal);
        Assert.Contains("FromArgb(250, 105, 26)", FonteHelper(), StringComparison.Ordinal);
    }

    [Fact] // TEST_18: status SAP destacado (badge preenchido + texto branco).
    public void Tela_StatusSap_Destacado()
    {
        string src = FonteTela();
        Assert.Contains("sapStatusPanel.FillColor = LegibilidadeRecebimentoMercadoria.LaranjaFugaPetEscuro;", src, StringComparison.Ordinal);
        Assert.Contains("sapStatusLabel.ForeColor = Color.White;", src, StringComparison.Ordinal);
    }
}
