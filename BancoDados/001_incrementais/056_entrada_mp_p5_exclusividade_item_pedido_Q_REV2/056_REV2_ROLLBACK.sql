\set ON_ERROR_STOP on
-- ============================================================
-- 056_REV2_ROLLBACK.sql
-- FugaPET | Ambientes QAS110 e QAS130 | Schema homologacao
-- ROLLBACK
--
-- ATENCAO: APAGA PROVA OPERACIONAL. Cada linha e a reivindicacao de recebimento
-- de um item de pedido, gravada por um lancamento real. Apagar nao devolve o
-- sistema ao estado anterior: devolve a POSSIBILIDADE de um segundo recebimento
-- do mesmo item, que e exatamente o que o GATE P5 existe para impedir.
--
-- Por isso este script RECUSA enquanto houver linha. Para desfazer mesmo assim:
--   psql -v confirmo_perder_exclusividade=sim -f <este arquivo>
--
-- Antes disso, considere a alternativa que nao perde nada: deixar a tabela. Uma
-- tabela a mais nao atrapalha; uma exclusividade perdida pode virar recebimento
-- em duplicidade no SAP.
--
-- EXIGE psql: a trava depende da variavel, substituida pelo CLIENTE antes de o
-- servidor ver o texto.
-- ============================================================

\if :{?confirmo_perder_exclusividade}
\else
\set confirmo_perder_exclusividade nao
\endif

BEGIN;

DO $$
DECLARE
    linhas bigint := 0;
BEGIN
    IF current_database() NOT IN ('fuga_jales_local_homologacao_q_v1_2',
                                  'fuga_jales_local_homologacao_q130_v1_3') THEN
        RAISE EXCEPTION '056 REV2 ROLLBACK ABORTADO: alvo nao reconhecido. Conectado em %.', current_database();
    END IF;

    IF to_regclass('homologacao.entrada_mp_p5_exclusividade_item_pedido') IS NULL THEN
        RAISE NOTICE '056 REV2 ROLLBACK: tabela ja nao existe. Nada a fazer.';
        RETURN;
    END IF;

    SELECT count(*) INTO linhas FROM homologacao.entrada_mp_p5_exclusividade_item_pedido;

    IF linhas > 0 AND :'confirmo_perder_exclusividade' <> 'sim' THEN
        RAISE EXCEPTION
            '056 REV2 ROLLBACK ABORTADO: % reivindicacao(oes) de recebimento seriam perdidas. Rode com -v confirmo_perder_exclusividade=sim se e isso mesmo.',
            linhas;
    END IF;

    IF linhas > 0 THEN
        RAISE WARNING '056 REV2 ROLLBACK: apagando % reivindicacao(oes), por confirmacao explicita.', linhas;
    END IF;
END
$$;

DROP TABLE IF EXISTS homologacao.entrada_mp_p5_exclusividade_item_pedido;

COMMIT;

\echo '== 056 REV2 revertido. =='
