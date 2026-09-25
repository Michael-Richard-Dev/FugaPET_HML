\set ON_ERROR_STOP on

-- ============================================================================
-- FugaPET Q - Migration 057
-- Produto Acabado - ledger itemizado do movimento 261 por tentativa
-- PROPOSTA GAIA V7 - GATE 107Q
--
-- SOURCE-ONLY. NAO EXECUTAR NESTE GATE.
-- Nao altera C#, nao altera funcoes 045 existentes e nao executa backfill.
-- ============================================================================

BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '90s';
SET LOCAL search_path = pg_catalog, homologacao;

DO $precheck$
DECLARE
    v_count integer;
BEGIN
    IF current_database() <> 'fuga_jales_local_homologacao_q_v1_2' THEN
        RAISE EXCEPTION '057 PRECHECK: database incorreto: %', current_database();
    END IF;

    IF to_regclass('homologacao.hu_caixa_etapa_sap_tentativa') IS NULL
       OR to_regclass('homologacao.hu_caixa_etapa_sap') IS NULL
       OR to_regclass('homologacao.hu_caixa_etapa_sap_evento') IS NULL THEN
        RAISE EXCEPTION '057 PRECHECK: baseline 045 obrigatoria ausente';
    END IF;

    IF to_regclass('homologacao.hu_caixa_etapa_sap_item') IS NOT NULL THEN
        RAISE EXCEPTION '057 PRECHECK: tabela hu_caixa_etapa_sap_item ja existe';
    END IF;

    IF to_regprocedure('homologacao.fn_pa_045_historico_append_only()') IS NULL THEN
        RAISE EXCEPTION '057 PRECHECK: fn_pa_045_historico_append_only ausente';
    END IF;

    IF to_regprocedure('homologacao.fn_pa_045_item_261_insert_guard()') IS NOT NULL THEN
        RAISE EXCEPTION '057 PRECHECK: fn_pa_045_item_261_insert_guard ja existe';
    END IF;

    SELECT count(*) INTO v_count
      FROM pg_trigger
     WHERE tgname IN (
        'trg_045_etapa_item_append_only',
        'trg_045_etapa_item_insert_guard'
     )
       AND NOT tgisinternal;

    IF v_count <> 0 THEN
        RAISE EXCEPTION '057 PRECHECK: trigger name collision count=%', v_count;
    END IF;
END
$precheck$;

CREATE TABLE homologacao.hu_caixa_etapa_sap_item
(
    codigo_hu_caixa_etapa_sap_item uuid NOT NULL DEFAULT gen_random_uuid(),
    codigo_hu_caixa_etapa_sap_tentativa uuid NOT NULL,
    item_ordinal integer NOT NULL,
    material varchar(40) NOT NULL,
    reservation varchar(10) NOT NULL,
    reservation_item varchar(4) NOT NULL,
    quantity_in_entry_unit numeric(13,3) NOT NULL,
    entry_unit varchar(3) NOT NULL,
    batch varchar(10) NOT NULL,
    criado_em timestamptz NOT NULL DEFAULT clock_timestamp(),

    CONSTRAINT hu_caixa_etapa_sap_item_pkey
        PRIMARY KEY (codigo_hu_caixa_etapa_sap_item),

    CONSTRAINT uq_045_etapa_item_ordinal
        UNIQUE (codigo_hu_caixa_etapa_sap_tentativa, item_ordinal),

    CONSTRAINT fk_045_etapa_item_tentativa
        FOREIGN KEY (codigo_hu_caixa_etapa_sap_tentativa)
        REFERENCES homologacao.hu_caixa_etapa_sap_tentativa
            (codigo_hu_caixa_etapa_sap_tentativa)
        ON DELETE RESTRICT,

    CONSTRAINT ck_045_etapa_item_ordinal
        CHECK (item_ordinal > 0),

    CONSTRAINT ck_045_etapa_item_quantity
        CHECK (quantity_in_entry_unit > 0::numeric),

    CONSTRAINT ck_045_etapa_item_material
        CHECK (char_length(btrim(material)) > 0),

    CONSTRAINT ck_045_etapa_item_reservation
        CHECK (char_length(btrim(reservation)) > 0),

    CONSTRAINT ck_045_etapa_item_reservation_item
        CHECK (char_length(btrim(reservation_item)) > 0),

    CONSTRAINT ck_045_etapa_item_entry_unit
        CHECK (char_length(btrim(entry_unit)) > 0)
);

COMMENT ON TABLE homologacao.hu_caixa_etapa_sap_item IS
'Snapshot imutavel dos itens do request 261 por tentativa 045. Batch vazio e valor valido e distinto de NULL.';

