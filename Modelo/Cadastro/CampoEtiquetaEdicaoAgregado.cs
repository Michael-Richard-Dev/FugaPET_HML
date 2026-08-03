namespace FugaPET_HML.Modelo.Cadastro;

public sealed record CampoEtiquetaEdicaoAgregado
{
    public CampoEtiquetaCadastro Campo { get; init; } = new();
    public MapeamentoCampoEtiquetaCadastro? MapeamentoAtivo { get; init; }
}
