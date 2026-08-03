namespace FugaPET_HML.Servicos.IntegracaoSap;

public sealed class IntegracaoSapBloqueadaException : InvalidOperationException
{
    public IntegracaoSapBloqueadaException(string mensagem)
        : base(mensagem)
    {
    }
}
