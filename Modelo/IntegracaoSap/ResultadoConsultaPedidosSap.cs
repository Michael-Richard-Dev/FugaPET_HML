namespace FugaPET_HML.Modelo.IntegracaoSap;

public sealed record ResultadoConsultaPedidosSap(
    IReadOnlyList<PedidoCompraSap> Pedidos,
    int PaginasProcessadas,
    int ItensProcessados);
