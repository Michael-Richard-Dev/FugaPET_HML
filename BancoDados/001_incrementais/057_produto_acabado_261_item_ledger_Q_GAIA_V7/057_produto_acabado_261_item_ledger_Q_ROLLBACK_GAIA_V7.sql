\set ON_ERROR_STOP on

-- ============================================================================
-- FugaPET Q - Migration 057
-- Produto Acabado - rollback do ledger itemizado 261
-- ROLLBACK TECNICO GAIA V7 - GATE 107Q
--
-- SOURCE-ONLY. EXECUCAO NAO AUTORIZADA NESTE GATE.
-- ============================================================================

BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '90s';
SET LOCAL search_path = pg_catalog, homologacao;

DO $precheck$
DECLARE
    v_count integer;
    v_rows bigint;
BEGIN
    IF current_database() <> 'fuga_jales_local_homologacao_q_v1_2' THEN
        RAISE EXCEPTION '057 ROLLBACK: database incorreto: %', current_database();
    END IF;

    IF to_regclass('homologacao.hu_caixa_etapa_sap_item') IS NULL THEN
        RAISE EXCEPTION '057 ROLLBACK: tabela ledger ausente';
    END IF;

    IF to_regprocedure('homologacao.fn_pa_045_item_261_insert_guard()') IS NULL THEN
        RAISE EXCEPTION '057 ROLLBACK: funcao especifica ausente';
    END IF;

    SELECT count(*) INTO v_count
      FROM information_schema.columns
     WHERE table_schema='homologacao'
       AND table_name='hu_caixa_etapa_sap_item';
    IF v_count <> 10 THEN
        RAISE EXCEPTION '057 ROLLBACK: shape de colunas divergiu; count=%', v_count;
    END IF;

    SELECT count(*) INTO v_count
      FROM pg_constraint c
      JOIN pg_class r ON r.oid=c.conrelid
      JOIN pg_namespace n ON n.oid=r.relnamespace
     WHERE n.nspname='homologacao'
       AND r.relname='hu_caixa_etapa_sap_item'
       AND c.conname IN (
          'hu_caixa_etapa_sap_item_pkey',
          'uq_045_etapa_item_ordinal',
          'fk_045_etapa_item_tentativa',
          'ck_045_etapa_item_ordinal',
          'ck_045_etapa_item_quantity',
          'ck_045_etapa_item_material',
          'ck_045_etapa_item_reservation',
          'ck_045_etapa_item_reservation_item',
          'ck_045_etapa_item_entry_unit'
       );
    IF v_count <> 9 THEN
        RAISE EXCEPTION '057 ROLLBACK: constraints divergiram; count=%', v_count;
    END IF;

    SELECT count(*) INTO v_count
      FROM pg_trigger t
      JOIN pg_class r ON r.oid=t.tgrelid
      JOIN pg_namespace n ON n.oid=r.relnamespace
     WHERE n.nspname='homologacao'
       AND r.relname='hu_caixa_etapa_sap_item'
       AND NOT t.tgisinternal
       AND t.tgname IN (
          'trg_045_etapa_item_insert_guard',
          'trg_045_etapa_item_append_only'
       );
    IF v_count <> 2 THEN
        RAISE EXCEPTION '057 ROLLBACK: triggers divergiram; count=%', v_count;
    END IF;

    EXECUTE 'SELECT count(*) FROM homologacao.hu_caixa_etapa_sap_item'
       INTO v_rows;
    IF v_rows <> 0 THEN
        RAISE EXCEPTION
            '057 ROLLBACK BLOQUEADO: ledger contem dados; rows=%',
            v_rows;
    END IF;
END
$precheck$;

DROP TRIGGER trg_045_etapa_item_append_only
ON homologacao.hu_caixa_etapa_sap_item;

DROP TRIGGER trg_045_etapa_item_insert_guard
ON homologacao.hu_caixa_etapa_sap_item;

DROP TABLE homologacao.hu_caixa_etapa_sap_item;

DROP FUNCTION homologacao.fn_pa_045_item_261_insert_guard();

DO $postcheck$
BEGIN
    IF to_regclass('homologacao.hu_caixa_etapa_sap_item') IS NOT NULL THEN
        RAISE EXCEPTION '057 ROLLBACK POSTCHECK: tabela ainda existe';
    END IF;

    IF to_regprocedure('homologacao.fn_pa_045_item_261_insert_guard()') IS NOT NULL THEN
        RAISE EXCEPTION '057 ROLLBACK POSTCHECK: funcao ainda existe';
    END IF;

    IF to_regclass('homologacao.hu_caixa_etapa_sap_tentativa') IS NULL
       OR to_regclass('homologacao.hu_caixa_etapa_sap') IS NULL
       OR to_regclass('homologacao.hu_caixa_etapa_sap_evento') IS NULL THEN
        RAISE EXCEPTION '057 ROLLBACK POSTCHECK: objeto 045 preexistente foi afetado';
    END IF;
END
$postcheck$;

SELECT 'ROLLBACK_057_STATUS=PASS';
SELECT 'EXISTING_045_OBJECTS_TOUCHED=NAO';

COMMIT;
