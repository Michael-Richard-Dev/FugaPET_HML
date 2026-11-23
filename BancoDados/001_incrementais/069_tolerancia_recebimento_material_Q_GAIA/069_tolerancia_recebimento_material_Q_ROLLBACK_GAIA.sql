\set ON_ERROR_STOP on
-- ============================================================
-- 069_tolerancia_recebimento_material_Q_ROLLBACK_GAIA.sql
-- FugaPET | Ambientes QAS110 e QAS130 | Schema homologacao
-- Incremento 069 (Q) - ROLLBACK
--
-- ATENCAO: APAGA CADASTRO. A tabela guarda decisoes do usuario (a tolerancia de cada
-- material), nao dados derivados — nada as reconstroi. Por isso este script NAO roda
-- sozinho: ele RECUSA enquanto houver linha, e so segue com o reconhecimento explicito.
--
-- Para desfazer MESMO com cadastro gravado:
--   psql -v confirmo_perder_cadastro=sim -f <este arquivo>
--
-- Antes disso, considere a alternativa que nao perde nada: deixar a tabela e desligar
-- o uso no aplicativo. Uma tabela sem leitor nao atrapalha ninguem.
--
-- ESTE ARQUIVO EXIGE psql, ao contrario dos outros tres do 069. A trava depende da
-- variavel :confirmo_perder_cadastro, substituida pelo CLIENTE psql antes de o servidor
-- ver o texto. Um cliente que so manda SQL nao a substitui e o script falha — desfecho
-- seguro, mas confuso se vier de surpresa.
-- ============================================================

\if :{?confirmo_perder_cadastro}
\else
\set confirmo_perder_cadastro nao
\endif

BEGIN;

DO $$
DECLARE
    linhas bigint := 0;
BEGIN
    IF current_database() NOT IN ('fuga_jales_local_homologacao_q_v1_2',
                                  'fuga_jales_local_homologacao_q130_v1_3') THEN
        RAISE EXCEPTION '069 Q ROLLBACK ABORTADO: alvo nao reconhecido. Conectado em %.', current_database();
    END IF;

    IF to_regclass('homologacao.tolerancia_recebimento_material') IS NULL THEN
        RAISE NOTICE '069 Q ROLLBACK: tabela ja nao existe. Nada a fazer.';
        RETURN;
    END IF;

    SELECT count(*) INTO linhas FROM homologacao.tolerancia_recebimento_material;

    IF linhas > 0 AND :'confirmo_perder_cadastro' <> 'sim' THEN
        RAISE EXCEPTION
            '069 Q ROLLBACK ABORTADO: % linha(s) de cadastro seriam perdidas. Rode com -v confirmo_perder_cadastro=sim se e isso mesmo.',
            linhas;
    END IF;

    IF linhas > 0 THEN
        RAISE WARNING '069 Q ROLLBACK: apagando % linha(s) de cadastro, por confirmacao explicita.', linhas;
    END IF;
END
$$;

DROP TABLE IF EXISTS homologacao.tolerancia_recebimento_material;

COMMIT;

\echo '== 069 Q revertido. =='
