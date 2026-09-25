\set ON_ERROR_STOP on

-- ============================================================================
-- FugaPET Q - Migration 058
-- Produto Acabado - boundary DB 045 pre-claim para ledger 261
-- PROPOSTA GAIA V7 - GATE 107S
--
-- SOURCE-ONLY. NAO EXECUTAR NESTE GATE.
-- ============================================================================
BEGIN;
SET LOCAL lock_timeout='5s';
SET LOCAL statement_timeout='120s';
SET LOCAL search_path=pg_catalog,homologacao;

DO $precheck$
DECLARE
    v_owner name;
    v_count integer;
BEGIN
    IF current_database()<>'fuga_jales_local_homologacao_q_v1_2' THEN
        RAISE EXCEPTION '058 PRECHECK: database incorreto: %',current_database();
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname='fugapet_q_app') THEN
        RAISE EXCEPTION '058 PRECHECK: role fugapet_q_app ausente';
    END IF;

    IF to_regclass('homologacao.hu_caixa') IS NULL
       OR to_regclass('homologacao.hu_caixa_etapa_sap') IS NULL
       OR to_regclass('homologacao.hu_caixa_etapa_sap_tentativa') IS NULL
       OR to_regclass('homologacao.hu_caixa_etapa_sap_evento') IS NULL
       OR to_regclass('homologacao.hu_caixa_etapa_sap_item') IS NULL THEN
        RAISE EXCEPTION '058 PRECHECK: baseline 045/057 ausente';
    END IF;

    SELECT pg_get_userbyid(c.relowner)
      INTO v_owner
      FROM pg_class c
     WHERE c.oid='homologacao.hu_caixa_etapa_sap_tentativa'::regclass;

    IF current_user<>v_owner THEN
        RAISE EXCEPTION
          '058 PRECHECK: executar como owner das tabelas 045; current_user=% owner=%',
          current_user,v_owner;
    END IF;

    IF to_regprocedure('homologacao.fn_pa_045_validar_ator(bigint,text)') IS NULL
       OR to_regprocedure('homologacao.fn_pa_045_preparar_etapa(bigint,character varying,jsonb,bigint,text)') IS NULL
       OR to_regprocedure('homologacao.fn_pa_045_claim_etapa(bigint,character varying,bigint,text)') IS NULL
       OR to_regprocedure('homologacao.fn_pa_045_historico_append_only()') IS NULL
       OR to_regprocedure('homologacao.fn_pa_045_item_261_insert_guard()') IS NULL THEN
        RAISE EXCEPTION '058 PRECHECK: funcoes obrigatorias 045/057 ausentes';
    END IF;

    SELECT count(*) INTO v_count
      FROM information_schema.columns
     WHERE table_schema='homologacao'
       AND table_name='hu_caixa_etapa_sap_tentativa'
       AND column_name IN ('claim_token','claim_obtido_em','claim_expira_em')
       AND is_nullable='NO';

    IF v_count<>3 THEN
        RAISE EXCEPTION
          '058 PRECHECK: trio claim tentativa deve estar NOT NULL no baseline; count=%',
          v_count;
    END IF;

    IF NOT EXISTS (
        SELECT 1
          FROM pg_constraint
         WHERE conrelid='homologacao.hu_caixa_etapa_sap_tentativa'::regclass
           AND conname='ck_045_etapa_tentativa_periodo'
           AND contype='c'
    ) THEN
        RAISE EXCEPTION '058 PRECHECK: ck_045_etapa_tentativa_periodo ausente';
    END IF;

    IF NOT EXISTS (
        SELECT 1
          FROM pg_trigger t
          JOIN pg_proc p ON p.oid=t.tgfoid
         WHERE t.tgrelid='homologacao.hu_caixa_etapa_sap_tentativa'::regclass
           AND t.tgname='trg_045_etapa_tentativa_append_only'
           AND NOT t.tgisinternal
           AND p.proname='fn_pa_045_etapa_tentativa_prepared_claim_guard'
    ) THEN
        RAISE EXCEPTION '058 POSTCHECK: trigger tentativa nao usa prepared-claim guard';
    END IF;

    IF has_table_privilege('fugapet_q_app','homologacao.hu_caixa_etapa_sap_item','INSERT')
       OR has_table_privilege('fugapet_q_app','homologacao.hu_caixa_etapa_sap_item','UPDATE')
       OR has_table_privilege('fugapet_q_app','homologacao.hu_caixa_etapa_sap_item','DELETE') THEN
        RAISE EXCEPTION '058 PRECHECK: runtime ja possui DML direto no ledger 057';
    END IF;

    IF EXISTS (
        SELECT 1
          FROM pg_proc p
          JOIN pg_namespace n ON n.oid=p.pronamespace
         WHERE n.nspname='homologacao'
           AND p.proname IN (
             'fn_pa_045_261_contexto_op',
             'fn_pa_045_261_hard_stops',
             'fn_pa_045_261_total_alocado',
             'fn_pa_045_261_inserir_itens',
             'fn_pa_045_261_preparar_tentativa',
             'fn_pa_045_261_readback',
             'fn_pa_045_claim_etapa_preparada',
             'fn_pa_045_etapa_tentativa_prepared_claim_guard'
           )
    ) THEN
        RAISE EXCEPTION '058 PRECHECK: objeto funcional 058 ja existe';
    END IF;
END
$precheck$;

-- --------------------------------------------------------------------------
-- Prepared attempt: o trio de claim passa a representar dois estados válidos:
-- PREPARADA = todos NULL
-- CLAIMED   = todos NOT NULL e expira > obtido
-- Tentativas antigas permanecem materialmente inalteradas.
-- --------------------------------------------------------------------------
ALTER TABLE homologacao.hu_caixa_etapa_sap_tentativa
    DROP CONSTRAINT ck_045_etapa_tentativa_periodo;

ALTER TABLE homologacao.hu_caixa_etapa_sap_tentativa
    ALTER COLUMN claim_token DROP NOT NULL,
    ALTER COLUMN claim_obtido_em DROP NOT NULL,
    ALTER COLUMN claim_expira_em DROP NOT NULL;

ALTER TABLE homologacao.hu_caixa_etapa_sap_tentativa
    ADD CONSTRAINT ck_045_etapa_tentativa_periodo
    CHECK (
        (
            claim_token IS NULL
            AND claim_obtido_em IS NULL
            AND claim_expira_em IS NULL
        )
        OR
        (
            claim_token IS NOT NULL
            AND claim_obtido_em IS NOT NULL
            AND claim_expira_em IS NOT NULL
            AND claim_expira_em>claim_obtido_em
        )
    );

-- --------------------------------------------------------------------------
-- Tentativa 045 permanece historica/imutavel, com UMA transicao focal:
-- PREPARED (claim trio NULL) -> CLAIMED (claim trio completo).
-- Nenhuma identidade, numero, request ou ator pode mudar.
-- --------------------------------------------------------------------------
CREATE FUNCTION homologacao.fn_pa_045_etapa_tentativa_prepared_claim_guard()
RETURNS trigger
LANGUAGE plpgsql
SET search_path TO 'pg_catalog','homologacao'
AS $fn$
DECLARE
    v_owner name;
