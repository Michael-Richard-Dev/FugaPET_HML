\set ON_ERROR_STOP on

-- ============================================================================
-- FugaPET Q - Migration 056
-- Produto Acabado - multiplas caixas pos-pesagem por terminal
-- PROPOSTA / UP GAIA
--
-- GATE:
-- FUGAPET-Q-PRODUTO-ACABADO-DEFER-SAP-SEQUENTIAL-INDEX-MIGRATION-107K
--
-- OFFLINE PACKAGE. ESTE ARQUIVO NAO FOI EXECUTADO POR GAIA.
-- ============================================================================

-- ESCOPO: somente DROP/CREATE do indice parcial.
-- Sem tabela, coluna, DML ou alteracao da state machine.
BEGIN;
SET LOCAL lock_timeout='15s';
SET LOCAL statement_timeout='180s';
SET LOCAL search_path=pg_catalog, homologacao;
LOCK TABLE homologacao.hu_caixa IN SHARE MODE;

DO $pre$
DECLARE
    v_count integer;
    v_expression text;
    v_predicate text;
BEGIN
    IF current_database() <> 'fuga_jales_local_homologacao_q_v1_2' THEN
        RAISE EXCEPTION '056 UP: database incorreto: %',current_database();
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

    IF v_count<>1
       OR v_expression IS DISTINCT FROM 'upper(btrim(terminal::text))'
       OR v_predicate IS DISTINCT FROM
          'status_hu_caixa::text <> ALL (ARRAY[''CONFIRMADA_SAP''::character varying, ''CANCELADA''::character varying]::text[])'
    THEN
        RAISE EXCEPTION '056 UP: baseline do indice divergiu';
    END IF;

    SELECT count(*) INTO v_count
      FROM pg_constraint
     WHERE conindid='homologacao.uq_hu_caixa_terminal_ativo'::regclass;
    IF v_count<>0 THEN RAISE EXCEPTION '056 UP: indice sustenta constraint'; END IF;

    SELECT count(*) INTO v_count
      FROM homologacao.hu_caixa
     WHERE terminal IS NULL OR btrim(terminal)='' OR char_length(btrim(terminal))>120;
    IF v_count<>0 THEN RAISE EXCEPTION '056 UP: terminal invalido; count=%',v_count; END IF;

    SELECT count(*) INTO v_count
      FROM (
        SELECT upper(btrim(terminal::text))
          FROM homologacao.hu_caixa
         WHERE status_hu_caixa='EM_PESAGEM'
         GROUP BY upper(btrim(terminal::text))
        HAVING count(*)>1
      ) x;
    IF v_count<>0 THEN RAISE EXCEPTION '056 UP: conflitos EM_PESAGEM; count=%',v_count; END IF;
END
$pre$;

DROP INDEX homologacao.uq_hu_caixa_terminal_ativo;

CREATE UNIQUE INDEX uq_hu_caixa_terminal_ativo
ON homologacao.hu_caixa
USING btree (upper(btrim((terminal)::text)))
WHERE status_hu_caixa = 'EM_PESAGEM';

DO $post$
DECLARE
    v_count integer;
    v_expression text;
    v_predicate text;
BEGIN
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

    IF v_count<>1
       OR v_expression IS DISTINCT FROM 'upper(btrim(terminal::text))'
       OR v_predicate IS DISTINCT FROM 'status_hu_caixa::text = ''EM_PESAGEM''::text'
    THEN
        RAISE EXCEPTION '056 UP: target divergente';
    END IF;
END
$post$;

SELECT 'MIGRATION_056_TARGET_PREDICATE=status_hu_caixa = EM_PESAGEM';
SELECT 'MIGRATION_056_NEW_TABLE=NAO';
SELECT 'MIGRATION_056_NEW_COLUMN=NAO';
SELECT 'MIGRATION_056_DML=0';
SELECT 'MIGRATION_056_STATUS=PASS';
COMMIT;
