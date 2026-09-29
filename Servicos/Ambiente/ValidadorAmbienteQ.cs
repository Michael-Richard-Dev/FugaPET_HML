using FugaPET_HML.AcessoDados.Banco;
using FugaPET_HML.Servicos.IntegracaoSap;

namespace FugaPET_HML.Servicos.Ambiente;

public static class ValidadorAmbienteQ
{
    public const string AmbienteEsperado = "Q";
    public const string VariavelAmbienteAppEnv = "FUGAPET_Q_APP_ENV";
    public const string SapHostEsperado = "vhfufqs4ci.sap.fugacouros.com.br";
    public const int SapPortaEsperada = 44300;
    public const string DatabaseEsperado = "fuga_jales_local_homologacao_q_v1_2";
    public const string SchemaEsperado = "homologacao";
    public const string RoleAplicacaoEsperada = "fugapet_q_app";

    public static ResultadoValidacaoAmbienteQ ValidarStartup(
        ConfiguracaoSap configuracaoSap,
        ConfiguracaoBancoPostgreSql configuracaoBanco,
        Func<string, string?> obterVariavelAmbiente)
    {
        ResultadoValidacaoAmbienteQ appEnv = ValidarAppEnv(obterVariavelAmbiente);
        if (!appEnv.Valido)
        {
            return appEnv;
        }

        ResultadoValidacaoAmbienteQ sap = ValidarSap(configuracaoSap);
        if (!sap.Valido)
        {
            return sap;
        }

        return ValidarBanco(configuracaoBanco);
    }

    public static ResultadoValidacaoAmbienteQ ValidarAppEnv(Func<string, string?> obterVariavelAmbiente)
    {
        string? valor = obterVariavelAmbiente(VariavelAmbienteAppEnv);
        return string.Equals(valor?.Trim(), AmbienteEsperado, StringComparison.OrdinalIgnoreCase)
            ? ResultadoValidacaoAmbienteQ.Ok()
            : ResultadoValidacaoAmbienteQ.Bloqueado($"Ambiente Q bloqueado: defina {VariavelAmbienteAppEnv}=Q.");
    }

    public static ResultadoValidacaoAmbienteQ ValidarSap(ConfiguracaoSap configuracao)
    {
        foreach (string url in ObterUrlsSap(configuracao))
        {
            if (!ValidarUriSap(url, out string mensagem))
            {
                return ResultadoValidacaoAmbienteQ.Bloqueado(mensagem);
            }
        }

        bool allowlistQ = configuracao.HostsPermitidos.Any(host =>
            string.Equals(host, SapHostEsperado, StringComparison.OrdinalIgnoreCase));
        bool allowlistDs = configuracao.HostsPermitidos.Any(host =>
            string.Equals(host, "vhfufds4ci.sap.fugacouros.com.br", StringComparison.OrdinalIgnoreCase));

        if (!allowlistQ || allowlistDs)
        {
            return ResultadoValidacaoAmbienteQ.Bloqueado("Ambiente Q bloqueado: allowlist SAP deve conter somente o host Q autorizado.");
        }

        string sapClient = configuracao.SapClient.Trim();
        if (sapClient.Length != 3 || !sapClient.All(char.IsDigit))
        {
            return ResultadoValidacaoAmbienteQ.Bloqueado("Ambiente Q bloqueado: sap-client Q dedicado ausente ou invalido.");
        }

        // GATE 112D — SAFE_HU_ONLY_STARTUP.
        // Todos os gates de escrita PERIGOSOS continuam obrigatoriamente false no startup. A ÚNICA exceção é
        // hu_write_habilitado, e apenas ISOLADA: ele autoriza exclusivamente POST /HandlingUnit no gateway de
        // HU (ProdutoAcabadoHandlingUnitSapGateway.EscritaHuPermitida recusa qualquer outro método/endpoint) e
        // NÃO habilita escrita SAP genérica, 101, 261, INT012 de palete nem o pipeline 261→101→HU.
        // Como qualquer um dos gates abaixo em true BLOQUEIA o startup, alcançar o retorno Ok() com
        // hu_write_habilitado=true prova que ele está isolado — o cenário HU-only é o único admitido.
        // Reforço de contrato existente: a própria composição do gateway de HU exige escrita_habilitada=false
        // (FabricaProdutoAcabadoHandlingUnitSapServico §13), então HU+genérica é incompatível nas duas camadas.
        string[] gatesPerigososLigados = ObterGatesPerigososLigados(configuracao);
        if (gatesPerigososLigados.Length > 0)
        {
            return ResultadoValidacaoAmbienteQ.Bloqueado(
                "Ambiente Q bloqueado: write gates devem iniciar false — ligado(s): "
                + string.Join(", ", gatesPerigososLigados)
                + ". Somente hu_write_habilitado pode iniciar true, e apenas isolado (HU-only).");
        }

        return ResultadoValidacaoAmbienteQ.Ok();
    }

