using System.Drawing;
using System.Windows.Forms;
using FugaPET_HML.Tela.Processo;

namespace FugaPET_HML.Tests.Tela;

/// <summary>
/// GATE 081 — legibilidade "Recebimento de Mercadoria": prova o escalador puro 2.0x (fontes+geometria, ícones
/// preservados, grid tratado) e, por varredura de fonte, a fiação na tela (branding FugaPET, ícone laranja/traços
/// brancos, status SAP destacado, escala 2.0x, escopo restrito ao modo, sem tocar regra funcional).
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

    private static string FonteTela()
    {
        string raiz = AppContext.BaseDirectory;
        for (int i = 0; i < 9; i++)
        {
            foreach (string cand in new[]
            {
                Path.Combine(raiz, "Tela", "Processo", "ProcessoEntradaProdutoForm.cs"),
                Path.Combine(raiz, "FugaPet_HML", "Tela", "Processo", "ProcessoEntradaProdutoForm.cs")
            })
            {
                if (File.Exists(cand)) return File.ReadAllText(cand);
            }
            raiz = Directory.GetParent(raiz)?.FullName ?? raiz;
        }
        throw new FileNotFoundException("ProcessoEntradaProdutoForm.cs");
    }

    // ---------------- escalador puro (mecanismo 2.0x) ----------------

    // TEST_08..TEST_11/TEST_14/TEST_15: título, valores, labels, laterais, rodapé — todos ~2.0x.
    // TEST_13: botões ~2.0x. TEST_16/TEST_17: geometria uniforme acomoda sem estourar o pai.
    [Fact]
    public void Escala_DobraFontesEGeometria_PreservaIcone()
        => ExecutarEmSta(() =>
        {
            using Panel raiz = new() { Size = new Size(400, 300) };
            Label titulo = new() { Location = new Point(10, 10), Size = new Size(200, 24), Font = new Font("Segoe UI", 12F, FontStyle.Bold) };
            Button botao = new() { Location = new Point(10, 50), Size = new Size(120, 30), Font = new Font("Segoe UI", 9F) };
            TextBox campo = new() { Location = new Point(10, 90), Size = new Size(150, 24), Font = new Font("Segoe UI", 10F) };
            PictureBox icone = new() { Location = new Point(300, 10), Size = new Size(29, 29) };
            raiz.Controls.AddRange([titulo, botao, campo, icone]);

            LegibilidadeRecebimentoMercadoria.AplicarEscala(raiz, 2.0f);

            Assert.Equal(24F, titulo.Font.Size, 3);   // 12 -> 24
            Assert.Equal(18F, botao.Font.Size, 3);    // 9  -> 18
            Assert.Equal(20F, campo.Font.Size, 3);    // 10 -> 20
            Assert.Equal(new Size(400, 48), titulo.Size);   // geometria dobrada
            Assert.Equal(new Point(20, 20), titulo.Location);
            Assert.Equal(new Point(20, 100), botao.Location);
            // §9: ícone mantém o tamanho (só reposiciona).
            Assert.Equal(new Size(29, 29), icone.Size);
            Assert.Equal(new Point(600, 20), icone.Location);
        });

    // TEST_12: DataGridView (headers + células + alturas + colunas) ~2.0x.
    [Fact]
    public void Escala_DataGridView_DobraFontesEAlturas()
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
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "c1", Width = 100, MinimumWidth = 40 });
            raiz.Controls.Add(grid);

            LegibilidadeRecebimentoMercadoria.AplicarEscala(raiz, 2.0f);

            Assert.Equal(16F, grid.Font.Size, 3);
            Assert.Equal(16F, grid.DefaultCellStyle.Font!.Size, 3);
            Assert.Equal(16F, grid.ColumnHeadersDefaultCellStyle.Font!.Size, 3);
            Assert.Equal(60, grid.ColumnHeadersHeight);   // 30 -> 60
            Assert.Equal(44, grid.RowTemplate.Height);    // 22 -> 44
            Assert.Equal(200, grid.Columns[0].Width);     // 100 -> 200
            Assert.Equal(80, grid.Columns[0].MinimumWidth); // 40 -> 80
        });

    // Herança de fonte não compõe (pai escalado não infla filho que herda).
    [Fact]
    public void Escala_NaoCompoePorHeranca()
        => ExecutarEmSta(() =>
        {
            using Panel raiz = new() { Font = new Font("Segoe UI", 10F) };
            Label herdeiro = new(); // sem Font explícito => herda 10
            raiz.Controls.Add(herdeiro);

            LegibilidadeRecebimentoMercadoria.AplicarEscala(raiz, 2.0f);

            Assert.Equal(20F, herdeiro.Font.Size, 3); // 10 -> 20, não 40
        });

    [Fact]
    public void Escala_ConstanteEhTwoX()
        => Assert.Equal(2.0f, LegibilidadeRecebimentoMercadoria.Escala);

    // ---------------- fiação na tela (varredura de fonte) ----------------

    [Fact] // TEST_18/escopo: a legibilidade é aplicada por último e SÓ no modo Recebimento de Mercadoria.
    public void Tela_AplicaLegibilidade_SomenteRecebimentoMercadoria()
    {
        string src = FonteTela();
        Assert.Contains("AplicarLegibilidadeRecebimentoMercadoria();", src, StringComparison.Ordinal);
        Assert.Contains("if (_modoEntrada != global::FugaPET_HML.Modelo.Processo.ModoEntradaMaterial.RecebimentoMercadoria)", src, StringComparison.Ordinal);
        // regra funcional intacta (consulta parametrizada por modo permanece).
        Assert.Contains("_controller.ConsultarPedidoAsync(numeroPedido, _modoEntrada", src, StringComparison.Ordinal);
    }

    [Fact] // TEST_01/02: branding FugaPET aplicado (logo aprovado), substituindo o branding local.
    public void Tela_UsaBrandingFugaPet()
    {
        string src = FonteTela();
        Assert.Contains("companyLogoPictureBox.Image = global::FugaPET_HML.Properties.Resources.fuga_2026_logo;", src, StringComparison.Ordinal);
        Assert.Contains("\"FUGAPET_LOGO\"", src, StringComparison.Ordinal);
    }

    [Fact] // TEST_03/04/05: ícone laranja + traços brancos (asset aprovado); sem vermelho/rosa.
    public void Tela_IconeLaranjaTracosBrancos_SemVermelhoRosa()
    {
        string src = FonteTela();
        int ini = src.IndexOf("private void AplicarLegibilidadeRecebimentoMercadoria()", StringComparison.Ordinal);
        Assert.True(ini >= 0);
        int fim = src.IndexOf("ResumeLayout(true);", ini, StringComparison.Ordinal);
        string corpo = src[ini..fim];

        Assert.Contains("headerTitleIconPanel.FillColor = LegibilidadeRecebimentoMercadoria.LaranjaFugaPet;", corpo, StringComparison.Ordinal);
        Assert.Contains("headerTitleIconPictureBox.Image = global::FugaPET_HML.Properties.Resources.production_title_icon;", corpo, StringComparison.Ordinal);
        Assert.Contains("\"FUGAPET_ICON_BRANCO\"", corpo, StringComparison.Ordinal);
        Assert.DoesNotContain("Red", corpo, StringComparison.Ordinal);
        Assert.DoesNotContain("Pink", corpo, StringComparison.Ordinal);
        Assert.DoesNotContain("Rosa", corpo, StringComparison.Ordinal);
        // laranja FugaPET definido no tema congelado.
        Assert.Contains("FromArgb(250, 105, 26)", FonteHelper(), StringComparison.Ordinal);
    }

    [Fact] // TEST_06/07: status SAP presente e com destaque alto (badge preenchido laranja + texto branco).
    public void Tela_StatusSap_Destacado()
    {
        string src = FonteTela();
        Assert.Contains("sapStatusPanel.FillColor = LegibilidadeRecebimentoMercadoria.LaranjaFugaPetEscuro;", src, StringComparison.Ordinal);
        Assert.Contains("sapStatusLabel.ForeColor = Color.White;", src, StringComparison.Ordinal);
        Assert.Contains("\"SAP_STATUS_DESTAQUE\"", src, StringComparison.Ordinal);
    }

    [Fact] // TEST_08..TEST_15: a escala aplicada à tela é exatamente 2.0x sobre toda a subárvore.
    public void Tela_AplicaEscala2x()
    {
        string src = FonteTela();
        Assert.Contains("LegibilidadeRecebimentoMercadoria.AplicarEscala(this, LegibilidadeRecebimentoMercadoria.Escala);", src, StringComparison.Ordinal);
        // §9/§10: janela cresce com rolagem, sem reduzir a fonte abaixo de 2.0x.
        Assert.Contains("AutoScroll = true;", src, StringComparison.Ordinal);
        Assert.Contains("AutoScrollMinSize = alvo;", src, StringComparison.Ordinal);
    }

    private static string FonteHelper()
    {
        string raiz = AppContext.BaseDirectory;
        for (int i = 0; i < 9; i++)
        {
            foreach (string cand in new[]
            {
                Path.Combine(raiz, "Tela", "Processo", "LegibilidadeRecebimentoMercadoria.cs"),
                Path.Combine(raiz, "FugaPet_HML", "Tela", "Processo", "LegibilidadeRecebimentoMercadoria.cs")
            })
            {
                if (File.Exists(cand)) return File.ReadAllText(cand);
            }
            raiz = Directory.GetParent(raiz)?.FullName ?? raiz;
        }
        throw new FileNotFoundException("LegibilidadeRecebimentoMercadoria.cs");
    }
}
