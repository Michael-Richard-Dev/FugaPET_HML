\set ON_ERROR_STOP on

-- ============================================================================
-- FugaPET Q - Migration 056
-- Produto Acabado - multiplas caixas pos-pesagem por terminal
-- ROLLBACK TECNICO GAIA
--
-- GATE:
-- FUGAPET-Q-PRODUTO-ACABADO-DEFER-SAP-SEQUENTIAL-INDEX-MIGRATION-107K
--
-- OFFLINE PACKAGE. ESTE ARQUIVO NAO FOI EXECUTADO POR GAIA.
-- ============================================================================

-- ROLLBACK EXECUTION NAO AUTORIZADO NESTE GATE.
-- Fail-closed: somente restaura contrato antigo se zero conflitos.
BEGIN;
SET LOCAL lock_timeout='15s';
SET LOCAL statement_timeout='180s';
SET LOCAL search_path=pg_catalog, homologacao;
LOCK TABLE homologacao.hu_caixa IN SHARE MODE;

DO $pre$
DECLARE
    v_count integer;
    v_predicate text;
BEGIN
    SELECT count(*),max(pg_get_expr(i.indpred,i.indrelid,true))
      INTO v_count,v_predicate
      FROM pg_index i
      JOIN pg_class ix ON ix.oid=i.indexrelid
      JOIN pg_class t ON t.oid=i.indrelid
      JOIN pg_namespace n ON n.oid=t.relnamespace
     WHERE n.nspname='homologacao'
       AND t.relname='hu_caixa'
       AND ix.relname='uq_hu_caixa_terminal_ativo'
       AND i.indisunique AND i.indisvalid AND i.indisready;

    IF v_count<>1
       OR v_predicate IS DISTINCT FROM 'status_hu_caixa::text = ''EM_PESAGEM''::text'
    THEN
        RAISE EXCEPTION '056 ROLLBACK: target atual divergente';
    END IF;

    SELECT count(*) INTO v_count
      FROM (
        SELECT upper(btrim(terminal::text))
          FROM homologacao.hu_caixa
         WHERE status_hu_caixa NOT IN ('CONFIRMADA_SAP','CANCELADA')
         GROUP BY upper(btrim(terminal::text))
        HAVING count(*)>1
      ) x;

    IF v_count<>0 THEN
        RAISE EXCEPTION '056 ROLLBACK BLOQUEADO: conflitos contrato antigo=%',v_count;
    END IF;
END
$pre$;

DROP INDEX homologacao.uq_hu_caixa_terminal_ativo;

CREATE UNIQUE INDEX uq_hu_caixa_terminal_ativo
ON homologacao.hu_caixa
USING btree (upper(btrim((terminal)::text)))
WHERE (
    (status_hu_caixa)::text <>
    ALL (
        (
            ARRAY[
                'CONFIRMADA_SAP'::character varying,
                'CANCELADA'::character varying
            ]
        )::text[]
    )
);

SELECT 'ROLLBACK_056_OLD_PREDICATE_CONFLICTS=0';
SELECT 'ROLLBACK_056_STATUS=PASS';
SELECT 'ROLLBACK_EXECUTION_AUTHORIZED=NAO';
COMMIT;