BEGIN
    SELECT pg_get_userbyid(c.relowner)
      INTO v_owner
      FROM pg_class c
     WHERE c.oid=TG_RELID;

    IF current_user<>v_owner THEN
        RAISE EXCEPTION
          'PA045_DML_DIRETO_TENTATIVA_BLOQUEADO_USE_FUNCOES_CONTROLADAS usuario=%',
          current_user;
    END IF;

    IF TG_OP='INSERT' THEN
        RETURN NEW;
    END IF;

    IF TG_OP='DELETE' THEN
        RAISE EXCEPTION 'PA045_TENTATIVA_APPEND_ONLY operacao=DELETE';
    END IF;

    IF OLD.claim_token IS NULL
       AND OLD.claim_obtido_em IS NULL
       AND OLD.claim_expira_em IS NULL
       AND NEW.claim_token IS NOT NULL
       AND NEW.claim_obtido_em IS NOT NULL
       AND NEW.claim_expira_em IS NOT NULL
       AND NEW.claim_expira_em>NEW.claim_obtido_em
       AND NEW.codigo_hu_caixa_etapa_sap_tentativa IS NOT DISTINCT FROM OLD.codigo_hu_caixa_etapa_sap_tentativa
       AND NEW.codigo_hu_caixa_etapa_sap IS NOT DISTINCT FROM OLD.codigo_hu_caixa_etapa_sap
       AND NEW.numero_tentativa IS NOT DISTINCT FROM OLD.numero_tentativa
       AND NEW.correlation_id IS NOT DISTINCT FROM OLD.correlation_id
       AND NEW.request_sanitizado IS NOT DISTINCT FROM OLD.request_sanitizado
       AND NEW.usuario_operacao IS NOT DISTINCT FROM OLD.usuario_operacao
       AND NEW.terminal_operacao IS NOT DISTINCT FROM OLD.terminal_operacao
       AND NEW.criado_em IS NOT DISTINCT FROM OLD.criado_em THEN
        RETURN NEW;
    END IF;

    RAISE EXCEPTION
      'PA045_TENTATIVA_APPEND_ONLY_TRANSICAO_INVALIDA operacao=% tentativa=%',
      TG_OP,OLD.codigo_hu_caixa_etapa_sap_tentativa;
END
$fn$;

DROP TRIGGER trg_045_etapa_tentativa_append_only
ON homologacao.hu_caixa_etapa_sap_tentativa;

CREATE TRIGGER trg_045_etapa_tentativa_append_only
BEFORE INSERT OR UPDATE OR DELETE
ON homologacao.hu_caixa_etapa_sap_tentativa
FOR EACH ROW
EXECUTE FUNCTION homologacao.fn_pa_045_etapa_tentativa_prepared_claim_guard();

-- --------------------------------------------------------------------------
-- 057 hardening:
-- runtime direto nunca pode inserir;
-- a porta controlada só admite etapa 261 PRONTA, tentativa corrente PREPARADA,
-- sem claim e sem qualquer evento.
-- --------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION homologacao.fn_pa_045_item_261_insert_guard()
RETURNS trigger
LANGUAGE plpgsql
SET search_path TO 'pg_catalog','homologacao'
AS $fn$
DECLARE
    v_owner name;
    v_etapa varchar;
    v_status varchar;
    v_numero_etapa integer;
    v_numero_tentativa integer;
    v_claim_etapa uuid;
    v_claim_tentativa uuid;
    v_claim_obtido timestamptz;
    v_claim_expira timestamptz;
BEGIN
    SELECT pg_get_userbyid(c.relowner)
      INTO v_owner
      FROM pg_class c
     WHERE c.oid=TG_RELID;

    IF current_user<>v_owner THEN
        RAISE EXCEPTION
          'PA045_ITEM261_DML_DIRETO_BLOQUEADO_USE_FUNCAO_CONTROLADA usuario=%',
          current_user;
    END IF;

    SELECT
        e.etapa_sap,
        e.status_etapa,
        e.numero_tentativa,
        e.claim_token,
        t.numero_tentativa,
        t.claim_token,
        t.claim_obtido_em,
        t.claim_expira_em
      INTO
        v_etapa,
        v_status,
        v_numero_etapa,
        v_claim_etapa,
        v_numero_tentativa,
        v_claim_tentativa,
        v_claim_obtido,
        v_claim_expira
      FROM homologacao.hu_caixa_etapa_sap_tentativa t
      JOIN homologacao.hu_caixa_etapa_sap e
        ON e.codigo_hu_caixa_etapa_sap=t.codigo_hu_caixa_etapa_sap
     WHERE t.codigo_hu_caixa_etapa_sap_tentativa=
           NEW.codigo_hu_caixa_etapa_sap_tentativa;

    IF NOT FOUND THEN
        RAISE EXCEPTION
          'PA045_ITEM261_TENTATIVA_INEXISTENTE tentativa=%',
          NEW.codigo_hu_caixa_etapa_sap_tentativa;
    END IF;

    IF v_etapa<>'261' THEN
        RAISE EXCEPTION
          'PA045_ITEM261_ETAPA_INVALIDA tentativa=% etapa=%',
          NEW.codigo_hu_caixa_etapa_sap_tentativa,v_etapa;
    END IF;

    IF v_status<>'PRONTA_PARA_ENVIO'
       OR v_numero_etapa<>v_numero_tentativa
       OR v_claim_etapa IS NOT NULL
       OR v_claim_tentativa IS NOT NULL
       OR v_claim_obtido IS NOT NULL
       OR v_claim_expira IS NOT NULL THEN
        RAISE EXCEPTION
          'PA045_ITEM261_INSERT_FORA_PRECLAIM tentativa=% status=% numero_etapa=% numero_tentativa=%',
          NEW.codigo_hu_caixa_etapa_sap_tentativa,
          v_status,v_numero_etapa,v_numero_tentativa;
    END IF;

    IF EXISTS (
        SELECT 1
          FROM homologacao.hu_caixa_etapa_sap_evento ev
         WHERE ev.codigo_hu_caixa_etapa_sap_tentativa=
               NEW.codigo_hu_caixa_etapa_sap_tentativa
    ) THEN
        RAISE EXCEPTION
          'PA045_ITEM261_INSERT_APOS_EVENTO_BLOQUEADO tentativa=%',
          NEW.codigo_hu_caixa_etapa_sap_tentativa;
    END IF;

    RETURN NEW;
END
$fn$;

REVOKE ALL PRIVILEGES
ON TABLE homologacao.hu_caixa_etapa_sap_item
FROM PUBLIC;

REVOKE ALL PRIVILEGES
ON TABLE homologacao.hu_caixa_etapa_sap_item
FROM fugapet_q_app;

