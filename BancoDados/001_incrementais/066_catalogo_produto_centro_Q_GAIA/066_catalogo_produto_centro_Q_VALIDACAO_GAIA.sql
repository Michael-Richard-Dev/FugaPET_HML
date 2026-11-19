\set ON_ERROR_STOP on
-- ============================================================
-- 066_catalogo_produto_centro_Q_VALIDACAO_GAIA.sql
-- FugaPET | Ambientes QAS110 e QAS130 | Schema homologacao
-- Incremento 066 (Q) - VALIDACAO (SOMENTE LEITURA)
--
-- Confere o que foi criado e PROVA as regras tentando viola-las dentro de uma
-- transacao sempre desfeita. Nenhuma linha sobra.
-- ============================================================

\echo '== 066 Q VALIDACAO =='

SELECT current_database() AS banco, current_user AS usuario;

\echo '-- tabela existe (esperado: t)'
SELECT to_regclass('homologacao.produto_catalogo_centro') IS NOT NULL AS tabela_existe;

\echo '-- constraints (esperado: pk, uq e 2 checks)'
SELECT conname, contype
  FROM pg_constraint
 WHERE conrelid = 'homologacao.produto_catalogo_centro'::regclass
   AND contype IN ('p','u','c','f')
 ORDER BY contype, conname;

\echo '-- indices (esperado: pk + uq + 2)'
SELECT indexname FROM pg_indexes
 WHERE schemaname='homologacao' AND tablename='produto_catalogo_centro'
 ORDER BY indexname;

\echo '-- o indice de pendentes e PARCIAL (esperado: t) - e ele que torna a sincronizacao incremental'
SELECT EXISTS (
    SELECT 1 FROM pg_indexes
     WHERE schemaname='homologacao'
       AND tablename='produto_catalogo_centro'
       AND indexname='ix_produto_catalogo_centro_pendente'
       AND indexdef ILIKE '%WHERE (mestre_lido_em IS NULL)%') AS indice_parcial_ok;

\echo '-- privilegios do papel de aplicacao DESTE banco (esperado: DELETE, INSERT, SELECT, UPDATE)'
SELECT privilege_type
  FROM information_schema.role_table_grants
 WHERE table_schema='homologacao' AND table_name='produto_catalogo_centro'
   AND grantee = CASE current_database()
       WHEN 'fuga_jales_local_homologacao_q_v1_2'    THEN 'fugapet_q_app'
       WHEN 'fuga_jales_local_homologacao_q130_v1_3' THEN 'fugapet_q130_app'
   END
 ORDER BY privilege_type;

\echo '-- privilegio na sequencia (esperado: t) - sem ele o INSERT falha na hora'
SELECT has_sequence_privilege(
    CASE current_database()
        WHEN 'fuga_jales_local_homologacao_q_v1_2'    THEN 'fugapet_q_app'
        WHEN 'fuga_jales_local_homologacao_q130_v1_3' THEN 'fugapet_q130_app'
    END,
    'homologacao.produto_catalogo_centro_codigo_catalogo_seq', 'USAGE') AS sequencia_ok;

\echo '-- NAO pode haver chave estrangeira para o vinculo (as duas sao independentes)'
SELECT count(*) AS fks_para_vinculo
  FROM pg_constraint
 WHERE conrelid = 'homologacao.produto_catalogo_centro'::regclass
   AND contype = 'f';

\echo '-- PROVA DAS REGRAS (tudo desfeito ao final)'
BEGIN;

    SAVEPOINT s1;
    DO $prova1$
    BEGIN
        INSERT INTO homologacao.produto_catalogo_centro (centro, produto)
        VALUES ('3007', 'VALIDACAO-066');
        INSERT INTO homologacao.produto_catalogo_centro (centro, produto)
        VALUES ('3007', 'VALIDACAO-066');
        RAISE EXCEPTION 'FALHA: o mesmo material entrou duas vezes no mesmo centro.';
    EXCEPTION WHEN unique_violation THEN
        RAISE NOTICE 'OK: material repetido no mesmo centro recusado.';
    END
    $prova1$;
    ROLLBACK TO SAVEPOINT s1;

    SAVEPOINT s2;
    DO $prova2$
    BEGIN
        INSERT INTO homologacao.produto_catalogo_centro (centro, produto)
        VALUES ('   ', 'VALIDACAO-066');
        RAISE EXCEPTION 'FALHA: centro em branco foi aceito.';
    EXCEPTION WHEN check_violation THEN
        RAISE NOTICE 'OK: centro em branco recusado.';
    END
    $prova2$;
    ROLLBACK TO SAVEPOINT s2;

    \echo '-- o mesmo material pode existir em CENTROS diferentes (esperado: 2 linhas)'
    SAVEPOINT s3;
    INSERT INTO homologacao.produto_catalogo_centro (centro, produto)
    VALUES ('3007', 'VALIDACAO-066'), ('2000', 'VALIDACAO-066');
    SELECT count(*) AS em_dois_centros
      FROM homologacao.produto_catalogo_centro WHERE produto = 'VALIDACAO-066';
    ROLLBACK TO SAVEPOINT s3;

    \echo '-- material novo nasce PENDENTE de mestre (esperado: t) - e o nulo que a sincronizacao procura'
    SAVEPOINT s4;
    INSERT INTO homologacao.produto_catalogo_centro (centro, produto)
    VALUES ('3007', 'VALIDACAO-066');
    SELECT mestre_lido_em IS NULL AS nasce_pendente, presente_no_sap AS nasce_presente
      FROM homologacao.produto_catalogo_centro WHERE produto = 'VALIDACAO-066';
    ROLLBACK TO SAVEPOINT s4;

ROLLBACK;

\echo '-- nada sobrou (esperado: 0)'
SELECT count(*) AS resto
  FROM homologacao.produto_catalogo_centro WHERE produto LIKE 'VALIDACAO-066%';
