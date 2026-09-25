\set ON_ERROR_STOP on

-- ============================================================================
-- FugaPET Q - Migration 056
-- Produto Acabado - multiplas caixas pos-pesagem por terminal
-- VALIDACAO GAIA
--
-- GATE:
-- FUGAPET-Q-PRODUTO-ACABADO-DEFER-SAP-SEQUENTIAL-INDEX-MIGRATION-107K
--
-- OFFLINE PACKAGE. ESTE ARQUIVO NAO FOI EXECUTADO POR GAIA.
-- ============================================================================

BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL lock_timeout='5s';
SET LOCAL statement_timeout='90s';
SET LOCAL search_path=pg_catalog, homologacao;

DO $validation$
DECLARE v_count integer;
BEGIN
    IF current_database() <> 'fuga_jales_local_homologacao_q_v1_2' THEN
        RAISE EXCEPTION '056 VALIDACAO: database incorreto';
    END IF;

    SELECT count(*) INTO v_count
      FROM pg_index i
      JOIN pg_class ix ON ix.oid=i.indexrelid
     WHERE ix.oid='homologacao.uq_hu_caixa_terminal_ativo'::regclass
       AND i.indisunique AND i.indisvalid AND i.indisready
       AND pg_get_expr(i.indexprs,i.indrelid,true)='upper(btrim(terminal::text))'
       AND pg_get_expr(i.indpred,i.indrelid,true)='status_hu_caixa::text = ''EM_PESAGEM''::text';
    IF v_count<>1 THEN RAISE EXCEPTION '056 VALIDACAO: target index invalido'; END IF;

    SELECT count(*) INTO v_count
      FROM (
        SELECT upper(btrim(terminal::text))
          FROM homologacao.hu_caixa
         WHERE status_hu_caixa='EM_PESAGEM'
         GROUP BY upper(btrim(terminal::text))
        HAVING count(*)>1
      ) x;
    IF v_count<>0 THEN RAISE EXCEPTION '056 VALIDACAO: conflito EM_PESAGEM'; END IF;

    SELECT count(*) INTO v_count
      FROM pg_indexes
     WHERE schemaname='homologacao'
       AND (
          (tablename='hu_caixa' AND indexname IN (
            'uq_hu_caixa_op_numero',
            'uq_hu_caixa_codigo_local',
            'uq_hu_caixa_correlation_id',
            'uq_hu_caixa_claim_token',
            'uq_hu_caixa_hu_sap_warehouse'
          ))
          OR (tablename='hu_caixa_etapa_sap' AND indexname='uq_045_hu_caixa_etapa')
          OR (tablename='hu_caixa_etapa_sap_tentativa' AND indexname IN (
            'uq_045_etapa_tentativa_claim',
            'uq_045_etapa_tentativa_numero'
          ))
       );
    IF v_count<>8 THEN RAISE EXCEPTION '056 VALIDACAO: uniques/claims esperados=8 atual=%',v_count; END IF;
END
$validation$;

SELECT 'A_PENDING_SAP_OUTSIDE_TARGET',
       pg_get_expr(i.indpred,i.indrelid,true)
FROM pg_index i
JOIN pg_class ix ON ix.oid=i.indexrelid
WHERE ix.oid='homologacao.uq_hu_caixa_terminal_ativo'::regclass;

SELECT 'B_EM_PESAGEM_CONFLICTS',count(*)
FROM (
    SELECT 1 FROM homologacao.hu_caixa
    WHERE status_hu_caixa='EM_PESAGEM'
    GROUP BY upper(btrim(terminal::text))
    HAVING count(*)>1
) x;

SELECT 'C_TERMINAL_STATE_MATRIX',
       upper(btrim(terminal::text)),
       count(*) FILTER (WHERE status_hu_caixa='EM_PESAGEM'),
       count(*) FILTER (WHERE status_hu_caixa IN (
          'FINALIZADA_LOCAL','PREVIEW_HU_GERADO','AGUARDANDO_AUTORIZACAO_SAP',
          'PRONTA_PARA_ENVIO','ERRO_SAP','INDETERMINADO_TIMEOUT','BLOQUEADA'
       ))
FROM homologacao.hu_caixa
GROUP BY upper(btrim(terminal::text))
ORDER BY 2;

SELECT 'D_OP_NUMERO_CAIXA_UNIQUE',indexdef
FROM pg_indexes
WHERE schemaname='homologacao' AND tablename='hu_caixa'
  AND indexname='uq_hu_caixa_op_numero';

SELECT 'E_PRESERVED_INDEX',tablename,indexname,indexdef
FROM pg_indexes
WHERE schemaname='homologacao'
  AND (
    (tablename='hu_caixa' AND indexname IN (
      'uq_hu_caixa_codigo_local','uq_hu_caixa_correlation_id',
      'uq_hu_caixa_claim_token','uq_hu_caixa_hu_sap_warehouse'
    ))
    OR (tablename='hu_caixa_etapa_sap' AND indexname='uq_045_hu_caixa_etapa')
    OR (tablename='hu_caixa_etapa_sap_tentativa' AND indexname IN (
      'uq_045_etapa_tentativa_claim','uq_045_etapa_tentativa_numero'
    ))
  )
ORDER BY tablename,indexname;

SELECT 'F_TARGET_INDEX',
       i.indisunique,i.indisvalid,i.indisready,
       pg_get_expr(i.indexprs,i.indrelid,true),
       pg_get_expr(i.indpred,i.indrelid,true)
FROM pg_index i
JOIN pg_class ix ON ix.oid=i.indexrelid
WHERE ix.oid='homologacao.uq_hu_caixa_terminal_ativo'::regclass;

SELECT 'VALIDATION_056_STATUS=PASS';
ROLLBACK;