-- --------------------------------------------------------------------------
-- Contexto autoritativo por OP.
-- BOX_SAP_COMPLETE:
--   261 confirmado + evento final POST 2xx + ledger da tentativa autoritativa
--   101 confirmado + evento final POST 2xx
--   HU local confirmada + external id + HTTP 201 persistido
-- --------------------------------------------------------------------------
CREATE FUNCTION homologacao.fn_pa_045_261_contexto_op(p_op text)
RETURNS TABLE(
    numero_ordem_producao text,
    codigo_hu_caixa bigint,
    numero_caixa integer,
    status_hu_caixa text,
    status_261 text,
    numero_tentativa_261 integer,
    status_101 text,
    numero_tentativa_101 integer,
    tentativa_preparada_261 uuid,
    ledger_item_count bigint,
    box_sap_complete boolean
)
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path TO 'pg_catalog','homologacao'
AS $fn$
WITH base AS (
    SELECT
        hc.numero_ordem_producao::text AS op,
        hc.codigo_hu_caixa,
        hc.numero_caixa,
        hc.status_hu_caixa::text,
        hc.handling_unit_external_id,
        hc.http_status AS hu_http,
        e261.codigo_hu_caixa_etapa_sap AS etapa261_id,
        e261.status_etapa::text AS status_261,
        e261.numero_tentativa AS numero_261,
        e261.material_document AS md261,
        e261.material_document_year AS year261,
        e101.codigo_hu_caixa_etapa_sap AS etapa101_id,
        e101.status_etapa::text AS status_101,
        e101.numero_tentativa AS numero_101,
        e101.material_document AS md101,
        e101.material_document_year AS year101
    FROM homologacao.hu_caixa hc
    LEFT JOIN homologacao.hu_caixa_etapa_sap e261
      ON e261.codigo_hu_caixa=hc.codigo_hu_caixa
     AND e261.etapa_sap='261'
    LEFT JOIN homologacao.hu_caixa_etapa_sap e101
      ON e101.codigo_hu_caixa=hc.codigo_hu_caixa
     AND e101.etapa_sap='101'
    WHERE upper(btrim(hc.numero_ordem_producao::text))=
          upper(btrim(COALESCE(p_op,'')))
      AND hc.situacao_hu_caixa IS TRUE
),
a261 AS (
    SELECT
        b.codigo_hu_caixa,
        t.codigo_hu_caixa_etapa_sap_tentativa AS tentativa_id,
        count(i.codigo_hu_caixa_etapa_sap_item) AS item_count,
        EXISTS (
            SELECT 1
              FROM homologacao.hu_caixa_etapa_sap_evento ev
             WHERE ev.codigo_hu_caixa_etapa_sap_tentativa=
                   t.codigo_hu_caixa_etapa_sap_tentativa
               AND ev.tipo_evento='RESULTADO_POST'
               AND ev.resultado='CONFIRMADO'
               AND ev.http_status BETWEEN 200 AND 299
               AND ev.material_document=b.md261
               AND ev.material_document_year=b.year261
        ) AS final_ok
    FROM base b
    JOIN homologacao.hu_caixa_etapa_sap_tentativa t
      ON t.codigo_hu_caixa_etapa_sap=b.etapa261_id
     AND t.numero_tentativa=b.numero_261
    LEFT JOIN homologacao.hu_caixa_etapa_sap_item i
      ON i.codigo_hu_caixa_etapa_sap_tentativa=
         t.codigo_hu_caixa_etapa_sap_tentativa
    GROUP BY
        b.codigo_hu_caixa,t.codigo_hu_caixa_etapa_sap_tentativa,
        b.md261,b.year261
),
a101 AS (
    SELECT
        b.codigo_hu_caixa,
        EXISTS (
            SELECT 1
              FROM homologacao.hu_caixa_etapa_sap_tentativa t
              JOIN homologacao.hu_caixa_etapa_sap_evento ev
                ON ev.codigo_hu_caixa_etapa_sap_tentativa=
                   t.codigo_hu_caixa_etapa_sap_tentativa
             WHERE t.codigo_hu_caixa_etapa_sap=b.etapa101_id
               AND t.numero_tentativa=b.numero_101
               AND ev.tipo_evento='RESULTADO_POST'
               AND ev.resultado='CONFIRMADO'
               AND ev.http_status BETWEEN 200 AND 299
               AND ev.material_document=b.md101
               AND ev.material_document_year=b.year101
        ) AS final_ok
    FROM base b
),
prepared AS (
    SELECT
        b.codigo_hu_caixa,
        t.codigo_hu_caixa_etapa_sap_tentativa
    FROM base b
    JOIN homologacao.hu_caixa_etapa_sap_tentativa t
      ON t.codigo_hu_caixa_etapa_sap=b.etapa261_id
     AND t.numero_tentativa=b.numero_261
    WHERE b.status_261='PRONTA_PARA_ENVIO'
      AND t.claim_token IS NULL
      AND t.claim_obtido_em IS NULL
      AND t.claim_expira_em IS NULL
)
SELECT
    b.op,
    b.codigo_hu_caixa,
    b.numero_caixa,
    b.status_hu_caixa,
    b.status_261,
    b.numero_261,
    b.status_101,
    b.numero_101,
    p.codigo_hu_caixa_etapa_sap_tentativa,
    COALESCE(a261.item_count,0),
    (
      b.status_261='CONFIRMADO_SAP'
      AND b.md261 IS NOT NULL
      AND b.year261 IS NOT NULL
      AND COALESCE(a261.final_ok,false)
      AND COALESCE(a261.item_count,0)>0
      AND b.status_101='CONFIRMADO_SAP'
      AND b.md101 IS NOT NULL
      AND b.year101 IS NOT NULL
      AND COALESCE(a101.final_ok,false)
      AND b.status_hu_caixa='CONFIRMADA_SAP'
      AND NULLIF(btrim(COALESCE(b.handling_unit_external_id,'')),'') IS NOT NULL
      AND b.hu_http=201
    ) AS box_sap_complete
FROM base b
LEFT JOIN a261 ON a261.codigo_hu_caixa=b.codigo_hu_caixa
LEFT JOIN a101 ON a101.codigo_hu_caixa=b.codigo_hu_caixa
LEFT JOIN prepared p ON p.codigo_hu_caixa=b.codigo_hu_caixa
ORDER BY b.numero_caixa,b.codigo_hu_caixa
$fn$;

-- --------------------------------------------------------------------------
-- Hard-stops estruturados por OP.
-- --------------------------------------------------------------------------
CREATE FUNCTION homologacao.fn_pa_045_261_hard_stops(
    p_op text,
    p_codigo_hu_caixa bigint DEFAULT NULL
)
RETURNS TABLE(
    scenario text,
    codigo_hu_caixa bigint,
    numero_caixa integer,
    detail text
)
LANGUAGE plpgsql
STABLE
SECURITY DEFINER
SET search_path TO 'pg_catalog','homologacao'
AS $fn$
DECLARE
    v_op text:=upper(btrim(COALESCE(p_op,'')));
    v_numero_atual integer;
