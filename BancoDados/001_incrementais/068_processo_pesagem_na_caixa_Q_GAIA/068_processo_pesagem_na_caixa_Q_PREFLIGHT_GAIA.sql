\set ON_ERROR_STOP on
-- ============================================================
-- 068_processo_pesagem_na_caixa_Q_PREFLIGHT_GAIA.sql
-- FugaPET | Ambientes QAS110 e QAS130 | Schema homologacao
-- Incremento 068 (Q) - PREFLIGHT (SOMENTE LEITURA) - nao altera nada
--
-- UM script para os DOIS bancos: o papel de aplicacao e escolhido pelo banco corrente.
-- ============================================================

\echo '== 068 Q PREFLIGHT =='

SELECT current_database() AS banco, current_user AS usuario;

\echo '-- alvo reconhecido? (esperado: t)'
SELECT current_database() IN (
           'fuga_jales_local_homologacao_q_v1_2',
           'fuga_jales_local_homologacao_q130_v1_3') AS alvo_reconhecido;

\echo '-- papel de aplicacao deste banco'
SELECT CASE current_database()
           WHEN 'fuga_jales_local_homologacao_q_v1_2'    THEN 'fugapet_q_app'
           WHEN 'fuga_jales_local_homologacao_q130_v1_3' THEN 'fugapet_q130_app'
       END AS papel_esperado;

\echo '-- a coluna ja existe? (esperado: f)'
SELECT EXISTS (
    SELECT 1 FROM information_schema.columns
     WHERE table_schema='homologacao' AND table_name='hu_caixa' AND column_name='processo_pesagem'
) AS coluna_ja_existe;

\echo '-- o indice de hoje, que e o que este incremento troca'
SELECT indexdef FROM pg_indexes
 WHERE schemaname='homologacao' AND indexname='uq_hu_caixa_terminal_ativo';

\echo '-- quantas caixas estao ATIVAS pela regra de hoje'
SELECT count(*) AS ativas_hoje
  FROM homologacao.hu_caixa
 WHERE status_hu_caixa NOT IN ('CONFIRMADA_SAP','CANCELADA');

\echo '-- ha terminal com MAIS DE UMA ativa? (esperado: 0 linhas; o indice de hoje impede)'
SELECT upper(btrim(terminal)) AS terminal, count(*) AS ativas
  FROM homologacao.hu_caixa
 WHERE status_hu_caixa NOT IN ('CONFIRMADA_SAP','CANCELADA')
 GROUP BY 1 HAVING count(*) > 1 ORDER BY 2 DESC;

\echo '-- privilegios de COLUNA do papel (esta base concede por coluna, nao por tabela)'
SELECT privilege_type, count(*) AS colunas
  FROM information_schema.role_column_grants
 WHERE table_schema='homologacao' AND table_name='hu_caixa'
   AND grantee = CASE current_database()
       WHEN 'fuga_jales_local_homologacao_q_v1_2'    THEN 'fugapet_q_app'
       WHEN 'fuga_jales_local_homologacao_q130_v1_3' THEN 'fugapet_q130_app'
   END
 GROUP BY 1 ORDER BY 1;
