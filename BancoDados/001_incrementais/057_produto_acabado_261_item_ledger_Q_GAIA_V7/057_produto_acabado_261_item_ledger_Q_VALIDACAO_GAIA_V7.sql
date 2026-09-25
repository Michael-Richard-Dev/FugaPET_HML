\set ON_ERROR_STOP on

-- ============================================================================
-- FugaPET Q - Migration 057
-- Produto Acabado - validacao read-only do ledger itemizado 261
-- VALIDACAO GAIA V7 - GATE 107Q
-- ============================================================================

BEGIN TRANSACTION
ISOLATION LEVEL REPEATABLE READ
READ ONLY;

SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '90s';
SET LOCAL search_path = pg_catalog, homologacao;

DO $validation$
DECLARE
    v_count integer;
    v_bad integer;
BEGIN
    IF current_database() <> 'fuga_jales_local_homologacao_q_v1_2' THEN
        RAISE EXCEPTION '057 VALIDACAO: database incorreto: %', current_database();
    END IF;

    IF to_regclass('homologacao.hu_caixa_etapa_sap_item') IS NULL THEN
        RAISE EXCEPTION '057 VALIDACAO: tabela ledger ausente';
    END IF;

    -- 10 colunas exatas.
    SELECT count(*) INTO v_count
      FROM information_schema.columns
     WHERE table_schema='homologacao'
       AND table_name='hu_caixa_etapa_sap_item';
    IF v_count <> 10 THEN
        RAISE EXCEPTION '057 VALIDACAO: column_count=% esperado=10', v_count;
    END IF;

    WITH expected(column_name, data_type, max_len, num_precision, num_scale, nullable) AS (
      VALUES
        ('codigo_hu_caixa_etapa_sap_item','uuid',NULL::integer,NULL::integer,NULL::integer,'NO'),
        ('codigo_hu_caixa_etapa_sap_tentativa','uuid',NULL,NULL,NULL,'NO'),
        ('item_ordinal','integer',NULL,NULL,NULL,'NO'),
        ('material','character varying',40,NULL,NULL,'NO'),
        ('reservation','character varying',10,NULL,NULL,'NO'),
        ('reservation_item','character varying',4,NULL,NULL,'NO'),
        ('quantity_in_entry_unit','numeric',NULL,13,3,'NO'),
        ('entry_unit','character varying',3,NULL,NULL,'NO'),
        ('batch','character varying',10,NULL,NULL,'NO'),
        ('criado_em','timestamp with time zone',NULL,NULL,NULL,'NO')
    ),
    actual AS (
      SELECT column_name,data_type,character_maximum_length,
             numeric_precision,numeric_scale,is_nullable
        FROM information_schema.columns
       WHERE table_schema='homologacao'
         AND table_name='hu_caixa_etapa_sap_item'
    )
    SELECT count(*) INTO v_bad
      FROM expected e
      LEFT JOIN actual a USING (column_name)
     WHERE a.column_name IS NULL
        OR a.data_type IS DISTINCT FROM e.data_type
        OR a.character_maximum_length IS DISTINCT FROM e.max_len
        OR a.numeric_precision IS DISTINCT FROM e.num_precision
        OR a.numeric_scale IS DISTINCT FROM e.num_scale
        OR a.is_nullable IS DISTINCT FROM e.nullable;

    IF v_bad <> 0 THEN
        RAISE EXCEPTION '057 VALIDACAO: TYPE_NULLABILITY_MISMATCH=%', v_bad;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
         WHERE table_schema='homologacao'
           AND table_name='hu_caixa_etapa_sap_item'
           AND column_name='codigo_hu_caixa_etapa_sap_item'
           AND column_default LIKE 'gen_random_uuid()%'
    ) THEN
        RAISE EXCEPTION '057 VALIDACAO: default UUID divergente';
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM information_schema.columns
         WHERE table_schema='homologacao'
           AND table_name='hu_caixa_etapa_sap_item'
           AND column_name='criado_em'
           AND column_default LIKE 'clock_timestamp()%'
    ) THEN
        RAISE EXCEPTION '057 VALIDACAO: default criado_em divergente';
    END IF;

    -- PK + UNIQUE + FK RESTRICT.
    IF NOT EXISTS (
        SELECT 1
          FROM pg_constraint c
          JOIN pg_class r ON r.oid=c.conrelid
          JOIN pg_namespace n ON n.oid=r.relnamespace
         WHERE n.nspname='homologacao'
           AND r.relname='hu_caixa_etapa_sap_item'
           AND c.conname='hu_caixa_etapa_sap_item_pkey'
           AND c.contype='p'
    ) THEN
        RAISE EXCEPTION '057 VALIDACAO: PK ausente';
    END IF;

    IF NOT EXISTS (
        SELECT 1
          FROM pg_constraint c
          JOIN pg_class r ON r.oid=c.conrelid
          JOIN pg_namespace n ON n.oid=r.relnamespace
         WHERE n.nspname='homologacao'
           AND r.relname='hu_caixa_etapa_sap_item'
           AND c.conname='uq_045_etapa_item_ordinal'
           AND c.contype='u'
           AND pg_get_constraintdef(c.oid) LIKE
               'UNIQUE (codigo_hu_caixa_etapa_sap_tentativa, item_ordinal)%'
    ) THEN
        RAISE EXCEPTION '057 VALIDACAO: UNIQUE tentativa+ordinal ausente/divergente';
    END IF;

    IF NOT EXISTS (
        SELECT 1
          FROM pg_constraint c
          JOIN pg_class r ON r.oid=c.conrelid
          JOIN pg_namespace n ON n.oid=r.relnamespace
         WHERE n.nspname='homologacao'
           AND r.relname='hu_caixa_etapa_sap_item'
           AND c.conname='fk_045_etapa_item_tentativa'
           AND c.contype='f'
           AND c.confdeltype='r'
           AND c.confrelid='homologacao.hu_caixa_etapa_sap_tentativa'::regclass
    ) THEN
        RAISE EXCEPTION '057 VALIDACAO: FK tentativa RESTRICT ausente/divergente';
    END IF;

    SELECT count(*) INTO v_count
      FROM pg_constraint c
      JOIN pg_class r ON r.oid=c.conrelid
      JOIN pg_namespace n ON n.oid=r.relnamespace
     WHERE n.nspname='homologacao'
       AND r.relname='hu_caixa_etapa_sap_item'
       AND c.contype='c'
       AND c.conname IN (
          'ck_045_etapa_item_ordinal',
          'ck_045_etapa_item_quantity',
          'ck_045_etapa_item_material',
          'ck_045_etapa_item_reservation',
          'ck_045_etapa_item_reservation_item',
          'ck_045_etapa_item_entry_unit'
       );
    IF v_count <> 6 THEN
        RAISE EXCEPTION '057 VALIDACAO: checks esperados=%/6', v_count;
    END IF;

    -- Batch vazio e estruturalmente permitido:
    -- NOT NULL + varchar(10), sem CHECK que imponha conteudo.
    SELECT count(*) INTO v_count
      FROM pg_constraint c
      JOIN pg_class r ON r.oid=c.conrelid
      JOIN pg_namespace n ON n.oid=r.relnamespace
     WHERE n.nspname='homologacao'
       AND r.relname='hu_caixa_etapa_sap_item'
       AND c.contype='c'
       AND pg_get_constraintdef(c.oid) ~* '\mbatch\M';
    IF v_count <> 0 THEN
        RAISE EXCEPTION '057 VALIDACAO: batch ganhou CHECK indevido';
    END IF;

    -- Dois triggers: append-only e guarda de insert.
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
        RAISE EXCEPTION '057 VALIDACAO: trigger_count=% esperado=2', v_count;
    END IF;

    IF NOT EXISTS (
        SELECT 1
          FROM pg_trigger t
          JOIN pg_class r ON r.oid=t.tgrelid
          JOIN pg_namespace n ON n.oid=r.relnamespace
          JOIN pg_proc p ON p.oid=t.tgfoid
         WHERE n.nspname='homologacao'
           AND r.relname='hu_caixa_etapa_sap_item'
           AND t.tgname='trg_045_etapa_item_append_only'
           AND p.proname='fn_pa_045_historico_append_only'
           AND NOT t.tgisinternal
    ) THEN
        RAISE EXCEPTION '057 VALIDACAO: append-only nao reutiliza funcao 045 canonica';
    END IF;

    IF NOT EXISTS (
        SELECT 1
          FROM pg_trigger t
          JOIN pg_class r ON r.oid=t.tgrelid
          JOIN pg_namespace n ON n.oid=r.relnamespace
          JOIN pg_proc p ON p.oid=t.tgfoid
         WHERE n.nspname='homologacao'
           AND r.relname='hu_caixa_etapa_sap_item'
           AND t.tgname='trg_045_etapa_item_insert_guard'
           AND p.proname='fn_pa_045_item_261_insert_guard'
           AND NOT t.tgisinternal
    ) THEN
        RAISE EXCEPTION '057 VALIDACAO: insert guard ausente/divergente';
    END IF;
