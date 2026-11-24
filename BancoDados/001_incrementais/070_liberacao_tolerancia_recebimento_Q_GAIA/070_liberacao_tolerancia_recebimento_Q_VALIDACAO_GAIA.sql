\set ON_ERROR_STOP on
-- ============================================================
-- 070_liberacao_tolerancia_recebimento_Q_VALIDACAO_GAIA.sql
-- FugaPET | Ambientes QAS110 e QAS130 | Schema homologacao
-- Incremento 070 (Q) - VALIDACAO (SOMENTE LEITURA) - nao escreve nada
--
-- Prova pela definicao REAL lida do catalogo, nunca inserindo linhas.
--
-- O item 5 e o mais importante e o menos obvio: a tabela NAO PODE ter coluna de
-- senha. Essa ausencia e uma decisao, nao um esquecimento, e um teste que a
-- vigia impede que alguem a acrescente "para completar o log" mais adiante.
-- ============================================================

\echo '== 070 VALIDACAO =='

SELECT current_database() AS banco, current_user AS usuario;

\echo '-- 1. estrutura da tabela de log'
SELECT CASE WHEN to_regclass('homologacao.liberacao_tolerancia_recebimento') IS NULL
            THEN 'FALHOU: tabela nao existe' ELSE 'OK: tabela existe' END AS tabela;

WITH esperado(coluna, tipo, nulavel) AS (
    VALUES ('codigo_liberacao',           'bigint',  'NO'),
           ('numero_pedido',              'text',    'NO'),
           ('numero_item',                'text',    'NO'),
           ('material',                   'text',    'NO'),
           ('tipo_estouro',               'text',    'NO'),
           ('resultado',                  'text',    'NO'),
           ('login_autorizador',          'text',    'NO'),
           ('codigo_usuario_autorizador', 'bigint',  'YES'),
           ('ocorrido_em',                'timestamp with time zone', 'NO')
)
SELECT e.coluna,
       CASE WHEN c.column_name IS NULL THEN 'FALHOU: ausente'
            WHEN c.data_type <> e.tipo THEN 'FALHOU: tipo ' || c.data_type
            WHEN c.is_nullable <> e.nulavel THEN 'FALHOU: nulavel ' || c.is_nullable
            ELSE 'OK' END AS veredito
  FROM esperado e
  LEFT JOIN information_schema.columns c
         ON c.table_schema='homologacao'
        AND c.table_name='liberacao_tolerancia_recebimento'
        AND c.column_name=e.coluna
 ORDER BY e.coluna;

\echo '-- 2. os CHECK de dominio, avaliados contra tabela-verdade'
DO $validacao$
DECLARE
    expr_tipo text; expr_res text; caso record; r boolean; falhas int := 0;
BEGIN
    SELECT pg_get_expr(conbin, conrelid) INTO expr_tipo FROM pg_constraint
     WHERE conrelid = to_regclass('homologacao.liberacao_tolerancia_recebimento')
       AND conname='ck_liberacao_tolerancia_tipo';
    SELECT pg_get_expr(conbin, conrelid) INTO expr_res FROM pg_constraint
     WHERE conrelid = to_regclass('homologacao.liberacao_tolerancia_recebimento')
       AND conname='ck_liberacao_tolerancia_resultado';

    IF expr_tipo IS NULL OR expr_res IS NULL THEN
        RAISE EXCEPTION 'FALHOU: um dos CHECK de dominio nao existe.';
    END IF;
    RAISE NOTICE 'tipo_estouro: %', expr_tipo;
    RAISE NOTICE 'resultado   : %', expr_res;

    FOR caso IN SELECT * FROM (VALUES
        ('ACIMA_DO_TETO', true), ('ABAIXO_DO_PISO', true), ('QUALQUER', false), ('', false)
    ) AS t(v, aceito) LOOP
        EXECUTE format('SELECT (%s)', replace(expr_tipo, 'tipo_estouro', quote_literal(caso.v))) INTO r;
        IF r IS DISTINCT FROM caso.aceito THEN
            falhas := falhas + 1; RAISE WARNING 'FALHOU tipo %: esperado %, veio %', caso.v, caso.aceito, r;
        END IF;
    END LOOP;

    FOR caso IN SELECT * FROM (VALUES
        ('AUTORIZADA', true), ('RECUSADA', true), ('TALVEZ', false)
    ) AS t(v, aceito) LOOP
        EXECUTE format('SELECT (%s)', replace(expr_res, 'resultado', quote_literal(caso.v))) INTO r;
        IF r IS DISTINCT FROM caso.aceito THEN
            falhas := falhas + 1; RAISE WARNING 'FALHOU resultado %: esperado %, veio %', caso.v, caso.aceito, r;
        END IF;
    END LOOP;

    IF falhas > 0 THEN RAISE EXCEPTION 'FALHOU: % caso(s) divergentes.', falhas; END IF;
    RAISE NOTICE 'OK: CHECK de tipo_estouro e resultado corretos (7 casos).';
