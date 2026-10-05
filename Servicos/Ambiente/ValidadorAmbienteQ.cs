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
        => ValidarStartup(configuracaoSap, configuracaoBanco, obterVariavelAmbiente, perfilAmbiente: null);

    /// <summary>
    /// GATE 120G: startup com o perfil de ambiente LOCAL, para o EXE iniciado por duplo clique.
    /// Perfil null/nao reconhecido mantem o comportamento anterior (exige FUGAPET_Q_APP_ENV=Q).
    /// </summary>
    public static ResultadoValidacaoAmbienteQ ValidarStartup(
        ConfiguracaoSap configuracaoSap,
        ConfiguracaoBancoPostgreSql configuracaoBanco,
        Func<string, string?> obterVariavelAmbiente,
        PerfilAmbienteLocal? perfilAmbiente)
    {
        ResultadoValidacaoAmbienteQ appEnv = ValidarAppEnv(obterVariavelAmbiente, perfilAmbiente);
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
        => ValidarAppEnv(obterVariavelAmbiente, perfilAmbiente: null);

    /// <summary>
    /// GATE 120G: o ambiente passa a poder ser determinado pela configuracao LOCAL, nao apenas por
    /// variavel de ambiente criada por script externo. Precedencia:
    /// <list type="number">
    /// <item>FUGAPET_Q_APP_ENV presente ⇒ decide (e precisa valer Q);</item>
    /// <item>ausente ⇒ perfil local Q;</item>
    /// <item>nenhum dos dois ⇒ BLOQUEADO (fail-closed).</item>
    /// </list>
    /// AMBIGUIDADE e tratada como bloqueio: variavel presente com valor != Q NAO e "corrigida" pelo
    /// perfil local, mesmo que o perfil diga Q.
    /// </summary>
    public static ResultadoValidacaoAmbienteQ ValidarAppEnv(
        Func<string, string?> obterVariavelAmbiente,
        PerfilAmbienteLocal? perfilAmbiente)
    {
        ArgumentNullException.ThrowIfNull(obterVariavelAmbiente);

        string valor = obterVariavelAmbiente(VariavelAmbienteAppEnv)?.Trim() ?? string.Empty;
        if (valor.Length > 0)
        {
            return string.Equals(valor, AmbienteEsperado, StringComparison.OrdinalIgnoreCase)
                ? ResultadoValidacaoAmbienteQ.Ok()
                : ResultadoValidacaoAmbienteQ.Bloqueado(
                    $"Ambiente Q bloqueado: {VariavelAmbienteAppEnv} presente com valor diferente de Q. "
                    + "O perfil local NAO sobrepoe um ambiente declarado explicitamente.");
        }

        if (perfilAmbiente?.EhQ == true)
        {
            return ResultadoValidacaoAmbienteQ.Ok();
        }

        return ResultadoValidacaoAmbienteQ.Bloqueado(
            $"Ambiente Q bloqueado: defina {VariavelAmbienteAppEnv}=Q ou declare "
            + $"\"{PerfilAmbienteLocal.SecaoAmbiente}\": {{ \"{PerfilAmbienteLocal.ChavePerfil}\": "
            + $"\"{PerfilAmbienteLocal.NomePerfilQ}\" }} na configuracao local.");
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

        // GATES 112D / 113E — modos de escrita ADMITIDOS no startup do Q, e SOMENTE eles.
        // Gates SEMPRE proibidos (nenhum modo os admite):
        //   escrita_habilitada (escrita SAP genérica) e pallet_write_habilitado (INT012 de palete).
        // Modos seguros:
        //   SAFE_HU_ONLY_STARTUP (112D) ..... hu_write isolado. Autoriza exclusivamente POST /HandlingUnit
        //       (EscritaHuPermitida recusa qualquer outro método/endpoint); nada de 261/101/INT012/pipeline.
        //   SAFE_PA_PIPELINE_STARTUP (113E) . pa_pipeline + pa_material_document + hu_write, os TRÊS juntos,
        //       porque o pipeline é uma cadeia ESTRITA 261→101→HU: habilitar parte dela é mais perigoso que
        //       nada — pa_material_document sozinho liberaria os gateways 261/101 fora da orquestração, e
        //       pipeline sem hu_write postaria 261+101 para travar na terceira etapa.
        // Qualquer combinação diferente ⇒ BLOCK.
        string[] gatesProibidosLigados = ObterGatesPerigososLigados(configuracao);
        if (gatesProibidosLigados.Length > 0)
        {
            return ResultadoValidacaoAmbienteQ.Bloqueado(
                "Ambiente Q bloqueado: write gates devem iniciar false — ligado(s): "
                + string.Join(", ", gatesProibidosLigados)
                + ". Apenas hu_write_habilitado isolado (HU-only) ou o trio "
                + "pa_pipeline_habilitado + pa_material_document_write_habilitado + hu_write_habilitado "
                + "(pipeline 261→101→HU) podem iniciar true.");
        }

        return ValidarModoEscritaAdmitido(configuracao);
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
    /// GATES 112D/113E: gates de escrita que NUNCA podem iniciar true em NENHUM modo.
    /// <c>hu_write_habilitado</c>, <c>pa_pipeline_habilitado</c> e
    /// <c>pa_material_document_write_habilitado</c> estão DELIBERADAMENTE fora desta lista: eles participam
    /// dos modos seguros e são validados como COMBINAÇÃO por <see cref="ValidarModoEscritaAdmitido"/>.
    /// </summary>
    internal static string[] ObterGatesPerigososLigados(ConfiguracaoSap configuracao)
    {
        ArgumentNullException.ThrowIfNull(configuracao);

        List<string> ligados = [];
        if (configuracao.EscritaHabilitada)
        {
            ligados.Add("escrita_habilitada (escrita SAP genérica)");
        }

        if (configuracao.PalletWriteHabilitado)
        {
            ligados.Add("pallet_write_habilitado (INT012 de palete)");
        }

        return [.. ligados];
    }

    /// <summary>
    /// GATE 113E: valida a COMBINAÇÃO dos gates que participam dos modos seguros. Pressupõe que os gates
    /// sempre proibidos já foram reprovados. Só dois modos passam:
    /// SAFE_HU_ONLY_STARTUP (hu isolado, ou nenhum gate) e
    /// SAFE_PA_PIPELINE_STARTUP (pa_pipeline + pa_material_document + hu_write, os três juntos).
    /// Combinação parcial é recusada NOMEANDO o que falta — habilitar meia cadeia 261→101→HU é pior que
    /// não habilitar.
    /// </summary>
    internal static ResultadoValidacaoAmbienteQ ValidarModoEscritaAdmitido(ConfiguracaoSap configuracao)
    {
        ArgumentNullException.ThrowIfNull(configuracao);

        bool pipeline = configuracao.ProdutoAcabadoPipelineHabilitado;
        bool materialDocumentPa = configuracao.ProdutoAcabadoMaterialDocumentWriteHabilitado;
        bool hu = configuracao.HuWriteHabilitado;

        // Modo 1 (112D): nenhum gate, ou hu_write isolado.
        if (!pipeline && !materialDocumentPa)
        {
            return ResultadoValidacaoAmbienteQ.Ok();
        }

        // Modo 2 (113E): o trio COMPLETO do pipeline.
        if (pipeline && materialDocumentPa && hu)
        {
            return ResultadoValidacaoAmbienteQ.Ok();
        }

        List<string> faltantes = [];
        if (!pipeline)
        {
            faltantes.Add("pa_pipeline_habilitado");
        }

        if (!materialDocumentPa)
        {
            faltantes.Add("pa_material_document_write_habilitado");
        }

        if (!hu)
        {
            faltantes.Add("hu_write_habilitado");
        }

        return ResultadoValidacaoAmbienteQ.Bloqueado(
            "Ambiente Q bloqueado: combinacao de write gates nao admitida — o pipeline 261→101→HU exige "
            + "pa_pipeline_habilitado + pa_material_document_write_habilitado + hu_write_habilitado juntos; "
            + "faltando: " + string.Join(", ", faltantes)
            + ". Alternativa admitida: hu_write_habilitado isolado (HU-only).");
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