BEGIN
    IF v_op='' THEN
        RAISE EXCEPTION 'PA045_261_OP_OBRIGATORIA';
    END IF;

    IF p_codigo_hu_caixa IS NOT NULL THEN
        SELECT hc.numero_caixa
          INTO v_numero_atual
          FROM homologacao.hu_caixa hc
         WHERE hc.codigo_hu_caixa=p_codigo_hu_caixa
           AND upper(btrim(hc.numero_ordem_producao::text))=v_op
           AND hc.situacao_hu_caixa IS TRUE;

        IF NOT FOUND THEN
            RETURN QUERY
            SELECT
              'CURRENT_BOX_NOT_FOUND'::text,
              p_codigo_hu_caixa,
              NULL::integer,
              'caixa nao pertence a OP ativa informada'::text;
            RETURN;
        END IF;
    END IF;

    RETURN QUERY
    SELECT
        'ENVIANDO_SAP'::text,
        hc.codigo_hu_caixa,
        hc.numero_caixa,
        '261 em ENVIANDO_SAP'::text
    FROM homologacao.hu_caixa hc
    JOIN homologacao.hu_caixa_etapa_sap e
      ON e.codigo_hu_caixa=hc.codigo_hu_caixa
     AND e.etapa_sap='261'
    WHERE upper(btrim(hc.numero_ordem_producao::text))=v_op
      AND hc.situacao_hu_caixa IS TRUE
      AND e.status_etapa='ENVIANDO_SAP';

    RETURN QUERY
    SELECT
        'INDETERMINADO_RECONCILIACAO_PENDENTE'::text,
        hc.codigo_hu_caixa,
        hc.numero_caixa,
        '261 INDETERMINADO_TIMEOUT com reconciliacao PENDENTE'::text
    FROM homologacao.hu_caixa hc
    JOIN homologacao.hu_caixa_etapa_sap e
      ON e.codigo_hu_caixa=hc.codigo_hu_caixa
     AND e.etapa_sap='261'
    WHERE upper(btrim(hc.numero_ordem_producao::text))=v_op
      AND hc.situacao_hu_caixa IS TRUE
      AND e.status_etapa='INDETERMINADO_TIMEOUT'
      AND e.status_reconciliacao='PENDENTE';

    RETURN QUERY
    SELECT
        'LEGACY_CONFIRMED_WITHOUT_ITEM_SNAPSHOT'::text,
        hc.codigo_hu_caixa,
        hc.numero_caixa,
        '261 confirmado com MaterialDocument/Year sem ledger da tentativa autoritativa'::text
    FROM homologacao.hu_caixa hc
    JOIN homologacao.hu_caixa_etapa_sap e
      ON e.codigo_hu_caixa=hc.codigo_hu_caixa
     AND e.etapa_sap='261'
    WHERE upper(btrim(hc.numero_ordem_producao::text))=v_op
      AND hc.situacao_hu_caixa IS TRUE
      AND e.status_etapa='CONFIRMADO_SAP'
      AND e.material_document IS NOT NULL
      AND e.material_document_year IS NOT NULL
      AND NOT EXISTS (
          SELECT 1
            FROM homologacao.hu_caixa_etapa_sap_tentativa t
            JOIN homologacao.hu_caixa_etapa_sap_evento ev
              ON ev.codigo_hu_caixa_etapa_sap_tentativa=
                 t.codigo_hu_caixa_etapa_sap_tentativa
           WHERE t.codigo_hu_caixa_etapa_sap=e.codigo_hu_caixa_etapa_sap
             AND t.numero_tentativa=e.numero_tentativa
             AND ev.material_document=e.material_document
             AND ev.material_document_year=e.material_document_year
             AND (
                 (ev.tipo_evento='RESULTADO_POST'
                  AND ev.resultado='CONFIRMADO'
                  AND ev.http_status BETWEEN 200 AND 299)
                 OR
                 (ev.tipo_evento='RESULTADO_HISTORICO'
                  AND ev.resultado='CONFIRMADO_HISTORICO')
             )
             AND EXISTS (
                 SELECT 1
                   FROM homologacao.hu_caixa_etapa_sap_item i
                  WHERE i.codigo_hu_caixa_etapa_sap_tentativa=
                        t.codigo_hu_caixa_etapa_sap_tentativa
             )
      );

    IF v_numero_atual IS NOT NULL THEN
        RETURN QUERY
        SELECT
            'PREDECESSOR_NOT_BOX_SAP_COMPLETE'::text,
            c.codigo_hu_caixa,
            c.numero_caixa,
            'existe caixa anterior da OP ainda nao BOX_SAP_COMPLETE'::text
        FROM homologacao.fn_pa_045_261_contexto_op(v_op) c
        WHERE c.numero_caixa<v_numero_atual
          AND NOT c.box_sap_complete;
    END IF;

    RETURN QUERY
    SELECT
        CASE
          WHEN e.codigo_hu_caixa=p_codigo_hu_caixa
               AND NOT EXISTS (
                   SELECT 1
                     FROM homologacao.hu_caixa_etapa_sap_item i
                    WHERE i.codigo_hu_caixa_etapa_sap_tentativa=
                          t.codigo_hu_caixa_etapa_sap_tentativa
               )
          THEN 'PREPARED_ATTEMPT_WITHOUT_LEDGER'
          ELSE 'PREPARED_ATTEMPT_ACTIVE'
        END::text,
        e.codigo_hu_caixa,
        hc.numero_caixa,
        CASE
          WHEN e.codigo_hu_caixa=p_codigo_hu_caixa
          THEN 'tentativa preparada da caixa corrente exige reload/readback; nao criar nova'
          ELSE 'outra caixa da OP possui tentativa 261 preparada e inconclusiva'
        END::text
    FROM homologacao.hu_caixa hc
    JOIN homologacao.hu_caixa_etapa_sap e
      ON e.codigo_hu_caixa=hc.codigo_hu_caixa
     AND e.etapa_sap='261'
    JOIN homologacao.hu_caixa_etapa_sap_tentativa t
      ON t.codigo_hu_caixa_etapa_sap=e.codigo_hu_caixa_etapa_sap
     AND t.numero_tentativa=e.numero_tentativa
    WHERE upper(btrim(hc.numero_ordem_producao::text))=v_op
      AND hc.situacao_hu_caixa IS TRUE
      AND e.status_etapa='PRONTA_PARA_ENVIO'
      AND t.claim_token IS NULL
      AND t.claim_obtido_em IS NULL
      AND t.claim_expira_em IS NULL
      AND (
          e.codigo_hu_caixa IS DISTINCT FROM p_codigo_hu_caixa
          OR NOT EXISTS (
              SELECT 1
                FROM homologacao.hu_caixa_etapa_sap_item i
               WHERE i.codigo_hu_caixa_etapa_sap_tentativa=
                     t.codigo_hu_caixa_etapa_sap_tentativa
          )
      );
END
$fn$;

-- --------------------------------------------------------------------------
-- TOTAL_JA_ALOCADO autoritativo.
-- Somente tentativa corrente autoritativamente confirmada por POST 2xx,
-- documento/ano compatíveis e ledger existente.
-- --------------------------------------------------------------------------
CREATE FUNCTION homologacao.fn_pa_045_261_total_alocado(p_op text)
RETURNS TABLE(
    material text,
    reservation text,
    reservation_item text,
    entry_unit text,
    total_ja_alocado numeric
)
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path TO 'pg_catalog','homologacao'
AS $fn$
SELECT
    i.material::text,
    i.reservation::text,
    i.reservation_item::text,
    i.entry_unit::text,
    sum(i.quantity_in_entry_unit)::numeric AS total_ja_alocado
FROM homologacao.hu_caixa hc
JOIN homologacao.hu_caixa_etapa_sap e
  ON e.codigo_hu_caixa=hc.codigo_hu_caixa
 AND e.etapa_sap='261'
JOIN homologacao.hu_caixa_etapa_sap_tentativa t
  ON t.codigo_hu_caixa_etapa_sap=e.codigo_hu_caixa_etapa_sap
 AND t.numero_tentativa=e.numero_tentativa
JOIN homologacao.hu_caixa_etapa_sap_evento ev
  ON ev.codigo_hu_caixa_etapa_sap_tentativa=
     t.codigo_hu_caixa_etapa_sap_tentativa
JOIN homologacao.hu_caixa_etapa_sap_item i
  ON i.codigo_hu_caixa_etapa_sap_tentativa=
     t.codigo_hu_caixa_etapa_sap_tentativa
WHERE upper(btrim(hc.numero_ordem_producao::text))=
      upper(btrim(COALESCE(p_op,'')))
  AND hc.situacao_hu_caixa IS TRUE
  AND e.status_etapa='CONFIRMADO_SAP'
  AND ev.tipo_evento='RESULTADO_POST'
  AND ev.resultado='CONFIRMADO'
  AND ev.http_status BETWEEN 200 AND 299
  AND ev.material_document=e.material_document
  AND ev.material_document_year=e.material_document_year
GROUP BY i.material,i.reservation,i.reservation_item,i.entry_unit
ORDER BY i.material,i.reservation,i.reservation_item,i.entry_unit
$fn$;

-- --------------------------------------------------------------------------
-- Helper interno. NAO conceder EXECUTE ao runtime.
-- ItemOrdinal deve ser 1..N na ordem do array JSON.
-- --------------------------------------------------------------------------
CREATE FUNCTION homologacao.fn_pa_045_261_inserir_itens(
    p_tentativa uuid,
    p_itens jsonb
)
RETURNS integer
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path TO 'pg_catalog','homologacao'
AS $fn$
DECLARE
    v_count integer;
    v_inserted integer;
