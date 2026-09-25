\set ON_ERROR_STOP on

-- ============================================================================
-- FugaPET Q - Migration 056
-- Produto Acabado - defer SAP / unicidade de terminal somente em EM_PESAGEM
-- PROPOSTA GAIA V7 - GATE 107K-V7
--
-- SOURCE-ONLY. NAO EXECUTAR NESTE GATE.
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
        RAISE EXCEPTION '056 PRECHECK: database incorreto: %', current_database();
    END IF;

    IF to_regclass('homologacao.hu_caixa') IS NULL THEN
        RAISE EXCEPTION '056 PRECHECK: tabela homologacao.hu_caixa ausente';
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

    IF v_index_oid IS NULL THEN
        RAISE EXCEPTION '056 PRECHECK: indice uq_hu_caixa_terminal_ativo ausente';
    END IF;

    IF NOT v_unique OR NOT v_valid OR NOT v_ready THEN
        RAISE EXCEPTION '056 PRECHECK: indice atual nao esta UNIQUE/VALID/READY';
    END IF;

    IF regexp_replace(v_expr, '\s+', ' ', 'g') <>
       'upper(btrim((terminal)::text))' THEN
        RAISE EXCEPTION '056 PRECHECK: expressao do indice atual divergiu: %', v_expr;
    END IF;

    IF regexp_replace(v_pred, '\s+', ' ', 'g') <>
       '((status_hu_caixa)::text <> ALL ((ARRAY[''CONFIRMADA_SAP''::character varying, ''CANCELADA''::character varying])::text[]))' THEN
        RAISE EXCEPTION '056 PRECHECK: predicate atual divergiu: %', v_pred;
    END IF;

    SELECT count(*) INTO v_backing_count
      FROM pg_constraint
     WHERE conindid = v_index_oid;

    IF v_backing_count <> 0 THEN
        RAISE EXCEPTION '056 PRECHECK: indice sustenta constraint; count=%', v_backing_count;
    END IF;
END
$precheck$;

-- Impede DML concorrente entre a prova de conflitos e a troca do indice.
LOCK TABLE homologacao.hu_caixa IN SHARE MODE;

DO $conflicts$
DECLARE
    v_conflicts integer;
BEGIN
    SELECT count(*) INTO v_conflicts
      FROM (
        SELECT upper(btrim(terminal::text))
          FROM homologacao.hu_caixa
         WHERE status_hu_caixa = 'EM_PESAGEM'
         GROUP BY upper(btrim(terminal::text))
        HAVING count(*) > 1
      ) d;

    IF v_conflicts <> 0 THEN
        RAISE EXCEPTION '056 PRECHECK: EM_PESAGEM_CONFLICTS=% esperado=0', v_conflicts;
    END IF;
END
$conflicts$;

DROP INDEX homologacao.uq_hu_caixa_terminal_ativo;

CREATE UNIQUE INDEX uq_hu_caixa_terminal_ativo
ON homologacao.hu_caixa
USING btree (upper(btrim((terminal)::text)))
WHERE status_hu_caixa = 'EM_PESAGEM';

DO $postcheck$
DECLARE
    v_index_oid oid;
    v_expr text;
    v_pred text;
    v_unique boolean;
    v_valid boolean;
    v_ready boolean;
    v_backing_count integer;
BEGIN
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
        RAISE EXCEPTION '056 POSTCHECK: indice target ausente ou nao UNIQUE/VALID/READY';
    END IF;

    IF regexp_replace(v_expr, '\s+', ' ', 'g') <>
       'upper(btrim((terminal)::text))' THEN
        RAISE EXCEPTION '056 POSTCHECK: expressao target divergiu: %', v_expr;
    END IF;

    IF regexp_replace(v_pred, '\s+', ' ', 'g') <>
       '((status_hu_caixa)::text = ''EM_PESAGEM''::text)' THEN
        RAISE EXCEPTION '056 POSTCHECK: predicate target divergiu: %', v_pred;
    END IF;

    SELECT count(*) INTO v_backing_count
      FROM pg_constraint
     WHERE conindid = v_index_oid;

    IF v_backing_count <> 0 THEN
        RAISE EXCEPTION '056 POSTCHECK: indice target passou a sustentar constraint';
    END IF;
END
$postcheck$;

SELECT 'MIGRATION_056_STATUS=PASS';
SELECT 'TARGET_PREDICATE=status_hu_caixa=EM_PESAGEM';
SELECT 'DDL_SCOPE=DROP_INDEX+CREATE_UNIQUE_INDEX';

COMMIT;
