\set ON_ERROR_STOP on

-- ============================================================================
-- FugaPET Q - Migration 056
-- Produto Acabado - multiplas caixas pos-pesagem por terminal
-- PREFLIGHT GAIA
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

DO $preflight$
DECLARE
    v_count integer;
    v_expression text;
    v_predicate text;
BEGIN
    IF current_database() <> 'fuga_jales_local_homologacao_q_v1_2' THEN
        RAISE EXCEPTION '056 PRECHECK: database incorreto: %', current_database();
    END IF;

    IF to_regclass('homologacao.hu_caixa') IS NULL THEN
        RAISE EXCEPTION '056 PRECHECK: hu_caixa ausente';
    END IF;

    SELECT count(*),
           max(pg_get_expr(i.indexprs,i.indrelid,true)),
           max(pg_get_expr(i.indpred,i.indrelid,true))
      INTO v_count,v_expression,v_predicate
      FROM pg_index i
      JOIN pg_class ix ON ix.oid=i.indexrelid
      JOIN pg_class t ON t.oid=i.indrelid
      JOIN pg_namespace n ON n.oid=t.relnamespace
     WHERE n.nspname='homologacao'
       AND t.relname='hu_caixa'
       AND ix.relname='uq_hu_caixa_terminal_ativo'
       AND i.indisunique AND i.indisvalid AND i.indisready;

    IF v_count <> 1
       OR v_expression IS DISTINCT FROM 'upper(btrim(terminal::text))'
       OR v_predicate IS DISTINCT FROM
          'status_hu_caixa::text <> ALL (ARRAY[''CONFIRMADA_SAP''::character varying, ''CANCELADA''::character varying]::text[])'
    THEN
        RAISE EXCEPTION '056 PRECHECK: indice atual divergente';
    END IF;

    SELECT count(*) INTO v_count
      FROM pg_constraint
     WHERE conindid='homologacao.uq_hu_caixa_terminal_ativo'::regclass;
    IF v_count <> 0 THEN
        RAISE EXCEPTION '056 PRECHECK: indice sustenta constraint; count=%',v_count;
    END IF;

    SELECT count(*) INTO v_count
      FROM information_schema.columns
     WHERE table_schema='homologacao'
       AND table_name='hu_caixa'
       AND column_name IN ('terminal','status_hu_caixa')
       AND is_nullable='NO';
    IF v_count <> 2 THEN
        RAISE EXCEPTION '056 PRECHECK: terminal/status devem ser NOT NULL';
    END IF;

    SELECT count(*) INTO v_count
      FROM homologacao.hu_caixa
     WHERE terminal IS NULL OR btrim(terminal)='' OR char_length(btrim(terminal))>120;
    IF v_count <> 0 THEN
        RAISE EXCEPTION '056 PRECHECK: terminal invalido; count=%',v_count;
    END IF;

    SELECT count(*) INTO v_count
      FROM (
        SELECT upper(btrim(terminal::text))
          FROM homologacao.hu_caixa
         WHERE status_hu_caixa='EM_PESAGEM'
         GROUP BY upper(btrim(terminal::text))
        HAVING count(*)>1
      ) x;
    IF v_count <> 0 THEN
        RAISE EXCEPTION '056 PRECHECK: conflitos EM_PESAGEM; count=%',v_count;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint c
        JOIN pg_class t ON t.oid=c.conrelid
        JOIN pg_namespace n ON n.oid=t.relnamespace
        WHERE n.nspname='homologacao' AND t.relname='hu_caixa'
          AND c.conname='ck_hu_caixa_status'
          AND pg_get_constraintdef(c.oid,true) LIKE '%EM_PESAGEM%'
    ) THEN
        RAISE EXCEPTION '056 PRECHECK: dominio status sem EM_PESAGEM';
    END IF;
END
$preflight$;

SELECT 'CURRENT_INDEX_NAME=uq_hu_caixa_terminal_ativo';
SELECT 'CURRENT_PREDICATE=status_hu_caixa NOT IN (CONFIRMADA_SAP,CANCELADA)';
SELECT 'TARGET_PREDICATE=status_hu_caixa = EM_PESAGEM';
SELECT 'EXISTING_EM_PESAGEM_CONFLICTS=0';
SELECT 'INDEX_BACKS_CONSTRAINT=NAO';
SELECT 'PREFLIGHT_056_STATUS=PASS';
ROLLBACK;
