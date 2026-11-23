\set ON_ERROR_STOP on
-- ============================================================
-- 069_tolerancia_recebimento_material_Q_PREFLIGHT_GAIA.sql
-- FugaPET | Ambientes QAS110 e QAS130 | Schema homologacao
-- Incremento 069 (Q) - Tolerancia de pesagem por material no Recebimento
-- PREFLIGHT (SOMENTE LEITURA) - nao escreve nada
--
-- Porte do incremento 069 da linha DEV, aplicado e validado la em 23/11/2026. Mesmo
-- objeto e mesmas regras; muda o schema (homologacao) e o papel, escolhido pelo banco.
--
-- Rode ANTES da PROPOSTA e leia a saida.
-- ============================================================

\echo '== 069 Q PREFLIGHT =='

SELECT current_database() AS banco,
       current_user       AS usuario,
       CASE current_database()
            WHEN 'fuga_jales_local_homologacao_q_v1_2'    THEN 'ALVO OK (QAS110) - papel fugapet_q_app'
            WHEN 'fuga_jales_local_homologacao_q130_v1_3' THEN 'ALVO OK (QAS130) - papel fugapet_q130_app'
            ELSE 'ALVO NAO RECONHECIDO - NAO APLIQUE'
       END AS veredito;

\echo '-- o papel da aplicacao existe neste banco?'
SELECT rolname FROM pg_roles
 WHERE rolname IN ('fugapet_q_app', 'fugapet_q130_app')
 ORDER BY rolname;

\echo '-- a tabela ja existe?'
SELECT to_regclass('homologacao.tolerancia_recebimento_material') AS tabela;

\echo '-- se existir, as colunas atuais'
SELECT column_name, data_type, numeric_precision, numeric_scale, is_nullable
  FROM information_schema.columns
 WHERE table_schema = 'homologacao'
   AND table_name   = 'tolerancia_recebimento_material'
 ORDER BY ordinal_position;

\echo '-- restricoes atuais, se houver'
SELECT conname, pg_get_constraintdef(oid) AS definicao
  FROM pg_constraint
 WHERE conrelid = to_regclass('homologacao.tolerancia_recebimento_material')
 ORDER BY conname;

\echo '-- quantas linhas (reaplicacao nao pode apagar cadastro existente)'
-- SQL DINAMICO de proposito: um CASE com SELECT count(*) seria analisado mesmo com o
-- ramo nao escolhido e falharia com 42P01 no caso MAIS COMUM — a tabela nao existir.
DO $contagem$
DECLARE
    linhas bigint;
BEGIN
    IF to_regclass('homologacao.tolerancia_recebimento_material') IS NULL THEN
        RAISE NOTICE 'tabela ainda nao existe (primeira aplicacao)';
        RETURN;
    END IF;

    EXECUTE 'SELECT count(*) FROM homologacao.tolerancia_recebimento_material' INTO linhas;
    RAISE NOTICE '% linha(s) ja cadastradas', linhas;
END
$contagem$;

\echo '== Fim do PREFLIGHT. Nada foi escrito. =='