END
$validation$;

-- --------------------------------------------------------------------------
-- A. Cadeia tentativa -> item.
-- --------------------------------------------------------------------------
SELECT
    e.codigo_hu_caixa,
    e.etapa_sap,
    t.numero_tentativa,
    t.codigo_hu_caixa_etapa_sap_tentativa,
    count(i.codigo_hu_caixa_etapa_sap_item) AS item_count
FROM homologacao.hu_caixa_etapa_sap e
JOIN homologacao.hu_caixa_etapa_sap_tentativa t
  ON t.codigo_hu_caixa_etapa_sap=e.codigo_hu_caixa_etapa_sap
LEFT JOIN homologacao.hu_caixa_etapa_sap_item i
  ON i.codigo_hu_caixa_etapa_sap_tentativa=t.codigo_hu_caixa_etapa_sap_tentativa
WHERE e.etapa_sap='261'
GROUP BY
    e.codigo_hu_caixa,e.etapa_sap,t.numero_tentativa,
    t.codigo_hu_caixa_etapa_sap_tentativa
ORDER BY e.codigo_hu_caixa,t.numero_tentativa;

-- --------------------------------------------------------------------------
-- B. Confirmado legado sem snapshot do CONFIRMED_ATTEMPT.
-- ZERO itens aqui NAO significa quantidade zero: classificar e bloquear allocator.
-- Inclui sucesso de POST e historico confirmado, ambos materialmente ligados
-- a uma tentativa/evento com MaterialDocument/Year.
-- --------------------------------------------------------------------------
WITH confirmed_attempt AS (
    SELECT DISTINCT
        e.codigo_hu_caixa,
        e.codigo_hu_caixa_etapa_sap,
        ev.codigo_hu_caixa_etapa_sap_tentativa,
        e.material_document,
        e.material_document_year
    FROM homologacao.hu_caixa_etapa_sap e
    JOIN homologacao.hu_caixa_etapa_sap_tentativa t
      ON t.codigo_hu_caixa_etapa_sap=e.codigo_hu_caixa_etapa_sap
    JOIN homologacao.hu_caixa_etapa_sap_evento ev
      ON ev.codigo_hu_caixa_etapa_sap_tentativa=t.codigo_hu_caixa_etapa_sap_tentativa
    WHERE e.etapa_sap='261'
      AND e.status_etapa='CONFIRMADO_SAP'
      AND e.material_document IS NOT NULL
      AND e.material_document_year IS NOT NULL
      AND ev.material_document=e.material_document
      AND ev.material_document_year=e.material_document_year
      AND (
          (ev.tipo_evento='RESULTADO_POST' AND ev.resultado='CONFIRMADO')
          OR
          (ev.tipo_evento='RESULTADO_HISTORICO' AND ev.resultado='CONFIRMADO_HISTORICO')
      )
)
SELECT
    hc.numero_ordem_producao,
    ca.codigo_hu_caixa,
    ca.material_document,
    ca.material_document_year,
    ca.codigo_hu_caixa_etapa_sap_tentativa,
    'LEGACY_CONFIRMED_WITHOUT_ITEM_SNAPSHOT' AS classificacao