BEGIN
    IF p_tentativa IS NULL
       OR p_itens IS NULL
       OR jsonb_typeof(p_itens)<>'array'
       OR jsonb_array_length(p_itens)<=0 THEN
        RAISE EXCEPTION 'PA045_261_LEDGER_ITENS_INVALIDOS';
    END IF;

    SELECT count(*) INTO v_count
      FROM jsonb_array_elements(p_itens) WITH ORDINALITY AS x(item,ord)
     WHERE jsonb_typeof(x.item)<>'object'
        OR NOT (x.item ? 'ItemOrdinal')
        OR NOT (x.item ? 'Material')
        OR NOT (x.item ? 'Reservation')
        OR NOT (x.item ? 'ReservationItem')
        OR NOT (x.item ? 'QuantityInEntryUnit')
        OR NOT (x.item ? 'EntryUnit')
        OR NOT (x.item ? 'Batch')
        OR COALESCE(x.item->>'ItemOrdinal','') !~ '^[0-9]+$'
        OR (x.item->>'ItemOrdinal')::integer<>x.ord::integer
        OR char_length(btrim(COALESCE(x.item->>'Material',''))) NOT BETWEEN 1 AND 40
        OR char_length(btrim(COALESCE(x.item->>'Reservation',''))) NOT BETWEEN 1 AND 10
        OR char_length(btrim(COALESCE(x.item->>'ReservationItem',''))) NOT BETWEEN 1 AND 4
        OR char_length(btrim(COALESCE(x.item->>'EntryUnit',''))) NOT BETWEEN 1 AND 3
        OR jsonb_typeof(x.item->'Batch')<>'string'
        OR char_length(COALESCE(x.item->>'Batch',''))>10;

    IF v_count<>0 THEN
        RAISE EXCEPTION
          'PA045_261_LEDGER_ITEM_CONTRATO_INVALIDO count=%',v_count;
    END IF;

    INSERT INTO homologacao.hu_caixa_etapa_sap_item(
        codigo_hu_caixa_etapa_sap_tentativa,
        item_ordinal,
        material,
        reservation,
        reservation_item,
        quantity_in_entry_unit,
        entry_unit,
        batch
    )
    SELECT
        p_tentativa,
        x.ord::integer,
        btrim(x.item->>'Material'),
        btrim(x.item->>'Reservation'),
        btrim(x.item->>'ReservationItem'),
        (x.item->>'QuantityInEntryUnit')::numeric(13,3),
        btrim(x.item->>'EntryUnit'),
        x.item->>'Batch'
    FROM jsonb_array_elements(p_itens) WITH ORDINALITY AS x(item,ord);

    GET DIAGNOSTICS v_inserted=ROW_COUNT;

    IF v_inserted<>jsonb_array_length(p_itens) THEN
        RAISE EXCEPTION
          'PA045_261_LEDGER_CARDINALIDADE_DIVERGENTE esperado=% obtido=%',
          jsonb_array_length(p_itens),v_inserted;
    END IF;

    IF EXISTS (
        SELECT 1
          FROM homologacao.hu_caixa_etapa_sap_item i
         WHERE i.codigo_hu_caixa_etapa_sap_tentativa=p_tentativa
           AND i.quantity_in_entry_unit<=0
    ) THEN
        RAISE EXCEPTION 'PA045_261_LEDGER_QUANTIDADE_NAO_POSITIVA';
    END IF;

    RETURN v_inserted;
END
$fn$;

-- --------------------------------------------------------------------------
-- Preparação atômica tentativa + ledger.
--
-- Lock key:
-- hashtextextended('FUGAPET_PA261_ALLOC|' || UPPER(BTRIM(OP)),0)
-- A eventual colisão de hash só causa serialização conservadora entre OPs
-- distintas; nunca permite concorrência que deveria ser bloqueada.
--
-- p_expected_total: array normalizado de objetos
-- {Material,Reservation,ReservationItem,EntryUnit,TotalAllocated}
-- --------------------------------------------------------------------------
CREATE FUNCTION homologacao.fn_pa_045_261_preparar_tentativa(
    p_codigo_hu_caixa bigint,
    p_itens jsonb,
    p_expected_total jsonb,
    p_request_sanitizado jsonb,
    p_usuario bigint,
    p_terminal text
)
RETURNS TABLE(
    codigo_hu_caixa_etapa_sap_tentativa uuid,
    numero_tentativa integer,
    correlation_id uuid,
    codigo_hu_caixa_etapa_sap uuid
)
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path TO 'pg_catalog','homologacao'
AS $fn$
DECLARE
    v_terminal varchar(120);
    v_op text;
    v_etapa homologacao.hu_caixa_etapa_sap%ROWTYPE;
    v_tentativa uuid;
    v_numero integer;
    v_stop jsonb;
    v_actual_total jsonb;
    v_expected_normalized jsonb;
    v_prepared_count integer;
    v_inserted integer;
