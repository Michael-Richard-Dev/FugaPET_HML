\set ON_ERROR_STOP on
-- ============================================================
-- 071_tolerancia_processo_producao_Q_PREFLIGHT_GAIA.sql
-- FugaPET | Ambientes QAS110 e QAS130 | Schema homologacao
-- Incremento 071 (Q) - PREFLIGHT (SOMENTE LEITURA) - nao escreve nada
-- ============================================================

\echo '== 071 PREFLIGHT =='

SELECT current_database() AS banco, current_user AS usuario,
       CASE current_database()
            WHEN 'fuga_jales_local_homologacao_q_v1_2'    THEN 'ALVO OK - QAS110'
            WHEN 'fuga_jales_local_homologacao_q130_v1_3' THEN 'ALVO OK - QAS130'
            ELSE 'ALVO ERRADO - NAO APLIQUE' END AS veredito;

\echo '-- papeis de aplicacao presentes'
-- OS DOIS PAPEIS EXISTEM NOS DOIS BANCOS DO Q. Ver o papel nao diz em qual ambiente estamos,
-- e conceder ao errado daria acesso ao ambiente vizinho. Quem decide e current_database().
SELECT rolname FROM pg_roles
 WHERE rolname IN ('fugapet_q_app','fugapet_q130_app') ORDER BY rolname;

\echo '-- o catalogo de produtos (066) existe? a tela le dele'
SELECT to_regclass('homologacao.produto_catalogo_centro')::text AS catalogo;

\echo '-- a tolerancia do RECEBIMENTO (069) continua separada e intacta'
SELECT to_regclass('homologacao.tolerancia_recebimento_material')::text AS tolerancia_069;

\echo '-- as tabelas do 071 ja existem?'
SELECT to_regclass('homologacao.tolerancia_processo_producao')::text  AS tolerancia_071,
       to_regclass('homologacao.liberacao_tolerancia_processo')::text AS log_071;

\echo '-- a permissao de liberacao (070) existe? o 071 REUSA a mesma, nao cria outra'
SELECT codigo_permissao, situacao_permissao
  FROM homologacao.permissao
 WHERE modulo_permissao='PROCESSO_PRODUCAO'
   AND rotina_permissao='ENTRADA_PRODUTO'
   AND acao_permissao='LIBERAR_TOLERANCIA';

\echo '-- quantas linhas ja gravadas (reaplicacao nao pode perder nada)'
DO $contagem$
DECLARE linhas bigint;
BEGIN
    IF to_regclass('homologacao.tolerancia_processo_producao') IS NULL THEN
        RAISE NOTICE 'tolerancia_processo_producao ainda nao existe (primeira aplicacao)';
    ELSE
        EXECUTE 'SELECT count(*) FROM homologacao.tolerancia_processo_producao' INTO linhas;
        RAISE NOTICE '% tolerancia(s) de processo ja cadastradas', linhas;
    END IF;

    IF to_regclass('homologacao.liberacao_tolerancia_processo') IS NULL THEN
        RAISE NOTICE 'liberacao_tolerancia_processo ainda nao existe (primeira aplicacao)';
    ELSE
        EXECUTE 'SELECT count(*) FROM homologacao.liberacao_tolerancia_processo' INTO linhas;
        RAISE NOTICE '% liberacao(oes) de processo ja gravadas', linhas;
    END IF;
END
$contagem$;

\echo '== Fim do PREFLIGHT. Nada foi escrito. =='
