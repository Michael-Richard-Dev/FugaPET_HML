\set ON_ERROR_STOP on
-- ============================================================
-- 068_processo_pesagem_na_caixa_Q_ROLLBACK_GAIA.sql
-- FugaPET | Ambientes QAS110 e QAS130 | Schema homologacao
-- Incremento 068 (Q) - ROLLBACK
--
-- Devolve o indice a regra antiga e remove a coluna.
--
-- ATENCAO, E ESTA E A PARTE QUE MORDE: se houver mais de uma caixa pendente no mesmo
-- terminal, o indice antigo NAO PODE ser recriado — e correto que nao possa, porque e
-- exatamente a situacao que ele proibe. O script ABORTA e mostra os terminais em
-- conflito, em vez de apagar ou alterar caixa de ninguem para caber. Resolver significa
-- enviar ou cancelar essas caixas pela tela, com gente decidindo.
--
-- Reverter o banco sem reverter o executavel deixa a tela do envio unico operando
-- contra um banco que recusa a segunda caixa. Reverter os dois, ou nenhum.
-- ============================================================

BEGIN;

DO $guarda$
DECLARE
    conflitos integer;
BEGIN
    IF current_database() NOT IN (
           'fuga_jales_local_homologacao_q_v1_2',
           'fuga_jales_local_homologacao_q130_v1_3') THEN
        RAISE EXCEPTION '068 Q ROLLBACK ABORTADO: alvo nao reconhecido. Conectado em %.', current_database();
    END IF;

    SELECT count(*) INTO conflitos FROM (
        SELECT upper(btrim(terminal))
          FROM homologacao.hu_caixa
         WHERE status_hu_caixa NOT IN ('CONFIRMADA_SAP','CANCELADA')
         GROUP BY 1 HAVING count(*) > 1
    ) t;

    IF conflitos > 0 THEN
        RAISE EXCEPTION
            '068 Q ROLLBACK ABORTADO: % terminal(is) com mais de uma caixa pendente. O indice antigo nao cabe. Envie ou cancele essas caixas pela tela primeiro.',
            conflitos;
    END IF;
END
$guarda$;

\echo '-- terminais com mais de uma pendente (esperado: 0 linhas, senao o script ja abortou)'
SELECT upper(btrim(terminal)) AS terminal, count(*) AS pendentes
  FROM homologacao.hu_caixa
 WHERE status_hu_caixa NOT IN ('CONFIRMADA_SAP','CANCELADA')
 GROUP BY 1 HAVING count(*) > 1 ORDER BY 2 DESC;

DROP INDEX IF EXISTS homologacao.uq_hu_caixa_terminal_ativo;

CREATE UNIQUE INDEX uq_hu_caixa_terminal_ativo
    ON homologacao.hu_caixa (upper(btrim(terminal)))
 WHERE status_hu_caixa NOT IN ('CONFIRMADA_SAP','CANCELADA');

ALTER TABLE homologacao.hu_caixa
    DROP CONSTRAINT IF EXISTS ck_hu_caixa_processo_pesagem;

ALTER TABLE homologacao.hu_caixa
    DROP COLUMN IF EXISTS processo_pesagem;

COMMIT;

\echo '== 068 Q revertido. O indice voltou a regra antiga. =='