END
$validacao$;

\echo '-- 3. a permissao nova existe e esta ativa'
SELECT CASE WHEN EXISTS (
         SELECT 1 FROM homologacao.permissao
          WHERE modulo_permissao='PROCESSO_PRODUCAO' AND rotina_permissao='ENTRADA_PRODUTO'
            AND acao_permissao='LIBERAR_TOLERANCIA' AND situacao_permissao IS TRUE)
       THEN 'OK: LIBERAR_TOLERANCIA ativa' ELSE 'FALHOU: permissao ausente ou inativa' END AS veredito;

\echo '-- 4. o perfil Gerente existe e TEM a permissao (e so ela)'
SELECT CASE WHEN EXISTS (
         SELECT 1
           FROM homologacao.perfil_acesso pa
           JOIN homologacao.perfil_permissao pp ON pp.codigo_perfil_acesso = pa.codigo_perfil_acesso
           JOIN homologacao.permissao p         ON p.codigo_permissao      = pp.codigo_permissao
          WHERE pa.nome_perfil_acesso='Gerente'
            AND p.acao_permissao='LIBERAR_TOLERANCIA'
            AND pp.situacao_perfil_permissao IS TRUE)
       THEN 'OK: perfil Gerente com LIBERAR_TOLERANCIA' ELSE 'FALHOU: vinculo ausente' END AS veredito;

SELECT count(*) AS permissoes_do_gerente
  FROM homologacao.perfil_acesso pa
  JOIN homologacao.perfil_permissao pp ON pp.codigo_perfil_acesso = pa.codigo_perfil_acesso
 WHERE pa.nome_perfil_acesso='Gerente' AND pp.situacao_perfil_permissao IS TRUE;

\echo '-- 4b. quantos USUARIOS estao no perfil Gerente (nasce em zero: vincule em Seguranca)'
SELECT count(*) AS usuarios_gerente
  FROM homologacao.perfil_acesso pa
  JOIN homologacao.usuario_perfil up ON up.codigo_perfil_acesso = pa.codigo_perfil_acesso
 WHERE pa.nome_perfil_acesso='Gerente' AND up.situacao_usuario_perfil IS TRUE;

\echo '-- 5. A TABELA NAO PODE TER COLUNA DE SENHA (decisao, nao esquecimento)'
SELECT CASE WHEN EXISTS (
         SELECT 1 FROM information_schema.columns
          WHERE table_schema='homologacao'
            AND table_name='liberacao_tolerancia_recebimento'
            AND (column_name ILIKE '%senha%' OR column_name ILIKE '%password%' OR column_name ILIKE '%hash%'))
       THEN 'FALHOU: existe coluna de credencial nesta tabela - leia o cabecalho da PROPOSTA'
       ELSE 'OK: nenhuma coluna de senha' END AS veredito;

\echo '-- 6. permissoes do papel DESTE banco (nao do papel vizinho)'
-- O papel e escolhido por current_database(), como na PROPOSTA. Conferir um papel fixo
-- aqui poderia dar 'OK' olhando o papel do OUTRO ambiente do Q, que existe neste banco.
DO $privilegios$
DECLARE
    papel  text := CASE current_database()
        WHEN 'fuga_jales_local_homologacao_q_v1_2'    THEN 'fugapet_q_app'
        WHEN 'fuga_jales_local_homologacao_q130_v1_3' THEN 'fugapet_q130_app'
    END;
    tabela text := 'homologacao.liberacao_tolerancia_recebimento';
    seq    text := 'homologacao.liberacao_tolerancia_recebimento_codigo_liberacao_seq';
    falhas int  := 0;
BEGIN
    IF papel IS NULL THEN
        RAISE EXCEPTION 'FALHOU: banco nao reconhecido (%).', current_database();
    END IF;
    RAISE NOTICE 'papel deste banco: %', papel;

    IF NOT has_table_privilege(papel, tabela, 'SELECT') THEN
        falhas := falhas + 1; RAISE WARNING 'FALHOU: sem SELECT.';
    END IF;
    IF NOT has_table_privilege(papel, tabela, 'INSERT') THEN
        falhas := falhas + 1; RAISE WARNING 'FALHOU: sem INSERT.';
    END IF;
    IF has_table_privilege(papel, tabela, 'DELETE') THEN
        falhas := falhas + 1;
        RAISE WARNING 'ATENCAO: o papel pode APAGAR liberacao - nao era a intencao.';
    END IF;
    IF NOT has_sequence_privilege(papel, seq, 'USAGE') THEN
        falhas := falhas + 1; RAISE WARNING 'FALHOU: sem USAGE na sequencia.';
    END IF;

    IF falhas > 0 THEN
        RAISE EXCEPTION 'FALHOU: % problema(s) de privilegio.', falhas;
    END IF;
    RAISE NOTICE 'OK: SELECT e INSERT presentes, DELETE ausente, USAGE na sequencia.';
END
$privilegios$;

\echo '== Fim da VALIDACAO. Nada foi escrito. =='