FROM confirmed_attempt ca
JOIN homologacao.hu_caixa hc
  ON hc.codigo_hu_caixa=ca.codigo_hu_caixa
WHERE NOT EXISTS (
    SELECT 1
      FROM homologacao.hu_caixa_etapa_sap_item i
     WHERE i.codigo_hu_caixa_etapa_sap_tentativa=
           ca.codigo_hu_caixa_etapa_sap_tentativa
)
ORDER BY hc.numero_ordem_producao,ca.codigo_hu_caixa;

-- --------------------------------------------------------------------------
-- C. Hard-stop por OP para futuro allocator.
-- --------------------------------------------------------------------------
SELECT DISTINCT
    hc.numero_ordem_producao,
    e.codigo_hu_caixa,
    e.status_etapa,
    e.status_reconciliacao,
    'ALLOCATOR_STOP' AS decisao
FROM homologacao.hu_caixa hc
JOIN homologacao.hu_caixa_etapa_sap e
  ON e.codigo_hu_caixa=hc.codigo_hu_caixa
WHERE e.etapa_sap='261'
  AND (
      e.status_etapa='ENVIANDO_SAP'
      OR (
          e.status_etapa='INDETERMINADO_TIMEOUT'
          AND e.status_reconciliacao='PENDENTE'
      )
  )
