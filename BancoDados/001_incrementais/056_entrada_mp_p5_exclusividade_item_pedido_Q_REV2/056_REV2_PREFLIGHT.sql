\set ON_ERROR_STOP on
-- ============================================================
-- 056_REV2_PREFLIGHT.sql
-- FugaPET | Ambientes QAS110 e QAS130 | Schema homologacao
-- Exclusividade duravel do item do pedido (fonte do GATE P5)
-- PREFLIGHT (SOMENTE LEITURA) - nao escreve nada
--
-- POR QUE EXISTE UMA REV2.
--
-- Ja havia um 056_Q no repositorio, nunca aplicado, com DOIS problemas que so
-- apareceriam em producao:
--   1. NENHUMA guarda de alvo. Rodava em qualquer banco conectado.
--   2. GRANT cravado em 'fugapet_q_app'. No QAS130 o papel da aplicacao e
--      'fugapet_q130_app' — e, como 'fugapet_q_app' TAMBEM existe naquele banco,
--      o GRANT passaria em silencio dando permissao ao papel ERRADO. A tabela
--      ficaria ilegivel para quem a usa, e o GATE P5 trataria isso como fonte
--      indisponivel, que BLOQUEIA o recebimento.
--
-- POR QUE APLICAR AGORA: o codigo do Q ja consulta esta tabela
-- (Servicos/Entrada/ExclusividadeItemPedidoDuravelServico.cs) e ela NAO EXISTE
-- em nenhum dos dois bancos do Q. E falha latente, nao melhoria nova.
--
-- Forma conferida contra o banco do DEV110 em 24/11/2026, que e a referencia.
-- ============================================================

\echo '== 056 REV2 PREFLIGHT =='

SELECT current_database() AS banco,
       current_user       AS usuario,
       CASE current_database()
            WHEN 'fuga_jales_local_homologacao_q_v1_2'    THEN 'ALVO OK (QAS110) - papel fugapet_q_app'
            WHEN 'fuga_jales_local_homologacao_q130_v1_3' THEN 'ALVO OK (QAS130) - papel fugapet_q130_app'
            ELSE 'ALVO NAO RECONHECIDO - NAO APLIQUE'
       END AS veredito;

\echo '-- papeis de aplicacao presentes (os DOIS existem nos dois bancos: nao sirva de criterio)'
SELECT rolname FROM pg_roles WHERE rolname IN ('fugapet_q_app','fugapet_q130_app') ORDER BY rolname;

\echo '-- a tabela ja existe?'
SELECT to_regclass('homologacao.entrada_mp_p5_exclusividade_item_pedido') AS tabela;

\echo '-- se existir, colunas atuais'
SELECT column_name, data_type, is_nullable, column_default
  FROM information_schema.columns
 WHERE table_schema='homologacao' AND table_name='entrada_mp_p5_exclusividade_item_pedido'
 ORDER BY ordinal_position;

\echo '-- quantas linhas (reaplicacao nao pode perder exclusividade ja obtida)'
DO $contagem$
DECLARE linhas bigint;
BEGIN
    IF to_regclass('homologacao.entrada_mp_p5_exclusividade_item_pedido') IS NULL THEN
        RAISE NOTICE 'tabela ainda nao existe (primeira aplicacao)';
        RETURN;
    END IF;
    EXECUTE 'SELECT count(*) FROM homologacao.entrada_mp_p5_exclusividade_item_pedido' INTO linhas;
    RAISE NOTICE '% linha(s) ja gravadas', linhas;
END
$contagem$;

\echo '== Fim do PREFLIGHT. Nada foi escrito. =='
