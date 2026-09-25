\set ON_ERROR_STOP on

-- ============================================================================
-- FugaPET Q - Migration 056
-- Produto Acabado - rollback da unicidade de terminal
-- ROLLBACK TECNICO GAIA V7 - GATE 107K-V7
--
-- SOURCE-ONLY. EXECUCAO NAO AUTORIZADA NESTE GATE.
-- ============================================================================

BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '90s';
SET LOCAL search_path = pg_catalog, homologacao;

DO $precheck$
DECLARE
    v_index_oid oid;
    v_expr text;
    v_pred text;
    v_unique boolean;
    v_valid boolean;
    v_ready boolean;
    v_backing_count integer;
BEGIN
    IF current_database() <> 'fuga_jales_local_homologacao_q_v1_2' THEN
        RAISE EXCEPTION '056 ROLLBACK: database incorreto: %', current_database();
    END IF;

    SELECT c.oid, i.indisunique, i.indisvalid, i.indisready,
           pg_get_expr(i.indexprs, i.indrelid),
           pg_get_expr(i.indpred, i.indrelid)
      INTO v_index_oid, v_unique, v_valid, v_ready, v_expr, v_pred
      FROM pg_class c
      JOIN pg_namespace n ON n.oid = c.relnamespace
      JOIN pg_index i ON i.indexrelid = c.oid
     WHERE n.nspname = 'homologacao'
       AND c.relname = 'uq_hu_caixa_terminal_ativo'
       AND c.relkind = 'i';

    IF v_index_oid IS NULL OR NOT v_unique OR NOT v_valid OR NOT v_ready THEN
        RAISE EXCEPTION '056 ROLLBACK: indice target ausente ou invalido';
    END IF;

    IF regexp_replace(v_expr, '\s+', ' ', 'g') <>
       'upper(btrim((terminal)::text))'
       OR regexp_replace(v_pred, '\s+', ' ', 'g') <>
       '((status_hu_caixa)::text = ''EM_PESAGEM''::text)' THEN
        RAISE EXCEPTION '056 ROLLBACK: definicao target divergiu; expr=% pred=%', v_expr, v_pred;
    END IF;

    SELECT count(*) INTO v_backing_count FROM pg_constraint WHERE conindid = v_index_oid;
    IF v_backing_count <> 0 THEN
        RAISE EXCEPTION '056 ROLLBACK: indice target sustenta constraint';
    END IF;
END
$precheck$;

LOCK TABLE homologacao.hu_caixa IN SHARE MODE;

-- Gate obrigatorio do rollback: o predicate antigo so pode ser restaurado
-- quando a sua populacao voltar a ser univoca.
DO $conflicts$
DECLARE
    v_conflicts integer;
BEGIN
    SELECT count(*) INTO v_conflicts
      FROM (
        SELECT upper(btrim(terminal::text))
          FROM homologacao.hu_caixa
         WHERE status_hu_caixa NOT IN ('CONFIRMADA_SAP','CANCELADA')
         GROUP BY upper(btrim(terminal::text))
        HAVING count(*) > 1
      ) d;

    IF v_conflicts <> 0 THEN
        RAISE EXCEPTION '056 ROLLBACK BLOQUEADO: OLD_PREDICATE_CONFLICTS=%', v_conflicts;
    END IF;
END
$conflicts$;

DROP INDEX homologacao.uq_hu_caixa_terminal_ativo;

CREATE UNIQUE INDEX uq_hu_caixa_terminal_ativo
ON homologacao.hu_caixa
USING btree (upper(btrim((terminal)::text)))
WHERE status_hu_caixa NOT IN ('CONFIRMADA_SAP','CANCELADA');

DO $postcheck$
DECLARE
    v_index_oid oid;
    v_expr text;
    v_pred text;
    v_unique boolean;
    v_valid boolean;
    v_ready boolean;
BEGIN
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

    IF v_index_oid IS NULL OR NOT v_unique OR NOT v_valid OR NOT v_ready THEN
        RAISE EXCEPTION '056 ROLLBACK POSTCHECK: indice antigo nao restaurado corretamente';
    END IF;

    IF regexp_replace(v_expr, '\s+', ' ', 'g') <>
       'upper(btrim((terminal)::text))'
       OR regexp_replace(v_pred, '\s+', ' ', 'g') <>
       '((status_hu_caixa)::text <> ALL ((ARRAY[''CONFIRMADA_SAP''::character varying, ''CANCELADA''::character varying])::text[]))' THEN
        RAISE EXCEPTION '056 ROLLBACK POSTCHECK: definicao antiga divergiu; expr=% pred=%', v_expr, v_pred;
    END IF;
END
$postcheck$;

SELECT 'ROLLBACK_056_STATUS=PASS';
SELECT 'OLD_PREDICATE_CONFLICTS=0';

COMMIT;
