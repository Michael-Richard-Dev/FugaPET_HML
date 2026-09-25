\set ON_ERROR_STOP on

-- ============================================================================
-- FugaPET Q - Migration 058
-- VALIDACAO READ-ONLY - boundary DB 045 pre-claim
-- ============================================================================
BEGIN TRANSACTION
ISOLATION LEVEL REPEATABLE READ
READ ONLY;

SET LOCAL lock_timeout='5s';
SET LOCAL statement_timeout='120s';
SET LOCAL search_path=pg_catalog,homologacao;

DO $validation$
DECLARE
    v_count integer;
    v_def text;
BEGIN
    IF current_database()<>'fuga_jales_local_homologacao_q_v1_2' THEN
        RAISE EXCEPTION '058 VALIDACAO: database incorreto: %',current_database();
    END IF;

    SELECT count(*) INTO v_count
      FROM information_schema.columns
     WHERE table_schema='homologacao'
       AND table_name='hu_caixa_etapa_sap_tentativa'
       AND column_name IN ('claim_token','claim_obtido_em','claim_expira_em')
       AND is_nullable='YES';
    IF v_count<>3 THEN
        RAISE EXCEPTION '058 VALIDACAO: prepared-attempt nullable trio=%/3',v_count;
    END IF;

    IF NOT EXISTS (
        SELECT 1
          FROM pg_constraint
         WHERE conrelid='homologacao.hu_caixa_etapa_sap_tentativa'::regclass
           AND conname='ck_045_etapa_tentativa_periodo'
           AND pg_get_constraintdef(oid) LIKE '%claim_token IS NULL%'
           AND pg_get_constraintdef(oid) LIKE '%claim_expira_em > claim_obtido_em%'
    ) THEN
        RAISE EXCEPTION '058 VALIDACAO: claim-state constraint divergente';
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
       AND p.prosecdef IS TRUE
       AND p.proconfig @> ARRAY['search_path=pg_catalog, homologacao'];
    IF v_count<>7 THEN
        RAISE EXCEPTION '058 VALIDACAO: security/search_path functions=% esperado=7',v_count;
    END IF;

    IF to_regprocedure(
        'homologacao.fn_pa_045_261_preparar_tentativa(bigint,jsonb,jsonb,jsonb,bigint,text)'
    ) IS NULL
       OR to_regprocedure(
        'homologacao.fn_pa_045_claim_etapa_preparada(bigint,uuid,bigint,text)'
    ) IS NULL
       OR to_regprocedure(
        'homologacao.fn_pa_045_261_readback(uuid)'
    ) IS NULL THEN
        RAISE EXCEPTION '058 VALIDACAO: signatures focais ausentes';
    END IF;

    SELECT pg_get_functiondef(
        'homologacao.fn_pa_045_261_preparar_tentativa(bigint,jsonb,jsonb,jsonb,bigint,text)'::regprocedure
    ) INTO v_def;

    IF v_def NOT LIKE '%pg_advisory_xact_lock%'
       OR v_def NOT LIKE '%hashtextextended%'
       OR v_def NOT LIKE '%FUGAPET_PA261_ALLOC|%'
       OR v_def NOT LIKE '%fn_pa_045_261_hard_stops%'
       OR v_def NOT LIKE '%fn_pa_045_261_total_alocado%'
       OR v_def NOT LIKE '%fn_pa_045_261_inserir_itens%' THEN
        RAISE EXCEPTION '058 VALIDACAO: prepare boundary incompleto';
    END IF;

    SELECT pg_get_functiondef(
        'homologacao.fn_pa_045_claim_etapa_preparada(bigint,uuid,bigint,text)'::regprocedure
    ) INTO v_def;

    IF v_def NOT LIKE '%status_etapa=''ENVIANDO_SAP''%'
       OR v_def NOT LIKE '%numero_tentativa=v_numero%'
       OR v_def LIKE '%numero_tentativa=numero_tentativa+1%' THEN
        RAISE EXCEPTION '058 VALIDACAO: prepared claim nao preserva mesma tentativa';
    END IF;

    SELECT pg_get_functiondef(
        'homologacao.fn_pa_045_item_261_insert_guard()'::regprocedure
    ) INTO v_def;

    IF v_def NOT LIKE '%DML_DIRETO_BLOQUEADO%'
       OR v_def NOT LIKE '%PRONTA_PARA_ENVIO%'
       OR v_def LIKE '%ENVIANDO_SAP' THEN
        RAISE EXCEPTION '058 VALIDACAO: late-insert guard divergente';
    END IF;

    IF has_table_privilege('fugapet_q_app','homologacao.hu_caixa_etapa_sap_item','INSERT')
       OR has_table_privilege('fugapet_q_app','homologacao.hu_caixa_etapa_sap_item','UPDATE')
       OR has_table_privilege('fugapet_q_app','homologacao.hu_caixa_etapa_sap_item','DELETE') THEN
        RAISE EXCEPTION '058 VALIDACAO: runtime direct ledger DML presente';
    END IF;

    IF has_function_privilege(
        'fugapet_q_app',
        'homologacao.fn_pa_045_261_inserir_itens(uuid,jsonb)',
        'EXECUTE'
    ) THEN
        RAISE EXCEPTION '058 VALIDACAO: helper interno exposto ao runtime';
    END IF;

    IF NOT has_function_privilege(
        'fugapet_q_app',
        'homologacao.fn_pa_045_261_preparar_tentativa(bigint,jsonb,jsonb,jsonb,bigint,text)',
        'EXECUTE'
    )
       OR NOT has_function_privilege(
        'fugapet_q_app',
        'homologacao.fn_pa_045_claim_etapa_preparada(bigint,uuid,bigint,text)',
        'EXECUTE'
    )
       OR NOT has_function_privilege(
        'fugapet_q_app',
        'homologacao.fn_pa_045_261_readback(uuid)',
        'EXECUTE'
    ) THEN
        RAISE EXCEPTION '058 VALIDACAO: runtime sem EXECUTE boundary focal';
    END IF;

    SELECT pg_get_functiondef(
        'homologacao.fn_pa_045_261_total_alocado(text)'::regprocedure
    ) INTO v_def;

    IF v_def NOT LIKE '%RESULTADO_POST%'
       OR v_def NOT LIKE '%CONFIRMADO%'
       OR v_def NOT LIKE '%BETWEEN 200 AND 299%'
       OR v_def NOT LIKE '%material_document%'
       OR v_def NOT LIKE '%hu_caixa_etapa_sap_item%'
       OR v_def LIKE '%request_sanitizado%' THEN
        RAISE EXCEPTION '058 VALIDACAO: allocated-total contract divergente';
    END IF;

    SELECT pg_get_functiondef(
        'homologacao.fn_pa_045_261_hard_stops(text,bigint)'::regprocedure
    ) INTO v_def;

    IF v_def NOT LIKE '%ENVIANDO_SAP%'
       OR v_def NOT LIKE '%INDETERMINADO_TIMEOUT%'
       OR v_def NOT LIKE '%LEGACY_CONFIRMED_WITHOUT_ITEM_SNAPSHOT%'
       OR v_def NOT LIKE '%PREDECESSOR_NOT_BOX_SAP_COMPLETE%' THEN
        RAISE EXCEPTION '058 VALIDACAO: hard-stop contract divergente';
    END IF;

    SELECT pg_get_functiondef(
        'homologacao.fn_pa_045_claim_etapa(bigint,character varying,bigint,text)'::regprocedure
    ) INTO v_def;

    IF v_def NOT LIKE '%PREPARED_ATTEMPT_REQUIRES_FOCAL_CLAIM%'
       OR v_def NOT LIKE '%numero_tentativa=numero_tentativa+1%' THEN
        RAISE EXCEPTION '058 VALIDACAO: backward-compatible claim guard ausente';
    END IF;