    public static ResultadoValidacaoAmbienteQ ValidarBanco(ConfiguracaoBancoPostgreSql configuracao)
    {
        if (string.IsNullOrWhiteSpace(configuracao.Servidor)
            || string.Equals(configuracao.Servidor, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(configuracao.Servidor, "127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            return ResultadoValidacaoAmbienteQ.Bloqueado("Ambiente Q bloqueado: servidor PostgreSQL Q ausente ou local nao autorizado.");
        }

        if (string.IsNullOrWhiteSpace(configuracao.NomeBanco))
        {
            return ResultadoValidacaoAmbienteQ.Bloqueado("Ambiente Q bloqueado: database Q esperado nao configurado.");
        }

        if (string.IsNullOrWhiteSpace(configuracao.Schema))
        {
            return ResultadoValidacaoAmbienteQ.Bloqueado("Ambiente Q bloqueado: schema Q esperado nao configurado.");
        }

        if (!string.Equals(configuracao.NomeBanco, DatabaseEsperado, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(configuracao.Schema, SchemaEsperado, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(configuracao.Usuario, RoleAplicacaoEsperada, StringComparison.OrdinalIgnoreCase))
        {
            return ResultadoValidacaoAmbienteQ.Bloqueado("Ambiente Q bloqueado: database, schema ou role nao correspondem ao contrato Q.");
        }

        return ResultadoValidacaoAmbienteQ.Ok();
    }

    public static ResultadoValidacaoAmbienteQ ValidarIdentidadeBanco(
        ConfiguracaoBancoPostgreSql configuracao,
        string databaseReal,
        string schemaReal)
    {
        ResultadoValidacaoAmbienteQ contrato = ValidarBanco(configuracao);
        if (!contrato.Valido)
        {
            return contrato;
        }

        if (!string.Equals(configuracao.NomeBanco, databaseReal, StringComparison.Ordinal))
        {
            return ResultadoValidacaoAmbienteQ.Bloqueado("Ambiente Q bloqueado: database fisico diferente do database Q configurado.");
        }

        return string.Equals(configuracao.Schema, schemaReal, StringComparison.Ordinal)
            ? ResultadoValidacaoAmbienteQ.Ok()
            : ResultadoValidacaoAmbienteQ.Bloqueado("Ambiente Q bloqueado: schema fisico diferente do schema Q configurado.");
    }

    public static Task<ResultadoProbeBancoQ> CriarProbeBancoAsync(
        Func<CancellationToken, Task<ResultadoProbeBancoQ>> executarProbeReadOnly,
        CancellationToken cancellationToken = default)
        => executarProbeReadOnly(cancellationToken);

    /// <summary>
    /// GATE 112D: gates de escrita que NUNCA podem iniciar true, com o nome da chave para diagnóstico.
    /// <c>hu_write_habilitado</c> está DELIBERADAMENTE fora desta lista (SAFE_HU_ONLY_STARTUP): é o único
    /// gate de escrita que pode iniciar true, e só se todos os desta lista estiverem false.
    /// </summary>
    internal static string[] ObterGatesPerigososLigados(ConfiguracaoSap configuracao)
    {
        ArgumentNullException.ThrowIfNull(configuracao);

        List<string> ligados = [];
        if (configuracao.EscritaHabilitada)
        {
            ligados.Add("escrita_habilitada (escrita SAP genérica)");
        }

        if (configuracao.ProdutoAcabadoMaterialDocumentWriteHabilitado)
        {
            ligados.Add("pa_material_document_write_habilitado (101 de Produto Acabado)");
        }

        if (configuracao.PalletWriteHabilitado)
        {
            ligados.Add("pallet_write_habilitado (INT012 de palete)");
        }

        if (configuracao.ProdutoAcabadoPipelineHabilitado)
        {
            ligados.Add("pa_pipeline_habilitado (pipeline 261→101→HU)");
        }

        return [.. ligados];
    }

    private static IEnumerable<string> ObterUrlsSap(ConfiguracaoSap configuracao)
    {
        string[] urls =
        [
            configuracao.BaseUrl,
            configuracao.MaterialDocumentBaseUrl,
            configuracao.ProductionOrderBaseUrl,
            configuracao.ProductionOrderBaseUrlEfetiva,
            configuracao.ProductionOrderConfirmationBaseUrl,
            configuracao.ProductionOrderConfirmationBaseUrlEfetiva,
            configuracao.ProductBaseUrl,
            configuracao.ProductMasterBaseUrlEfetiva,
            configuracao.HandlingUnitBaseUrl,
            // GATE 095F: ProductionVersion removida do readiness do Controle (roteiro agora é V3 direto).
            configuracao.ProductionRoutingBaseUrlEfetiva
        ];

        return urls.Where(url => !string.IsNullOrWhiteSpace(url)).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static bool ValidarUriSap(string url, out string mensagem)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, SapHostEsperado, StringComparison.OrdinalIgnoreCase)
            || uri.Port != SapPortaEsperada)
        {
            mensagem = "Ambiente Q bloqueado: host/porta SAP efetivos nao correspondem ao Q autorizado.";
            return false;
        }

        mensagem = string.Empty;
        return true;
    }
}

public sealed record ResultadoValidacaoAmbienteQ(bool Valido, string Mensagem)
{
    public static ResultadoValidacaoAmbienteQ Ok() => new(true, string.Empty);
    public static ResultadoValidacaoAmbienteQ Bloqueado(string mensagem) => new(false, mensagem);
}

public sealed record ResultadoProbeBancoQ(string Database, string Schema);