BEGIN
    v_terminal:=homologacao.fn_pa_045_validar_ator(p_usuario,p_terminal);

    IF p_request_sanitizado IS NULL
       OR jsonb_typeof(p_request_sanitizado)<>'object' THEN
        RAISE EXCEPTION 'PA045_261_REQUEST_SANITIZADO_INVALIDO';
    END IF;

    SELECT upper(btrim(hc.numero_ordem_producao::text))
      INTO v_op
      FROM homologacao.hu_caixa hc
     WHERE hc.codigo_hu_caixa=p_codigo_hu_caixa
       AND hc.situacao_hu_caixa IS TRUE
     FOR SHARE;

    IF v_op IS NULL OR v_op='' THEN
        RAISE EXCEPTION
          'PA045_261_CAIXA_OP_INVALIDA codigo=%',p_codigo_hu_caixa;
    END IF;

    PERFORM pg_advisory_xact_lock(
        hashtextextended('FUGAPET_PA261_ALLOC|' || v_op,0)
    );

    SELECT jsonb_agg(to_jsonb(h) ORDER BY h.scenario,h.numero_caixa,h.codigo_hu_caixa)
      INTO v_stop
      FROM homologacao.fn_pa_045_261_hard_stops(
          v_op,p_codigo_hu_caixa
      ) h;

    IF v_stop IS NOT NULL THEN
        RAISE EXCEPTION
          'PA045_261_HARD_STOP op=% scenarios=%',v_op,v_stop;
    END IF;

    SELECT * INTO v_etapa
      FROM homologacao.hu_caixa_etapa_sap e
     WHERE e.codigo_hu_caixa=p_codigo_hu_caixa
       AND e.etapa_sap='261'
     FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION
          'PA045_261_ETAPA_NAO_INICIALIZADA codigo=%',p_codigo_hu_caixa;
    END IF;

    IF v_etapa.origem_261='HISTORICO_EXISTENTE' THEN
        RAISE EXCEPTION
          'PA045_261_HISTORICO_NAO_USA_ALLOCATOR codigo=%',
          p_codigo_hu_caixa;
    END IF;

    IF v_etapa.status_etapa<>'PRONTA_PARA_ENVIO' THEN
        IF NOT homologacao.fn_pa_045_preparar_etapa(
            p_codigo_hu_caixa,
            '261',
            p_request_sanitizado,
            p_usuario,
            v_terminal
        ) THEN
            RAISE EXCEPTION
              'PA045_261_ETAPA_NAO_PREPARAVEL codigo=% status=%',
              p_codigo_hu_caixa,v_etapa.status_etapa;
        END IF;

        SELECT * INTO v_etapa
          FROM homologacao.hu_caixa_etapa_sap e
         WHERE e.codigo_hu_caixa=p_codigo_hu_caixa
           AND e.etapa_sap='261'
         FOR UPDATE;
    END IF;

    IF v_etapa.status_etapa<>'PRONTA_PARA_ENVIO'
       OR v_etapa.claim_token IS NOT NULL THEN
        RAISE EXCEPTION
          'PA045_261_PRECLAIM_STATE_INVALIDO codigo=% status=%',
          p_codigo_hu_caixa,v_etapa.status_etapa;
    END IF;

    SELECT count(*) INTO v_prepared_count
      FROM homologacao.hu_caixa_etapa_sap_tentativa t
     WHERE t.codigo_hu_caixa_etapa_sap=v_etapa.codigo_hu_caixa_etapa_sap
       AND t.claim_token IS NULL
       AND t.claim_obtido_em IS NULL
       AND t.claim_expira_em IS NULL;

    IF v_prepared_count<>0 THEN
        RAISE EXCEPTION
          'PA045_261_PREPARED_ATTEMPT_EXISTS_RELOAD_REQUIRED codigo=% count=%',
          p_codigo_hu_caixa,v_prepared_count;
    END IF;

    SELECT COALESCE(
        jsonb_agg(
          jsonb_build_object(
            'Material',a.material,
            'Reservation',a.reservation,
            'ReservationItem',a.reservation_item,
            'EntryUnit',a.entry_unit,
            'TotalAllocated',a.total_ja_alocado
          )
          ORDER BY a.material,a.reservation,a.reservation_item,a.entry_unit
        ),
        '[]'::jsonb
    )
      INTO v_actual_total
      FROM homologacao.fn_pa_045_261_total_alocado(v_op) a;

    IF p_expected_total IS NULL
       OR jsonb_typeof(p_expected_total)<>'array' THEN
        RAISE EXCEPTION 'PA045_261_EXPECTED_TOTAL_INVALIDO';
    END IF;

    SELECT COALESCE(
        jsonb_agg(
          jsonb_build_object(
            'Material',btrim(x.item->>'Material'),
            'Reservation',btrim(x.item->>'Reservation'),
            'ReservationItem',btrim(x.item->>'ReservationItem'),
            'EntryUnit',btrim(x.item->>'EntryUnit'),
            'TotalAllocated',(x.item->>'TotalAllocated')::numeric
          )
          ORDER BY
            btrim(x.item->>'Material'),
            btrim(x.item->>'Reservation'),
            btrim(x.item->>'ReservationItem'),
            btrim(x.item->>'EntryUnit')
        ),
        '[]'::jsonb
    )
      INTO v_expected_normalized
      FROM jsonb_array_elements(p_expected_total) AS x(item);

    IF v_actual_total IS DISTINCT FROM v_expected_normalized THEN
        RAISE EXCEPTION
          'PA045_261_CONCORRENCIA_ALOCACAO op=% expected=% actual=%',
          v_op,v_expected_normalized,v_actual_total;
    END IF;

    v_numero:=v_etapa.numero_tentativa+1;

    UPDATE homologacao.hu_caixa_etapa_sap
       SET numero_tentativa=v_numero,
           request_sanitizado=p_request_sanitizado,
           response_sanitizado=NULL,
           erro_sanitizado=NULL,
           http_status=NULL,
           claim_token=NULL,
           claim_obtido_em=NULL,
           claim_expira_em=NULL,
           recovery_claim_token=NULL,
           recovery_claim_obtido_em=NULL,
           recovery_claim_expira_em=NULL,
           iniciado_em=NULL,
           confirmado_em=NULL,
           falhou_em=NULL,
           indeterminado_em=NULL,
           material_document=NULL,
           material_document_year=NULL,
           status_reconciliacao='NAO_NECESSARIA',
           reconciliado_em=NULL,
           pode_reprocessar=false,
           usuario_operacao=p_usuario,
           terminal_operacao=v_terminal,
           atualizado_em=clock_timestamp()
     WHERE codigo_hu_caixa_etapa_sap=v_etapa.codigo_hu_caixa_etapa_sap
       AND status_etapa='PRONTA_PARA_ENVIO'
       AND claim_token IS NULL
       AND numero_tentativa=v_etapa.numero_tentativa;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'PA045_261_PREPARACAO_CONCORRENTE_PERDIDA';
    END IF;

    INSERT INTO homologacao.hu_caixa_etapa_sap_tentativa(
        codigo_hu_caixa_etapa_sap,
        numero_tentativa,
        correlation_id,
        claim_token,
        claim_obtido_em,
        claim_expira_em,
        request_sanitizado,
        usuario_operacao,
        terminal_operacao
    )
    VALUES(
        v_etapa.codigo_hu_caixa_etapa_sap,
        v_numero,
        v_etapa.correlation_id,
        NULL,NULL,NULL,
        p_request_sanitizado,
        p_usuario,
        v_terminal
    )
    RETURNING hu_caixa_etapa_sap_tentativa.codigo_hu_caixa_etapa_sap_tentativa
      INTO v_tentativa;

    v_inserted:=homologacao.fn_pa_045_261_inserir_itens(v_tentativa,p_itens);

    IF v_inserted<=0 THEN
        RAISE EXCEPTION 'PA045_261_LEDGER_VAZIO';
    END IF;

    IF EXISTS (
        SELECT 1
          FROM homologacao.hu_caixa_etapa_sap_evento ev
         WHERE ev.codigo_hu_caixa_etapa_sap_tentativa=v_tentativa
    ) THEN
        RAISE EXCEPTION
          'PA045_261_PREPARACAO_GEROU_EVENTO_INDEVIDO tentativa=%',
          v_tentativa;
    END IF;

    RETURN QUERY
    SELECT
        v_tentativa,
        v_numero,
        v_etapa.correlation_id,
        v_etapa.codigo_hu_caixa_etapa_sap;
END
$fn$;

-- --------------------------------------------------------------------------
-- Readback exato: command 261 futuro deve nascer exclusivamente daqui.
-- --------------------------------------------------------------------------
CREATE FUNCTION homologacao.fn_pa_045_261_readback(p_tentativa uuid)
RETURNS TABLE(
    attempt_id uuid,
    numero_tentativa integer,
    correlation_id uuid,
    codigo_hu_caixa bigint,
    numero_ordem_producao text,
    item_ordinal integer,
    material text,
    reservation text,
    reservation_item text,
    quantity_in_entry_unit numeric,
    entry_unit text,
    batch text
)
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path TO 'pg_catalog','homologacao'
AS $fn$
SELECT
    t.codigo_hu_caixa_etapa_sap_tentativa,
    t.numero_tentativa,
    t.correlation_id,
    e.codigo_hu_caixa,
    hc.numero_ordem_producao::text,
    i.item_ordinal,
    i.material::text,
    i.reservation::text,
    i.reservation_item::text,
    i.quantity_in_entry_unit::numeric,
    i.entry_unit::text,
    i.batch::text
FROM homologacao.hu_caixa_etapa_sap_tentativa t
JOIN homologacao.hu_caixa_etapa_sap e
  ON e.codigo_hu_caixa_etapa_sap=t.codigo_hu_caixa_etapa_sap
JOIN homologacao.hu_caixa hc
  ON hc.codigo_hu_caixa=e.codigo_hu_caixa
JOIN homologacao.hu_caixa_etapa_sap_item i
  ON i.codigo_hu_caixa_etapa_sap_tentativa=
     t.codigo_hu_caixa_etapa_sap_tentativa
WHERE t.codigo_hu_caixa_etapa_sap_tentativa=p_tentativa
  AND e.etapa_sap='261'
ORDER BY i.item_ordinal
$fn$;