END
$validation$;

-- Signatures / ACL material evidence.
SELECT
    p.proname,
    pg_get_function_identity_arguments(p.oid) AS args,
    pg_get_function_result(p.oid) AS result,
    p.prosecdef AS security_definer,
    p.proconfig
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
    'fn_pa_045_claim_etapa'
  )
ORDER BY p.proname;

SELECT
    has_table_privilege('fugapet_q_app','homologacao.hu_caixa_etapa_sap_item','SELECT') AS ledger_select,
    has_table_privilege('fugapet_q_app','homologacao.hu_caixa_etapa_sap_item','INSERT') AS ledger_insert,
    has_table_privilege('fugapet_q_app','homologacao.hu_caixa_etapa_sap_item','UPDATE') AS ledger_update,
    has_table_privilege('fugapet_q_app','homologacao.hu_caixa_etapa_sap_item','DELETE') AS ledger_delete;

-- Read-only live diagnostics; zero fixture.
SELECT * FROM homologacao.fn_pa_045_261_hard_stops(
    '__VALIDATION_NO_SUCH_OP__',NULL
);

SELECT * FROM homologacao.fn_pa_045_261_total_alocado(
    '__VALIDATION_NO_SUCH_OP__'
);

SELECT 'MIGRATION_058_VALIDATION_STATUS=PASS';
SELECT 'PREPARED_ATTEMPT_RESTART_SUPPORT=SIM';
SELECT 'LATE_INSERT_DB_PROTECTION_FINAL=ACL+OWNER_GUARD+PRONTA_PRECLAIM_ONLY';
SELECT 'RUNTIME_DIRECT_LEDGER_DML=PROIBIDO';
SELECT 'BACKWARD_COMPATIBLE=SIM';

ROLLBACK;
