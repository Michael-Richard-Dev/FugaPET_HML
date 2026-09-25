namespace FugaPET_HML.Modelo.Processo;

public sealed class ProdutoAcabadoOrdem
{
    public string NumeroOrdem { get; init; } = string.Empty;
    public string MaterialProduzido { get; init; } = string.Empty;
    public string DescricaoMaterial { get; init; } = string.Empty;
    public string Centro { get; init; } = string.Empty;
    public string DepositoDestino { get; init; } = string.Empty;
    public decimal QuantidadePlanejada { get; init; }
    public decimal QuantidadeEntregue { get; init; }
    public decimal QuantidadePendente { get; init; }
    public string Unidade { get; init; } = "KG";
    public string Lote { get; init; } = string.Empty;
    public string ItemOrdem { get; init; } = string.Empty;
    public string Operacao { get; init; } = string.Empty;
    public string StatusOrdem { get; init; } = string.Empty;
    public bool Liberada { get; init; }
    public bool EncerradaOuDeletada { get; init; }
    public IReadOnlyList<ProdutoAcabadoComponenteOrdem> Componentes { get; init; } = [];
}

public sealed record ProdutoAcabadoComponenteOrdem
{
    public string Material { get; init; } = string.Empty;
    public string Centro { get; init; } = string.Empty;
    public string Deposito { get; init; } = string.Empty;
    public decimal QuantidadeNecessaria { get; init; }
    public string Unidade { get; init; } = string.Empty;
    public string Reserva { get; init; } = string.Empty;
    public string ItemReserva { get; init; } = string.Empty;
    public string Lote { get; init; } = string.Empty;
    public string TipoMovimento { get; init; } = string.Empty;

    /// <summary>
    /// GATE 107N: metadata SAP TRI-STATE do componente, propagada sem perda até a origem do pipeline para
    /// uso EXCLUSIVO da decisão do futuro allocator 261. null nos campos = DESCONHECIDO (nunca false/0).
    /// Nenhum cálculo/rateio é feito aqui: este modelo apenas TRANSPORTA.
    /// </summary>
    public FugaPET_HML.Modelo.IntegracaoSap.MetadataAlocacao261Sap MetadataAlocacao261 { get; init; } = new();
}