-- --------------------------------------------------------------------------
-- Claim focal da MESMA tentativa preparada.
-- Claim + ENVIANDO são uma única alteração atômica da etapa.
-- A tentativa histórica permanece imutável; o claim efetivo fica na etapa
-- e posteriormente nos eventos, como já ocorre no 045.
-- --------------------------------------------------------------------------
CREATE FUNCTION homologacao.fn_pa_045_claim_etapa_preparada(
    p_codigo_hu_caixa bigint,
    p_tentativa uuid,
    p_usuario bigint,
    p_terminal text
)
RETURNS SETOF homologacao.hu_caixa_etapa_sap
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path TO 'pg_catalog','homologacao'
AS $fn$
DECLARE
    v_terminal varchar(120);
    v homologacao.hu_caixa_etapa_sap%ROWTYPE;
    v_numero integer;
    v_item_count integer;
    v_max_ordinal integer;
    v_claim_token uuid;
    v_claim_obtido_em timestamptz;
    v_claim_expira_em timestamptz;
    v_attempt_rows integer;
    v_stage_rows integer;
BEGIN
    v_terminal:=homologacao.fn_pa_045_validar_ator(p_usuario,p_terminal);

    SELECT e.*
      INTO v
      FROM homologacao.hu_caixa_etapa_sap_tentativa t
      JOIN homologacao.hu_caixa_etapa_sap e
        ON e.codigo_hu_caixa_etapa_sap=t.codigo_hu_caixa_etapa_sap
     WHERE t.codigo_hu_caixa_etapa_sap_tentativa=p_tentativa
       AND e.codigo_hu_caixa=p_codigo_hu_caixa
       AND e.etapa_sap='261'
       AND t.claim_token IS NULL
       AND t.claim_obtido_em IS NULL
       AND t.claim_expira_em IS NULL
     FOR UPDATE OF e;

    IF NOT FOUND THEN
        RETURN;
    END IF;

    SELECT t.numero_tentativa
      INTO v_numero
      FROM homologacao.hu_caixa_etapa_sap_tentativa t
     WHERE t.codigo_hu_caixa_etapa_sap_tentativa=p_tentativa;

    IF v.status_etapa<>'PRONTA_PARA_ENVIO'
       OR v.numero_tentativa<>v_numero
       OR v.claim_token IS NOT NULL THEN
        RAISE EXCEPTION
          'PA045_261_CLAIM_PREPARADA_IDENTIDADE_DIVERGENTE tentativa=% etapa_numero=% tentativa_numero=% status=%',
          p_tentativa,v.numero_tentativa,v_numero,v.status_etapa;
    END IF;

    IF EXISTS (
        SELECT 1
          FROM homologacao.hu_caixa_etapa_sap_evento ev
         WHERE ev.codigo_hu_caixa_etapa_sap_tentativa=p_tentativa
    ) THEN
        RAISE EXCEPTION
          'PA045_261_CLAIM_PREPARADA_JA_POSSUI_EVENTO tentativa=%',
          p_tentativa;
    END IF;

    SELECT count(*),max(item_ordinal)
      INTO v_item_count,v_max_ordinal
      FROM homologacao.hu_caixa_etapa_sap_item
     WHERE codigo_hu_caixa_etapa_sap_tentativa=p_tentativa;

    IF v_item_count<=0
       OR v_max_ordinal<>v_item_count
       OR EXISTS (
           SELECT 1
             FROM homologacao.hu_caixa_etapa_sap_item
            WHERE codigo_hu_caixa_etapa_sap_tentativa=p_tentativa
              AND item_ordinal<1
       ) THEN
        RAISE EXCEPTION
          'PA045_261_CLAIM_PREPARADA_LEDGER_INVALIDO tentativa=% count=% max_ordinal=%',
          p_tentativa,v_item_count,v_max_ordinal;
    END IF;

    -- Um unico claim e um unico par temporal para ETAPA + TENTATIVA.
    v_claim_token:=gen_random_uuid();
    v_claim_obtido_em:=clock_timestamp();
    v_claim_expira_em:=v_claim_obtido_em+interval '5 minutes';

    UPDATE homologacao.hu_caixa_etapa_sap_tentativa
       SET claim_token=v_claim_token,
           claim_obtido_em=v_claim_obtido_em,
           claim_expira_em=v_claim_expira_em
     WHERE codigo_hu_caixa_etapa_sap_tentativa=p_tentativa
       AND codigo_hu_caixa_etapa_sap=v.codigo_hu_caixa_etapa_sap
       AND numero_tentativa=v_numero
       AND claim_token IS NULL
       AND claim_obtido_em IS NULL
       AND claim_expira_em IS NULL;

    GET DIAGNOSTICS v_attempt_rows=ROW_COUNT;

    IF v_attempt_rows<>1 THEN
        RAISE EXCEPTION
          'PA045_261_CLAIM_PREPARADA_ATTEMPT_ROWCOUNT tentativa=% esperado=1 obtido=%',
          p_tentativa,v_attempt_rows;
    END IF;

    UPDATE homologacao.hu_caixa_etapa_sap
       SET status_etapa='ENVIANDO_SAP',
           claim_token=v_claim_token,
           claim_obtido_em=v_claim_obtido_em,
           claim_expira_em=v_claim_expira_em,
           iniciado_em=v_claim_obtido_em,
           http_status=NULL,
           response_sanitizado=NULL,
           erro_sanitizado=NULL,
           falhou_em=NULL,
           indeterminado_em=NULL,
           pode_reprocessar=false,
           usuario_operacao=p_usuario,
           terminal_operacao=v_terminal,
           atualizado_em=v_claim_obtido_em
     WHERE codigo_hu_caixa_etapa_sap=v.codigo_hu_caixa_etapa_sap
       AND status_etapa='PRONTA_PARA_ENVIO'
       AND numero_tentativa=v_numero
       AND claim_token IS NULL
       AND claim_obtido_em IS NULL
       AND claim_expira_em IS NULL
     RETURNING * INTO v;

    GET DIAGNOSTICS v_stage_rows=ROW_COUNT;

    IF v_stage_rows<>1 THEN
        RAISE EXCEPTION
          'PA045_261_CLAIM_PREPARADA_STAGE_ROWCOUNT tentativa=% esperado=1 obtido=%',
          p_tentativa,v_stage_rows;
    END IF;

    IF NOT EXISTS (
        SELECT 1
          FROM homologacao.hu_caixa_etapa_sap_tentativa t
         WHERE t.codigo_hu_caixa_etapa_sap_tentativa=p_tentativa
           AND t.numero_tentativa=v.numero_tentativa
           AND t.claim_token IS NOT DISTINCT FROM v.claim_token
           AND t.claim_obtido_em IS NOT DISTINCT FROM v.claim_obtido_em
           AND t.claim_expira_em IS NOT DISTINCT FROM v.claim_expira_em
           AND t.claim_token IS NOT NULL
           AND t.claim_obtido_em IS NOT NULL
           AND t.claim_expira_em IS NOT NULL
    ) THEN
        RAISE EXCEPTION
          'PA045_261_CLAIM_PREPARADA_PERSISTENCIA_DIVERGENTE tentativa=%',
          p_tentativa;
    END IF;

    RETURN NEXT v;
END
$fn$;

-- --------------------------------------------------------------------------
-- Backward compatibility:
-- claim legado segue idêntico para 101 e para 261 sem tentativa preparada.
-- Se o novo estado preparado existir, o claim legado falha fechado para não
-- criar uma segunda tentativa.
-- --------------------------------------------------------------------------
CREATE OR REPLACE FUNCTION homologacao.fn_pa_045_claim_etapa(
    p_codigo_hu_caixa bigint,
    p_etapa character varying,
    p_usuario bigint,
    p_terminal text
)
RETURNS SETOF homologacao.hu_caixa_etapa_sap
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path TO 'pg_catalog','homologacao'
AS $fn$
DECLARE
    v_terminal varchar(120);
    v_now timestamptz:=clock_timestamp();
    v homologacao.hu_caixa_etapa_sap%ROWTYPE;
