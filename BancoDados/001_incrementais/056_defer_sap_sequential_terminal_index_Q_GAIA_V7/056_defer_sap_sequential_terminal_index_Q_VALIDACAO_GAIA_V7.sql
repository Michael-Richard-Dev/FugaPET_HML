\set ON_ERROR_STOP on

-- ============================================================================
-- FugaPET Q - Migration 056
-- Produto Acabado - validacao read-only da unicidade de terminal em EM_PESAGEM
-- VALIDACAO GAIA V7 - GATE 107K-V7
-- ============================================================================

BEGIN TRANSACTION
ISOLATION LEVEL REPEATABLE READ
READ ONLY;

SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '90s';
SET LOCAL search_path = pg_catalog, homologacao;

DO $validation$
DECLARE
    v_index_oid oid;
    v_expr text;
    v_pred text;
    v_unique boolean;
    v_valid boolean;
    v_ready boolean;
    v_backing_count integer;
    v_conflicts integer;
BEGIN
    IF current_database() <> 'fuga_jales_local_homologacao_q_v1_2' THEN
        RAISE EXCEPTION '056 VALIDACAO: database incorreto: %', current_database();
    END IF;

    SELECT c.oid, i.indisunique, i.indisvalid, i.indisready,
           pg_get_expr(i.indexprs, i.indrelid),
           pg_get_expr(i.indpred, i.indrelid)
      INTO v_index_oid, v_unique, v_valid, v_ready, v_expr, v_pred
      FROM pg_class c
      JOIN pg_namespace n ON n.oid = c.relnamespace
      JOIN pg_index i ON i.indexrelid = c.oid
     WHERE n.nspname='homologacao'
       AND c.relname='uq_hu_caixa_terminal_ativo'
       AND c.relkind='i';

    IF v_index_oid IS NULL THEN
        RAISE EXCEPTION '056 VALIDACAO: indice target ausente';
    END IF;

    IF NOT v_unique OR NOT v_valid OR NOT v_ready THEN
        RAISE EXCEPTION '056 VALIDACAO: UNIQUE/VALID/READY divergente';
    END IF;

    IF regexp_replace(v_expr, '\s+', ' ', 'g') <>
       'upper(btrim((terminal)::text))'
       OR regexp_replace(v_pred, '\s+', ' ', 'g') <>
       '((status_hu_caixa)::text = ''EM_PESAGEM''::text)' THEN
        RAISE EXCEPTION '056 VALIDACAO: definicao target divergiu; expr=% pred=%', v_expr, v_pred;
    END IF;

    SELECT count(*) INTO v_backing_count FROM pg_constraint WHERE conindid=v_index_oid;
    IF v_backing_count <> 0 THEN
        RAISE EXCEPTION '056 VALIDACAO: indice target sustenta constraint';
    END IF;

    SELECT count(*) INTO v_conflicts
      FROM (
        SELECT upper(btrim(terminal::text))
          FROM homologacao.hu_caixa
         WHERE status_hu_caixa='EM_PESAGEM'
         GROUP BY upper(btrim(terminal::text))
        HAVING count(*) > 1
      ) d;

    IF v_conflicts <> 0 THEN
        RAISE EXCEPTION '056 VALIDACAO: EM_PESAGEM_CONFLICTS=% esperado=0', v_conflicts;
    END IF;
END
$validation$;

SELECT
    c.relname AS index_name,
    i.indisunique,
    i.indisvalid,
    i.indisready,
    pg_get_expr(i.indexprs, i.indrelid) AS index_expression,
    pg_get_expr(i.indpred, i.indrelid) AS index_predicate
FROM pg_class c
JOIN pg_namespace n ON n.oid=c.relnamespace
JOIN pg_index i ON i.indexrelid=c.oid
WHERE n.nspname='homologacao'
  AND c.relname='uq_hu_caixa_terminal_ativo';

SELECT upper(btrim(terminal::text)) AS terminal_normalizado, count(*) AS total
FROM homologacao.hu_caixa
WHERE status_hu_caixa='EM_PESAGEM'
GROUP BY upper(btrim(terminal::text))
HAVING count(*) > 1;

SELECT 'MIGRATION_056_VALIDATION_STATUS=PASS';
SELECT 'EM_PESAGEM_CONFLICTS=0';

ROLLBACK;
