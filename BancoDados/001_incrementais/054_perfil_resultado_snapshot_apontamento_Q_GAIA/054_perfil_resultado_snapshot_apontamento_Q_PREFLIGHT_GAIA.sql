\set ON_ERROR_STOP on
-- ============================================================
-- 054_perfil_resultado_snapshot_apontamento_Q_PREFLIGHT_GAIA.sql
-- FugaPET | Ambiente Q | Schema homologacao
-- Incremento 054 - Snapshot do perfil de resultado no apontamento
-- Autoridade: FUGAPET-Q-CONTROLE-APONTAMENTOS-GOLDEN-PROFILE-MIGRATION-093C-M1
--             (contrato DB Gaia V6 - Gate 093C)
--
-- PREFLIGHT (SOMENTE LEITURA): confirma pre-condicoes do incremento 054.
--   NAO altera dados nem estrutura. NAO DML. NAO DDL. NAO SAP.
-- ============================================================

DO $$
BEGIN
    IF current_database() <> 'fuga_jales_local_homologacao_q_v1_2' THEN
        RAISE EXCEPTION '054 PREFLIGHT: database incorreto: %', current_database();
    END IF;

    IF to_regclass('homologacao.operacao_producao_apontamento') IS NULL THEN
        RAISE EXCEPTION '054 PREFLIGHT: tabela homologacao.operacao_producao_apontamento ausente.';
    END IF;

    -- A coluna-alvo NAO deve existir ainda (fail-closed contra estado divergente).
    IF EXISTS (
        SELECT 1
        FROM information_schema.columns
        WHERE table_schema = 'homologacao'
          AND table_name = 'operacao_producao_apontamento'
          AND column_name = 'codigo_perfil_resultado'
    ) THEN
        RAISE EXCEPTION '054 PREFLIGHT: coluna codigo_perfil_resultado ja existe.';
    END IF;

    -- A fonte autoritativa normalizada deve existir (contrato inalterado por 054).
    IF to_regclass('homologacao.operacao_resultado_perfil') IS NULL THEN
        RAISE EXCEPTION '054 PREFLIGHT: tabela normalizada homologacao.operacao_resultado_perfil ausente.';
    END IF;

    RAISE NOTICE '054 PREFLIGHT OK: pronto para adicionar codigo_perfil_resultado (snapshot).';
END
$$;

\echo '054_PREFLIGHT_CONCLUIDO'
\echo 'TARGET_TABLE=homologacao.operacao_producao_apontamento'
\echo 'TARGET_COLUMN_PRESENT=NAO'
\echo 'NORMALIZED_SOURCE_PRESENT=SIM'
\echo 'DML=0'
\echo 'DDL=0'
\echo 'SAP_WRITE=0'
