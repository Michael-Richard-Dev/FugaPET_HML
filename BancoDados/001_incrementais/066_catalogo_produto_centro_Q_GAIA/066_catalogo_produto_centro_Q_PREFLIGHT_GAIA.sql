\set ON_ERROR_STOP on
-- ============================================================
-- 066_catalogo_produto_centro_Q_PREFLIGHT_GAIA.sql
-- FugaPET | Ambientes QAS110 e QAS130 | Schema homologacao
-- Incremento 066 (Q) - Catalogo LOCAL de produtos do centro
-- PREFLIGHT (SOMENTE LEITURA) - nao altera nada
--
-- O NUMERO 066 espelha o incremento da linha DEV, de proposito: e o mesmo objeto,
-- e parear pelo numero vale mais do que seguir a sequencia local do Q.
--
-- UM script para os DOIS bancos: o papel de aplicacao e escolhido pelo banco
-- corrente, e qualquer outro alvo e recusado.
-- ============================================================

\echo '== 066 Q PREFLIGHT =='

SELECT current_database() AS banco, current_schema() AS schema_corrente, current_user AS usuario;

\echo '-- alvo reconhecido? (esperado: t)'
SELECT current_database() IN (
           'fuga_jales_local_homologacao_q_v1_2',
           'fuga_jales_local_homologacao_q130_v1_3') AS alvo_reconhecido;

\echo '-- papel de aplicacao esperado neste banco'
SELECT CASE current_database()
           WHEN 'fuga_jales_local_homologacao_q_v1_2'    THEN 'fugapet_q_app'
           WHEN 'fuga_jales_local_homologacao_q130_v1_3' THEN 'fugapet_q130_app'
       END AS papel_esperado;

\echo '-- o papel existe? (esperado: t)'
SELECT EXISTS (
    SELECT 1 FROM pg_roles WHERE rolname = CASE current_database()
        WHEN 'fuga_jales_local_homologacao_q_v1_2'    THEN 'fugapet_q_app'
        WHEN 'fuga_jales_local_homologacao_q130_v1_3' THEN 'fugapet_q130_app'
    END) AS papel_existe;

\echo '-- a tabela ja existe? (esperado: f)'
SELECT to_regclass('homologacao.produto_catalogo_centro') IS NOT NULL AS tabela_ja_existe;

\echo '-- o vinculo (065) ja esta aplicado? e independente, mas convem conferir a frente'
SELECT to_regclass('homologacao.produto_etiqueta_vinculo') IS NOT NULL AS vinculo_existe;

\echo '-- quantos vinculos ja decididos (eles NAO sao tocados por este incremento)'
SELECT count(*) AS vinculos FROM homologacao.produto_etiqueta_vinculo;
