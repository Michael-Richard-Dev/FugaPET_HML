\set ON_ERROR_STOP on

-- ============================================================================
-- FugaPET Q - Migration 058
-- ROLLBACK TECNICO - boundary DB 045 pre-claim
-- SOURCE-ONLY. EXECUCAO NAO AUTORIZADA NESTE GATE.
-- ============================================================================
BEGIN;
SET LOCAL lock_timeout='5s';
SET LOCAL statement_timeout='120s';
SET LOCAL search_path=pg_catalog,homologacao;

DO $precheck$
DECLARE
    v_owner name;
    v_prepared bigint;
BEGIN
    IF current_database()<>'fuga_jales_local_homologacao_q_v1_2' THEN
        RAISE EXCEPTION '058 ROLLBACK: database incorreto: %',current_database();
    END IF;

    SELECT pg_get_userbyid(c.relowner)
      INTO v_owner
      FROM pg_class c
     WHERE c.oid='homologacao.hu_caixa_etapa_sap_tentativa'::regclass;

    IF current_user<>v_owner THEN
        RAISE EXCEPTION
          '058 ROLLBACK: executar como owner; current_user=% owner=%',
          current_user,v_owner;
    END IF;

    SELECT count(*) INTO v_prepared
      FROM homologacao.hu_caixa_etapa_sap_tentativa
     WHERE claim_token IS NULL
        OR claim_obtido_em IS NULL
        OR claim_expira_em IS NULL;

    IF v_prepared<>0 THEN
        RAISE EXCEPTION
          '058 ROLLBACK BLOQUEADO: prepared/unclaimed attempts existem count=%',
          v_prepared;
    END IF;
END
$precheck$;

-- Restaura o trigger de tentativa exatamente ao guard append-only 045 anterior.
DROP TRIGGER trg_045_etapa_tentativa_append_only
ON homologacao.hu_caixa_etapa_sap_tentativa;

CREATE TRIGGER trg_045_etapa_tentativa_append_only
BEFORE INSERT OR UPDATE OR DELETE
ON homologacao.hu_caixa_etapa_sap_tentativa
FOR EACH ROW
EXECUTE FUNCTION homologacao.fn_pa_045_historico_append_only();

DROP FUNCTION homologacao.fn_pa_045_etapa_tentativa_prepared_claim_guard();

REVOKE EXECUTE ON FUNCTION homologacao.fn_pa_045_261_contexto_op(text) FROM fugapet_q_app;
REVOKE EXECUTE ON FUNCTION homologacao.fn_pa_045_261_hard_stops(text,bigint) FROM fugapet_q_app;
REVOKE EXECUTE ON FUNCTION homologacao.fn_pa_045_261_total_alocado(text) FROM fugapet_q_app;
REVOKE EXECUTE ON FUNCTION homologacao.fn_pa_045_261_preparar_tentativa(bigint,jsonb,jsonb,jsonb,bigint,text) FROM fugapet_q_app;
REVOKE EXECUTE ON FUNCTION homologacao.fn_pa_045_261_readback(uuid) FROM fugapet_q_app;
REVOKE EXECUTE ON FUNCTION homologacao.fn_pa_045_claim_etapa_preparada(bigint,uuid,bigint,text) FROM fugapet_q_app;

DROP FUNCTION homologacao.fn_pa_045_claim_etapa_preparada(bigint,uuid,bigint,text);
DROP FUNCTION homologacao.fn_pa_045_261_readback(uuid);
DROP FUNCTION homologacao.fn_pa_045_261_preparar_tentativa(bigint,jsonb,jsonb,jsonb,bigint,text);
DROP FUNCTION homologacao.fn_pa_045_261_inserir_itens(uuid,jsonb);
DROP FUNCTION homologacao.fn_pa_045_261_total_alocado(text);
DROP FUNCTION homologacao.fn_pa_045_261_hard_stops(text,bigint);
DROP FUNCTION homologacao.fn_pa_045_261_contexto_op(text);

-- Restaura exatamente o guard criado no 057.
CREATE OR REPLACE FUNCTION homologacao.fn_pa_045_item_261_insert_guard()
RETURNS trigger
LANGUAGE plpgsql
SET search_path TO 'pg_catalog','homologacao'
AS $fn$
DECLARE
    v_etapa varchar;
    v_status varchar;
BEGIN
    SELECT e.etapa_sap,e.status_etapa
      INTO v_etapa,v_status
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

    IF v_status NOT IN ('PRONTA_PARA_ENVIO','ENVIANDO_SAP') THEN
        RAISE EXCEPTION
          'PA045_ITEM261_INSERT_TARDIO_BLOQUEADO tentativa=% status=%',
          NEW.codigo_hu_caixa_etapa_sap_tentativa,v_status;
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

-- Restaura exatamente o claim 045 anterior ao 058.
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

ALTER TABLE homologacao.hu_caixa_etapa_sap_tentativa
    DROP CONSTRAINT ck_045_etapa_tentativa_periodo;

ALTER TABLE homologacao.hu_caixa_etapa_sap_tentativa
    ALTER COLUMN claim_token SET NOT NULL,
    ALTER COLUMN claim_obtido_em SET NOT NULL,
    ALTER COLUMN claim_expira_em SET NOT NULL;

ALTER TABLE homologacao.hu_caixa_etapa_sap_tentativa
    ADD CONSTRAINT ck_045_etapa_tentativa_periodo
    CHECK (claim_expira_em>claim_obtido_em);

-- O 057 nao concedeu DML direto ao runtime; nada deve ser concedido aqui.
REVOKE ALL PRIVILEGES
ON TABLE homologacao.hu_caixa_etapa_sap_item
FROM fugapet_q_app;

SELECT 'ROLLBACK_058_STATUS=PASS';
SELECT 'TABLE_057_PRESERVED=SIM';
SELECT 'INDEX_056_TOUCHED=NAO';
SELECT 'DATA_MUTATION=0';

COMMIT;
