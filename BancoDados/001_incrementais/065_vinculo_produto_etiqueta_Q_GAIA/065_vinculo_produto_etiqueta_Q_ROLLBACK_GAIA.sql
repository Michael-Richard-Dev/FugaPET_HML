\set ON_ERROR_STOP on
-- ============================================================
-- 065_vinculo_produto_etiqueta_Q_ROLLBACK_GAIA.sql
-- FugaPET | Ambientes QAS110 e QAS130 | Schema homologacao
-- Incremento 065 (Q) - ROLLBACK
--
-- DESTRUTIVO: apaga a tabela e, com ela, TODOS os vinculos ja cadastrados. Nao ha
-- como recuperar as decisoes do usuario depois disso.
--
-- ATENCAO: a INATIVACAO DE ETIQUETA consulta esta tabela. Revertendo aqui sem
-- reverter tambem o executavel, inativar etiqueta passa a falhar com "relation
-- does not exist". Reverter os dois, ou nenhum.
-- ============================================================

BEGIN;

DO $$
BEGIN
    IF current_database() NOT IN (
        'fuga_jales_local_homologacao_q_v1_2',
        'fuga_jales_local_homologacao_q130_v1_3') THEN
        RAISE EXCEPTION '065 Q ROLLBACK ABORTADO: alvo nao reconhecido. Conectado em %.', current_database();
    END IF;
END
$$;

\echo '-- quantos vinculos serao perdidos:'
SELECT count(*) AS vinculos_a_perder
  FROM homologacao.produto_etiqueta_vinculo;

DROP TABLE IF EXISTS homologacao.produto_etiqueta_vinculo;

COMMIT;

\echo '== 065 Q revertido. =='
