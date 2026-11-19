\set ON_ERROR_STOP on
-- ============================================================
-- 067_cadastro_etiqueta_caixa_Q_ROLLBACK_GAIA.sql
-- FugaPET | Ambientes QAS110 e QAS130 | Schema homologacao
-- Incremento 067 (Q) - ROLLBACK
--
-- Remove as duas linhas de cadastro, na ordem: etiqueta primeiro, modelo depois
-- (a FK e ON DELETE RESTRICT).
--
-- ABORTA se houver VINCULO apontando para a etiqueta. Vinculo e decisao de gente, e
-- este script nao desfaz decisao de gente em silencio: se for isso que se quer,
-- apague o vinculo pela tela, com a pessoa sabendo.
-- ============================================================

BEGIN;

DO $guarda$
DECLARE
    presos integer;
BEGIN
    IF current_database() NOT IN (
           'fuga_jales_local_homologacao_q_v1_2',
           'fuga_jales_local_homologacao_q130_v1_3') THEN
        RAISE EXCEPTION '067 Q ROLLBACK ABORTADO: alvo nao reconhecido. Conectado em %.', current_database();
    END IF;

    SELECT count(*) INTO presos
      FROM homologacao.produto_etiqueta_vinculo v
      JOIN homologacao.etiqueta e ON e.codigo_etiqueta = v.codigo_etiqueta
     WHERE upper(btrim(e.codigo_interno)) = upper(btrim('CAIXA-PA-8X5'));

    IF presos > 0 THEN
        RAISE EXCEPTION '067 Q ROLLBACK ABORTADO: % vinculo(s) apontam para esta etiqueta. Remova pela tela primeiro.', presos;
    END IF;
END
$guarda$;

DELETE FROM homologacao.etiqueta
 WHERE upper(btrim(codigo_interno)) = upper(btrim('CAIXA-PA-8X5'));

DELETE FROM homologacao.modelo_etiqueta
 WHERE upper(btrim(nome_modelo_etiqueta)) = upper(btrim('Caixa Produto Acabado 8x5'))
   AND versao = 1;

COMMIT;

\echo '== 067 Q revertido. O cadastro voltou a ficar vazio. =='