-- Guarda minima de INSERT.
--
-- DB_ENFORCED:
--   * somente tentativa pertencente a etapa 261;
--   * etapa ainda em PRONTA_PARA_ENVIO ou ENVIANDO_SAP;
--   * zero evento ja registrado para a tentativa.
--
-- APPLICATION_ENFORCED:
--   * todos os itens devem ser persistidos, COMMITados e relidos/validados
--     antes da chamada HTTP. O baseline 045 atual nao possui marcador
--     duravel "HTTP_STARTED" e cria a tentativa durante o claim que move
--     a etapa para ENVIANDO_SAP; a migration 057 nao altera esse pipeline.
CREATE FUNCTION homologacao.fn_pa_045_item_261_insert_guard()
RETURNS trigger
LANGUAGE plpgsql
SET search_path TO 'pg_catalog', 'homologacao'
AS $fn$
DECLARE
    v_etapa varchar;
    v_status varchar;
BEGIN
    SELECT e.etapa_sap, e.status_etapa
      INTO v_etapa, v_status
      FROM homologacao.hu_caixa_etapa_sap_tentativa t
      JOIN homologacao.hu_caixa_etapa_sap e
        ON e.codigo_hu_caixa_etapa_sap = t.codigo_hu_caixa_etapa_sap
     WHERE t.codigo_hu_caixa_etapa_sap_tentativa =
           NEW.codigo_hu_caixa_etapa_sap_tentativa;

    IF NOT FOUND THEN
        RAISE EXCEPTION
            'PA045_ITEM261_TENTATIVA_INEXISTENTE tentativa=%',
            NEW.codigo_hu_caixa_etapa_sap_tentativa;
    END IF;

    IF v_etapa <> '261' THEN
        RAISE EXCEPTION
            'PA045_ITEM261_ETAPA_INVALIDA tentativa=% etapa=%',
            NEW.codigo_hu_caixa_etapa_sap_tentativa, v_etapa;
    END IF;

    IF v_status NOT IN ('PRONTA_PARA_ENVIO','ENVIANDO_SAP') THEN
        RAISE EXCEPTION
            'PA045_ITEM261_INSERT_TARDIO_BLOQUEADO tentativa=% status=%',
            NEW.codigo_hu_caixa_etapa_sap_tentativa, v_status;
    END IF;

    IF EXISTS (
        SELECT 1
          FROM homologacao.hu_caixa_etapa_sap_evento ev
         WHERE ev.codigo_hu_caixa_etapa_sap_tentativa =
               NEW.codigo_hu_caixa_etapa_sap_tentativa
    ) THEN
        RAISE EXCEPTION
            'PA045_ITEM261_INSERT_APOS_EVENTO_BLOQUEADO tentativa=%',
            NEW.codigo_hu_caixa_etapa_sap_tentativa;
    END IF;

    RETURN NEW;
END
$fn$;

CREATE TRIGGER trg_045_etapa_item_insert_guard
BEFORE INSERT ON homologacao.hu_caixa_etapa_sap_item
FOR EACH ROW
EXECUTE FUNCTION homologacao.fn_pa_045_item_261_insert_guard();

-- Reuso deliberado do padrao append-only do 045 para UPDATE/DELETE.
CREATE TRIGGER trg_045_etapa_item_append_only
BEFORE UPDATE OR DELETE ON homologacao.hu_caixa_etapa_sap_item
FOR EACH ROW
EXECUTE FUNCTION homologacao.fn_pa_045_historico_append_only();

DO $postcheck$
DECLARE
    v_count integer;
BEGIN
    IF to_regclass('homologacao.hu_caixa_etapa_sap_item') IS NULL THEN
        RAISE EXCEPTION '057 POSTCHECK: tabela ledger ausente';
    END IF;

    SELECT count(*) INTO v_count
      FROM information_schema.columns
     WHERE table_schema='homologacao'
       AND table_name='hu_caixa_etapa_sap_item';
    IF v_count <> 10 THEN
        RAISE EXCEPTION '057 POSTCHECK: column_count=% esperado=10', v_count;
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
        RAISE EXCEPTION '057 POSTCHECK: constraints=% esperado=9', v_count;
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
        RAISE EXCEPTION '057 POSTCHECK: triggers=% esperado=2', v_count;
    END IF;
END
$postcheck$;

SELECT 'MIGRATION_057_STATUS=PASS';
SELECT 'TABLE=homologacao.hu_caixa_etapa_sap_item';
SELECT 'DB_APPEND_ONLY_ENFORCEMENT=SIM';
SELECT 'DB_LATE_INSERT_EVENT_BARRIER=SIM';
SELECT 'PERSIST_BEFORE_HTTP_FULL_ENFORCEMENT=APPLICATION_ENFORCED';

COMMIT;
