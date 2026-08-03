namespace FugaPET_HML.Servicos.IntegracaoSap;

/// <summary>Resultado de uma atualizacao (PATCH) enviada ao SAP.</summary>
public sealed record ResultadoPatchSap(bool Sucesso, int? HttpStatus, string Mensagem);