BEGIN
    v_terminal:=homologacao.fn_pa_045_validar_ator(p_usuario,p_terminal);

    IF p_etapa NOT IN ('261','101') THEN
        RAISE EXCEPTION 'PA045_ETAPA_INVALIDA etapa=%',p_etapa;
    END IF;

    IF p_etapa='261' AND EXISTS (
        SELECT 1
          FROM homologacao.hu_caixa_etapa_sap e
          JOIN homologacao.hu_caixa_etapa_sap_tentativa t
            ON t.codigo_hu_caixa_etapa_sap=e.codigo_hu_caixa_etapa_sap
           AND t.numero_tentativa=e.numero_tentativa
         WHERE e.codigo_hu_caixa=p_codigo_hu_caixa
           AND e.etapa_sap='261'
           AND e.status_etapa='PRONTA_PARA_ENVIO'
           AND t.claim_token IS NULL
           AND t.claim_obtido_em IS NULL
           AND t.claim_expira_em IS NULL
    ) THEN
        RAISE EXCEPTION
          'PA045_261_PREPARED_ATTEMPT_REQUIRES_FOCAL_CLAIM codigo=%',
          p_codigo_hu_caixa;
    END IF;

    UPDATE homologacao.hu_caixa_etapa_sap
       SET status_etapa='ENVIANDO_SAP',
           numero_tentativa=numero_tentativa+1,
           claim_token=gen_random_uuid(),
           claim_obtido_em=v_now,
           claim_expira_em=v_now+interval '5 minutes',
           iniciado_em=v_now,
           http_status=NULL,
           response_sanitizado=NULL,
           erro_sanitizado=NULL,
           falhou_em=NULL,
           indeterminado_em=NULL,
           pode_reprocessar=false,
           usuario_operacao=p_usuario,
           terminal_operacao=v_terminal,
           atualizado_em=v_now
     WHERE codigo_hu_caixa=p_codigo_hu_caixa
       AND etapa_sap=p_etapa
       AND status_etapa='PRONTA_PARA_ENVIO'
     RETURNING * INTO v;

    IF NOT FOUND THEN
        RETURN;
    END IF;

    INSERT INTO homologacao.hu_caixa_etapa_sap_tentativa(
        codigo_hu_caixa_etapa_sap,
        numero_tentativa,
        correlation_id,
        claim_token,
        claim_obtido_em,
        claim_expira_em,
        request_sanitizado,
        usuario_operacao,
        terminal_operacao
    )
    VALUES(
        v.codigo_hu_caixa_etapa_sap,
        v.numero_tentativa,
        v.correlation_id,
        v.claim_token,
        v.claim_obtido_em,
        v.claim_expira_em,
        v.request_sanitizado,
        p_usuario,
        v_terminal
    );

    RETURN NEXT v;
END
$fn$;

-- --------------------------------------------------------------------------
-- ACL: runtime usa funções; não usa DML direto nem helper interno.
-- --------------------------------------------------------------------------
REVOKE ALL ON FUNCTION homologacao.fn_pa_045_261_contexto_op(text) FROM PUBLIC;
REVOKE ALL ON FUNCTION homologacao.fn_pa_045_261_hard_stops(text,bigint) FROM PUBLIC;
REVOKE ALL ON FUNCTION homologacao.fn_pa_045_261_total_alocado(text) FROM PUBLIC;
REVOKE ALL ON FUNCTION homologacao.fn_pa_045_261_inserir_itens(uuid,jsonb) FROM PUBLIC;
REVOKE ALL ON FUNCTION homologacao.fn_pa_045_261_preparar_tentativa(bigint,jsonb,jsonb,jsonb,bigint,text) FROM PUBLIC;
REVOKE ALL ON FUNCTION homologacao.fn_pa_045_261_readback(uuid) FROM PUBLIC;
REVOKE ALL ON FUNCTION homologacao.fn_pa_045_claim_etapa_preparada(bigint,uuid,bigint,text) FROM PUBLIC;

REVOKE ALL ON FUNCTION homologacao.fn_pa_045_261_inserir_itens(uuid,jsonb) FROM fugapet_q_app;

GRANT EXECUTE ON FUNCTION homologacao.fn_pa_045_261_contexto_op(text) TO fugapet_q_app;
GRANT EXECUTE ON FUNCTION homologacao.fn_pa_045_261_hard_stops(text,bigint) TO fugapet_q_app;
GRANT EXECUTE ON FUNCTION homologacao.fn_pa_045_261_total_alocado(text) TO fugapet_q_app;
GRANT EXECUTE ON FUNCTION homologacao.fn_pa_045_261_preparar_tentativa(bigint,jsonb,jsonb,jsonb,bigint,text) TO fugapet_q_app;
GRANT EXECUTE ON FUNCTION homologacao.fn_pa_045_261_readback(uuid) TO fugapet_q_app;
GRANT EXECUTE ON FUNCTION homologacao.fn_pa_045_claim_etapa_preparada(bigint,uuid,bigint,text) TO fugapet_q_app;

DO $postcheck$
DECLARE
    v_count integer;
BEGIN
    SELECT count(*) INTO v_count
      FROM information_schema.columns
     WHERE table_schema='homologacao'
       AND table_name='hu_caixa_etapa_sap_tentativa'
       AND column_name IN ('claim_token','claim_obtido_em','claim_expira_em')
       AND is_nullable='YES';
    IF v_count<>3 THEN
        RAISE EXCEPTION '058 POSTCHECK: trio claim nullable count=%',v_count;
    END IF;

    IF has_table_privilege('fugapet_q_app','homologacao.hu_caixa_etapa_sap_item','INSERT')
       OR has_table_privilege('fugapet_q_app','homologacao.hu_caixa_etapa_sap_item','UPDATE')
       OR has_table_privilege('fugapet_q_app','homologacao.hu_caixa_etapa_sap_item','DELETE') THEN
        RAISE EXCEPTION '058 POSTCHECK: runtime ainda possui DML direto ledger';
    END IF;

    IF has_function_privilege(
        'fugapet_q_app',
        'homologacao.fn_pa_045_261_inserir_itens(uuid,jsonb)',
        'EXECUTE'
    ) THEN
        RAISE EXCEPTION '058 POSTCHECK: helper interno ficou executavel pelo runtime';
    END IF;

    SELECT count(*) INTO v_count
      FROM pg_proc p
      JOIN pg_namespace n ON n.oid=p.pronamespace
     WHERE n.nspname='homologacao'
       AND p.proname IN (
         'fn_pa_045_261_contexto_op',
         'fn_pa_045_261_hard_stops',
         'fn_pa_045_261_total_alocado',
         'fn_pa_045_261_inserir_itens',
         'fn_pa_045_261_preparar_tentativa',
         'fn_pa_045_261_readback',
         'fn_pa_045_claim_etapa_preparada'
       )
       AND p.prosecdef IS TRUE;
    IF v_count<>7 THEN
        RAISE EXCEPTION '058 POSTCHECK: security-definer functions=% esperado=7',v_count;
    END IF;
END
$postcheck$;

SELECT 'MIGRATION_058_STATUS=PASS';
SELECT 'OP_SERIALIZATION=pg_advisory_xact_lock(hashtextextended(namespace|normalized_op,0))';
SELECT 'PREPARED_ATTEMPT_BEFORE_CLAIM=SIM';
SELECT 'RUNTIME_DIRECT_LEDGER_DML=PROIBIDO';
SELECT 'LATE_INSERT_DB_PROTECTION_FINAL=ACL+OWNER_GUARD+PRONTA_PRECLAIM_ONLY';
SELECT 'BACKWARD_COMPATIBLE=SIM';

COMMIT;
