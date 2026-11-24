\set ON_ERROR_STOP on
-- ============================================================
-- 070_liberacao_tolerancia_recebimento_Q_PREFLIGHT_GAIA.sql
-- FugaPET | Ambientes QAS110 e QAS130 | Schema homologacao
-- Incremento 070 (Q) - PREFLIGHT (SOMENTE LEITURA) - nao escreve nada
-- ============================================================

\echo '== 070 PREFLIGHT =='

SELECT current_database() AS banco, current_user AS usuario,
       CASE current_database()
            WHEN 'fuga_jales_local_homologacao_q_v1_2'    THEN 'ALVO OK - QAS110'
            WHEN 'fuga_jales_local_homologacao_q130_v1_3' THEN 'ALVO OK - QAS130'
            ELSE 'ALVO ERRADO - NAO APLIQUE' END AS veredito;

\echo '-- papeis de aplicacao presentes'
-- OS DOIS PAPEIS EXISTEM NOS DOIS BANCOS DO Q. Ver o papel nao diz em qual ambiente
-- estamos, e conceder ao papel errado daria acesso ao ambiente vizinho. Quem decide e
-- current_database(), e e assim que a PROPOSTA escolhe.
SELECT rolname FROM pg_roles
 WHERE rolname IN ('fugapet_q_app','fugapet_q130_app') ORDER BY rolname;

\echo '-- tabelas de seguranca de que o incremento depende'
SELECT to_regclass('homologacao.permissao')::text        AS permissao,
       to_regclass('homologacao.perfil_acesso')::text    AS perfil_acesso,
       to_regclass('homologacao.perfil_permissao')::text AS perfil_permissao;

\echo '-- a tabela de log ja existe?'
SELECT to_regclass('homologacao.liberacao_tolerancia_recebimento')::text AS tabela;

\echo '-- a permissao LIBERAR_TOLERANCIA ja existe?'
SELECT codigo_permissao, situacao_permissao
  FROM homologacao.permissao
 WHERE modulo_permissao='PROCESSO_PRODUCAO'
   AND rotina_permissao='ENTRADA_PRODUTO'
   AND acao_permissao='LIBERAR_TOLERANCIA';

\echo '-- perfis atuais (o incremento acrescenta Gerente se faltar)'
SELECT codigo_perfil_acesso, nome_perfil_acesso, situacao_perfil_acesso
  FROM homologacao.perfil_acesso ORDER BY codigo_perfil_acesso;

\echo '-- quantas liberacoes ja gravadas (reaplicacao nao pode perder log)'
DO $contagem$
DECLARE linhas bigint;
BEGIN
    IF to_regclass('homologacao.liberacao_tolerancia_recebimento') IS NULL THEN
        RAISE NOTICE 'tabela ainda nao existe (primeira aplicacao)';
        RETURN;
    END IF;
    EXECUTE 'SELECT count(*) FROM homologacao.liberacao_tolerancia_recebimento' INTO linhas;
    RAISE NOTICE '% liberacao(oes) ja gravadas', linhas;
END
$contagem$;

\echo '== Fim do PREFLIGHT. Nada foi escrito. =='