ORDER BY hc.numero_ordem_producao,e.codigo_hu_caixa;

-- --------------------------------------------------------------------------
-- D. Acumulado somente da tentativa autoritativamente confirmada por POST.
-- Tentativas com erro/timeout/retry nao entram.
-- Batch permanece evidência do request, mas nao compoe esta chave agregada.
-- --------------------------------------------------------------------------
SELECT
    hc.numero_ordem_producao,
    i.material,
    i.reservation,
    i.reservation_item,
    i.entry_unit,
    sum(i.quantity_in_entry_unit) AS total_ja_alocado_pelo_pipeline
FROM homologacao.hu_caixa hc
JOIN homologacao.hu_caixa_etapa_sap e
  ON e.codigo_hu_caixa=hc.codigo_hu_caixa
JOIN homologacao.hu_caixa_etapa_sap_tentativa t
  ON t.codigo_hu_caixa_etapa_sap=e.codigo_hu_caixa_etapa_sap
JOIN homologacao.hu_caixa_etapa_sap_evento ev
  ON ev.codigo_hu_caixa_etapa_sap_tentativa=t.codigo_hu_caixa_etapa_sap_tentativa
JOIN homologacao.hu_caixa_etapa_sap_item i
  ON i.codigo_hu_caixa_etapa_sap_tentativa=t.codigo_hu_caixa_etapa_sap_tentativa
WHERE e.etapa_sap='261'
  AND e.status_etapa='CONFIRMADO_SAP'
  AND ev.tipo_evento='RESULTADO_POST'
  AND ev.resultado='CONFIRMADO'
  AND ev.http_status BETWEEN 200 AND 299
  AND ev.material_document=e.material_document
  AND ev.material_document_year=e.material_document_year
GROUP BY
    hc.numero_ordem_producao,
    i.material,i.reservation,i.reservation_item,i.entry_unit
ORDER BY
    hc.numero_ordem_producao,
    i.material,i.reservation,i.reservation_item,i.entry_unit;

SELECT 'MIGRATION_057_VALIDATION_STATUS=PASS';
SELECT 'BATCH_EMPTY_STRING_STRUCTURALLY_ALLOWED=SIM';
SELECT 'APPEND_ONLY_DB_ENFORCED=SIM';
SELECT 'LATE_INSERT_AFTER_ATTEMPT_EVENT_DB_BLOCKED=SIM';
SELECT 'PERSIST_BEFORE_HTTP_FULL_ENFORCEMENT=APPLICATION_ENFORCED';

ROLLBACK;
