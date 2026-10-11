using FugaPET_HML.Modelo.IntegracaoSap;

namespace FugaPET_HML.Servicos.IntegracaoSap;

/// <summary>
/// GATE 126V: resolve a tara FISICA do material de embalagem do palete a partir da configuracao operacional.
/// Funcao PURA: nao le arquivo, nao abre HTTP, nao toca banco — recebe o mapeamento ja carregado.
/// <para>
/// FAIL-CLOSED em todos os desfechos que nao sejam "um unico mapeamento valido": ausencia, ambiguidade,
/// unidade diferente de KG, valor negativo ou precisao acima do contrato NUNCA viram zero nem valor assumido.
/// </para>
/// </summary>
public static class ResolvedorTaraFisicaEmbalagemPalete
{
    /// <summary>Unica unidade aceita. Conversao silenciosa de outra unidade e PROIBIDA.</summary>
    public const string UnidadeExigida = "KG";

    /// <summary>Precisao do contrato SAP de peso (identica a usada no 261).</summary>
    public const int CasasDecimaisContratoSap = 3;

    public static ResultadoTaraFisicaEmbalagem Resolver(
        IReadOnlyList<TaraFisicaEmbalagemConfigurada>? mapeamentos,
        string? sapClientAtivo,
        string? centro,
        string? materialEmbalagem)
    {
        string mandante = (sapClientAtivo ?? string.Empty).Trim();
        string plant = (centro ?? string.Empty).Trim();
        string material = (materialEmbalagem ?? string.Empty).Trim();

        if (mandante.Length == 0)
        {
            return ResultadoTaraFisicaEmbalagem.NaoConfigurada(
                "mandante SAP ativo nao identificado — o mapeamento de tara fisica e restrito por mandante.");
        }

        if (plant.Length == 0 || material.Length == 0)
        {
            return ResultadoTaraFisicaEmbalagem.NaoConfigurada(
                "centro ou material de embalagem do palete ausentes — chave logica incompleta.");
        }

        if (mapeamentos is null || mapeamentos.Count == 0)
        {
            return ResultadoTaraFisicaEmbalagem.NaoConfigurada(
                "nenhum mapeamento de tara fisica de embalagem declarado na configuracao operacional.");
        }

        TaraFisicaEmbalagemConfigurada[] candidatos = [.. mapeamentos.Where(mapa =>
            Igual(mapa.SapClient, mandante)
            && Igual(mapa.Centro, plant)
            && Igual(mapa.MaterialEmbalagem, material))];

        if (candidatos.Length == 0)
        {
            return ResultadoTaraFisicaEmbalagem.NaoConfigurada(
                "nao ha mapeamento para o mandante, centro e material de embalagem deste palete. "
                + "Ausencia de configuracao NAO equivale a tara zero.");
        }

        if (candidatos.Length > 1)
        {
            return ResultadoTaraFisicaEmbalagem.Ambigua(
                "ha mais de um mapeamento para a mesma chave logica (mandante + centro + material). "
                + "Ambiguidade nao e resolvida por escolha automatica.");
        }

        TaraFisicaEmbalagemConfigurada unico = candidatos[0];

        if (!Igual(unico.Unidade, UnidadeExigida))
        {
            return ResultadoTaraFisicaEmbalagem.Invalida(
                $"unidade declarada diferente de {UnidadeExigida} — conversao automatica de unidade e proibida.");
        }

        if (unico.PesoKg < 0m)
        {
            return ResultadoTaraFisicaEmbalagem.Invalida("peso negativo nao e admitido.");
        }

        if (decimal.Round(unico.PesoKg, CasasDecimaisContratoSap, MidpointRounding.AwayFromZero) != unico.PesoKg)
        {
            return ResultadoTaraFisicaEmbalagem.Invalida(
                $"peso com precisao acima do contrato ({CasasDecimaisContratoSap} casas decimais).");
        }

        return ResultadoTaraFisicaEmbalagem.Resolvida(unico.PesoKg);
    }

    private static bool Igual(string? esquerda, string direita)
        => string.Equals((esquerda ?? string.Empty).Trim(), direita, StringComparison.OrdinalIgnoreCase);
}
