\set ON_ERROR_STOP on
-- ============================================================
-- 054_perfil_resultado_snapshot_apontamento_Q_VALIDACAO_GAIA.sql
-- FugaPET | Ambiente Q | Schema homologacao
-- Incremento 054 - Validacao pos-migracao (SOMENTE LEITURA)
-- Autoridade: FUGAPET-Q-CONTROLE-APONTAMENTOS-GOLDEN-PROFILE-MIGRATION-093C-M1
--
-- VALIDACAO: confirma que a coluna codigo_perfil_resultado existe como
--   bigint NULL, sem default, sem FK, sem index. NAO altera nada.
--   NAO DML. NAO DDL. NAO SAP.
-- ============================================================

DO $$
DECLARE
    v_data_type text;
    v_is_nullable text;
    v_default text;
    v_fk bigint;
    v_idx bigint;
BEGIN
    IF current_database() <> 'fuga_jales_local_homologacao_q_v1_2' THEN
        RAISE EXCEPTION '054 VALIDACAO: database incorreto: %', current_database();
    END IF;

    SELECT data_type, is_nullable, column_default
      INTO v_data_type, v_is_nullable, v_default
      FROM information_schema.columns
     WHERE table_schema = 'homologacao'
       AND table_name = 'operacao_producao_apontamento'
       AND column_name = 'codigo_perfil_resultado';

    IF NOT FOUND THEN
        RAISE EXCEPTION '054 VALIDACAO: coluna codigo_perfil_resultado ausente.';
    END IF;

    IF v_data_type <> 'bigint' THEN
        RAISE EXCEPTION '054 VALIDACAO: tipo inesperado: % (esperado bigint)', v_data_type;
    END IF;

    IF v_is_nullable <> 'YES' THEN
        RAISE EXCEPTION '054 VALIDACAO: nullable inesperado: % (esperado YES)', v_is_nullable;
    END IF;

    IF v_default IS NOT NULL THEN
        RAISE EXCEPTION '054 VALIDACAO: default inesperado: % (esperado NONE)', v_default;
    END IF;

    -- Nenhuma FK deve referenciar/originar-se desta coluna neste incremento.
    SELECT count(*)
      INTO v_fk
      FROM information_schema.key_column_usage kcu
      JOIN information_schema.table_constraints tc
        ON tc.constraint_name = kcu.constraint_name
       AND tc.constraint_schema = kcu.constraint_schema
     WHERE kcu.table_schema = 'homologacao'
       AND kcu.table_name = 'operacao_producao_apontamento'
       AND kcu.column_name = 'codigo_perfil_resultado'
       AND tc.constraint_type = 'FOREIGN KEY';

    IF v_fk <> 0 THEN
        RAISE EXCEPTION '054 VALIDACAO: FK inesperada na coluna (esperado 0).';
    END IF;

    -- Nenhum indice deve cobrir a coluna neste incremento.
    SELECT count(*)
      INTO v_idx
      FROM pg_index i
      JOIN pg_class c ON c.oid = i.indrelid
      JOIN pg_namespace n ON n.oid = c.relnamespace
      JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum = ANY (i.indkey)
     WHERE n.nspname = 'homologacao'
       AND c.relname = 'operacao_producao_apontamento'
       AND a.attname = 'codigo_perfil_resultado';

    IF v_idx <> 0 THEN
        RAISE EXCEPTION '054 VALIDACAO: index inesperado sobre a coluna (esperado 0).';
    END IF;

    RAISE NOTICE '054 VALIDACAO OK: codigo_perfil_resultado bigint NULL, sem default/FK/index.';
END
$$;

\echo '054_VALIDACAO_CONCLUIDO'
\echo 'COLUMN=codigo_perfil_resultado'
\echo 'TYPE=bigint'
\echo 'NULLABLE=YES'
\echo 'DEFAULT=NONE'
\echo 'FK=NAO'
\echo 'INDEX=NAO'
\echo 'SAP_WRITE=0'
