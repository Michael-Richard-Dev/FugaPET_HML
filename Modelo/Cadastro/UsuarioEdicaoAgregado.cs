namespace FugaPET_HML.Modelo.Cadastro;

public sealed record UsuarioEdicaoAgregado
{
    public UsuarioCadastro Usuario { get; init; } = new();
    public long? IdPerfilAcessoAtivo { get; init; }
    public long? IdSetorPadraoAtivo { get; init; }
}
