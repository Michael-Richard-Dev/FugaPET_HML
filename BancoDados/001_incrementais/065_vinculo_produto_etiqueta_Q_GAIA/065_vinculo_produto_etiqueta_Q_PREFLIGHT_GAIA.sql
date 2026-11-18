\set ON_ERROR_STOP on
-- ============================================================
-- 065_vinculo_produto_etiqueta_Q_PREFLIGHT_GAIA.sql
-- FugaPET | Ambientes QAS110 e QAS130 | Schema homologacao
-- Incremento 065 (Q) - Vinculo de produto acabado a etiqueta e processo
-- PREFLIGHT (SOMENTE LEITURA) - nao altera nada
--
-- O NUMERO 065 espelha o incremento da linha DEV, de proposito: e o mesmo objeto,
-- e parear pelo numero vale mais do que seguir a sequencia local do Q (que no
-- banco ja passou do 056 que aparece no repositorio).
--
-- UM script para os DOIS bancos: o papel de aplicacao e escolhido pelo banco
-- corrente, e qualquer outro alvo e recusado.
-- ============================================================

\echo '== 065 Q PREFLIGHT =='

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
SELECT to_regclass('homologacao.produto_etiqueta_vinculo') IS NOT NULL AS tabela_ja_existe;

\echo '-- dependencia: tabela etiqueta (esperado: t)'
SELECT to_regclass('homologacao.etiqueta') IS NOT NULL AS etiqueta_existe;

\echo '-- coluna da chave de etiqueta (esperado: 1 linha, bigint)'
SELECT column_name, data_type
  FROM information_schema.columns
 WHERE table_schema='homologacao' AND table_name='etiqueta' AND column_name='codigo_etiqueta';

\echo '-- quantas etiquetas cadastradas, e quantas do tipo CAIXA (as que a tela oferece)'
SELECT count(*) AS etiquetas,
       count(*) FILTER (WHERE tipo_etiqueta = 'CAIXA' AND situacao_etiqueta) AS caixa_ativas
  FROM homologacao.etiqueta;
